using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Головна система рендеру рейок. Реєструється як IRenderer і рендерить
    /// всі видимі секції через instanced rendering.
    /// </summary>
    public class RailWayRendererSystem : IRenderer, IDisposable
    {
        private ICoreClientAPI capi;
        private IShaderProgram prog;

        // Словник: ключ матеріалу → рендерер шпал
        private Dictionary<string, SleeperRenderer> sleeperRenderers = new Dictionary<string, SleeperRenderer>();

        // Словник: ключ матеріалу → рендерер рейок
        private Dictionary<string, RailSectionRenderer> railRenderers = new Dictionary<string, RailSectionRenderer>();

        private long lightListenerId;

        // Шейдер для карти тіней. Стандартний шейдер інстансів у грі тіней не вміє, а шейдер тіней гри
        // не вміє інстансів, тому тут свій: лише позиція вершини, матриця інстанса і матриця світла
        private IShaderProgram shadowProg;

        private const string ShadowVertexShader = @"#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPosition;
layout(location = 5) in mat4 transform;

uniform mat4 mvpMatrix;

void main()
{
	gl_Position = mvpMatrix * transform * vec4(vertexPosition, 1.0);
}
";

        private const string ShadowFragmentShader = @"#version 330 core

out vec4 outColor;

void main()
{
	outColor = vec4(1.0);
}
";

        public double RenderOrder => 0.5;
        public int RenderRange => 350;

        public RailWayRendererSystem(ICoreClientAPI capi)
        {
            this.capi = capi;
            this.prog = capi.Shader.GetProgramByName("instanced");
            // Before: готуємо буфери раз на кадр. Далі ті самі буфери малюються в картах тіней і в основному проході
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "railsection-prepare");
            capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "railsection-shadowfar");
            capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "railsection-shadownear");
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "railsection");

            capi.Event.ReloadShader += LoadShadowShader;
            LoadShadowShader();
            // Світло блоків змінюється (факели, перекрите небо), тому перечитуємо його двічі на секунду
            lightListenerId = capi.Event.RegisterGameTickListener(UpdateLights, 500);
        }

        /// <summary>
        /// Додає шпалу для рендеру.
        /// </summary>
        public void AddSleeper(Vec3i chunkCoord, string material, Vec3d position, Vec3f rotation, float length)
        {
            if (!sleeperRenderers.TryGetValue(material, out var renderer))
            {
                ItemStack stack = CreateStack("sleeper", material);
                if (stack == null) return;
                renderer = new SleeperRenderer(capi, stack);
                sleeperRenderers[material] = renderer;
            }
            renderer.AddRailWayPart(position, new SleeperInstance(rotation, length, chunkCoord.Clone()));
        }

        /// <summary>
        /// Додає важіль стрілки. Малюється тим самим рендером, що й шпали, і зникає разом зі шпалами свого чанка.
        /// </summary>
        public void AddLever(Vec3i chunkCoord, Vec3d position, Vec3f rotation)
        {
            const string key = "#switchlever";
            if (!sleeperRenderers.TryGetValue(key, out var renderer))
            {
                ItemStack stack = CreateStack("switchlever", "iron");
                if (stack == null) return;
                renderer = new SleeperRenderer(capi, stack);
                sleeperRenderers[key] = renderer;
            }
            renderer.AddRailWayPart(position, new SleeperInstance(rotation, 1f, chunkCoord.Clone()));
        }

        /// <summary>
        /// Видаляє всі шпали чанка.
        /// </summary>
        public void RemoveChunkParts(Vec3i chunkCoord)
        {
            foreach (var renderer in sleeperRenderers.Values)
            {
                renderer.RemoveChunk(chunkCoord);
            }
        }

        /// <summary>
        /// Замінює рейки чанка. Кожна рейка малюється рендерером свого матеріалу.
        /// </summary>
        public void RebuildRails(Vec3i chunkCoord, List<Section> sections)
        {
            RemoveRailChunk(chunkCoord);

            for (int i = 0; i < sections.Count; i++)
            {
                Section s = sections[i];
                if (s.FirstRailInstalled) AddRail(chunkCoord, i, s, left: true);
                if (s.SecondRailInstalled) AddRail(chunkCoord, i, s, left: false);
            }
        }

        private void AddRail(Vec3i chunkCoord, int index, Section section, bool left)
        {
            string material = section.GetMaterial(left ? SectionPart.FirstRail : SectionPart.SecondRail) ?? "iron";

            if (!railRenderers.TryGetValue(material, out var renderer))
            {
                ItemStack stack = CreateStack("rail", material);
                if (stack == null) return;
                renderer = new RailSectionRenderer(capi, stack);
                railRenderers[material] = renderer;
            }
            renderer.AddRail(chunkCoord, index, section, left);
        }

        public void RemoveRailChunk(Vec3i chunkCoord)
        {
            foreach (var renderer in railRenderers.Values)
                renderer.RemoveChunk(chunkCoord);
        }

        private ItemStack CreateStack(string itemCode, string material)
        {
            Item item = capi.World.GetItem(new AssetLocation("railworld", itemCode));
            if (item == null) return null;
            ItemStack stack = new ItemStack(item);
            stack.Attributes.SetString("type", "normal");
            stack.Attributes.SetString("material", material);
            return stack;
        }

        private void UpdateLights(float dt)
        {
            foreach (var renderer in sleeperRenderers.Values)
                renderer.UpdateLights();
            foreach (var renderer in railRenderers.Values)
                renderer.UpdateLights();
        }

        // Збирає шейдер тіней. Викликається при старті і щоразу, коли гра перезбирає шейдери
        private bool LoadShadowShader()
        {
            IShaderProgram loaded = capi.Shader.NewShaderProgram();
            loaded.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            loaded.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            loaded.VertexShader.Code = ShadowVertexShader;
            loaded.FragmentShader.Code = ShadowFragmentShader;

            capi.Shader.RegisterMemoryShaderProgram("railworldshadow", loaded);
            bool ok = loaded.Compile();
            shadowProg = ok ? loaded : null;
            return ok;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage == EnumRenderStage.Before)
            {
                foreach (var renderer in sleeperRenderers.Values) renderer.PrepareFrame();
                foreach (var renderer in railRenderers.Values) renderer.PrepareFrame();
                return;
            }

            if (stage == EnumRenderStage.ShadowFar || stage == EnumRenderStage.ShadowNear)
            {
                RenderShadows();
                return;
            }

            if (prog.Disposed)
                prog = capi.Shader.GetProgramByName("instanced");

            capi.Render.GlDisableCullFace();
            capi.Render.GlToggleBlend(false, EnumBlendMode.Standard);

            prog.Use();
            prog.Uniform("rgbaFogIn", capi.Render.FogColor);
            prog.Uniform("rgbaAmbientIn", capi.Render.AmbientColor);
            prog.Uniform("fogMinIn", capi.Render.FogMin);
            prog.Uniform("fogDensityIn", capi.Render.FogDensity);
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            prog.UniformMatrix("modelViewMatrix", capi.Render.CameraMatrixOriginf);

            foreach (var renderer in sleeperRenderers.Values)
            {
                prog.BindTexture2D("tex", renderer.TextureId, 0);
                renderer.Draw();
            }

            foreach (var renderer in railRenderers.Values)
            {
                prog.BindTexture2D("tex", renderer.TextureId, 0);
                renderer.Draw();
            }

            prog.Stop();
            capi.Render.GlEnableCullFace();
        }

        // Малює рейки й шпали в карту тіней. На час малювання підміняє шейдер тіней гри своїм і повертає його назад
        private void RenderShadows()
        {
            if (shadowProg == null || shadowProg.Disposed) return;
            if (sleeperRenderers.Count == 0 && railRenderers.Count == 0) return;

            IShaderProgram gameShader = capi.Render.CurrentActiveShader;
            gameShader?.Stop();

            shadowProg.Use();
            // Проекція і вид від джерела світла: та сама матриця, якою гра малює в тіні блоки.
            // Перемножувати CurrentProjectionMatrix і CurrentModelviewMatrix не можна: вони повертають один і той самий масив
            shadowProg.UniformMatrix("mvpMatrix", capi.Render.CurrentShadowProjectionMatrix);

            capi.Render.GlDisableCullFace();
            foreach (var renderer in sleeperRenderers.Values) renderer.Draw();
            foreach (var renderer in railRenderers.Values) renderer.Draw();
            capi.Render.GlEnableCullFace();

            shadowProg.Stop();
            gameShader?.Use();
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            capi.Event.ReloadShader -= LoadShadowShader;
            capi.Event.UnregisterGameTickListener(lightListenerId);
            foreach (var renderer in sleeperRenderers.Values)
                renderer.Dispose();
            sleeperRenderers.Clear();
            foreach (var renderer in railRenderers.Values)
                renderer.Dispose();
            railRenderers.Clear();
        }
    }
}
