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
    /// Головна система рендеру колії. Реєструється як IRenderer і малює всю колію, яку клієнт знає,
    /// однаковими деталями пачками по чанках (instanced rendering). Що в полі зору, а що ні, не перевіряє.
    /// </summary>
    public class RailWayRendererSystem : IRenderer, IDisposable
    {
        private ICoreClientAPI capi;
        // Свій шейдер інстансів: копія ігрового з параметром origin. Уся колія малюється ним: матриці деталей
        // задані відносно кута їхнього чанка, а origin це той кут відносно камери. Ігровий шейдер інстансів
        // для цього не годиться: він рахує туман, тіні й згасання на межі дальності прямо з матриці інстанса
        // і чекає, що вона вже відносно камери
        private IShaderProgram chunkProg;

        // Рендери деталей колії: ключ це дерево шпали, деталь з металом або готовий меш → рендер
        private Dictionary<string, TrackPartRenderer> partRenderers = new Dictionary<string, TrackPartRenderer>();

        // Словник: ключ матеріалу → рендерер рейок
        private Dictionary<string, RailSectionRenderer> railRenderers = new Dictionary<string, RailSectionRenderer>();

        // Усі рендери деталей пачками, і шпал, і рейок: світло, підготовка кадру, малювання і звільнення в них
        // однакові
        private List<IInstancedParts> instanced = new List<IInstancedParts>();

        private long lightListenerId;
        // Один на рендер, а не новий щотакту: між тактами він лише очищається (LightProbe.Clear)
        private LightProbe lightProbe;
        // Прив'язка текстури гостряків перед кожним із них. Один делегат на рендер, а не новий щокадру
        private readonly Action<int> bindBladeTexture;

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
            bindBladeTexture = textureId => chunkProg.BindTexture2D("tex", textureId, 0);
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
            if (!partRenderers.TryGetValue(material, out var renderer))
            {
                ItemStack stack = CreateStack("sleeper", material);
                if (stack == null) return;
                renderer = new TrackPartRenderer(capi, stack);
                partRenderers[material] = renderer;
                instanced.Add(renderer);
            }
            // Модель шпали намальована своєї звичайної довжини; довший брус стрілки це вона ж, розтягнута
            renderer.Add(chunkCoord, position, rotation, length / renderer.ModelLength);
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
        public void AddBolt(Vec3i chunkCoord, string material, Vec3d axis, Vec3d outward, Vec3d along)
        {
            AddOutward(chunkCoord, BoltPart, material, axis, outward, along);
        }

        /// <summary>
        /// Додає деталь, що стоїть з одного боку рейки: болт у шийці, костилі з одного боку підошви. axis це
        /// точка на осі рейки на рівні низу підошви, outward бік, куди деталь дивиться, along напрямок рейки.
        /// Обидва напрямки беруться разом з ухилом колії: деталь лягає в її площину, а не стоїть рівно.
        /// </summary>
        public void AddOutward(Vec3i chunkCoord, string part, string material, Vec3d axis, Vec3d outward, Vec3d along)
        {
            // Такі моделі намальовані з боку +X від осі рейки: повертаємо їх цим боком куди треба
            AddRailPart(chunkCoord, part, material, axis, InPlane(outward, along));
        }

        /// <summary>
        /// Три осі деталі, що лежить у площині колії: x уздовж sideways, z уздовж колії along упоперек x, в один чи
        /// другий її бік, y вгору від площини. Обидва напрямки беруться з ухилом. На полотні, нахиленому одразу і
        /// вздовж, і впоперек, ухил і поперечний нахил не можна брати окремо з двох напрямків: осі рахуються разом.
        /// Якщо площини з цих напрямків не скласти (sideways нульовий або лежить уздовж along), деталь лише
        /// повертається в плані за sideways і стоїть рівно. З цих осей кути бере InPlane, а матрицю важіль стрілки
        /// (SwitchLeverRenderer).
        /// </summary>
        public static void PlaneBasis(Vec3d sideways, Vec3d along, out Vec3d x, out Vec3d y, out Vec3d z)
        {
            if (sideways.Length() >= 1e-9)
            {
                x = sideways.Clone().Normalize();
                double lean = along.X * x.X + along.Y * x.Y + along.Z * x.Z;
                z = new Vec3d(along.X - x.X * lean, along.Y - x.Y * lean, along.Z - x.Z * lean);
                if (z.Length() >= 1e-9)
                {
                    z.Normalize();
                    y = z.Cross(x);
                    if (y.Y < 0)
                    {
                        z = new Vec3d(-z.X, -z.Y, -z.Z);
                        y = new Vec3d(-y.X, -y.Y, -y.Z);
                    }
                    return;
                }
            }

            // Рівно, повернута в плані за sideways: як модель, повернута лише довкола вертикалі
            double flat = Math.Atan2(-sideways.Z, sideways.X);
            x = new Vec3d(Math.Cos(flat), 0, -Math.Sin(flat));
            y = new Vec3d(0, 1, 0);
            z = new Vec3d(Math.Sin(flat), 0, Math.Cos(flat));
        }

        /// <summary>
        /// Поворот деталі, що лежить у площині колії: вісь X моделі дивиться вздовж sideways, вісь Z уздовж
        /// колії along, в один чи другий її бік, як вийде. Обидва напрямки беруться з ухилом. Знаки нахилів
        /// виходять із самих напрямків, тож байдуже, в який бік покладена секція і куди дивиться sideways.
        /// </summary>
        public static Vec3f InPlane(Vec3d sideways, Vec3d along)
        {
            PlaneBasis(sideways, along, out Vec3d x, out Vec3d y, out Vec3d z);

            // TrackPartRenderer повертає деталь довкола Y на yaw, потім довкола X на -pitch, потім довкола Z на -roll.
            // Після цього вісь Z моделі це (sin yaw cos pitch, sin pitch, cos yaw cos pitch), а висоти осей X і Y
            // це -sin roll cos pitch і cos roll cos pitch: звідси й кути
            double pitch = Math.Asin(GameMath.Clamp(z.Y, -1, 1));
            double yaw = Math.Atan2(z.X, z.Z);
            double roll = Math.Atan2(-x.Y, y.Y);
            return new Vec3f((float)pitch, (float)yaw, (float)roll);
        }

        /// <summary>
        /// Додає металеву деталь рейки з моделі shapes/part: з того самого металу, що й рейка. Малюється тим
        /// самим рендером, що й шпали, і зникає разом зі шпалами свого чанка.
        /// </summary>
        public void AddRailPart(Vec3i chunkCoord, string part, string material, Vec3d position, Vec3f rotation)
        {
            string key = "#" + part + "-" + material;
            if (!partRenderers.TryGetValue(key, out var renderer))
            {
                renderer = new TrackPartRenderer(capi, TesselatePart(part, material));
                partRenderers[key] = renderer;
                instanced.Add(renderer);
            }
            renderer.Add(chunkCoord, position, rotation, 1f);
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

        /// <summary>
        /// Ставить деталь із готового меша mesh, якого немає серед моделей shapes/part: нерухомий важіль у
        /// своїй позі. key відрізняє такі меші між собою. Малюється тим самим рендером деталей, що й шпали.
        /// </summary>
        public void AddMeshPart(Vec3i chunkCoord, string key, MeshData mesh, Vec3d position, Vec3f rotation)
        {
            foreach (TrackPartRenderer renderer in MeshPartRenderers(key, mesh))
                renderer.Add(chunkCoord, position, rotation, 1f);
        }

        /// <summary>Прибирає деталь із готового меша з ключем key, що стоїть у чанку на місці position.</summary>
        public void RemoveMeshPart(Vec3i chunkCoord, string key, Vec3d position)
        {
            if (!meshParts.TryGetValue(key, out List<TrackPartRenderer> renderers)) return;
            foreach (TrackPartRenderer renderer in renderers) renderer.Remove(chunkCoord, position);
        }

        // Рендери деталі з готового меша: по одному на кожну сторінку атласу, на якій лежать його текстури.
        // Рендер деталей малює меш з однією сторінкою, а модель може брати текстури з кількох
        private readonly Dictionary<string, List<TrackPartRenderer>> meshParts = new Dictionary<string, List<TrackPartRenderer>>();

        private List<TrackPartRenderer> MeshPartRenderers(string key, MeshData mesh)
        {
            if (meshParts.TryGetValue(key, out List<TrackPartRenderer> renderers)) return renderers;

            renderers = new List<TrackPartRenderer>();
            MeshData[] pages = mesh.SplitByTextureId();
            for (int page = 0; page < pages.Length; page++)
            {
                var renderer = new TrackPartRenderer(capi, pages[page], mesh.TextureIds[page]);
                partRenderers["@" + key + "#" + page] = renderer;
                instanced.Add(renderer);
                renderers.Add(renderer);
            }
            meshParts[key] = renderers;
            return renderers;
        }

        // Важелі стрілок: нерухомі малюються деталями, рухомі анімованими моделями
        private SwitchLeverRenderer leverRenderer;

        /// <summary>
        /// Ставить важіль стрілки або переводить уже поставлений у положення thrown. key відрізняє важелі між
        /// собою. Викликати під час перебудови чанка, між BeginParts і EndParts.
        /// </summary>
        public void SetLever(Vec3i chunkCoord, SectionEndKey key, Vec3d position, Vec3d toTrack, Vec3d along, bool thrown)
        {
            if (leverRenderer == null) leverRenderer = new SwitchLeverRenderer(capi, this);
            leverRenderer.Set(chunkCoord, key, position, toTrack, along, thrown);
        }

        /// <summary>
        /// Прибирає всі деталі чанка, що малюються пачками: шпали, бруси, скріплення, болти.
        /// </summary>
        public void RemoveChunkParts(Vec3i chunkCoord)
        {
            foreach (var renderer in partRenderers.Values)
            {
                renderer.RemoveChunk(chunkCoord);
            }
        }

        /// <summary>
        /// Спорожнює ті самі пачки чанка перед перебудовою його колії. Буфери у відеокарті лишаються, і нові
        /// деталі лягають у них, якщо їх не побільшало.
        /// </summary>
        public void ClearChunkParts(Vec3i chunkCoord)
        {
            foreach (var renderer in partRenderers.Values)
            {
                renderer.ClearChunk(chunkCoord);
            }
        }

        /// <summary>
        /// Замінює рейки чанка. Кожна рейка малюється рендерером свого матеріалу.
        /// </summary>
        /// <param name="replaced">Каже, чи місце рейки секції займає деталь стрілки або перетину: така рейка
        /// не малюється. Другий аргумент це котра рейка: true перша, з боку нормалі.</param>
        internal void RebuildRails(Vec3i chunkCoord, List<Section> sections, System.Func<Section, bool, bool> replaced)
        {
            ClearRails(chunkCoord);

            foreach (Section s in sections)
            {
                if (s.FirstRailInstalled && !replaced(s, true)) AddRail(chunkCoord, s, true);
                if (s.SecondRailInstalled && !replaced(s, false)) AddRail(chunkCoord, s, false);
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

            RailSectionRenderer renderer = RailRenderer(material);
            if (renderer == null) return;

            if (bladeRenderer == null) bladeRenderer = new BladeRenderer(capi);
            var key = new BladeKey(new SectionKey(chunkCoord, index), left, tag);

            long hash = Fingerprint.Mix(Fingerprint.Start, material);
            foreach (LoftPart part in parts) hash = BladeMesh.Mix(hash, part);
            if (bladeRenderer.Keep(key, hash)) return;

            MeshData mesh = BladeMesh.BuildLoft(renderer.TemplateMesh, parts, out RailFrame basis);
            if (mesh == null) return;
            bladeRenderer.Set(chunkCoord, key, hash, mesh, basis, renderer.TextureId);
        }

        /// <summary>
        /// Ставить рухому деталь стрілки, гостряк, у двох крайніх положеннях closed і thrown і переставляє її в
        /// положення open між ними. Поки колія та сама, меші не перезбираються: рух лише змішує положення
        /// (MoveLoft). false, якщо два положення не дали відповідних мешів: тоді деталь треба поставити через SetLoft.
        /// </summary>
        internal bool SetMovingLoft(Vec3i chunkCoord, int index, bool left, string tag, string material, List<LoftPart> closed, List<LoftPart> thrown, double open)
        {
            if (closed == null || closed.Count == 0 || thrown == null || thrown.Count == 0) return false;

            RailSectionRenderer renderer = RailRenderer(material);
            if (renderer == null) return true;

            if (bladeRenderer == null) bladeRenderer = new BladeRenderer(capi);
            var key = new BladeKey(new SectionKey(chunkCoord, index), left, tag);

            // Відбиток обох крайніх положень, а не теперішнього: рух його не міняє
            long hash = Fingerprint.Mix(Fingerprint.Start, material);
            foreach (LoftPart part in closed) hash = BladeMesh.Mix(hash, part);
            foreach (LoftPart part in thrown) hash = BladeMesh.Mix(hash, part);
            if (bladeRenderer.Keep(key, hash) && bladeRenderer.Move(key, open)) return true;

            MeshData from = BladeMesh.BuildLoft(renderer.TemplateMesh, closed, out RailFrame basis);
            if (from == null) return true;
            MeshData to = BladeMesh.BuildLoft(renderer.TemplateMesh, thrown, basis);
            return bladeRenderer.SetMoving(chunkCoord, key, hash, from, to, basis, renderer.TextureId, open);
        }

        /// <summary>Переставляє рухому деталь, поставлену SetMovingLoft, у положення open. false, якщо її немає.</summary>
        internal bool MoveLoft(Vec3i chunkCoord, int index, bool left, string tag, double open)
        {
            return bladeRenderer != null && bladeRenderer.Move(new BladeKey(new SectionKey(chunkCoord, index), left, tag), open);
        }

        private void AddRail(Vec3i chunkCoord, Section section, bool left)
        {
            string material = section.GetMaterial(left ? SectionPart.FirstRail : SectionPart.SecondRail) ?? "iron";

            // Два шматки секції беруть текстуру з різних її частин, а сусідня секція з двох наступних: уздовж
            // колії виходить 1-2-3-4. Номери секцій, покладених одним маршрутом, ідуть підряд
            RailSectionRenderer renderer = RailRenderer(material);
            for (int piece = 0; piece < 2; piece++)
            {
                int variant = (section.IndexInChunk * 2 + piece) % RailSectionRenderer.Variants;
                renderer?.AddRailPiece(chunkCoord, section, left, piece, variant);
            }
        }

        // Рендер рейок з цього металу. null, якщо предмета немає
        private RailSectionRenderer RailRenderer(string material)
        {
            if (railRenderers.TryGetValue(material, out var renderer)) return renderer;

            ItemStack stack = CreateStack("rail", material);
            if (stack == null) return null;
            renderer = new RailSectionRenderer(capi, stack);
            railRenderers[material] = renderer;
            instanced.Add(renderer);
            return renderer;
        }

        public void RemoveRailChunk(Vec3i chunkCoord)
        {
            foreach (var renderer in railRenderers.Values)
                renderer.RemoveChunk(chunkCoord);
            bladeRenderer?.RemoveChunk(chunkCoord);
            leverRenderer?.RemoveChunk(chunkCoord);
        }

        // Спорожнює звичайні рейки чанка перед перебудовою; буфери лишаються. Деталі стрілок лишаються теж:
        // вони перебудовуються окремо
        internal void ClearRails(Vec3i chunkCoord)
        {
            foreach (var renderer in railRenderers.Values)
                renderer.ClearChunk(chunkCoord);
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
            // Один запас світла на такт для всіх рендерів: деталі одного блока питають той самий блок
            LightProbe probe = lightProbe ?? (lightProbe = new LightProbe(capi.World.BlockAccessor));
            probe.Clear();
            foreach (IInstancedParts renderer in instanced)
                renderer.UpdateLights(LightBudget, probe);

            if (++lightTick < FullLightTicks) return;
            lightTick = 0;
            bladeRenderer?.UpdateLights(probe);
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
            if (!ok) capi.Logger.Error("[RailWorld] шейдер railinstanced не зібрався: колія не малюватиметься");
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
                foreach (IInstancedParts renderer in instanced) renderer.PrepareFrame();
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
            foreach (IInstancedParts renderer in instanced)
            {
                chunkProg.BindTexture2D("tex", renderer.TextureId, 0);
                renderer.Draw(chunkProg, cameraPos, false);
            }
            bladeRenderer?.Draw(chunkProg, cameraPos, bindBladeTexture, false);
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
            if (instanced.Count == 0) return;

            IShaderProgram gameShader = capi.Render.CurrentActiveShader;
            gameShader?.Stop();

            shadowProg.Use();
            // Проекція і вид від джерела світла: та сама матриця, якою гра малює в тіні блоки.
            // Перемножувати CurrentProjectionMatrix і CurrentModelviewMatrix не можна: вони повертають один і той самий масив
            shadowProg.UniformMatrix("mvpMatrix", capi.Render.CurrentShadowProjectionMatrix);

            capi.Render.GlDisableCullFace();
            foreach (IInstancedParts renderer in instanced) renderer.Draw(shadowProg, cameraPos, true);
            bladeRenderer?.Draw(shadowProg, cameraPos, null, true);
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
            foreach (IInstancedParts renderer in instanced)
                renderer.Dispose();
            instanced.Clear();
            partRenderers.Clear();
            meshParts.Clear();
            railRenderers.Clear();
            bladeRenderer?.Dispose();
            bladeRenderer = null;
            leverRenderer?.Dispose();
            leverRenderer = null;
        }
    }
}
