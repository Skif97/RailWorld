using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
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
        // Свій шейдер інстансів: копія ігрового з параметром origin. Уся колія малюється ним: матриці деталей
        // задані відносно кута їхнього чанка, а origin це той кут відносно камери. Ігровий шейдер інстансів
        // для цього не годиться: він рахує туман, тіні й згасання на межі дальності прямо з матриці інстанса
        // і чекає, що вона вже відносно камери
        private IShaderProgram chunkProg;

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
uniform vec3 origin;

void main()
{
	gl_Position = mvpMatrix * (transform * vec4(vertexPosition, 1.0) + vec4(origin, 0.0));
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
            // Before: готуємо буфери раз на кадр. Далі ті самі буфери малюються в картах тіней і в основному проході
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "railsection-prepare");
            capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "railsection-shadowfar");
            capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "railsection-shadownear");
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "railsection");

            capi.Event.ReloadShader += LoadShaders;
            LoadShaders();
            // Світло блоків змінюється (факели, перекрите небо), а гра про це моди не повідомляє, тому його
            // доводиться опитувати: потроху кожен такт, а не все одразу
            lightListenerId = capi.Event.RegisterGameTickListener(UpdateLights, LightTickMs);
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
            // Модель шпали намальована своєї звичайної довжини; довший брус стрілки це вона ж, розтягнута
            renderer.AddRailWayPart(position, new SleeperInstance(rotation, length / renderer.ModelLength, chunkCoord.Clone()));
        }

        /// <summary>Скріплення рейки зі шпалою: підкладка з костилями.</summary>
        public const string FasteningPart = "fastening";
        /// <summary>Стикова накладка на болтах: стоїть там, де стикуються дві рейки.</summary>
        public const string JointPart = "joint";

        /// <summary>Сама підкладка, без костилів: у вузлах костилі ставляться окремо з кожного боку рейки.</summary>
        public const string PlatePart = "plate";
        /// <summary>Прокладка під підошвою рейки там, де підкладці немає місця: рейка має на чомусь лежати.</summary>
        public const string PadPart = "pad";
        /// <summary>Пара костилів з одного боку підошви рейки. Ставиться в місце самих костилів.</summary>
        public const string SpikePairPart = "spikepair";

        /// <summary>Пластина на брусі стрілки під гостряки: від рамної рейки до рамної.</summary>
        public const string SlidePlatePart = "slideplate";

        /// <summary>Головка болта на шийці рейки.</summary>
        public const string BoltPart = "bolt";

        /// <summary>
        /// Додає болт у шийку рейки. axis це точка на осі рейки на рівні низу підошви, outward бік, куди
        /// дивиться головка. Наскрізний болт це два таких: головка з одного боку і гайка з іншого.
        /// </summary>
        public void AddBolt(Vec3i chunkCoord, string material, Vec3d axis, Vec3d outward)
        {
            AddOutward(chunkCoord, BoltPart, material, axis, outward);
        }

        /// <summary>
        /// Додає деталь, що стоїть з одного боку рейки: болт у шийці, костилі з одного боку підошви. axis це
        /// точка на осі рейки на рівні низу підошви, outward бік, куди деталь дивиться.
        /// </summary>
        public void AddOutward(Vec3i chunkCoord, string part, string material, Vec3d axis, Vec3d outward)
        {
            // Такі моделі намальовані з боку +X від осі рейки: повертаємо їх цим боком куди треба
            float yaw = (float)Math.Atan2(-outward.Z, outward.X);
            AddRailPart(chunkCoord, part, material, axis, new Vec3f(0, yaw, 0));
        }

        /// <summary>
        /// Додає металеву деталь рейки з моделі shapes/part: з того самого металу, що й рейка. Малюється тим
        /// самим рендером, що й шпали, і зникає разом зі шпалами свого чанка.
        /// </summary>
        public void AddRailPart(Vec3i chunkCoord, string part, string material, Vec3d position, Vec3f rotation)
        {
            string key = "#" + part + "-" + material;
            if (!sleeperRenderers.TryGetValue(key, out var renderer))
            {
                renderer = new SleeperRenderer(capi, TesselatePart(part, material));
                sleeperRenderers[key] = renderer;
            }
            renderer.AddRailWayPart(position, new SleeperInstance(rotation, 1f, chunkCoord.Clone()));
        }

        // Меш деталі з моделі shapes/part, з текстурою потрібного металу. Свого предмета в такої деталі немає:
        // її не тримають у руках, вона з'являється разом з рейкою. null, якщо моделі немає
        private MeshData TesselatePart(string name, string material)
        {
            var location = new AssetLocation("railworld", "shapes/part/" + name + ".json");
            Shape shape = capi.Assets.TryGet(location)?.ToObject<Shape>();
            if (shape == null)
            {
                capi.Logger.Error("[RailWorld] немає моделі " + location + ": деталь не малюватиметься");
                return null;
            }

            // Та сама текстура, що й у рейки з цього металу (itemtypes/rail.json)
            var source = new ShapeTextureSource(capi, shape, location.ToString());
            var texture = new CompositeTexture(new AssetLocation("game", "block/metal/plate/" + material));
            texture.Bake(capi.Assets);
            source.textures["metal"] = texture;

            capi.Tesselator.TesselateShape("RailWorld " + name, shape, out MeshData mesh, source);
            return mesh;
        }

        // Важелі стрілок: анімовані моделі, кожен зі своїм положенням
        private SwitchLeverRenderer leverRenderer;

        /// <summary>
        /// Ставить важіль стрілки або переводить уже поставлений. key відрізняє важелі між собою. Під час
        /// перебудови чанка викликати між BeginParts і EndParts; поза нею лише переводить важіль.
        /// </summary>
        public void SetLever(Vec3i chunkCoord, string key, Vec3d position, float yaw, float pitch, double thrown)
        {
            if (leverRenderer == null) leverRenderer = new SwitchLeverRenderer(capi);
            leverRenderer.Set(chunkCoord, key, position, yaw, pitch, thrown);
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
        /// <param name="shapeOf">Каже, чи рейка секції особлива (гостряк стрілки) і яка саме; null для звичайної.
        /// Другий аргумент це котра рейка: true перша, з боку нормалі.</param>
        internal void RebuildRails(Vec3i chunkCoord, List<Section> sections, System.Func<Section, bool, RailShape> shapeOf = null)
        {
            ClearRails(chunkCoord);

            for (int i = 0; i < sections.Count; i++)
            {
                Section s = sections[i];
                if (s.FirstRailInstalled) AddRail(chunkCoord, i, s, true, shapeOf?.Invoke(s, true));
                if (s.SecondRailInstalled) AddRail(chunkCoord, i, s, false, shapeOf?.Invoke(s, false));
            }
        }

        // Гостряки стрілок: кожен власним мешем
        private BladeRenderer bladeRenderer;

        /// <summary>
        /// Ставить деталь стрілки з того самого металу, що й рейка: гостряк, хрестовину, контррейку.
        /// Меш збирається як поверхня на контурах, див. BladeMesh.BuildLoft. tag відрізняє деталі однієї
        /// рейки між собою. Викликати між BeginParts і EndParts цього чанка.
        /// </summary>
        internal void SetLoft(Vec3i chunkCoord, int index, bool left, string tag, string material, List<LoftPart> parts)
        {
            if (parts == null || parts.Count == 0) return;

            RailSectionRenderer renderer = RailRenderer(material, 0);
            if (renderer == null) return;

            if (bladeRenderer == null) bladeRenderer = new BladeRenderer(capi);
            string key = $"{chunkCoord.X}_{chunkCoord.Y}_{chunkCoord.Z}_{index}_{(left ? "L" : "R")}{tag}";

            long hash = BladeMesh.Mix(BladeMesh.HashStart, material);
            foreach (LoftPart part in parts) hash = BladeMesh.Mix(hash, part);
            if (bladeRenderer.Keep(key, hash)) return;

            MeshData mesh = BladeMesh.BuildLoft(renderer.TemplateMesh, parts, out RailFrame basis);
            if (mesh == null) return;
            bladeRenderer.Set(chunkCoord, key, hash, mesh, basis, renderer.TextureId);
        }

        private void AddRail(Vec3i chunkCoord, int index, Section section, bool left, RailShape shape)
        {
            if (shape != null && shape.Hidden) return;

            string material = section.GetMaterial(left ? SectionPart.FirstRail : SectionPart.SecondRail) ?? "iron";

            // Два шматки секції беруть текстуру з різних її частин, а сусідня секція з двох наступних: уздовж
            // колії виходить 1-2-3-4. Номери секцій, покладених одним маршрутом, ідуть підряд
            for (int piece = 0; piece < 2; piece++)
            {
                int variant = (section.IndexInChunk * 2 + piece) % RailSectionRenderer.Variants;
                RailRenderer(material, variant)?.AddRailPiece(chunkCoord, section, left, shape, piece);
            }
        }

        // Рендер рейок з цього металу, з текстурою з variant-ї її частини вздовж рейки. null, якщо предмета немає
        private RailSectionRenderer RailRenderer(string material, int variant)
        {
            string key = material + "#" + variant;
            if (railRenderers.TryGetValue(key, out var renderer)) return renderer;

            ItemStack stack = CreateStack("rail", material);
            if (stack == null) return null;
            renderer = new RailSectionRenderer(capi, stack, variant);
            railRenderers[key] = renderer;
            return renderer;
        }

        public void RemoveRailChunk(Vec3i chunkCoord)
        {
            ClearRails(chunkCoord);
            bladeRenderer?.RemoveChunk(chunkCoord);
            leverRenderer?.RemoveChunk(chunkCoord);
        }

        // Прибирає звичайні рейки чанка. Деталі стрілок лишаються: вони перебудовуються окремо
        internal void ClearRails(Vec3i chunkCoord)
        {
            foreach (var renderer in railRenderers.Values)
                renderer.RemoveChunk(chunkCoord);
        }

        /// <summary>
        /// Початок і кінець перебудови деталей стрілок чанка. Між ними кожна деталь і кожен важіль ставиться
        /// знову; те, що не змінилося, лишається з готовим мешем, а те, чого не поставили, зникає.
        /// </summary>
        internal void BeginParts(Vec3i chunkCoord)
        {
            bladeRenderer?.BeginChunk(chunkCoord);
            leverRenderer?.BeginChunk(chunkCoord);
        }

        internal void EndParts(Vec3i chunkCoord)
        {
            bladeRenderer?.EndChunk(chunkCoord);
            leverRenderer?.EndChunk(chunkCoord);
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

        // Як часто перевіряється світло і скільки деталей кожного рендера за один раз
        private const int LightTickMs = 100;
        private const int LightBudget = 2000;
        // Деталі стрілок перевіряються всі разом, раз на стільки тактів: двічі на секунду. Їх небагато
        private const int FullLightTicks = 5;
        private int lightTick;

        private void UpdateLights(float dt)
        {
            foreach (var renderer in sleeperRenderers.Values)
                renderer.UpdateLights(LightBudget);
            foreach (var renderer in railRenderers.Values)
                renderer.UpdateLights(LightBudget);

            if (++lightTick < FullLightTicks) return;
            lightTick = 0;
            bladeRenderer?.UpdateLights();
        }

        // Збирає свої шейдери. Викликається при старті і щоразу, коли гра перезбирає шейдери
        private bool LoadShaders()
        {
            bool shadows = LoadShadowShader();

            // З файлів assets/railworld/shaders/railinstanced.vsh і .fsh
            IShaderProgram loaded = capi.Shader.NewShaderProgram();
            loaded.AssetDomain = "railworld";
            loaded.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            loaded.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            capi.Shader.RegisterFileShaderProgram("railinstanced", loaded);
            bool ok = loaded.Compile();
            chunkProg = ok ? loaded : null;
            if (!ok) capi.Logger.Error("[RailWorld] шейдер railinstanced не зібрався: шпали не малюватимуться");
            return ok && shadows;
        }

        // Збирає шейдер тіней
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
            Vec3d cameraPos = capi.World.Player.Entity.CameraPos;

            if (stage == EnumRenderStage.ShadowFar || stage == EnumRenderStage.ShadowNear)
            {
                RenderShadows(cameraPos);
                return;
            }

            if (chunkProg == null || chunkProg.Disposed) return;

            capi.Render.GlDisableCullFace();
            capi.Render.GlToggleBlend(false, EnumBlendMode.Standard);

            chunkProg.Use();
            SetFrameUniforms(chunkProg);
            foreach (var renderer in sleeperRenderers.Values)
            {
                chunkProg.BindTexture2D("tex", renderer.TextureId, 0);
                renderer.Draw(chunkProg, cameraPos);
            }
            foreach (var renderer in railRenderers.Values)
            {
                chunkProg.BindTexture2D("tex", renderer.TextureId, 0);
                renderer.Draw(chunkProg, cameraPos);
            }
            bladeRenderer?.Draw(chunkProg, cameraPos, textureId => chunkProg.BindTexture2D("tex", textureId, 0));
            chunkProg.Stop();

            capi.Render.GlEnableCullFace();
        }

        // Те, що шейдер інстансів має знати про кадр: туман, загальне світло, проекція і вид
        private void SetFrameUniforms(IShaderProgram shader)
        {
            shader.Uniform("rgbaFogIn", capi.Render.FogColor);
            shader.Uniform("rgbaAmbientIn", capi.Render.AmbientColor);
            shader.Uniform("fogMinIn", capi.Render.FogMin);
            shader.Uniform("fogDensityIn", capi.Render.FogDensity);
            shader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            shader.UniformMatrix("modelViewMatrix", capi.Render.CameraMatrixOriginf);
        }

        // Малює рейки й шпали в карту тіней. На час малювання підміняє шейдер тіней гри своїм і повертає його назад
        private void RenderShadows(Vec3d cameraPos)
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
            foreach (var renderer in sleeperRenderers.Values) renderer.Draw(shadowProg, cameraPos);
            foreach (var renderer in railRenderers.Values) renderer.Draw(shadowProg, cameraPos);
            bladeRenderer?.Draw(shadowProg, cameraPos, null);
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
            capi.Event.ReloadShader -= LoadShaders;
            capi.Event.UnregisterGameTickListener(lightListenerId);
            foreach (var renderer in sleeperRenderers.Values)
                renderer.Dispose();
            sleeperRenderers.Clear();
            foreach (var renderer in railRenderers.Values)
                renderer.Dispose();
            railRenderers.Clear();
            bladeRenderer?.Dispose();
            bladeRenderer = null;
            leverRenderer?.Dispose();
            leverRenderer = null;
        }
    }
}
