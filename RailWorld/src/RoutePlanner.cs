using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Розмітка маршруту по точках, на клієнті. Гравець ставить точки, бачить майбутню колію двома лініями рейок,
    /// пофарбованими за ухилом, переглядає нарізані секції і лише тоді відправляє маршрут серверу на побудову.
    /// До підтвердження на сервер не йде нічого.
    /// Готові форми з меню (RouteTemplate) ідуть тим самим шляхом: один клік ставить усі точки форми
    /// й одразу відкриває перегляд.
    /// </summary>
    public class RoutePlanner : IRenderer, IDisposable
    {
        public const string ModeCode = "Points";

        public static RoutePlanner Instance { get; private set; }

        // Ухил, з якого лінія вже повністю жовта, у градусах. Рівна лінія зелена; червоне лишається порушенням
        private const double YellowSlopeDeg = 45;

        private static readonly int[] MarkerRgba = { 255, 255, 255, 230 };
        private static readonly int[] HoverRgba = { 120, 200, 255, 200 };
        // Точка, через яку маршрут проходить точно
        private static readonly int[] ExactRgba = { 255, 210, 60, 230 };
        private static readonly int[] PreviewRgba = { 255, 220, 40, 150 };
        private static readonly int[] ProblemRgba = { 230, 40, 40, 230 };

        // Чому маршрут зараз не можна будувати; null, якщо можна
        private string routeProblem;
        // Чому точку не вдалося поставити на колію під прицілом; null, якщо причини немає
        private string snapRefusal;

        // Номер маршруту, відправленого на побудову, на який сервер ще не відповів; 0, якщо такого немає
        private int awaitingId;
        private int lastRouteId;
        // Чому сервер відхилив останній відправлений маршрут; зникає, щойно точки змінилися
        private string serverRefusal;

        private ICoreClientAPI capi;
        private RailWaySystem system;
        private RouteHud hud;

        private List<RouteMarker> markers = new List<RouteMarker>();
        private RouteMarker hover;
        private bool preview;

        // Перша точка шаблону, якщо точки розмітки поставив він, а не гравець. Решта точок рахуються з неї
        // і з параметрів меню наново, тож зміна параметрів одразу видна в перегляді
        private RouteMarker templateAnchor;
        // Шаблон під прицілом, поки його ще не поставлено
        private List<RouteMarker> hoverTemplate;

        // Ліва кнопка скасовує один крок за один натиск, а не за кожен повтор, поки її тримають
        private bool undoArmed = true;

        // Те, що між перерахунками маршруту лишається тим самим: поки гравець веде останню точку,
        // міняється лише хвіст маршруту. Дійсне, доки не змінилася покладена колія
        private readonly RouteCache cache = new RouteCache();
        private int cacheRevision = -1;

        // Маршрут малюється частинами по PartPoints точок нарізки. Поки гравець веде останню точку, міняється
        // лише хвіст маршруту, і наново збираються й завантажуються лише ті частини, що справді змінилися
        private const int PartPoints = 64;

        private class MeshPart
        {
            // Усе, від чого залежить вигляд частини: якщо воно те саме, меш лишається
            public double[] Signature;
            public MeshRef Mesh;
        }

        private readonly List<MeshPart> parts = new List<MeshPart>();
        // Стовпчики на точках розмітки: їх небагато, збираються щоразу
        private MeshRef markerMesh;
        private Vec3d meshOrigin = new Vec3d();
        private string meshKey = "";
        private Matrixf mat = new Matrixf();

        public double RenderOrder => 0.61;
        public int RenderRange => 999;

        /// <summary>Розмітка розпочата: є хоч одна точка або відкрито перегляд.</summary>
        public bool IsActive => markers.Count > 0 || preview;

        public RoutePlanner(ICoreClientAPI capi, RailWaySystem system)
        {
            this.capi = capi;
            this.system = system;
            Instance = this;
            hud = new RouteHud(capi);
            capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "railroute");
        }

        private ItemStack Tool()
        {
            ItemStack stack = capi.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            return stack?.Block is BlockRail ? stack : null;
        }

        private bool HoldingTool()
        {
            return Tool() != null;
        }

        // Чи в меню вибрано готову форму, а не розмітку по точках
        private bool TemplateMode()
        {
            return RouteTemplate.IsTemplate(Tool()?.Attributes.GetString("railMode", RouteTemplate.SingleBlock));
        }

        // Точки шаблону від цієї першої точки з параметрами, які зараз стоять у меню
        private List<RouteMarker> BuildTemplate(RouteMarker anchor)
        {
            ITreeAttribute attributes = Tool().Attributes;
            return RouteTemplate.Build(anchor.Block, anchor.Yaw, attributes.GetString("railMode", RouteTemplate.SingleBlock),
                attributes.GetInt("railLengRad", 30), attributes.GetInt("railClimDes", 0), attributes.GetString("railDirection", "Left") == "Left");
        }

        private void Cancel()
        {
            markers = new List<RouteMarker>();
            templateAnchor = null;
            preview = false;
            serverRefusal = null;
            cache.Clear();
        }

        // ── Кліки ────────────────────────────────────────────────────────────

        /// <summary>
        /// Правий клік: нова точка; по останній точці ще раз це перехід до перегляду; у перегляді це побудова.
        /// З готовою формою клік ставить усі її точки й одразу відкриває перегляд.
        /// </summary>
        public void OnRightClick()
        {
            if (preview)
            {
                Confirm();
                return;
            }

            if (TemplateMode())
            {
                RouteMarker anchor = MakeMarker(true, false);
                if (anchor == null) return;

                templateAnchor = anchor;
                markers = BuildTemplate(anchor);
                preview = true;
                serverRefusal = null;
                capi.ShowChatMessage("Перегляд. Правий клік будує, лівий скасовує");
                return;
            }

            RouteMarker target = MakeMarker(markers.Count == 0);
            if (target == null)
            {
                if (snapRefusal != null) capi.ShowChatMessage(snapRefusal);
                return;
            }

            if (markers.Count > 0 && target.Block.Equals(markers[markers.Count - 1].Block))
            {
                if (markers.Count < 2)
                {
                    capi.ShowChatMessage("Маршруту потрібні щонайменше дві точки");
                    return;
                }
                preview = true;
                capi.ShowChatMessage("Перегляд маршруту. Правий клік будує, лівий повертає до точок");
                return;
            }

            target.Exact = target.Aimed = ExactKeyDown();
            markers.Add(target);
            serverRefusal = null;
            capi.ShowChatMessage((markers.Count == 1 ? "Першу точку обрано" : "Точку " + markers.Count + " обрано")
                + (target.Exact ? ": маршрут пройде точно через неї, а на кінці піде туди, куди ви дивитеся" : ""));
        }

        /// <summary>
        /// Лівий клік: крок назад. З перегляду до точок, далі по одній точці до початку.
        /// Повертає true, якщо клік використано і гра не має робити з ним нічого іншого.
        /// </summary>
        public bool OnLeftClick()
        {
            if (!IsActive) return false;
            if (!undoArmed) return true;
            undoArmed = false;

            if (preview && templateAnchor != null)
            {
                // Точок шаблону гравець сам не ставив, повертатися нема до чого
                Cancel();
                capi.ShowChatMessage("Скасовано");
            }
            else if (preview)
            {
                preview = false;
                capi.ShowChatMessage("Повернулися до точок");
            }
            else
            {
                markers.RemoveAt(markers.Count - 1);
                serverRefusal = null;
                capi.ShowChatMessage(markers.Count == 0 ? "Розмітку скасовано" : "Останню точку прибрано");
            }
            return true;
        }

        // Точка, поставлена із затиснутим Ctrl, точна: маршрут проходить саме через неї, а на кінці маршруту
        // йде туди, куди дивиться гравець. Без Ctrl проміжна точка це кут, який маршрут скруглює дугою, а крайня
        // бере напрямок, яким маршрут приходить у неї найплавніше
        private bool ExactKeyDown()
        {
            bool[] keys = capi.Input.KeyboardKeyStateRaw;
            return keys[(int)GlKeys.ControlLeft] || keys[(int)GlKeys.ControlRight];
        }

        private void Confirm()
        {
            if (awaitingId != 0)
            {
                capi.ShowChatMessage("Маршрут уже відправлено, чекаємо відповіді сервера");
                return;
            }
            if (routeProblem != null)
            {
                capi.ShowChatMessage("Маршрут не будується: " + routeProblem + ". Лівий клік повертає до точок");
                return;
            }

            // Що з цих точок вийде, вирішує сервер. Перегляд лишається, доки він не відповість:
            // якщо відхилить, точки на місці, і їх можна виправити
            awaitingId = ++lastRouteId;
            serverRefusal = null;
            RailWorld.SendRoute(markers, awaitingId);
            capi.ShowChatMessage("Маршрут відправлено. Перегляд лишається, доки сервер не відповість");
        }

        /// <summary>
        /// Сервер відповів на маршрут з таким номером: побудував або відхилив, і тоді reason каже чому.
        /// </summary>
        public void OnServerAnswer(int id, bool built, string reason)
        {
            // Відповідь на маршрут, якого вже не чекаємо
            if (id != awaitingId) return;
            awaitingId = 0;

            if (built)
            {
                Cancel();
                return;
            }

            serverRefusal = string.IsNullOrEmpty(reason) ? "причина невідома" : reason;
            capi.ShowChatMessage("Сервер не побудував маршрут: " + serverRefusal + ". Точки лишилися, їх можна виправити");
        }

        // ── Точки ────────────────────────────────────────────────────────────

        // Точка під прицілом. Напрямок точки це куди дивиться гравець, тобто куди маршрут іде далі.
        // Він діє лише для першої і останньої точки маршруту: проміжні вирівнюються по сусідах
        // onTrack каже, чи точка може стати на покладену колію. Готова форма починається лише з блока:
        // її напрямки кратні 45 градусам, а колія в стику може йти як завгодно
        private RouteMarker MakeMarker(bool first, bool onTrack = true)
        {
            IClientPlayer player = capi.World.Player;
            if (player?.Entity == null) return null;

            // Уже покладена колія: точка стає на стик її секцій, положення і напрямок звідти
            SectionSelection sel = system.CurrentSelection;
            if (sel != null && sel.Part == SectionPart.Whole)
            {
                if (!onTrack) return null;
                return SnapToTrack(sel.Section, sel.HitPosition, first, player.Entity.Pos.GetViewVector());
            }

            snapRefusal = null;
            BlockSelection blockSel = PickBlock(player);
            if (blockSel == null) return null;

            // Клацнули згори: блок над вибраним. Збоку: сусідній із того боку
            BlockPos block = blockSel.Position.AddCopy(blockSel.Face);

            Vec3f view = player.Entity.Pos.GetViewVector();
            double yaw = GameMath.Mod((float)Math.Atan2(-view.Z, view.X), GameMath.TWOPI);
            RouteMarker free = RouteMarker.OnBlock(block, yaw);

            // Блок біля покладеної колії: точка стає на колію, ніби приціл навели на неї в найближчому місці
            if (onTrack && TrackSurvey.NearestAxis(free.Position, TrackRules.MarkerReach,
                new TrackIndex(chunk => DataInChunk.GetClient(chunk)?.Grid), out Section near, out Vec3d at))
            {
                return SnapToTrack(near, at, first, view);
            }
            return free;
        }

        /// <summary>
        /// Точка на вже покладеній колії: найближчий до hit стик секції section. hit це місце, куди влучив
        /// приціл, або найближча до блока розмітки точка осі, якщо блок поруч із колією. Маршрут може від нього почати,
        /// це відгалуження, або ним закінчити, це примикання; в обох випадках у стику виникає стрілка.
        /// У стику колія має два напрямки. Перша точка маршруту бере той, куди дивиться гравець,
        /// остання той, яким маршрут до неї прийшов. Якщо в потрібний бік стрілка вже є
        /// (у кінця секції два сусіди), береться інший бік, а якщо зайняті обидва, точка не ставиться.
        /// </summary>
        private RouteMarker SnapToTrack(Section section, Vec3d hit, bool first, Vec3f view)
        {
            snapRefusal = null;
            bool atStart = hit.SquareDistanceTo(section.FullStartPosition) < hit.SquareDistanceTo(section.FullEndPosition);

            // Біля відкритого краю колії точка стає на сам край, а не на стик, у який влучив приціл:
            // промах на секцію-другу дав би стрілку з куцим хвостиком замість продовження колії
            if (SwitchZone.FindOpenEnd(section, hit, FindClientSection, out Section endSection, out bool endAtStart))
            {
                section = endSection;
                atStart = endAtStart;
            }

            Vec3d position = section.GetEndPosition(atStart);
            Vec3f o = section.GetOutwardDirection(atStart);
            Vec3d outward = new Vec3d(o.X, o.Y, o.Z).Normalize();

            double wanted;
            if (first)
            {
                wanted = view.X * outward.X + view.Z * outward.Z;
            }
            else
            {
                Vec3d previous = markers[markers.Count - 1].Position;
                wanted = (position.X - previous.X) * outward.X + (position.Z - previous.Z) * outward.Z;
            }
            int sign = wanted >= 0 ? 1 : -1;

            if (!JointHasRoom(section, atStart, first, sign))
            {
                sign = -sign;
                if (!JointHasRoom(section, atStart, first, sign))
                {
                    snapRefusal = "Тут уже є стрілка в обидва боки";
                    return null;
                }
            }

            RouteMarker marker = RouteMarker.OnJoint(section, atStart, sign, first, FindClientSection);

            // Маршрут чіпляється до кінця, з якого колія вже йде далі: тут виникне стрілка.
            // Поруч з іншою стрілкою її ставити не можна
            if (marker.Track != null && SwitchZone.JointInBody(section, atStart, FindClientSection))
            {
                snapRefusal = "Надто близько до іншої стрілки";
                return null;
            }
            return marker;
        }

        // Чи можна в цьому стику приєднати маршрут, що йде в бік sign відносно виходу із секції.
        // Маршрут чіпляється до того кінця, який дивиться йому назустріч: це або кінець самої секції,
        // або кінець її сусіда по той бік стику. У цього кінця має бути вільне місце для другого сусіда
        private bool JointHasRoom(Section section, bool atStart, bool first, int sign)
        {
            // Маршрут, що виходить зі стику в бік виходу із секції, продовжує її саму. Маршрут, що приходить
            // у стик проти виходу, теж упирається в неї. В інших двох випадках ідеться про сусіда
            bool ownEnd = first ? sign > 0 : sign < 0;
            List<SectionLink> links = section.GetLinks(atStart);
            if (ownEnd) return links.Count < SectionLinker.MaxLinksPerEnd;

            // Сусід має бути рівно один: якщо їх два, цей стик уже стрілка, і по той бік дві різні колії
            if (links.Count != 1) return false;
            Section neighbour = FindClientSection(links[0]);
            return neighbour != null && neighbour.GetLinks(links[0].AtStart).Count < SectionLinker.MaxLinksPerEnd;
        }

        // Секція, на яку вказує посилання, з того, що клієнт знає про колію
        private Section FindClientSection(SectionLink link)
        {
            return system.FindClientSection(link);
        }

        // Блок під прицілом для розмітки. Від звичайного прицілу гри відрізняється двома речами:
        // вода вибирається, тож точку можна поставити над нею, а висока трава, шар снігу й інші блоки,
        // які колія при побудові однаково замінить, не помічаються: промінь летить крізь них до землі
        private BlockSelection PickBlock(IClientPlayer player)
        {
            EntityPlayer entity = player.Entity;
            IBlockAccessor accessor = capi.World.BlockAccessor;

            Vec3d origin = entity.Pos.XYZ.Add(entity.LocalEyePos);
            Vec3f view = entity.Pos.GetViewVector();
            float range = player.WorldData.PickingRange;
            Vec3d target = origin.AddCopy(view.X * range, view.Y * range, view.Z * range);

            BlockFilter filter = (pos, block) =>
            {
                // У клітинці з водою у фільтр може прийти не сама вода, а порожній блок із твердого шару
                if (accessor.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid()) return true;
                return block != null && block.Id != 0 && block.Replaceable < TrackBed.ReplaceableThreshold;
            };

            BlockSelection blockSel = null;
            EntitySelection entitySel = null;

            // Так само гра робить воду вибираною, коли в руці відро
            bool liquidSelectable = capi.World.ForceLiquidSelectable;
            capi.World.ForceLiquidSelectable = true;
            try
            {
                capi.World.RayTraceForSelection(origin, target, ref blockSel, ref entitySel, filter, e => false);
            }
            finally
            {
                capi.World.ForceLiquidSelectable = liquidSelectable;
            }

            return blockSel;
        }

        // ── Малювання ────────────────────────────────────────────────────────

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (!capi.Input.InWorldMouseButton.Left) undoArmed = true;

            if (!HoldingTool())
            {
                // Розмітка лишається, але показується лише з інструментом у руці
                hud.Hide();
                meshKey = "";
                return;
            }

            bool template = TemplateMode();
            hover = null;
            hoverTemplate = null;

            // Поки сервер не відповів, відправлене не чіпаємо
            if (awaitingId == 0)
            {
                // У меню вибрано інший режим: точки одного режиму іншому не годяться
                if (markers.Count > 0 && template != (templateAnchor != null)) Cancel();
                // Параметри в меню могли змінитися
                if (templateAnchor != null) markers = BuildTemplate(templateAnchor);
            }

            if (template && !preview)
            {
                RouteMarker anchor = MakeMarker(true, false);
                if (anchor != null) hoverTemplate = BuildTemplate(anchor);
            }
            else if (!template && !preview)
            {
                hover = MakeMarker(markers.Count == 0);
                // Повторне наведення на останню точку нічого не додає: там клік означає перехід до перегляду
                if (hover != null && markers.Count > 0 && hover.Block.Equals(markers[markers.Count - 1].Block)) hover = null;
                if (hover != null) hover.Exact = hover.Aimed = ExactKeyDown();
            }

            string key = BuildKey();
            if (key != meshKey)
            {
                meshKey = key;
                RebuildMesh();
            }

            if (parts.Count == 0 && markerMesh == null) return;

            Vec3d cam = capi.World.Player.Entity.CameraPos;
            IShaderProgram prog = capi.Shader.GetProgram((int)EnumShaderProgram.Blockhighlights);
            prog.Use();
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            mat.Set(capi.Render.CameraMatrixOriginf)
               .Translate((float)(meshOrigin.X - cam.X), (float)(meshOrigin.Y - cam.Y), (float)(meshOrigin.Z - cam.Z));
            prog.UniformMatrix("modelViewMatrix", mat.Values);

            capi.Render.GlDisableCullFace();
            foreach (MeshPart part in parts) capi.Render.RenderMesh(part.Mesh);
            if (markerMesh != null) capi.Render.RenderMesh(markerMesh);
            capi.Render.GlEnableCullFace();
            prog.Stop();
        }

        // Опис усього, від чого залежить меш. Поки він той самий, меш не перебудовується
        private string BuildKey()
        {
            var sb = new System.Text.StringBuilder();
            // Номер зміни покладеної колії теж: маршрут рахується відносно неї, і те, що під ним щось поклали
            // чи прибрали, міняє і картинку, і відповідь, чи його можна будувати
            sb.Append(preview ? 'P' : 'M').Append(awaitingId).Append(serverRefusal).Append('#').Append(system.ClientRevision);
            foreach (RouteMarker marker in markers) AppendMarker(sb, marker);
            if (hover != null)
            {
                sb.Append('|');
                AppendMarker(sb, hover);
            }
            if (hoverTemplate != null)
            {
                sb.Append('|');
                foreach (RouteMarker marker in hoverTemplate) AppendMarker(sb, marker);
            }
            return sb.ToString();
        }

        private static void AppendMarker(System.Text.StringBuilder sb, RouteMarker marker)
        {
            sb.Append(marker.Block.X).Append(',').Append(marker.Block.Y).Append(',').Append(marker.Block.Z).Append(':')
              .Append(Math.Round(marker.Heading.X, 2)).Append(',').Append(Math.Round(marker.Heading.Z, 2)).Append(';');
        }

        private void DisposeMesh()
        {
            foreach (MeshPart part in parts) part.Mesh.Dispose();
            parts.Clear();
            markerMesh?.Dispose();
            markerMesh = null;
        }

        private void RebuildMesh()
        {
            var shown = new List<RouteMarker>(markers);
            if (hover != null) shown.Add(hover);
            if (hoverTemplate != null) shown.AddRange(hoverTemplate);
            if (shown.Count == 0)
            {
                DisposeMesh();
                hud.Hide();
                return;
            }

            meshOrigin = new Vec3d(shown[0].Block.X, shown[0].Block.Y, shown[0].Block.Z);
            var conflictAt = new List<int>();
            // Колія змінилася: запам'ятоване посилається на секції, яких уже може не бути
            if (cacheRevision != system.ClientRevision)
            {
                cache.Clear();
                cacheRevision = system.ClientRevision;
            }
            var laid = new TrackIndex(chunk => DataInChunk.GetClient(chunk)?.Grid);
            List<RouteMarker> settled = RouteBuilder.SettleSwitches(shown, FindClientSection);
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(settled), FindClientSection, out string conflict, laid, conflictAt, cache);
            // Секції в чужому приваті: сервер такий маршрут не покладе (RailWorld.BuildRoute)
            if (TrackBed.ClaimedSections(capi.World, capi.World.Player, points, conflictAt)) conflict = conflict ?? TrackBed.ClaimedRefusal;

            RouteCheck check = RouteRules.Check(points);
            bool[] problems = check.Problems;

            // Порушення жорстких умов теж підсвічуються: там, де воно сталося, а якщо місце невідоме, увесь маршрут
            foreach (int index in conflictAt)
            {
                if (index >= 0 && index < problems.Length) problems[index] = true;
            }
            if (conflict != null && conflictAt.Count == 0)
            {
                for (int i = 0; i < problems.Length; i++) problems[i] = true;
            }

            double maxSlope = MaxSlope(points);
            UpdateParts(points, problems);

            // Мітки точок: стовпчик у кожній, у тієї, що під прицілом, іншого кольору
            MeshData data = new MeshData(24 * shown.Count, 36 * shown.Count, false, false, true, false);
            Vec3d unitX = new Vec3d(1, 0, 0), unitY = new Vec3d(0, 1, 0), unitZ = new Vec3d(0, 0, 1);
            for (int i = 0; i < shown.Count; i++)
            {
                Vec3d p = shown[i].Position;
                bool exact = i == 0 || i == shown.Count - 1 || shown[i].Exact || shown[i].Snapped;
                RailSelectionRenderer.AddBox(data, shown[i] == hover ? HoverRgba : exact ? ExactRgba : MarkerRgba,
                    p.X - meshOrigin.X, p.Y - meshOrigin.Y, p.Z - meshOrigin.Z, unitX, unitY, unitZ, 0.08, 0, 0.6, 0.08);
            }

            markerMesh?.Dispose();
            markerMesh = capi.Render.UploadMesh(data);

            // Що саме не так, одним рядком: він іде і в куток екрана, і у відмову будувати
            List<string> reasons = check.Reasons();
            if (conflict != null) reasons.Add(conflict);
            routeProblem = reasons.Count == 0 ? null : string.Join(", ", reasons);

            bool template = templateAnchor != null || hoverTemplate != null;
            hud.Show(string.Format("{0}\nТочок: {1}\nНайбільший ухил: {2:0.0}°\nНайменший радіус: {3}, по висоті: {4}{5}{6}",
                preview ? "Перегляд маршруту" : template ? "Готова форма: клік ставить її" : "Розмітка маршруту", shown.Count, maxSlope,
                RadiusText(check.MinRadius), RadiusText(check.MinVerticalRadius),
                template ? "" : "\nCtrl + клік: точка, через яку маршрут пройде точно",
                (routeProblem == null ? "" : "\nНе можна будувати: " + routeProblem)
                + (awaitingId != 0 ? "\nЧекаємо відповіді сервера" : serverRefusal != null ? "\nСервер відхилив: " + serverRefusal : "")));
        }

        private static string RadiusText(double radius)
        {
            return radius > 999 ? "прямо" : radius.ToString("0.0");
        }

        // Приводить частини меша у відповідність до маршруту: незмінні лишає, змінені збирає наново, зайві прибирає
        private void UpdateParts(List<PointOnBezierCurve> points, bool[] problems)
        {
            int count = 0;
            for (int from = 0; from + 1 < points.Count; from += PartPoints, count++)
            {
                int to = Math.Min(from + PartPoints, points.Count - 1);
                double[] signature = Signature(points, problems, from, to);

                MeshPart part = count < parts.Count ? parts[count] : null;
                if (part != null && Same(part.Signature, signature)) continue;

                // У перегляді секція це шпала і чотири шматки рейок, у розмітці на кожен відрізок два шматки
                int boxes = preview ? (to - from) / 2 * 5 + 5 : (to - from) * 2;
                MeshData data = new MeshData(24 * boxes, 36 * boxes, false, false, true, false);
                if (preview) AddPreviewSections(data, points, problems, from, to);
                else AddRailLines(data, points, problems, from, to);

                part?.Mesh.Dispose();
                var built = new MeshPart { Signature = signature, Mesh = capi.Render.UploadMesh(data) };
                if (part != null) parts[count] = built;
                else parts.Add(built);
            }

            for (int i = parts.Count - 1; i >= count; i--)
            {
                parts[i].Mesh.Dispose();
                parts.RemoveAt(i);
            }
        }

        // Усе, від чого залежить вигляд частини маршруту між точками from і to: режим показу, початок відліку
        // меша, а для кожної точки положення, нахил полотна і чи є в ній порушення
        private double[] Signature(List<PointOnBezierCurve> points, bool[] problems, int from, int to)
        {
            double[] signature = new double[4 + (to - from + 1) * 7];
            signature[0] = preview ? 1 : 0;
            signature[1] = meshOrigin.X;
            signature[2] = meshOrigin.Y;
            signature[3] = meshOrigin.Z;

            int at = 4;
            for (int i = from; i <= to; i++)
            {
                PointOnBezierCurve point = points[i];
                signature[at++] = point.position.X;
                signature[at++] = point.position.Y;
                signature[at++] = point.position.Z;
                signature[at++] = point.normal.X;
                signature[at++] = point.normal.Y;
                signature[at++] = point.normal.Z;
                signature[at++] = problems[i] ? 1 : 0;
            }
            return signature;
        }

        private static bool Same(double[] a, double[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        // Дві лінії на місці рейок між точками from і to, кожен відрізок пофарбований за своїм ухилом
        private void AddRailLines(MeshData data, List<PointOnBezierCurve> points, bool[] problems, int from, int to)
        {
            double offset = TrackGauge.StandardWidth / 2;

            for (int i = from; i < to; i++)
            {
                double slope = RouteRules.SlopeDeg(points[i].position, points[i + 1].position);

                // Від зеленого (0, 255, 40) до жовтого (255, 220, 40)
                double t = GameMath.Clamp(slope / YellowSlopeDeg, 0, 1);
                int[] rgba = { (int)(255 * t), (int)(255 - 35 * t), 40, 230 };
                // Порушення обмежень маршруту видно окремим кольором, незалежно від ухилу
                if (problems[i] || problems[i + 1]) rgba = ProblemRgba;

                for (int side = -1; side <= 1; side += 2)
                {
                    AddSegment(data, rgba, points[i], points[i + 1], offset * side, 0.04, 0, SectionBox.RailHeight);
                }
            }
        }

        // Нарізані секції між точками from і to жовтим: шпала і дві рейки кожної, як вони ляжуть у світ.
        // Секція це три точки поспіль, починаючи з парної: частини меша починаються з парних точок
        private void AddPreviewSections(MeshData data, List<PointOnBezierCurve> points, bool[] problems, int from, int to)
        {
            double offset = TrackGauge.StandardWidth / 2;

            for (int i = from; i + 2 <= to; i += 2)
            {
                PointOnBezierCurve start = points[i], middle = points[i + 1], end = points[i + 2];
                int[] rgba = problems[i] || problems[i + 1] || problems[i + 2] ? ProblemRgba : PreviewRgba;

                // Рейка секції це два шматки, через середню точку, як вона й буде намальована у світі
                for (int rail = -1; rail <= 1; rail += 2)
                {
                    AddSegment(data, rgba, start, middle, offset * rail, SectionBox.RailHalfWidth, 0, SectionBox.RailHeight);
                    AddSegment(data, rgba, middle, end, offset * rail, SectionBox.RailHalfWidth, 0, SectionBox.RailHeight);
                }

                // Шпала посередині секції, впоперек колії
                Vec3d along = end.position - start.position;
                if (along.Length() < 1e-9) continue;
                along.Normalize();
                Vec3d side = Orthogonal(middle.normal, along);
                Vec3d up = side.Cross(along);
                Vec3d center = (start.position + end.position) * 0.5;

                RailSelectionRenderer.AddBox(data, rgba,
                    center.X - meshOrigin.X, center.Y - meshOrigin.Y, center.Z - meshOrigin.Z, side, up, along,
                    TrackGauge.StandardSleeperLength / 2, -SectionBox.SleeperDepth, 0, SectionBox.SleeperHalfWidth);
            }
        }

        // Брусок між двома точками маршруту, зсунутий убік від осі на offset
        private void AddSegment(MeshData data, int[] rgba, PointOnBezierCurve from, PointOnBezierCurve to, double offset,
            double halfWidth, double minUp, double maxUp)
        {
            Vec3d a = new Vec3d(from.position.X + from.normal.X * offset, from.position.Y + from.normal.Y * offset, from.position.Z + from.normal.Z * offset);
            Vec3d b = new Vec3d(to.position.X + to.normal.X * offset, to.position.Y + to.normal.Y * offset, to.position.Z + to.normal.Z * offset);

            Vec3d along = b - a;
            double length = along.Length();
            if (length < 1e-9) return;
            along.Normalize();

            Vec3d side = Orthogonal(from.normal, along);
            Vec3d up = side.Cross(along);

            RailSelectionRenderer.AddBox(data, rgba,
                (a.X + b.X) / 2 - meshOrigin.X, (a.Y + b.Y) / 2 - meshOrigin.Y, (a.Z + b.Z) / 2 - meshOrigin.Z,
                side, up, along, halfWidth, minUp, maxUp, length / 2);
        }

        // Нормаль колії, вирівняна перпендикулярно до напрямку
        private static Vec3d Orthogonal(Vec3f normal, Vec3d along)
        {
            Vec3d side = new Vec3d(normal.X, normal.Y, normal.Z);
            side.Sub(along.Clone().Mul(side.Dot(along)));
            if (side.Length() < 1e-9) side = new Vec3d(-along.Z, 0, along.X);
            return side.Normalize();
        }

        private static double MaxSlope(List<PointOnBezierCurve> points)
        {
            double max = 0;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                max = Math.Max(max, RouteRules.SlopeDeg(points[i].position, points[i + 1].position));
            }
            return max;
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
            DisposeMesh();
            hud?.Hide();
            hud?.Dispose();
            if (Instance == this) Instance = null;
        }

        // Напис у кутку екрана: стан розмітки і найбільший ухил
        private class RouteHud : HudElement
        {
            private bool shown;

            public RouteHud(ICoreClientAPI capi) : base(capi)
            {
                ElementBounds text = ElementBounds.Fixed(0, 0, 360, 150);
                ElementBounds bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding / 2);
                bg.BothSizing = ElementSizing.FitToChildren;
                bg.WithChildren(text);

                ElementBounds dialog = ElementStdBounds.AutosizedMainDialog
                    .WithAlignment(EnumDialogArea.RightBottom)
                    .WithFixedAlignmentOffset(-20, -140);

                SingleComposer = capi.Gui.CreateCompo("railroutehud", dialog)
                    .AddShadedDialogBG(bg, false)
                    .AddDynamicText("", CairoFont.WhiteSmallText(), text, "text")
                    .Compose();
            }

            public override string ToggleKeyCombinationCode => null;

            public void Show(string text)
            {
                SingleComposer.GetDynamicText("text").SetNewText(text);
                if (shown) return;
                shown = true;
                TryOpen();
            }

            public void Hide()
            {
                if (!shown) return;
                shown = false;
                TryClose();
            }
        }
    }
}
