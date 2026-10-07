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

        // Точка розмітки
        private class Marker
        {
            public BlockPos Block;
            public Vec3d Position;
            // Напрямок руху по маршруту в цій точці
            public Vec3d Heading;
            // Точка взята зі стику вже покладеної колії: положення і напрямок звідти, а не з блока і погляду
            public bool Snapped;
            // Нахил полотна в цьому стику; NaN для точки не на колії
            public double Cant = double.NaN;
            // Секція покладеної колії, поверх якої маршрут лежить одразу за цим стиком, і котрим кінцем вона в стику
            public Section Track;
            public bool TrackJointAtStart;
        }

        // Ухил, з якого лінія вже повністю червона, у градусах
        private const double RedSlopeDeg = 45;

        private static readonly int[] MarkerRgba = { 255, 255, 255, 230 };
        private static readonly int[] HoverRgba = { 120, 200, 255, 200 };
        private static readonly int[] PreviewRgba = { 255, 220, 40, 150 };
        private static readonly int[] ProblemRgba = { 200, 60, 255, 230 };

        // Обмеження маршруту. Найменший радіус повороту в плані і найменший радіус перелому по висоті, в блоках
        public const double MinRadius = 3;
        // Крива між точками не ідеальне коло, тому радіус трохи менший за межу ще не порушення
        private const double RadiusTolerance = 0.25;
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

        private ICoreClientAPI capi;
        private RailWaySystem system;
        private RouteHud hud;

        private List<Marker> markers = new List<Marker>();
        private Marker hover;
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

            Marker target = MakeMarker(markers.Count == 0);
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

            markers.Add(target);
            capi.ShowChatMessage(markers.Count == 1 ? "Першу точку обрано" : "Точку " + markers.Count + " обрано");
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
                capi.ShowChatMessage(markers.Count == 0 ? "Розмітку скасовано" : "Останню точку прибрано");
            }
            return true;
        }

        private void Confirm()
        {
            if (routeProblem != null)
            {
                capi.ShowChatMessage("Маршрут не будується: " + routeProblem + ". Лівий клік повертає до точок");
                return;
            }

            List<RoutePoint> route = BuildRoute(markers);
            if (route != null) RailWorld.SendRoute(route);

            markers.Clear();
            preview = false;
            capi.ShowChatMessage("Маршрут відправлено на побудову");
        }

        // ── Точки ────────────────────────────────────────────────────────────

        // Точка під прицілом. Напрямок точки це куди дивиться гравець, тобто куди маршрут іде далі.
        // Він діє лише для першої і останньої точки маршруту: проміжні вирівнюються по сусідах
        private Marker MakeMarker(bool first)
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
            // Напрямок округлюється до 45°
            Vec3d heading = ModMath.GetDirectionVectorXZ(yaw).Normalize();

            return new Marker
            {
                Block = block,
                // Центр блока, на товщину шпали над його підлогою, як і в інших режимах укладання
                Position = new Vec3d(block.X + 0.5, block.Y + SectionBox.SleeperDepth, block.Z + 0.5),
                Heading = heading
            };
        }

        /// <summary>
        /// Точка на вже покладеній колії: найближчий до прицілу стик секції. Маршрут може від нього почати,
        /// це відгалуження, або ним закінчити, це примикання; в обох випадках у стику виникає стрілка.
        /// У стику колія має два напрямки. Перша точка маршруту бере той, куди дивиться гравець,
        /// остання той, яким маршрут до неї прийшов. Якщо в потрібний бік стрілка вже є
        /// (у кінця секції два сусіди), береться інший бік, а якщо зайняті обидва, точка не ставиться.
        /// </summary>
        private Marker SnapToTrack(SectionSelection sel, bool first, Vec3f view)
        {
            snapRefusal = null;
            Section section = sel.Section;
            bool atStart = sel.HitPosition.SquareDistanceTo(section.FullStartPosition) < sel.HitPosition.SquareDistanceTo(section.FullEndPosition);

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

            Vec3d heading = outward.Clone().Mul(sign);

            // Поверх якої секції маршрут піде за стиком. Чіпляється він до одного кінця, а лежить на тому,
            // що по інший бік стику: якщо причепився до кінця цієї секції, то на її сусіді, і навпаки
            bool ownEnd = first ? sign > 0 : sign < 0;
            Section track = section;
            bool trackJointAtStart = atStart;
            if (ownEnd)
            {
                List<SectionLink> links = section.GetLinks(atStart);
                track = links.Count == 1 ? FindClientSection(links[0]) : null;
                trackJointAtStart = links.Count == 1 && links[0].AtStart;
            }

            // Маршрут чіпляється до кінця, з якого колія вже йде далі: тут виникне стрілка.
            // Поруч з іншою стрілкою її ставити не можна
            if (track != null && SwitchZone.IsBlocked(section, atStart, FindClientSection))
            {
                snapRefusal = "Надто близько до іншої стрілки";
                return null;
            }

            // Нахил полотна в стику, у знаку маршруту: його нормаль дивиться ліворуч від напрямку руху,
            // а нормаль секції може дивитися і в інший бік
            Vec3f normal = atStart ? section.StartNormal : section.EndNormal;
            double sameSide = normal.X * -heading.Z + normal.Z * heading.X >= 0 ? 1 : -1;
            double cant = Math.Asin(GameMath.Clamp(sameSide * normal.Y, -1, 1));

            return new Marker
            {
                Block = position.AsBlockPos,
                Position = position,
                Heading = heading,
                Snapped = true,
                Cant = cant,
                Track = track,
                TrackJointAtStart = trackJointAtStart
            };
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

        // З точок розмітки робить маршрут: кінці виносить на край блока, проміжні точки вирівнює по сусідах
        private static List<RoutePoint> BuildRoute(List<Marker> points)
        {
            int count = points.Count;
            if (count < 2) return null;

            var route = new List<RoutePoint>();
            for (int i = 0; i < count; i++)
            {
                Marker marker = points[i];
                Vec3d position = marker.Position.Clone();
                Vec3d tangent = marker.Heading.Clone();

                if (!marker.Snapped)
                {
                    if (i == 0)
                    {
                        // Початок маршруту на краю блока позаду, щоб колія займала блок цілком
                        position = ModMath.DirectionVectorPositionCorrection(position, tangent.Clone(), true);
                    }
                    else if (i == count - 1)
                    {
                        position = ModMath.DirectionVectorPositionCorrection(position, tangent.Clone().Mul(-1), true);
                    }
                    else
                    {
                        // Проміжна точка задає лише місце, а не напрямок. Напрямок це бісектриса між тим, звідки
                        // маршрут прийшов, і тим, куди піде далі: поворот ділиться порівну на обидва боки точки
                        // і не залежить від того, яка з двох ділянок довша
                        Vec3d prev = points[i - 1].Position;
                        Vec3d next = points[i + 1].Position;
                        double before = HorizontalDistance(prev, marker.Position);
                        double after = HorizontalDistance(marker.Position, next);

                        if (before > 1e-6 && after > 1e-6)
                        {
                            double inX = (marker.Position.X - prev.X) / before, inZ = (marker.Position.Z - prev.Z) / before;
                            double outX = (next.X - marker.Position.X) / after, outZ = (next.Z - marker.Position.Z) / after;
                            double x = inX + outX, z = inZ + outZ;
                            double length = Math.Sqrt(x * x + z * z);

                            // Розворот назад або майже назад: бісектриса зникає або стрибає з боку на бік
                            // від найменшого зсуву точок. Тоді дотична стає впоперек, у той бік, куди маршрут
                            // уже закручувався на попередній ділянці: так точки на двох колонах по черзі
                            // дають спіраль, а не зигзаг, що лягає сам на себе
                            if (length < UTurnSum)
                            {
                                Vec3d arriving = route[i - 1].Tangent;
                                double sense = arriving.X * inZ - arriving.Z * inX;
                                // Попередня ділянка пряма: бік підказує сама геометрія точок, якщо підказує
                                if (Math.Abs(sense) < 0.05) sense = inX * outZ - inZ * outX;

                                double side = sense < 0 ? -1 : 1;
                                x = -side * inZ;
                                z = side * inX;
                                length = 1;
                            }

                            // На кінцях маршрут горизонтальний. У проміжній точці дотична нахилена так,
                            // щоб плавно вести від висоти попередньої точки до висоти наступної
                            tangent = new Vec3d(x / length, (next.Y - prev.Y) / (before + after), z / length).Normalize();
                        }
                    }
                }

                route.Add(new RoutePoint
                {
                    Position = position, Tangent = tangent, Cant = marker.Cant,
                    Track = marker.Track, TrackJointAtStart = marker.TrackJointAtStart
                });
            }
            return route;
        }

        // Сума одиничних напрямків «прийшли» і «підемо», менша за це, означає розворот більш як на 150°
        private const double UTurnSum = 0.52;

        private static double HorizontalDistance(Vec3d a, Vec3d b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dz * dz);
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
            sb.Append(preview ? 'P' : 'M');
            foreach (Marker marker in markers) AppendMarker(sb, marker);
            if (hover != null)
            {
                sb.Append('|');
                AppendMarker(sb, hover);
            }
            return sb.ToString();
        }

        private static void AppendMarker(System.Text.StringBuilder sb, Marker marker)
        {
            sb.Append(marker.Block.X).Append(',').Append(marker.Block.Y).Append(',').Append(marker.Block.Z).Append(':')
              .Append(Math.Round(marker.Heading.X, 2)).Append(',').Append(Math.Round(marker.Heading.Z, 2)).Append(';');
        }

        private void RebuildMesh()
        {
            mesh?.Dispose();
            mesh = null;

            var shown = new List<Marker>(markers);
            if (hover != null) shown.Add(hover);
            if (shown.Count == 0)
            {
                hud.Hide();
                return;
            }

            meshOrigin = new Vec3d(shown[0].Block.X, shown[0].Block.Y, shown[0].Block.Z);
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(BuildRoute(shown), FindClientSection, out string conflict);

            int boxes = shown.Count + (preview ? points.Count / 2 * 5 : Math.Max(0, points.Count - 1) * 2);
            MeshData data = new MeshData(24 * boxes, 36 * boxes, false, false, true, false);

            bool[] problems = CheckRoute(points, out double minRadius, out double minVerticalRadius, out bool crossesItself, out bool broken);

            double maxSlope = 0;
            if (preview) AddPreviewSections(data, points, problems);
            else maxSlope = AddRailLines(data, points, problems);

            if (preview) maxSlope = MaxSlope(points);

            // Мітки точок: стовпчик у кожній, у тієї, що під прицілом, іншого кольору
            Vec3d unitX = new Vec3d(1, 0, 0), unitY = new Vec3d(0, 1, 0), unitZ = new Vec3d(0, 0, 1);
            for (int i = 0; i < shown.Count; i++)
            {
                Vec3d p = shown[i].Position;
                RailSelectionRenderer.AddBox(data, shown[i] == hover ? HoverRgba : MarkerRgba,
                    p.X - meshOrigin.X, p.Y - meshOrigin.Y, p.Z - meshOrigin.Z, unitX, unitY, unitZ, 0.08, 0, 0.6, 0.08);
            }

            mesh = capi.Render.UploadMesh(data);

            // Що саме не так, одним рядком: він іде і в куток екрана, і у відмову будувати
            var reasons = new List<string>();
            if (minRadius < MinRadius - RadiusTolerance) reasons.Add("поворот крутіший за радіус " + MinRadius);
            if (minVerticalRadius < MinRadius - RadiusTolerance) reasons.Add("перелом по висоті крутіший за радіус " + MinRadius);
            if (crossesItself) reasons.Add("маршрут перетинає сам себе");
            if (broken) reasons.Add("рейки розриваються по висоті");
            if (conflict != null) reasons.Add(conflict);
            routeProblem = reasons.Count == 0 ? null : string.Join(", ", reasons);

            hud.Show(string.Format("{0}\nТочок: {1}\nНайбільший ухил: {2:0.0}°\nНайменший радіус: {3}, по висоті: {4}{5}",
                preview ? "Перегляд маршруту" : "Розмітка маршруту", markers.Count, maxSlope,
                RadiusText(minRadius), RadiusText(minVerticalRadius),
                routeProblem == null ? "" : "\nНе можна будувати: " + routeProblem));
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
            if (minRadius < MinRadius - RadiusTolerance) return "поворот крутіший за радіус " + MinRadius;
            if (minVerticalRadius < MinRadius - RadiusTolerance) return "перелом по висоті крутіший за радіус " + MinRadius;
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
                            if (HorizontalDistance(p, q) >= reach) continue;

                            problems[i] = true;
                            problems[j] = true;
                            crossesItself = true;
                        }
                    }
                }
            }

            return problems;
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
            double run = HorizontalDistance(a, b);
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
