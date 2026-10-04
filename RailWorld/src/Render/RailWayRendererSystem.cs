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

        public double RenderOrder => 0.5;
        public int RenderRange => 350;

        public RailWayRendererSystem(ICoreClientAPI capi)
        {
            this.capi = capi;
            this.prog = capi.Shader.GetProgramByName("instanced");
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "railsection");
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

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (prog.Disposed)
                prog = capi.Shader.GetProgramByName("instanced");

            if (stage != EnumRenderStage.Opaque) return;

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
                renderer.OnRenderFrame(deltaTime, prog);
            }

            foreach (var renderer in railRenderers.Values)
            {
                prog.BindTexture2D("tex", renderer.TextureId, 0);
                renderer.OnRenderFrame(deltaTime, prog);
            }

            prog.Stop();
            capi.Render.GlEnableCullFace();
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
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
