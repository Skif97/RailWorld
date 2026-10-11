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
            // Маршрут має пройти точно через цю точку. Інакше проміжна точка це кут, який маршрут скруглює
            public bool Exact;
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

            target.Exact = ExactKeyDown();
            markers.Add(target);
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

            return RoundCorners(points, route);
        }

        // Дуга, якою маршрут скруглює кут, не тісніша за найменший радіус із невеликим запасом: крива Безьє
        // повторює дугу кола не точно і місцями вийшла б на волосину крутішою
        private const double CornerRadiusFactor = 1.05;

        // Радіус дуги, якою маршрут сходить з напрямку першої точки і заходить у напрямок останньої, у найменших
        // радіусах: спершу пробуємо просторішу, і лише якщо вона не вміщається, найтіснішу дозволену
        private static readonly double[] LeadRadiusFactors = { 1.6, CornerRadiusFactor };

        // Вузол маршруту під час скруглення
        private class Node
        {
            public Vec3d Position;
            public Vec3d Tangent;
            // Кут ламаної, який маршрут скруглює; решта вузлів це точки, через які він проходить
            public bool Corner;
            // Точка, додана між крайньою точкою маршруту і першим кутом: кінець дуги, якою маршрут сходить
            // із заданого напрямку. Її напрямок уже збігається з прямою до кута
            public bool Lead;
            // Дуга сходу вийшла надто короткою, щоб стати окремою ділянкою. Точка лише задає пряму до кута,
            // а в маршрут не потрапляє: кут з'єднується з крайньою точкою однією кривою
            public bool Hidden;
            public RoutePoint Point;
            // Скільки дуга кута забирає від кожної з двох прямих
            public double Cut;
            public double Turn;
        }

        /// <summary>
        /// Скруглює кути маршруту. route це по одній точці на кожну позначку, як їх зібрав BuildRoute.
        /// Перша, остання, точні й узяті з колії точки лишаються як є: маршрут проходить через них.
        /// Решта проміжних це кути ламаної. Маршрут через такий кут не проходить, а обходить його зсередини
        /// дугою, що торкається прямих до сусідніх точок: замість кута стають дві точки, початок і кінець дуги.
        /// Між дугами маршрут іде прямо, і це справді пряма: напрямок на обох її кінцях той самий.
        ///
        /// Місце на прямих ділиться так. Спершу кожен кут отримує стільки, скільки треба для дуги найменшого
        /// радіуса; якщо двом сусіднім кутам разом не вистачає прямої між ними, вони діляться нею порівну
        /// до потреби, і там поворот лишиться крутішим за дозволений. Те, що лишилося після цього, кути
        /// розбирають порівну, і дуги стають ширшими. Тому тісний кут не страждає через просторого сусіда.
        ///
        /// На початку й у кінці маршруту напрямок заданий: поглядом або колією, з якою маршрут стикується.
        /// Там першим ділом ставиться дуга, якою маршрут плавно сходить із цього напрямку на пряму до першого
        /// кута (і так само заходить з останнього кута): це важливіше, ніж рівно поділити поворот між точками.
        /// </summary>
        private static List<RoutePoint> RoundCorners(List<Marker> markers, List<RoutePoint> route)
        {
            int count = route.Count;
            if (count < 3) return route;

            var nodes = new List<Node>();
            for (int i = 0; i < count; i++)
            {
                nodes.Add(new Node
                {
                    Position = route[i].Position, Tangent = route[i].Tangent, Point = route[i],
                    Corner = i > 0 && i < count - 1 && !markers[i].Exact && !markers[i].Snapped
                });
            }

            // Схід із напрямку першої точки і захід у напрямок останньої
            if (nodes[1].Corner)
            {
                Node lead = LeadIn(nodes[0], nodes[1].Position, 1);
                if (lead != null) nodes.Insert(1, lead);
            }
            if (nodes[nodes.Count - 2].Corner)
            {
                Node lead = LeadIn(nodes[nodes.Count - 1], nodes[nodes.Count - 2].Position, -1);
                if (lead != null) nodes.Insert(nodes.Count - 1, lead);
            }

            int n = nodes.Count;
            double[] leg = new double[n];
            for (int i = 0; i + 1 < n; i++) leg[i] = HorizontalDistance(nodes[i].Position, nodes[i + 1].Position);

            // Скільки кут може взяти від прямої до сусіда, якщо сусід не кут: до точки, чий напрямок уже лежить
            // на цій прямій, усю; до точної точки трохи менше, їй потрібне місце на власний вигин
            double Limit(int neighbour, double length)
            {
                return nodes[neighbour].Corner || nodes[neighbour].Lead ? length : Math.Max(length - 1, length / 2);
            }

            // Потреба кожного кута: дуга найменшого радіуса
            double[] need = new double[n];
            for (int i = 1; i + 1 < n; i++)
            {
                Node node = nodes[i];
                if (!node.Corner) continue;

                Vec3d before = nodes[i - 1].Position, after = nodes[i + 1].Position;
                double inX = node.Position.X - before.X, inZ = node.Position.Z - before.Z;
                double outX = after.X - node.Position.X, outZ = after.Z - node.Position.Z;
                if (leg[i - 1] < 1e-6 || leg[i] < 1e-6) continue;

                node.Turn = Math.Acos(GameMath.Clamp((inX * outX + inZ * outZ) / (leg[i - 1] * leg[i]), -1, 1));
                // Точка на прямій або розворот назад: дуги тут немає
                if (node.Turn < 0.01 || node.Turn > Math.PI - 0.05) continue;

                need[i] = MinRadius * CornerRadiusFactor * Math.Tan(node.Turn / 2);
                node.Cut = Math.Min(need[i], Math.Min(Limit(i - 1, leg[i - 1]), Limit(i + 1, leg[i])));
            }

            // Двом сусіднім кутам не вистачає прямої між ними: ділять її відповідно до потреб
            for (int i = 1; i + 2 < n; i++)
            {
                if (!nodes[i].Corner || !nodes[i + 1].Corner) continue;
                double both = nodes[i].Cut + nodes[i + 1].Cut;
                if (both <= leg[i] || both < 1e-9) continue;
                double scale = leg[i] / both;
                nodes[i].Cut *= scale;
                nodes[i + 1].Cut *= scale;
            }

            // Решту місця кути розбирають порівну: кожен бере половину того, що ще вільне з тіснішого боку
            for (int pass = 0; pass < 12; pass++)
            {
                for (int i = 1; i + 1 < n; i++)
                {
                    if (!nodes[i].Corner || need[i] <= 0) continue;
                    double inFree = (nodes[i - 1].Corner ? leg[i - 1] - nodes[i - 1].Cut : Limit(i - 1, leg[i - 1])) - nodes[i].Cut;
                    double outFree = (nodes[i + 1].Corner ? leg[i] - nodes[i + 1].Cut : Limit(i + 1, leg[i])) - nodes[i].Cut;
                    double free = Math.Min(inFree, outFree);
                    if (free > 1e-6) nodes[i].Cut += free / 2;
                }
            }

            var result = new List<RoutePoint>();
            // Точки, через які маршрут має пройти точно: їх не можна ні зсувати, ні зливати з сусідами
            var keep = new HashSet<RoutePoint>();

            for (int i = 0; i < n; i++)
            {
                Node node = nodes[i];
                if (node.Hidden) continue;
                if (!node.Corner)
                {
                    RoutePoint point = node.Point ?? new RoutePoint { Position = node.Position, Tangent = node.Tangent };
                    result.Add(point);
                    keep.Add(point);
                    continue;
                }

                // Точка на прямій: кута немає
                if (node.Turn < 0.01) continue;
                // Розворот назад, або дуга виходить надто короткою для нарізки: точка лишається як є
                double cut = node.Cut;
                if (need[i] <= 0 || 2 * cut / Math.Tan(node.Turn / 2) * Math.Sin(node.Turn / 2) < RouteCurve.MinSpan)
                {
                    result.Add(node.Point);
                    continue;
                }

                Vec3d before = nodes[i - 1].Position, corner = node.Position, after = nodes[i + 1].Position;
                double inX = corner.X - before.X, inZ = corner.Z - before.Z, outX = after.X - corner.X, outZ = after.Z - corner.Z;
                double inLength = leg[i - 1], outLength = leg[i];
                double inPart = cut / inLength, outPart = cut / outLength;

                result.Add(new RoutePoint
                {
                    Position = new Vec3d(corner.X - inX * inPart, corner.Y - (corner.Y - before.Y) * inPart, corner.Z - inZ * inPart),
                    Tangent = new Vec3d(inX / inLength, (corner.Y - before.Y) / inLength, inZ / inLength).Normalize()
                });
                result.Add(new RoutePoint
                {
                    Position = new Vec3d(corner.X + outX * outPart, corner.Y + (after.Y - corner.Y) * outPart, corner.Z + outZ * outPart),
                    Tangent = new Vec3d(outX / outLength, (after.Y - corner.Y) / outLength, outZ / outLength).Normalize()
                });
            }

            // Дві точки ближче одна до одної, ніж дозволяє нарізка: між ними не вийшло б жодної секції.
            // Кінець однієї дуги і початок наступної на спільній прямій зливаються в одну точку посередині;
            // поруч із точною точкою лишається вона
            for (int k = 0; k + 1 < result.Count; k++)
            {
                RoutePoint a = result[k], b = result[k + 1];
                if (HorizontalDistance(a.Position, b.Position) >= RouteCurve.MinSpan) continue;

                if (keep.Contains(a) && keep.Contains(b)) continue;
                if (keep.Contains(b)) result.RemoveAt(k);
                else if (keep.Contains(a)) result.RemoveAt(k + 1);
                else
                {
                    result[k] = new RoutePoint
                    {
                        Position = new Vec3d((a.Position.X + b.Position.X) / 2, (a.Position.Y + b.Position.Y) / 2, (a.Position.Z + b.Position.Z) / 2),
                        Tangent = new Vec3d(a.Tangent.X + b.Tangent.X, a.Tangent.Y + b.Tangent.Y, a.Tangent.Z + b.Tangent.Z).Normalize()
                    };
                    result.RemoveAt(k + 1);
                }
                k--;
            }

            return result.Count < 2 ? route : result;
        }

        /// <summary>
        /// Дуга, якою маршрут сходить із заданого напрямку крайньої точки end на пряму до першого кута target.
        /// Коло торкається напрямку в самій точці; з кута до кола ведеться дотична, і точка дотику це кінець
        /// дуги. Повертає цю точку з напрямком уздовж дотичної, або null, якщо дуга не потрібна (кут і так
        /// майже прямо по курсу) чи не вміщається (кут усередині кола найменшого радіуса).
        /// direction це 1 для початку маршруту і -1 для кінця: там маршрут іде навпаки, з кута в точку.
        /// </summary>
        private static Node LeadIn(Node end, Vec3d target, int direction)
        {
            double tx = end.Tangent.X * direction, tz = end.Tangent.Z * direction;
            double length = Math.Sqrt(tx * tx + tz * tz);
            if (length < 1e-6) return null;
            tx /= length;
            tz /= length;

            Vec3d p = end.Position;
            double dx = target.X - p.X, dz = target.Z - p.Z;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (distance < 2) return null;

            double cross = tx * dz - tz * dx, dot = tx * dx + tz * dz;
            if (Math.Atan2(Math.Abs(cross), dot) < 0.05) return null;

            // Бік, у який треба повертати, і напрямок обходу кола
            double side = cross > 0 ? 1 : -1;
            double nx = -tz * side, nz = tx * side;

            foreach (double factor in LeadRadiusFactors)
            {
                double radius = MinRadius * factor;
                double ox = p.X + nx * radius, oz = p.Z + nz * radius;
                double vx = target.X - ox, vz = target.Z - oz;
                double far = Math.Sqrt(vx * vx + vz * vz);
                if (far <= radius * 1.02) continue;

                double angle = Math.Acos(radius / far);
                double ux = vx / far, uz = vz / far;
                // Обхід кола: від точки в напрямку руху
                double sense = (-nx) * tz - (-nz) * tx >= 0 ? 1 : -1;

                for (int sign = -1; sign <= 1; sign += 2)
                {
                    double cos = Math.Cos(angle * sign), sin = Math.Sin(angle * sign);
                    double rx = ux * cos - uz * sin, rz = ux * sin + uz * cos;
                    double qx = ox + rx * radius, qz = oz + rz * radius;

                    double hx = target.X - qx, hz = target.Z - qz;
                    double straight = Math.Sqrt(hx * hx + hz * hz);
                    if (straight < 0.5) continue;
                    hx /= straight;
                    hz /= straight;

                    // Дотична має вести далі в той самий бік, у який маршрут іде по колу
                    if ((-rz * sense) * hx + (rx * sense) * hz < 0.99) continue;

                    double chord = Math.Sqrt((qx - p.X) * (qx - p.X) + (qz - p.Z) * (qz - p.Z));

                    // Висота міняється рівномірно від крайньої точки до кута
                    double total = chord + straight;
                    double slope = (target.Y - p.Y) / total;
                    return new Node
                    {
                        Position = new Vec3d(qx, p.Y + slope * chord, qz),
                        Tangent = new Vec3d(hx * direction, slope * direction, hz * direction).Normalize(),
                        Lead = true,
                        // Напрямок міняється зовсім трохи, і дуга коротша за найменшу ділянку. Якби її просто
                        // відкинути, пряма від кута цілила б у саму крайню точку, а не по дотичній до дуги,
                        // і весь поворот зібрався б перегином біля кінця
                        Hidden = chord < RouteCurve.MinSpan
                    };
                }
            }
            return null;
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
            var conflictAt = new List<int>();
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(BuildRoute(shown), FindClientSection, out string conflict, system.ClientSectionsIn, conflictAt);

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
