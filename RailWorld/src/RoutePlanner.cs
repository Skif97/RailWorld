using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Розмітка маршруту по точках, на клієнті. Гравець ставить точки, бачить майбутню колію двома лініями рейок,
    /// пофарбованими за ухилом, переглядає нарізані секції і лише тоді відправляє маршрут серверу на побудову.
    /// До підтвердження на сервер не йде нічого.
    /// </summary>
    public class RoutePlanner : IRenderer, IDisposable
    {
        public const string ModeCode = "Points";

        public static RoutePlanner Instance { get; private set; }

        // Ухил, з якого лінія вже повністю червона, у градусах
        private const double RedSlopeDeg = 45;

        private static readonly int[] MarkerRgba = { 255, 255, 255, 230 };
        private static readonly int[] HoverRgba = { 120, 200, 255, 200 };
        // Точка, через яку маршрут проходить точно
        private static readonly int[] ExactRgba = { 255, 210, 60, 230 };
        private static readonly int[] PreviewRgba = { 255, 220, 40, 150 };
        private static readonly int[] ProblemRgba = { 200, 60, 255, 230 };

        // Обмеження маршруту. Найменший радіус повороту в плані і найменший радіус перелому по висоті, в блоках
        // Вісім ширин колії, як і решта розмірів стрілки: від ширини колії. При колії 0.78 це 6.24 блока
        public const double MinRadius = TrackGauge.StandardWidth * 4.5;
        // Крива між точками не ідеальне коло, тому радіус трохи менший за межу ще не порушення
        private const double RadiusTolerance = TrackGauge.StandardWidth * 0.3;
        // Скільки блоків по висоті має бути між двома витками маршруту, що проходять один над одним
        public const double Clearance = 3;
        // Точки маршруту, ближчі одна до одної за це вздовж колії, на перетин не перевіряються: це сусіди
        private const double NeighbourRun = 6;
        // На скількох точках нарізки в кожен бік міряється радіус (точка це чверть блока)
        private const int RadiusWindow = 4;

        // Чому маршрут зараз не можна будувати; null, якщо можна
        private string routeProblem;
        // Між сусідніми точками нарізки ухил, крутіший за це, означає розрив рейок, а не підйом, градуси
        public const double BreakSlopeDeg = 60;

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

        // Ліва кнопка скасовує один крок за один натиск, а не за кожен повтор, поки її тримають
        private bool undoArmed = true;

        private MeshRef mesh;
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

        private bool HoldingTool()
        {
            ItemStack stack = capi.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            return stack?.Block is BlockRail && stack.Attributes.GetString("railMode") == ModeCode;
        }

        // ── Кліки ────────────────────────────────────────────────────────────

        /// <summary>
        /// Правий клік: нова точка; по останній точці ще раз це перехід до перегляду; у перегляді це побудова.
        /// </summary>
        public void OnRightClick()
        {
            if (preview)
            {
                Confirm();
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

            target.Exact = ExactKeyDown();
            markers.Add(target);
            serverRefusal = null;
            capi.ShowChatMessage(markers.Count == 1 ? "Першу точку обрано"
                : "Точку " + markers.Count + " обрано" + (target.Exact ? ": маршрут пройде точно через неї" : ""));
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

            if (preview)
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

        // Точка, поставлена із затиснутим Ctrl, точна: маршрут проходить саме через неї.
        // Без Ctrl проміжна точка це кут, який маршрут скруглює дугою
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
                markers.Clear();
                preview = false;
                return;
            }

            serverRefusal = string.IsNullOrEmpty(reason) ? "причина невідома" : reason;
            capi.ShowChatMessage("Сервер не побудував маршрут: " + serverRefusal + ". Точки лишилися, їх можна виправити");
        }

        // ── Точки ────────────────────────────────────────────────────────────

        // Точка під прицілом. Напрямок точки це куди дивиться гравець, тобто куди маршрут іде далі.
        // Він діє лише для першої і останньої точки маршруту: проміжні вирівнюються по сусідах
        private RouteMarker MakeMarker(bool first)
        {
            IClientPlayer player = capi.World.Player;
            if (player?.Entity == null) return null;

            // Уже покладена колія: точка стає на стик її секцій, положення і напрямок звідти
            SectionSelection sel = system.CurrentSelection;
            if (sel != null && sel.Part == SectionPart.Whole) return SnapToTrack(sel, first, player.Entity.Pos.GetViewVector());

            snapRefusal = null;
            BlockSelection blockSel = PickBlock(player);
            if (blockSel == null) return null;

            // Клацнули згори: блок над вибраним. Збоку: сусідній із того боку
            BlockPos block = blockSel.Position.AddCopy(blockSel.Face);

            Vec3f view = player.Entity.Pos.GetViewVector();
            double yaw = GameMath.Mod((float)Math.Atan2(-view.Z, view.X), GameMath.TWOPI);
            return RouteMarker.OnBlock(block, yaw);
        }

        /// <summary>
        /// Точка на вже покладеній колії: найближчий до прицілу стик секції. Маршрут може від нього почати,
        /// це відгалуження, або ним закінчити, це примикання; в обох випадках у стику виникає стрілка.
        /// У стику колія має два напрямки. Перша точка маршруту бере той, куди дивиться гравець,
        /// остання той, яким маршрут до неї прийшов. Якщо в потрібний бік стрілка вже є
        /// (у кінця секції два сусіди), береться інший бік, а якщо зайняті обидва, точка не ставиться.
        /// </summary>
        private RouteMarker SnapToTrack(SectionSelection sel, bool first, Vec3f view)
        {
            snapRefusal = null;
            Section section = sel.Section;
            bool atStart = sel.HitPosition.SquareDistanceTo(section.FullStartPosition) < sel.HitPosition.SquareDistanceTo(section.FullEndPosition);

            // Біля відкритого краю колії точка стає на сам край, а не на стик, у який влучив приціл:
            // промах на секцію-другу дав би стрілку з куцим хвостиком замість продовження колії
            if (SwitchZone.FindOpenEnd(section, sel.HitPosition, FindClientSection, out Section endSection, out bool endAtStart))
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

        private Section FindClientSection(SectionLink link)
        {
            List<SectionBox> boxes = system.GetClientBoxes(link.Chunk);
            if (boxes == null) return null;

            foreach (SectionBox box in boxes)
            {
                if (box.Part == SectionPart.Whole && box.Section.IndexInChunk == link.Index) return box.Section;
            }
            return null;
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

            hover = preview ? null : MakeMarker(markers.Count == 0);
            // Повторне наведення на останню точку нічого не додає: там клік означає перехід до перегляду
            if (hover != null && markers.Count > 0 && hover.Block.Equals(markers[markers.Count - 1].Block)) hover = null;
            if (hover != null) hover.Exact = ExactKeyDown();

            string key = BuildKey();
            if (key != meshKey)
            {
                meshKey = key;
                RebuildMesh();
            }

            if (mesh == null) return;

            Vec3d cam = capi.World.Player.Entity.CameraPos;
            IShaderProgram prog = capi.Shader.GetProgram((int)EnumShaderProgram.Blockhighlights);
            prog.Use();
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            mat.Set(capi.Render.CameraMatrixOriginf)
               .Translate((float)(meshOrigin.X - cam.X), (float)(meshOrigin.Y - cam.Y), (float)(meshOrigin.Z - cam.Z));
            prog.UniformMatrix("modelViewMatrix", mat.Values);

            capi.Render.GlDisableCullFace();
            capi.Render.RenderMesh(mesh);
            capi.Render.GlEnableCullFace();
            prog.Stop();
        }

        // Опис усього, від чого залежить меш. Поки він той самий, меш не перебудовується
        private string BuildKey()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(preview ? 'P' : 'M').Append(awaitingId).Append(serverRefusal);
            foreach (RouteMarker marker in markers) AppendMarker(sb, marker);
            if (hover != null)
            {
                sb.Append('|');
                AppendMarker(sb, hover);
            }
            return sb.ToString();
        }

        private static void AppendMarker(System.Text.StringBuilder sb, RouteMarker marker)
        {
            sb.Append(marker.Block.X).Append(',').Append(marker.Block.Y).Append(',').Append(marker.Block.Z).Append(':')
              .Append(Math.Round(marker.Heading.X, 2)).Append(',').Append(Math.Round(marker.Heading.Z, 2)).Append(';');
        }

        private void RebuildMesh()
        {
            mesh?.Dispose();
            mesh = null;

            var shown = new List<RouteMarker>(markers);
            if (hover != null) shown.Add(hover);
            if (shown.Count == 0)
            {
                hud.Hide();
                return;
            }

            meshOrigin = new Vec3d(shown[0].Block.X, shown[0].Block.Y, shown[0].Block.Z);
            var conflictAt = new List<int>();
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(shown), FindClientSection, out string conflict, system.ClientSectionsIn, conflictAt);

            int boxes = shown.Count + (preview ? points.Count / 2 * 5 : Math.Max(0, points.Count - 1) * 2);
            MeshData data = new MeshData(24 * boxes, 36 * boxes, false, false, true, false);

            bool[] problems = CheckRoute(points, out double minRadius, out double minVerticalRadius, out bool crossesItself, out bool broken);

            // Порушення жорстких умов теж підсвічуються: там, де воно сталося, а якщо місце невідоме, увесь маршрут
            foreach (int index in conflictAt)
            {
                if (index >= 0 && index < problems.Length) problems[index] = true;
            }
            if (conflict != null && conflictAt.Count == 0)
            {
                for (int i = 0; i < problems.Length; i++) problems[i] = true;
            }

            double maxSlope = 0;
            if (preview) AddPreviewSections(data, points, problems);
            else maxSlope = AddRailLines(data, points, problems);

            if (preview) maxSlope = MaxSlope(points);

            // Мітки точок: стовпчик у кожній, у тієї, що під прицілом, іншого кольору
            Vec3d unitX = new Vec3d(1, 0, 0), unitY = new Vec3d(0, 1, 0), unitZ = new Vec3d(0, 0, 1);
            for (int i = 0; i < shown.Count; i++)
            {
                Vec3d p = shown[i].Position;
                bool exact = i == 0 || i == shown.Count - 1 || shown[i].Exact || shown[i].Snapped;
                RailSelectionRenderer.AddBox(data, shown[i] == hover ? HoverRgba : exact ? ExactRgba : MarkerRgba,
                    p.X - meshOrigin.X, p.Y - meshOrigin.Y, p.Z - meshOrigin.Z, unitX, unitY, unitZ, 0.08, 0, 0.6, 0.08);
            }

            mesh = capi.Render.UploadMesh(data);

            // Що саме не так, одним рядком: він іде і в куток екрана, і у відмову будувати
            var reasons = new List<string>();
            if (minRadius < MinRadius - RadiusTolerance) reasons.Add("поворот крутіший за радіус " + MinRadius.ToString("0.0"));
            if (minVerticalRadius < MinRadius - RadiusTolerance) reasons.Add("перелом по висоті крутіший за радіус " + MinRadius.ToString("0.0"));
            if (crossesItself) reasons.Add("маршрут перетинає сам себе");
            if (broken) reasons.Add("рейки розриваються по висоті");
            if (conflict != null) reasons.Add(conflict);
            routeProblem = reasons.Count == 0 ? null : string.Join(", ", reasons);

            hud.Show(string.Format("{0}\nТочок: {1}\nНайбільший ухил: {2:0.0}°\nНайменший радіус: {3}, по висоті: {4}\nCtrl + клік: точка, через яку маршрут пройде точно{5}",
                preview ? "Перегляд маршруту" : "Розмітка маршруту", markers.Count, maxSlope,
                RadiusText(minRadius), RadiusText(minVerticalRadius),
                (routeProblem == null ? "" : "\nНе можна будувати: " + routeProblem)
                + (awaitingId != 0 ? "\nЧекаємо відповіді сервера" : serverRefusal != null ? "\nСервер відхилив: " + serverRefusal : "")));
        }

        private static string RadiusText(double radius)
        {
            return radius > 999 ? "прямо" : radius.ToString("0.0");
        }

        /// <summary>
        /// Перевіряє нарізаний маршрут. Повертає для кожної точки, чи є в ній порушення: поворот у плані або перелом
        /// по висоті крутіший за найменший радіус, або інший виток маршруту проходить тут ближче за просвіт.
        /// </summary>
        /// <summary>
        /// Коротка причина, чому нарізаний маршрут не можна будувати, або null, якщо можна.
        /// Сервер перевіряє цим те, що надіслав клієнт: дані клієнта могли застаріти.
        /// </summary>
        public static string FindProblem(List<PointOnBezierCurve> points)
        {
            CheckRoute(points, out double minRadius, out double minVerticalRadius, out bool crossesItself, out bool broken);

            if (broken) return "рейки розриваються по висоті";
            if (minRadius < MinRadius - RadiusTolerance) return "поворот крутіший за радіус " + MinRadius.ToString("0.0");
            if (minVerticalRadius < MinRadius - RadiusTolerance) return "перелом по висоті крутіший за радіус " + MinRadius.ToString("0.0");
            if (crossesItself) return "маршрут перетинає сам себе";
            return null;
        }

        private static bool[] CheckRoute(List<PointOnBezierCurve> points, out double minRadius, out double minVerticalRadius, out bool crossesItself, out bool broken)
        {
            int count = points.Count;
            bool[] problems = new bool[count];
            minRadius = double.MaxValue;
            minVerticalRadius = double.MaxValue;
            crossesItself = false;
            broken = false;
            if (count < 3) return problems;

            // Сходинка: сусідні точки нарізки стоять на різній висоті майже одна над одною
            for (int i = 0; i + 1 < count; i++)
            {
                if (SlopeDeg(points[i].position, points[i + 1].position) <= BreakSlopeDeg) continue;
                problems[i] = true;
                problems[i + 1] = true;
                broken = true;
            }

            // Відстань уздовж колії від початку маршруту
            double[] run = new double[count];
            for (int i = 1; i < count; i++) run[i] = run[i - 1] + (points[i].position - points[i - 1].position).Length();

            // Радіус це довжина відрізка колії, поділена на кут, на який на ньому повернула дотична:
            // окремо в плані і окремо по висоті
            for (int i = 0; i < count; i++)
            {
                int a = Math.Max(0, i - RadiusWindow);
                int b = Math.Min(count - 1, i + RadiusWindow);
                double length = run[b] - run[a];
                if (length < 1e-6) continue;

                Vec3f ta = points[a].tangent, tb = points[b].tangent;
                double turn = Math.Abs(Math.Atan2(ta.X * tb.Z - ta.Z * tb.X, ta.X * tb.X + ta.Z * tb.Z));
                double pitch = Math.Abs(Pitch(tb) - Pitch(ta));

                double radius = turn > 1e-6 ? length / turn : double.MaxValue;
                double verticalRadius = pitch > 1e-6 ? length / pitch : double.MaxValue;

                if (radius < minRadius) minRadius = radius;
                if (verticalRadius < minVerticalRadius) minVerticalRadius = verticalRadius;
                if (radius < MinRadius - RadiusTolerance || verticalRadius < MinRadius - RadiusTolerance) problems[i] = true;
            }

            // Злам між сусідніми секціями. Попередня перевірка порівнює напрямок на кінцях відрізка
            // у два блоки і не бачить вигину, що вміщається всередину нього: колія пірнає і вирівнюється
            // або звертає вбік і одразу назад, а напрямок на обох кінцях той самий. Колія ж малюється
            // секціями по дві точки нарізки, і злам це кут між двома сусідніми: його і міряємо.
            // У плані досить осі: рейки там повторюють її на сталій відстані
            for (int i = 2; i + 2 < count; i++)
            {
                Vec3d before = points[i - 2].position, here = points[i].position, after = points[i + 2].position;
                double ax = here.X - before.X, az = here.Z - before.Z;
                double bx = after.X - here.X, bz = after.Z - here.Z;
                double turn = Math.Abs(Math.Atan2(ax * bz - az * bx, ax * bx + az * bz));
                if (turn < 1e-6) continue;

                // Від середини однієї секції до середини наступної
                double radius = (run[i + 2] - run[i - 2]) / 2 / turn;
                if (radius < minRadius) minRadius = radius;
                if (radius < MinRadius - RadiusTolerance) problems[i] = true;
            }

            // Те саме по висоті.
            // Міряється по осі колії і по кожній рейці окремо. Там, де різко міняється нахил полотна, вісь
            // лишається на місці, а рейки розходяться: одна йде вгору, друга вниз, і ламаються обидві
            double halfGauge = TrackGauge.StandardWidth / 2;
            for (int i = 2; i + 2 < count; i++)
            {
                for (int side = -1; side <= 1; side++)
                {
                    double offset = side * halfGauge;
                    if (side != 0 && (points[i - 2].normal == null || points[i].normal == null || points[i + 2].normal == null)) continue;

                    double bend = Math.Abs(ChordPitch(RailPoint(points[i], offset), RailPoint(points[i + 2], offset))
                        - ChordPitch(RailPoint(points[i - 2], offset), RailPoint(points[i], offset)));
                    if (bend < 1e-6) continue;

                    // Від середини однієї секції до середини наступної
                    double verticalRadius = (run[i + 2] - run[i - 2]) / 2 / bend;
                    if (verticalRadius < minVerticalRadius) minVerticalRadius = verticalRadius;
                    if (verticalRadius < MinRadius - RadiusTolerance) problems[i] = true;
                }
            }

            // Перетин із собою. Точки розкладені по клітинках світу, щоб не порівнювати кожну з кожною
            var cells = new Dictionary<long, List<int>>();
            for (int i = 0; i < count; i++)
            {
                long cell = CellKey((int)Math.Floor(points[i].position.X), (int)Math.Floor(points[i].position.Z));
                if (!cells.TryGetValue(cell, out List<int> list)) cells[cell] = list = new List<int>();
                list.Add(i);
            }

            double reach = TrackGauge.StandardSleeperLength;
            int around = (int)Math.Ceiling(reach);

            for (int i = 0; i < count; i++)
            {
                Vec3d p = points[i].position;
                int cx = (int)Math.Floor(p.X), cz = (int)Math.Floor(p.Z);

                for (int dx = -around; dx <= around; dx++)
                {
                    for (int dz = -around; dz <= around; dz++)
                    {
                        if (!cells.TryGetValue(CellKey(cx + dx, cz + dz), out List<int> list)) continue;

                        foreach (int j in list)
                        {
                            // Кожну пару дивимося один раз, і лише далекі одна від одної вздовж колії
                            if (j <= i || run[j] - run[i] < NeighbourRun) continue;

                            Vec3d q = points[j].position;
                            if (Math.Abs(q.Y - p.Y) >= Clearance) continue;
                            if (RouteBuilder.HorizontalDistance(p, q) >= reach) continue;

                            problems[i] = true;
                            problems[j] = true;
                            crossesItself = true;
                        }
                    }
                }
            }

            return problems;
        }

        // Точка на рейці: за offset блоків убік від осі колії вздовж нормалі полотна. Нуль це сама вісь
        private static Vec3d RailPoint(PointOnBezierCurve point, double offset)
        {
            if (offset == 0 || point.normal == null) return point.position;
            return new Vec3d(point.position.X + point.normal.X * offset, point.position.Y + point.normal.Y * offset, point.position.Z + point.normal.Z * offset);
        }

        // Нахил відрізка між двома точками, радіани
        private static double ChordPitch(Vec3d from, Vec3d to)
        {
            double dx = to.X - from.X, dz = to.Z - from.Z;
            return Math.Atan2(to.Y - from.Y, Math.Sqrt(dx * dx + dz * dz));
        }

        private static double Pitch(Vec3f tangent)
        {
            return Math.Atan2(tangent.Y, Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z));
        }

        private static long CellKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        // Дві лінії на місці рейок, кожен відрізок пофарбований за своїм ухилом. Повертає найбільший ухил у градусах
        private double AddRailLines(MeshData data, List<PointOnBezierCurve> points, bool[] problems)
        {
            double maxSlope = 0;
            double offset = TrackGauge.StandardWidth / 2;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                double slope = SlopeDeg(points[i].position, points[i + 1].position);
                if (slope > maxSlope) maxSlope = slope;

                double t = GameMath.Clamp(slope / RedSlopeDeg, 0, 1);
                int[] rgba = { (int)(255 * t), (int)(255 * (1 - t)), 40, 230 };
                // Порушення обмежень маршруту видно окремим кольором, незалежно від ухилу
                if (problems[i] || problems[i + 1]) rgba = ProblemRgba;

                for (int side = -1; side <= 1; side += 2)
                {
                    AddSegment(data, rgba, points[i], points[i + 1], offset * side, 0.04, 0, SectionBox.RailHeight);
                }
            }
            return maxSlope;
        }

        // Нарізані секції жовтим: шпала і дві рейки кожної, як вони ляжуть у світ
        private void AddPreviewSections(MeshData data, List<PointOnBezierCurve> points, bool[] problems)
        {
            double offset = TrackGauge.StandardWidth / 2;

            for (int i = 0; i + 2 < points.Count; i += 2)
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

        private static double SlopeDeg(Vec3d a, Vec3d b)
        {
            double run = RouteBuilder.HorizontalDistance(a, b);
            return Math.Atan2(Math.Abs(b.Y - a.Y), run) * GameMath.RAD2DEG;
        }

        private static double MaxSlope(List<PointOnBezierCurve> points)
        {
            double max = 0;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                max = Math.Max(max, SlopeDeg(points[i].position, points[i + 1].position));
            }
            return max;
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
            mesh?.Dispose();
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
