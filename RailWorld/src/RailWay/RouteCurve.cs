using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Точка маршруту: де проходить колія і в якому напрямку вона там іде.
    /// </summary>
    public class RoutePoint
    {
        public Vec3d Position;

        /// <summary>Напрямок руху по маршруту в цій точці, одиничний вектор.</summary>
        public Vec3d Tangent;

        /// <summary>
        /// Нахил полотна в цій точці, радіани, якщо точка стоїть на вже покладеній колії: маршрут має
        /// почати або закінчити з нього, а не з нуля. Знак як у ModMath.ApplyCant. NaN, якщо точка вільна.
        /// Має зміст лише для першої і останньої точки маршруту.
        /// </summary>
        public double Cant = double.NaN;

        /// <summary>
        /// Секція вже покладеної колії, поверх якої маршрут лежить одразу за цією точкою: стрілка тут,
        /// і перші секції відгалуження йдуть по тій самій колії. null, якщо точка не на колії або колія
        /// по той бік стику не продовжується. Має зміст лише для першої і останньої точки маршруту.
        /// </summary>
        public Section Track;

        /// <summary>Котрим кінцем секція Track лежить у цій точці: true початком, false кінцем.</summary>
        public bool TrackJointAtStart;
    }

    /// <summary>
    /// Маршрут через контрольні точки. Між сусідніми точками лежить кубічна крива Безьє, а керівні вектори
    /// двох кривих, що сходяться в точці, лежать на одній прямій, тож перехід між кривими плавний.
    /// Той самий розрахунок іде на клієнті для показу і на сервері для побудови.
    /// </summary>
    public static class RouteCurve
    {
        // Вага середніх точок кривої: та сама, що й у старих режимів укладання
        public const double Weight = 0.8;

        // На відрізки якої довжини нарізається крива. Секція це два таких відрізки
        public const double PieceLength = 0.25;

        // Точки, ближчі за це, кривою не з'єднуються: нарізці нема чого різати
        public const double MinSpan = 1;

        /// <summary>
        /// Скільки секцій відгалуження, вже зійшовши з основної колії, ще йде її площиною. У кінці цієї ділянки
        /// стоїть невидима опорна точка, і лише від неї висота плавно веде до наступної точки маршруту.
        /// </summary>
        public const int HoldSections = 5;

        // Довжина переходу по висоті від стрілки до власної висоти маршруту: стільки блоків на кожен блок
        // різниці висот, але не коротше і не довше за ці межі
        private const double RampPerBlock = 6;
        private const double RampMin = 4;
        private const double RampMax = 16;

        // Найдовша ділянка, на якій відгалуження йде поверх основної колії, у точках нарізки. Запобіжник
        private const int MaxFollowPoints = 512;

        /// <summary>
        /// Довжина керівного вектора як частка відстані між точками. Залежить від того, на який кут маршрут
        /// повертає між двома точками: з такою довжиною крива з вагою Weight проходить через середину дуги кола,
        /// яке торкається обох дотичних, і радіус уздовж неї майже сталий, від плавного вигину до півкола.
        /// Для повороту на 90° виходить 0.415, майже ті самі 0.4, що були тут сталою.
        /// </summary>
        public static double HandleFactor(Vec3d from, Vec3d to)
        {
            double straight = (2 + 6 * Weight) / (24 * Weight);

            double lengths = Math.Sqrt((from.X * from.X + from.Z * from.Z) * (to.X * to.X + to.Z * to.Z));
            if (lengths < 1e-9) return straight;

            double cos = GameMath.Clamp((from.X * to.X + from.Z * to.Z) / lengths, -1, 1);
            double turn = Math.Acos(cos);
            if (turn < 1e-3) return straight;

            return Math.Tan(turn / 4) * (2 + 6 * Weight) / (12 * Weight * Math.Sin(turn / 2));
        }

        /// <summary>
        /// Нарізає весь маршрут на точки для секцій: кожні три поспіль точки з кроком два це одна секція.
        /// Нахил полотна рахується по всьому маршруту разом, а не окремо по кожній кривій.
        /// resolve знаходить секцію покладеної колії за посиланням; потрібен, коли маршрут починається
        /// або закінчується стрілкою, щоб пройти вздовж основної колії.
        /// </summary>
        public static List<PointOnBezierCurve> BuildPoints(List<RoutePoint> route, Func<SectionLink, Section> resolve = null)
        {
            return BuildPoints(route, resolve, out _);
        }

        // Наскільки можуть розходитися дві висоти, які мають збігатися, блоків
        private const double HeightTolerance = 0.02;

        /// <summary>
        /// Те саме, але ще каже, чи вдалося виконати жорсткі умови маршруту. Жорстка умова це вимога лежати
        /// в площині вже покладеної колії біля стрілки. Якщо таких умов дві (маршрут і починається, і закінчується
        /// стрілкою) і вони вимагають різної висоти в одному місці або не лишають місця на перехід між собою,
        /// conflict каже, що саме не так, і такий маршрут будувати не можна: рейки на межі розірвало б сходинкою.
        /// </summary>
        /// sectionsIn каже, які секції покладеної колії зберігаються в чанку; потрібен, щоб знайти колії,
        /// які маршрут перетинає. Без нього перетини не шукаються.
        /// conflictAt, якщо заданий, збирає номери точок нарізки, де маршрут порушує правила перетину:
        /// за ними показ підсвічує саме це місце.
        public static List<PointOnBezierCurve> BuildPoints(List<RoutePoint> route, Func<SectionLink, Section> resolve, out string conflict,
            Func<Vec3i, IEnumerable<Section>> sectionsIn = null, List<int> conflictAt = null)
        {
            conflict = null;
            var all = new List<PointOnBezierCurve>();
            if (route == null) return all;

            // На яких місцях у нарізці стоять точки маршруту
            var joints = new List<int>();

            for (int i = 0; i + 1 < route.Count; i++)
            {
                RoutePoint a = route[i];
                RoutePoint b = route[i + 1];

                List<PointOnBezierCurve> points = Leg(a, b);
                if (points == null) continue;

                // Кінець попередньої кривої і початок цієї це та сама точка
                if (all.Count > 0) points.RemoveAt(0);
                else joints.Add(0);
                all.AddRange(points);
                joints.Add(all.Count - 1);
            }

            if (all.Count < 3 || route.Count < 2) return all;

            // Там, де маршрут починається або закінчується стрілкою, він спершу йде поверх основної колії:
            // на цій ділянці бере її висоту і нахил полотна, а зійшовши з неї, плавно переходить на свої
            RoutePoint first = route[0], last = route[route.Count - 1];
            double[] weight = new double[all.Count];
            double[] trackCant = new double[all.Count];

            bool followed = FollowTracks(all, first, last, resolve, weight, trackCant, joints, ref conflict);
            // Де маршрут перетинає вже покладену колію на близькій висоті, він лягає в її площину
            if (sectionsIn != null && LevelCrossings(all, first, last, resolve, sectionsIn, weight, trackCant, conflictAt, joints, ref conflict)) followed = true;
            if (followed) RecomputeSlopes(all);

            ModMath.ApplyCant(all, first.Cant, last.Cant);
            if (followed) BlendCant(all, weight, trackCant);
            if (sectionsIn != null) CheckAgainstLaidTrack(all, resolve, sectionsIn, conflictAt, ref conflict);
            return all;
        }

        // Маршрут не можна будувати, якщо його секції, з'єднавшись із покладеною колією, дали б стрілку в зоні
        // іншої стрілки. Це буває і без прив'язки точки до колії: досить, щоб кінець ділянки ліг на чужий стик.
        // І не можна, якщо він надто довго лежить на покладеній колії: тоді дві колії не розходяться
        private static void CheckAgainstLaidTrack(List<PointOnBezierCurve> points, Func<SectionLink, Section> resolve,
            Func<Vec3i, IEnumerable<Section>> sectionsIn, List<int> conflictAt, ref string conflict)
        {
            // Секції, які маршрут справді покладе: така сама, що вже лежить, удруге не кладеться
            var planned = new List<Section>();
            var plannedAt = new List<int>();
            for (int i = 0; i + 2 < points.Count; i += 2)
            {
                var section = new Section(points[i], points[i + 1], points[i + 2], TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength);
                if (SectionLinker.HasSameSection(sectionsIn(section.ChunkAddres), section)) continue;

                planned.Add(section);
                plannedAt.Add(i);
            }

            // Довге накладання підсвічується все, від першої секції до останньої
            if (SectionLinker.FindLongOverlap(planned, sectionsIn, resolve, out int from, out int to))
            {
                conflict = conflict ?? "колії надто довго йдуть одна поверх одної";
                if (conflictAt != null)
                {
                    for (int i = plannedAt[from]; i <= Math.Min(plannedAt[to] + 2, points.Count - 1); i++) conflictAt.Add(i);
                }
            }

            List<int> close = SectionLinker.FindTooCloseAbove(planned, sectionsIn);
            if (close.Count > 0)
            {
                conflict = conflict ?? "колія проходить надто близько над чи під іншою: потрібно " + CrossingClearance.ToString("0") + " блоки просвіту";
                if (conflictAt != null)
                {
                    // Підсвічується кожна така секція: її три точки нарізки
                    foreach (int section in close)
                    {
                        for (int i = plannedAt[section]; i <= Math.Min(plannedAt[section] + 2, points.Count - 1); i++) conflictAt.Add(i);
                    }
                }
            }

            int lever = SectionLinker.FindLeverInTrack(planned, sectionsIn);
            if (lever >= 0)
            {
                conflict = conflict ?? "колія проходить по важелю стрілки";
                if (conflictAt != null)
                {
                    for (int i = Math.Max(plannedAt[lever] - 2, 0); i <= Math.Min(plannedAt[lever] + 4, points.Count - 1); i++) conflictAt.Add(i);
                }
            }

            string switchProblem = SectionLinker.FindSwitchProblem(planned, sectionsIn, resolve, out int found);
            if (switchProblem == null) return;

            conflict = conflict ?? switchProblem;
            if (conflictAt == null) return;
            for (int i = Math.Max(plannedAt[found] - 3, 0); i <= Math.Min(plannedAt[found] + 5, points.Count - 1); i++) conflictAt.Add(i);
        }

        // ── Перетини з уже покладеними коліями ──

        /// <summary>Найгостріший кут, під яким маршрут може перетнути іншу колію, градуси.</summary>
        public const double MinCrossingAngleDeg = 15;

        /// <summary>
        /// Якщо колія, яку маршрут перетинає в плані, вища чи нижча за нього менше ніж на стільки блоків,
        /// це перетин в одному рівні, і маршрут вирівнюється по ній. Якщо більше, маршрут проходить над нею
        /// або під нею, і вони одна одної не стосуються.
        /// </summary>
        public const double CrossingClearance = 3;

        // Скільки блоків за краєм перетину маршрут іще йде площиною чужої колії
        private const double CrossingMargin = 0.5;
        // Довжина переходу від площини перетину до власної висоти маршруту: стільки блоків на кожен блок
        // різниці висот, але не коротше і не довше за ці межі
        private const double CrossingRampPerBlock = 6;
        private const double CrossingRampMin = 3;
        private const double CrossingRampMax = 12;

        private class Crossing
        {
            public double At;
            public Section Section;
            public double Angle;
            // Місце перетину осей і номер точки нарізки перед ним
            public Vec3d Where;
            public int Index;
            // Пів довжини вузла вздовж маршруту і вздовж колії, яку він перетинає
            public double Half;
            public double HalfOnTrack;
        }

        /// <summary>
        /// Пів довжини вузла перетину вздовж однієї з двох колій: поки колії перекриваються. Колія це смуга
        /// завширшки зі свій габарит (Footprint), а не лише її рейки: рейки двох колій розходяться раніше,
        /// ніж шпали, і якби вузол кінчався по рейках, шпала однієї колії лишалася б під рейкою другої.
        /// own це ширина габариту колії, вздовж якої міряємо, other тієї, що її перетинає.
        /// Що гостріший кут, то довший вузол.
        /// </summary>
        internal static double CrossingHalf(double own, double other, double angle)
        {
            return other / 2 / Math.Sin(angle) + own / 2 / Math.Tan(angle);
        }

        /// <summary>
        /// Шукає, де маршрут перетинає вже покладені колії, і для кожного такого місця вирішує, що це.
        /// Колія далеко вгорі чи внизу: проїзд над нею або під нею, нічого не міняється.
        /// Колія на близькій висоті: перетин в одному рівні. На всій його довжині, поки рейки двох колій
        /// перетинаються, маршрут лягає в площину тієї колії, а до і після плавно переходить до своєї висоти,
        /// так само, як біля стрілки.
        /// Перетнути стрілку не можна ніде, і перетнути колію гостріше за MinCrossingAngleDeg теж: тоді conflict
        /// каже, що не так, і маршрут не будується.
        /// Повертає true, якщо висоти маршруту змінилися.
        /// </summary>
        private static bool LevelCrossings(List<PointOnBezierCurve> points, RoutePoint first, RoutePoint last,
            Func<SectionLink, Section> resolve, Func<Vec3i, IEnumerable<Section>> sectionsIn, double[] weight, double[] trackCant,
            List<int> conflictAt, List<int> joints, ref string conflict)
        {
            int count = points.Count;
            // Позначає місце порушення, з кількома точками по боках, щоб його було видно
            void Mark(int index)
            {
                if (conflictAt == null) return;
                for (int i = Math.Max(index - 3, 0); i <= Math.Min(index + 3, count - 1); i++) conflictAt.Add(i);
            }

            // Секції покладеної колії поблизу маршруту: з чанків, яких він торкається, із запасом
            const int chunkSize = 32;
            var chunks = new HashSet<Vec3i>();
            for (int i = 0; i < count; i += 2)
            {
                Vec3d p = points[i].position;
                for (int dx = -4; dx <= 4; dx += 8)
                {
                    for (int dy = -1; dy <= 1; dy += 2)
                    {
                        for (int dz = -4; dz <= 4; dz += 8)
                        {
                            chunks.Add(new Vec3i((int)Math.Floor((p.X + dx) / chunkSize), (int)Math.Floor((p.Y + dy * (CrossingClearance + 1)) / chunkSize),
                                (int)Math.Floor((p.Z + dz) / chunkSize)));
                        }
                    }
                }
            }
            var candidates = new List<Section>();
            foreach (Vec3i chunk in chunks)
            {
                IEnumerable<Section> found = sectionsIn(chunk);
                if (found != null) candidates.AddRange(found);
            }
            if (candidates.Count == 0) return false;

            // Відстань у плані вздовж маршруту від його початку
            double[] run = new double[count];
            for (int k = 1; k < count; k++)
            {
                Vec3d a = points[k - 1].position, b = points[k].position;
                run[k] = run[k - 1] + Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            }

            // Біля своїх стрілок маршрут лежить поверх колії, від якої відходить: це не перетин
            int ownUntil = -1, ownFrom = count;
            if (first.Track != null)
            {
                while (ownUntil + 1 < count && weight[ownUntil + 1] >= 1) ownUntil++;
            }
            if (last.Track != null)
            {
                while (ownFrom - 1 >= 0 && weight[ownFrom - 1] >= 1) ownFrom--;
            }

            double minAngle = MinCrossingAngleDeg * Math.PI / 180;
            var crossings = new List<Crossing>();
            for (int k = 0; k + 1 < count; k++)
            {
                Vec3d a = points[k].position, b = points[k + 1].position;
                bool atOwnSwitch = k + 1 <= ownUntil || k >= ownFrom;

                foreach (Section section in candidates)
                {
                    Vec3d c = section.FullStartPosition, d = section.FullEndPosition;
                    double rx = b.X - a.X, rz = b.Z - a.Z, sx = d.X - c.X, sz = d.Z - c.Z;
                    double denominator = rx * sz - rz * sx;
                    if (Math.Abs(denominator) < 1e-12) continue;
                    double t = ((c.X - a.X) * sz - (c.Z - a.Z) * sx) / denominator;
                    double u = ((c.X - a.X) * rz - (c.Z - a.Z) * rx) / denominator;
                    if (t < 0 || t >= 1 || u < 0 || u >= 1) continue;

                    // Самий початок і кінець маршруту: там він стикується з колією, яку продовжує
                    double at = run[k] + (run[k + 1] - run[k]) * t;
                    if (at < 0.3 || at > run[count - 1] - 0.3) continue;

                    Vec3d where = new Vec3d(a.X + rx * t, a.Y + (b.Y - a.Y) * t, a.Z + rz * t);
                    if (!CrossPlane(section, where, rx, rz, out double planeY, out _)) continue;
                    // Проїзд над колією або під нею
                    if (Math.Abs(planeY - where.Y) >= CrossingClearance) continue;

                    double cos = Math.Abs(rx * sx + rz * sz) / Math.Sqrt((rx * rx + rz * rz) * (sx * sx + sz * sz));
                    double angle = Math.Acos(GameMath.Clamp(cos, -1, 1));

                    if (atOwnSwitch)
                    {
                        // Колія, від якої маршрут відходить: він лежить у її площині і розходиться з нею
                        // під малим кутом. Усе інше тут це чужа колія під стрілкою або над нею
                        if (Math.Abs(planeY - where.Y) < 0.1 && angle < minAngle) continue;
                        conflict = conflict ?? "стрілка маршруту лягає на перетин з іншою колією";
                        Mark(k);
                        continue;
                    }
                    if (SwitchZone.InBody(section, resolve))
                    {
                        conflict = conflict ?? "маршрут перетинає стрілку";
                        Mark(k);
                        continue;
                    }

                    if (angle < minAngle)
                    {
                        conflict = conflict ?? "перетин з іншою колією гостріший за " + MinCrossingAngleDeg.ToString("0") + "°";
                        Mark(k);
                        continue;
                    }

                    // Вузол перетину тягнеться вздовж колії в обидва боки від місця, де перетнулися осі: на косому
                    // перетині на кілька блоків. На тіло стрілки він не накладається ніде, а не лише в цій секції
                    if (SwitchBodyWithin(section, where, CrossingHalf(Footprint.Width(section), Footprint.StandardWidth, angle), resolve))
                    {
                        conflict = conflict ?? "перетин надто близько до стрілки";
                        Mark(k);
                        continue;
                    }

                    crossings.Add(new Crossing { At = at, Section = section, Angle = angle, Where = where, Index = k,
                        Half = CrossingHalf(Footprint.StandardWidth, Footprint.Width(section), angle),
                        HalfOnTrack = CrossingHalf(Footprint.Width(section), Footprint.StandardWidth, angle) });
                }
            }
            if (crossings.Count == 0) return false;
            crossings.Sort((x, y) => x.At.CompareTo(y.At));

            // Вузол одного перетину не накладається на вузол іншого: кожен це вузол рівно двох колій.
            // Спершу два перетини самого маршруту, що стоять надто близько вздовж нього: сюди ж потрапляє
            // прохід крізь місце, де дві колії вже перетинаються
            for (int i = 0; i + 1 < crossings.Count; i++)
            {
                Crossing near = crossings[i], far = crossings[i + 1];
                if (far.At - near.At >= near.Half + far.Half) continue;

                conflict = conflict ?? "два перетини надто близько один до одного";
                Mark(near.Index);
                Mark(far.Index);
            }
            // Потім перетин, поруч з яким на тій самій колії вже є перетин з іншою, якої маршрут не зачіпає
            foreach (Crossing crossing in crossings)
            {
                if (!NearLaidCrossing(crossing, candidates, resolve)) continue;

                conflict = conflict ?? "перетин надто близько до іншого перетину";
                Mark(crossing.Index);
            }

            // Власні висоти маршруту, якими вони були до вирівнювання
            double[] original = new double[count];
            for (int i = 0; i < count; i++) original[i] = points[i].position.Y;

            // Спершу всі перетини кладуться у площини своїх колій, потім до них ведуться переходи
            var zones = new List<int[]>();
            foreach (Crossing crossing in crossings)
            {
                // Поки колії перекриваються, і ще трохи: далі від точки перетину осей, якщо кут гостріший
                double half = crossing.Half + CrossingMargin;

                int from = -1, to = -1;
                for (int i = 0; i < count; i++)
                {
                    if (Math.Abs(run[i] - crossing.At) > half) continue;

                    Vec3d p = points[i].position;
                    Vec3f travel = points[i].tangent;
                    Section plane = NearestInChain(crossing.Section, p, resolve);
                    if (!CrossPlane(plane, p, travel.X, travel.Z, out double planeY, out double cant)) continue;

                    if (weight[i] > 0)
                    {
                        // Тут уже своя жорстка умова: стрілка маршруту або інший перетин
                        if (weight[i] < 1 || Math.Abs(p.Y - planeY) > HeightTolerance)
                        {
                            conflict = conflict ?? "перетин надто близько до стрілки маршруту або до іншого перетину на іншій висоті";
                            Mark(i);
                        }
                    }
                    else
                    {
                        PointOnBezierCurve point = points[i];
                        point.position = new Vec3d(p.X, planeY, p.Z);
                        points[i] = point;
                        weight[i] = 1;
                        trackCant[i] = cant;
                    }

                    if (from < 0) from = i;
                    to = i;
                }
                if (from >= 0) zones.Add(new[] { from, to });
            }

            var routePoints = new HashSet<int>(joints);
            foreach (int[] zone in zones)
            {
                string before = conflict;
                Ramp(points, zone[0], -1, original, run, weight, trackCant, routePoints, ref conflict);
                Ramp(points, zone[1], 1, original, run, weight, trackCant, routePoints, ref conflict);
                if (conflict != before)
                {
                    Mark(zone[0]);
                    Mark(zone[1]);
                }
            }
            return true;
        }

        /// <summary>
        /// Скільки блоків уздовж колії потрібно, щоб плавно змінити висоту на lift блоків і не зламати колію
        /// крутіше за найменший радіус. Плавний перехід між двома рівними ділянками найкрутіший на своїх
        /// кінцях, і радіус там дорівнює квадрату довжини, поділеному на шість різниць висот.
        /// </summary>
        internal static double RampRoom(double lift)
        {
            return Math.Sqrt(6 * lift * RouteRules.MinRadius);
        }

        /// <summary>
        /// Перехід по висоті від краю перетину edge; direction це куди від перетину, 1 або -1.
        /// Веде він до найближчої в цей бік точки маршруту (routePoints це їхні місця в нарізці), одним плавним
        /// відрізком: так, ніби край перетину сам був точкою, через яку маршрут проходить. Якби натомість
        /// повертати маршрут до його власної висоти за кілька блоків, він пірнав би до чужої колії і одразу
        /// ліз назад: власна висота нічого не знає про перетин.
        /// Точка маршруту, ближча за довжину, потрібну на таку різницю висот, не зупиняє перехід: він іде
        /// крізь неї до наступної.
        /// </summary>
        private static void Ramp(List<PointOnBezierCurve> points, int edge, int direction, double[] original, double[] run,
            double[] weight, double[] trackCant, HashSet<int> routePoints, ref string conflict)
        {
            int count = points.Count;
            double lift = Math.Abs(points[edge].position.Y - original[edge]);
            double want = Math.Max(GameMath.Clamp(lift * CrossingRampPerBlock, CrossingRampMin, CrossingRampMax), RampRoom(lift));

            // Якщо раніше за точку маршруту трапиться ділянка з власною жорсткою висотою (стрілка,
            // інший перетин), перехід веде до неї
            int target = edge, hit = -1;
            for (int i = edge + direction; i >= 0 && i < count; i += direction)
            {
                if (weight[i] > 0)
                {
                    hit = i;
                    break;
                }
                target = i;
                if (routePoints.Contains(i) && Math.Abs(run[i] - run[edge]) >= want) break;
            }

            if (hit >= 0)
            {
                if (hit == edge + direction) return;
                if (weight[hit] < 1 || Math.Abs(hit - edge) < 2)
                {
                    if (weight[hit] < 1 || Math.Abs(points[hit].position.Y - points[edge].position.Y) > HeightTolerance * 5)
                        conflict = conflict ?? "між перетином і стрілкою чи іншим перетином не лишається місця на перехід по висоті";
                    return;
                }
                // Перехід між двома жорсткими висотами займає стільки місця, скільки є. Якщо його замало,
                // колія зламається по висоті: кажемо про це тут, де відома причина
                // Рейка проходить більшу різницю висот, ніж вісь, якщо полотна двох колій нахилені по-різному:
                // до різниці висот осі додається те, на скільки рейку піднімає чи опускає перекіс
                double step = Math.Abs(points[hit].position.Y - points[edge].position.Y)
                    + TrackGauge.StandardWidth / 2 * Math.Abs(Math.Sin(trackCant[hit]) - Math.Sin(trackCant[edge]));
                if (step > HeightTolerance && Math.Abs(run[hit] - run[edge]) < RampRoom(step))
                    conflict = conflict ?? "між перетином і стрілкою чи іншим перетином не лишається місця на перехід по висоті";

                int low = Math.Min(edge, hit), high = Math.Max(edge, hit);
                Transition(points, low, high, VirtualSlope(points, run, low, 1), VirtualSlope(points, run, high, -1), run, weight, trackCant, 2);
                return;
            }

            if (Math.Abs(target - edge) < 2) return;
            if (direction > 0)
            {
                Transition(points, edge, target, VirtualSlope(points, run, edge, 1), SlopeAlong(points[target].tangent, 1), run, weight, trackCant, 0);
            }
            else
            {
                Transition(points, target, edge, SlopeAlong(points[target].tangent, 1), VirtualSlope(points, run, edge, -1), run, weight, trackCant, 1);
            }
        }

        // Чи вздовж колії, не далі ніж за reach блоків від точки where на секції section, починається тіло стрілки
        private static bool SwitchBodyWithin(Section section, Vec3d where, double reach, Func<SectionLink, Section> resolve)
        {
            for (int end = 0; end < 2; end++)
            {
                Section current = section;
                bool exitAtStart = end == 0;
                // Скільки пройдено вздовж колії до початку поточної секції, якщо йти від where
                double passed = -PlanLength(where, current.GetEndPosition(!exitAtStart));

                for (int hop = 0; hop < 64 && current != null && passed <= reach; hop++)
                {
                    if (SwitchZone.InBody(current, resolve)) return true;
                    passed += PlanLength(current.GetEndPosition(!exitAtStart), current.GetEndPosition(exitAtStart));

                    List<SectionLink> links = current.GetLinks(exitAtStart);
                    // Колія розходиться: це стик стрілки, і її тіло починається просто за ним
                    if (links.Count >= 2) return passed <= reach;
                    if (links.Count != 1 || resolve == null) break;
                    exitAtStart = !links[0].AtStart;
                    current = resolve(links[0]);
                }
            }
            return false;
        }

        // Чи на колії, яку маршрут перетинає, поруч уже є перетин з іншою колією: так близько, що два вузли
        // наклалися б. Іде по колії від місця перетину в обидва боки і шукає, що її перетинає
        private static bool NearLaidCrossing(Crossing crossing, List<Section> candidates, Func<SectionLink, Section> resolve)
        {
            // Найдовший вузол, який може лежати на цій колії: під найгострішим дозволеним кутом
            double length = Footprint.Width(crossing.Section);
            double reach = crossing.HalfOnTrack + CrossingHalf(length, length, MinCrossingAngleDeg * Math.PI / 180);

            for (int end = 0; end < 2; end++)
            {
                Section current = crossing.Section;
                bool exitAtStart = end == 0;
                // Скільки пройдено вздовж колії до початку поточної секції, якщо йти від місця перетину
                Vec3d exit = current.GetEndPosition(exitAtStart);
                double passed = -PlanLength(crossing.Where, current.GetEndPosition(!exitAtStart));

                for (int hop = 0; hop < 64 && current != null; hop++)
                {
                    Vec3d from = current.GetEndPosition(!exitAtStart);
                    exit = current.GetEndPosition(exitAtStart);

                    foreach (Section other in candidates)
                    {
                        if (other == current || !Crosses(from, exit, other, out double t, out double angle)) continue;

                        double away = passed + PlanLength(from, exit) * t;
                        // Сам маршрут перетинає колію в нулі; до нього вузол не рахується, лише від нього
                        if (away <= 1e-6) continue;
                        if (Math.Abs(other.GetGlobalPos().Y - current.GetGlobalPos().Y) >= CrossingClearance) continue;
                        if (away < crossing.HalfOnTrack + CrossingHalf(Footprint.Width(current), Footprint.Width(other), angle)) return true;
                    }

                    passed += PlanLength(from, exit);
                    if (passed > reach) break;

                    List<SectionLink> links = current.GetLinks(exitAtStart);
                    if (links.Count != 1 || resolve == null) break;
                    Section next = resolve(links[0]);
                    exitAtStart = !links[0].AtStart;
                    current = next;
                }
            }
            return false;
        }

        // Чи відрізок від from до to перетинає в плані вісь секції, і де: t це частка відрізка, angle кут між ними.
        // Спільний кінець двох секцій однієї колії і спільний початок двох гілок стрілки це не перетин
        internal static bool Crosses(Vec3d from, Vec3d to, Section other, out double t, out double angle)
        {
            t = 0;
            angle = 0;
            Vec3d c = other.FullStartPosition, d = other.FullEndPosition;
            double rx = to.X - from.X, rz = to.Z - from.Z, sx = d.X - c.X, sz = d.Z - c.Z;
            double denominator = rx * sz - rz * sx;
            if (Math.Abs(denominator) < 1e-12) return false;

            t = ((c.X - from.X) * sz - (c.Z - from.Z) * sx) / denominator;
            double u = ((c.X - from.X) * rz - (c.Z - from.Z) * rx) / denominator;
            const double edge = 1e-4;
            if (t <= edge || t >= 1 - edge || u <= edge || u >= 1 - edge)
            {
                // На самому стику секцій перетин належить рівно одній з них: тій, де він на початку
                bool onJoint = (t > -edge && t <= edge && u > edge && u < 1 - edge) || (u > -edge && u <= edge && t > edge && t < 1 - edge);
                if (!onJoint) return false;
            }

            double cos = Math.Abs(rx * sx + rz * sz) / Math.Sqrt((rx * rx + rz * rz) * (sx * sx + sz * sz));
            angle = Math.Acos(GameMath.Clamp(cos, -1, 1));
            return true;
        }

        private static double PlanLength(Vec3d a, Vec3d b)
        {
            return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
        }

        // Секція тієї самої колії, що й section, найближча в плані до точки: площина колії міняється вздовж неї
        private static Section NearestInChain(Section section, Vec3d p, Func<SectionLink, Section> resolve)
        {
            Section best = section;
            double bestDistance = PlanDistance(section, p);
            if (resolve == null) return best;

            for (int end = 0; end < 2; end++)
            {
                Section current = section;
                bool exitAtStart = end == 0;
                for (int hop = 0; hop < 12; hop++)
                {
                    List<SectionLink> links = current.GetLinks(exitAtStart);
                    if (links.Count != 1) break;
                    Section next = resolve(links[0]);
                    if (next == null) break;

                    double distance = PlanDistance(next, p);
                    if (distance < bestDistance)
                    {
                        best = next;
                        bestDistance = distance;
                    }
                    else if (distance > bestDistance + 1) break;

                    current = next;
                    exitAtStart = !links[0].AtStart;
                }
            }
            return best;
        }

        private static double PlanDistance(Section section, Vec3d p)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            double mx = (start.X + end.X) / 2 - p.X, mz = (start.Z + end.Z) / 2 - p.Z;
            return Math.Sqrt(mx * mx + mz * mz);
        }

        // Висота площини секції над точкою і нахил цієї площини для маршруту, що йде по ній у напрямку
        // (travelX, travelZ): наскільки вона піднімається ліворуч від руху. Площина та сама, якою секція
        // займає блоки: через середину хорди, з ухилом хорди і нахилом полотна. Маршрут іде не конче вздовж
        // колії: на перетині він іде навскоси, на стрілці поступово відвертає, тож нахил полотна в нього свій.
        // На схилі це видно навіть без нахилу полотна в самої колії: що більше маршрут відвернув,
        // то більше її ухил стає для нього нахилом упоперек
        internal static bool CrossPlane(Section section, Vec3d p, double travelX, double travelZ, out double planeY, out double cant)
        {
            planeY = p.Y;
            cant = 0;
            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;
            Vec3d chord = new Vec3d(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            if (chord.Length() < 1e-9) return false;
            chord.Normalize();

            Vec3f normal = section.CenterNormal;
            Vec3d side = new Vec3d(normal.X, normal.Y, normal.Z);
            side.Sub(chord.Clone().Mul(side.Dot(chord)));
            if (side.Length() < 1e-9) return false;
            side.Normalize();

            Vec3d up = side.Cross(chord);
            if (Math.Abs(up.Y) < 0.2) return false;

            double mx = (start.X + end.X) / 2, my = (start.Y + end.Y) / 2, mz = (start.Z + end.Z) / 2;
            planeY = my - (up.X * (p.X - mx) + up.Z * (p.Z - mz)) / up.Y;

            double length = Math.Sqrt(travelX * travelX + travelZ * travelZ);
            if (length < 1e-9) return true;
            double leftX = -travelZ / length, leftZ = travelX / length;
            cant = Math.Atan(-(up.X * leftX + up.Z * leftZ) / up.Y);
            return true;
        }

        // Крива між двома сусідніми точками маршруту, нарізана на точки; null, якщо точки надто близько
        private static List<PointOnBezierCurve> Leg(RoutePoint a, RoutePoint b)
        {
            double span = a.Position.DistanceTo(b.Position);
            if (span < MinSpan) return null;

            double handle = span * HandleFactor(a.Tangent, b.Tangent);
            CubicBezierCurve3d curve = new CubicBezierCurve3d(
                a.Position.X, a.Position.Y, a.Position.Z,
                a.Position.X + a.Tangent.X * handle, a.Position.Y + a.Tangent.Y * handle, a.Position.Z + a.Tangent.Z * handle,
                b.Position.X - b.Tangent.X * handle, b.Position.Y - b.Tangent.Y * handle, b.Position.Z - b.Tangent.Z * handle,
                b.Position.X, b.Position.Y, b.Position.Z,
                1, Weight, Weight, 1);

            List<PointOnBezierCurve> points = curve.CutIntoEqualPieces(PieceLength);

            // Нарізка інколи дає зайву точку впритул до кінцевої. Тут це зсунуло б усі наступні секції
            // на пів секції, тому таку точку прибираємо, і точок у кривої завжди непарна кількість
            if (points.Count % 2 == 0 && points.Count >= 2) points.RemoveAt(points.Count - 2);
            return points.Count < 3 ? null : points;
        }

        // Що відомо про стрілку на одному з кінців маршруту після проходу вздовж основної колії
        private class Follow
        {
            // Перша точка маршруту, яка вже не лежить на основній колії; -1, якщо маршрут з неї так і не зійшов
            public int Exit = -1;
            // Остання точка, що лежить у площині основної колії. Після ділянки «ще кілька секцій тією ж площиною»
            // це і є невидима опорна точка, від якої починається перехід
            public int Virtual;
            // Секція основної колії, над якою маршрут зійшов: її площина продовжується далі
            public Section Plane;
        }

        /// <summary>
        /// Веде кінці маршруту, що стоять на стрілках, поверх уже покладеної колії. Куди маршрут іде в плані,
        /// тут не міняється: кут розходження вибирає гравець. Міняються висота і нахил полотна, у три кроки.
        /// 1. Від стрілки точка за точкою і паралельно секція за секцією по основній колії. Поки вісь маршруту
        ///    лежить у габариті основної колії (Footprint), це ще стрілка;
        ///    точка бере висоту з площини тієї секції, над якою вона опинилася, і її нахил полотна.
        /// 2. Зійшовши, маршрут іще HoldSections секцій іде площиною останньої секції основної колії.
        ///    У кінці цієї ділянки стоїть невидима опорна точка.
        /// 3. Від опорної точки висота гладкою кривою веде до найближчої справжньої точки маршруту.
        ///    Якщо стрілки на обох кінцях, а справжніх точок між ними немає, крива одна:
        ///    від опорної точки однієї стрілки до опорної точки іншої.
        /// Обидві стрілки рахуються разом, бо ціль переходу однієї залежить від того, де закінчується інша.
        /// </summary>
        private static bool FollowTracks(List<PointOnBezierCurve> points, RoutePoint first, RoutePoint last,
            Func<SectionLink, Section> resolve, double[] weight, double[] trackCant, List<int> joints, ref string conflict)
        {
            int count = points.Count;

            Follow start = first.Track == null ? null : FollowOnTrack(points, true, first.Track, first.TrackJointAtStart, resolve, weight, trackCant, ref conflict);
            Follow end = last.Track == null ? null : FollowOnTrack(points, false, last.Track, last.TrackJointAtStart, resolve, weight, trackCant, ref conflict);
            if (start == null && end == null) return false;

            // Маршрут, що з якоїсь колії так і не зійшов, увесь лежить на ній: переходити нема куди
            if ((start != null && start.Exit < 0) || (end != null && end.Exit < 0)) return true;

            // Відстань у плані вздовж маршруту від його початку
            double[] run = new double[count];
            for (int k = 1; k < count; k++)
            {
                Vec3d a = points[k - 1].position, b = points[k].position;
                run[k] = run[k - 1] + Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            }

            // Вільна частина маршруту: між місцями, де він зійшов з основних колій
            int freeFrom = start != null ? start.Exit : 0;
            int freeTo = end != null ? end.Exit : count - 1;

            if (freeFrom > freeTo)
            {
                // Стрілки на обох кінцях, і ділянки поверх двох колій зустрілися або перекрилися.
                // Де вони вимагали різної висоти, це вже записано; якщо ж висоти зійшлися, маршрут годиться
                if (Math.Abs(points[start.Virtual].position.Y - points[Math.Min(start.Virtual + 1, count - 1)].position.Y) > 0.5)
                    conflict = conflict ?? "між двома стрілками не лишається місця на перехід по висоті";
                return true;
            }

            // Точки маршруту у вільній частині, до яких веде перехід по висоті від кожної стрілки.
            // Беремо не просто найближчу: точки можуть стояти густо (початок і кінець кожної дуги теж точки),
            // і перехід до точки за два блоки від стрілки вийшов би горбом. Потрібна перша точка, до якої
            // вистачає місця: ділянка площиною основної колії плюс довжина переходу, що залежить від різниці висот
            double holdNeeded = HoldSections * PieceLength * 2;
            int nearStart = -1, nearEnd = -1;
            int firstFree = -1, lastFree = -1;
            foreach (int joint in joints)
            {
                if (joint < freeFrom || joint > freeTo) continue;
                if (start != null && joint == 0) continue;
                if (end != null && joint == count - 1) continue;
                if (firstFree < 0 || joint < firstFree) firstFree = joint;
                if (joint > lastFree) lastFree = joint;

                if (start != null)
                {
                    double ramp = GameMath.Clamp(Math.Abs(points[joint].position.Y - points[start.Virtual].position.Y) * RampPerBlock, RampMin, RampMax);
                    if (run[joint] - run[freeFrom] >= holdNeeded + ramp && (nearStart < 0 || joint < nearStart)) nearStart = joint;
                }
                if (end != null)
                {
                    double ramp = GameMath.Clamp(Math.Abs(points[joint].position.Y - points[end.Virtual].position.Y) * RampPerBlock, RampMin, RampMax);
                    if (run[freeTo] - run[joint] >= holdNeeded + ramp && joint > nearEnd) nearEnd = joint;
                }
            }
            // Жодна точка не лежить достатньо далеко: беремо найдальшу, яка є
            if (start != null && nearStart < 0) nearStart = lastFree;
            if (end != null && nearEnd < 0) nearEnd = firstFree;
            // Стрілки на обох кінцях, і їхні переходи зайшли б один за одного: тоді перехід один, між стрілками
            if (start != null && end != null && nearStart >= 0 && nearEnd >= 0 && nearStart > nearEnd)
            {
                nearStart = -1;
                nearEnd = -1;
            }

            double holdLength = HoldSections * PieceLength * 2;

            if (start != null && end != null && nearStart < 0)
            {
                // Між двома стрілками жодної справжньої точки: один перехід від опорної точки до опорної
                double room = run[freeTo] - run[freeFrom];
                double hold = Math.Min(holdLength, room / 3);
                HoldPlane(points, start, 1, hold, freeTo, run, weight, trackCant, ref conflict);
                HoldPlane(points, end, -1, hold, start.Virtual, run, weight, trackCant, ref conflict);

                if (end.Virtual - start.Virtual < 2)
                {
                    if (Math.Abs(points[start.Virtual].position.Y - points[end.Virtual].position.Y) > HeightTolerance * 5)
                        conflict = conflict ?? "між двома стрілками не лишається місця на перехід по висоті";
                    return true;
                }

                Transition(points, start.Virtual, end.Virtual, VirtualSlope(points, run, start.Virtual, 1),
                    VirtualSlope(points, run, end.Virtual, -1), run, weight, trackCant, 2);
                return true;
            }

            if (start != null)
            {
                int target = nearStart >= 0 ? nearStart : count - 1;
                HoldPlane(points, start, 1, Math.Min(holdLength, (run[target] - run[freeFrom]) / 2), target, run, weight, trackCant, ref conflict);

                if (target - start.Virtual >= 2)
                {
                    Transition(points, start.Virtual, target, VirtualSlope(points, run, start.Virtual, 1),
                        SlopeAlong(points[target].tangent, 1), run, weight, trackCant, 0);
                }
                else if (Math.Abs(points[start.Virtual].position.Y - points[target].position.Y) > HeightTolerance * 5)
                {
                    conflict = conflict ?? "стрілка і наступна точка маршруту на різній висоті, а місця на перехід немає";
                }
            }

            if (end != null)
            {
                int target = nearEnd >= 0 ? nearEnd : 0;
                HoldPlane(points, end, -1, Math.Min(holdLength, (run[freeTo] - run[target]) / 2), target, run, weight, trackCant, ref conflict);

                if (end.Virtual - target >= 2)
                {
                    Transition(points, target, end.Virtual, SlopeAlong(points[target].tangent, 1),
                        VirtualSlope(points, run, end.Virtual, -1), run, weight, trackCant, 1);
                }
                else if (Math.Abs(points[end.Virtual].position.Y - points[target].position.Y) > HeightTolerance * 5)
                {
                    conflict = conflict ?? "стрілка і попередня точка маршруту на різній висоті, а місця на перехід немає";
                }
            }

            return true;
        }

        // Крок 1: від стрілки поверх основної колії, поки маршрут із неї не зійде
        private static Follow FollowOnTrack(List<PointOnBezierCurve> points, bool fromStart, Section track, bool jointAtStart,
            Func<SectionLink, Section> resolve, double[] weight, double[] trackCant, ref string conflict)
        {
            int count = points.Count;
            int anchor = fromStart ? 0 : count - 1;
            int step = fromStart ? 1 : -1;

            Follow follow = new Follow { Virtual = anchor, Plane = track };
            Section current = track;
            bool enteredAtStart = jointAtStart;
            Section behind = null;

            for (int i = anchor, guard = 0; i >= 0 && i < count; i += step, guard++)
            {
                if (guard >= MaxFollowPoints)
                {
                    conflict = conflict ?? "маршрут надто довго йде поверх уже покладеної колії";
                    break;
                }

                Vec3d p = points[i].position;

                // Переходимо на наступну секцію основної колії, поки точка лежить за кінцем поточної
                bool ended = false;
                for (int hop = 0; hop < 8; hop++)
                {
                    if (Along(current, enteredAtStart, p, out double length, out _) <= length + 1e-6) break;

                    SectionLink link = current.GetActiveLink(!enteredAtStart);
                    Section next = link == null || resolve == null ? null : resolve(link);
                    if (next == null)
                    {
                        ended = true;
                        break;
                    }
                    behind = current;
                    current = next;
                    enteredAtStart = link.AtStart;
                }

                // Край стрілки там, де габарити двох колій перестають перекриватися. Маршрут тут це шматок
                // від цієї точки нарізки до сусідньої; основна колія це секція навпроти і її сусіди
                int neighbour = i + step >= 0 && i + step < count ? i + step : i - step;
                Vec3d q = points[neighbour].position;
                SectionLink aheadLink = current.GetActiveLink(!enteredAtStart);
                Section ahead = aheadLink == null || resolve == null ? null : resolve(aheadLink);
                bool lies = OnFootprint(p, q, current) || (behind != null && OnFootprint(p, q, behind)) || (ahead != null && OnFootprint(p, q, ahead));

                if (ended || !lies)
                {
                    follow.Exit = i;
                    break;
                }

                follow.Plane = current;
                if (!PutOnPlane(points, i, current, weight, trackCant, ref conflict)) break;
                follow.Virtual = i;
            }

            return follow;
        }

        // Чи шматок маршруту між двома точками нарізки лежить у габариті секції покладеної колії
        private static bool OnFootprint(Vec3d from, Vec3d to, Section section)
        {
            return Footprint.Overlap(from, to, Footprint.StandardWidth, section.FullStartPosition, section.FullEndPosition, Footprint.Width(section), out _, out _);
        }

        // Крок 2: ще трохи площиною останньої секції основної колії. step це напрямок від стрілки,
        // length скільки блоків так іти, stop точка, до якої не доходити
        private static void HoldPlane(List<PointOnBezierCurve> points, Follow follow, int step, double length, int stop,
            double[] run, double[] weight, double[] trackCant, ref string conflict)
        {
            if (follow.Exit < 0 || length <= 0) return;

            for (int i = follow.Exit; i >= 0 && i < points.Count && (stop - i) * step > 0; i += step)
            {
                if (Math.Abs(run[i] - run[follow.Exit]) > length) break;
                if (!PutOnPlane(points, i, follow.Plane, weight, trackCant, ref conflict)) break;
                follow.Virtual = i;
            }
        }

        // Ухил маршруту в опорній точці, за рухом по маршруту. Береться з сусідньої точки з боку стрілки,
        // яка вже лежить у тій самій площині; away це напрямок від стрілки
        private static double VirtualSlope(List<PointOnBezierCurve> points, double[] run, int index, int away)
        {
            int neighbour = index - away;
            if (neighbour < 0 || neighbour >= points.Count || Math.Abs(run[index] - run[neighbour]) < 1e-9)
                return SlopeAlong(points[index].tangent, 1);

            return (points[index].position.Y - points[neighbour].position.Y) / (run[index] - run[neighbour]);
        }

        /// <summary>
        /// Крок 3: висота точок між from і to. Це кубічна крива з заданими висотою й ухилом на обох кінцях,
        /// така сама за змістом, як крива між двома точками маршруту. Самі from і to не міняються.
        /// kind каже, де площина колії: 0 на початку (нахил полотна згасає від неї до власного),
        /// 1 в кінці (наростає до неї), 2 на обох кінцях (переходить від однієї колії до іншої).
        /// </summary>
        private static void Transition(List<PointOnBezierCurve> points, int from, int to, double fromSlope, double toSlope,
            double[] run, double[] weight, double[] trackCant, int kind)
        {
            double span = run[to] - run[from];
            if (span < 1e-6) return;

            double fromY = points[from].position.Y, toY = points[to].position.Y;
            double fromCant = trackCant[from], toCant = trackCant[to];

            for (int i = from + 1; i < to; i++)
            {
                double u = GameMath.Clamp((run[i] - run[from]) / span, 0, 1);
                double u2 = u * u, u3 = u2 * u;
                double y = (2 * u3 - 3 * u2 + 1) * fromY + (u3 - 2 * u2 + u) * span * fromSlope
                    + (-2 * u3 + 3 * u2) * toY + (u3 - u2) * span * toSlope;

                PointOnBezierCurve point = points[i];
                Vec3d p = point.position;
                point.position = new Vec3d(p.X, y, p.Z);
                points[i] = point;

                double smooth = u2 * (3 - 2 * u);
                if (kind == 0)
                {
                    weight[i] = 1 - smooth;
                    trackCant[i] = fromCant;
                }
                else if (kind == 1)
                {
                    weight[i] = smooth;
                    trackCant[i] = toCant;
                }
                else
                {
                    weight[i] = 1;
                    trackCant[i] = fromCant + (toCant - fromCant) * smooth;
                }
            }
        }

        // Кладе точку на площину секції основної колії: висота з площини, нахил полотна такий, щоб шпала
        // маршруту лежала в цій площині
        private static bool PutOnPlane(List<PointOnBezierCurve> points, int i, Section section, double[] weight, double[] trackCant, ref string conflict)
        {
            Vec3d p = points[i].position;
            if (!CrossPlane(section, p, points[i].tangent.X, points[i].tangent.Z, out double planeY, out double cant)) return false;

            // Ділянки поверх двох різних колій можуть накластися. Обидві жорсткі: якщо вони хочуть
            // різної висоти в одній точці, виконати обидві не можна
            if (weight[i] >= 1)
            {
                if (Math.Abs(p.Y - planeY) > HeightTolerance)
                    conflict = conflict ?? "дві стрілки маршруту вимагають різної висоти в одному місці";
                return true;
            }

            PointOnBezierCurve point = points[i];
            point.position = new Vec3d(p.X, planeY, p.Z);
            points[i] = point;

            weight[i] = 1;
            trackCant[i] = cant;
            return true;
        }

        // Ухил дотичної в напрямку обходу: step 1 це за рухом по маршруту, -1 проти
        private static double SlopeAlong(Vec3f tangent, int step)
        {
            double length = Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z);
            return length < 1e-6 ? 0 : tangent.Y / length * step;
        }

        // Де точка відносно секції, якщо йти по ній від кінця, яким у неї зайшли: відстань уздовж хорди,
        // довжина хорди і відстань убік від неї. Усе в плані
        internal static double Along(Section section, bool enteredAtStart, Vec3d p, out double length, out double sideways)
        {
            Vec3d from = section.GetEndPosition(enteredAtStart);
            Vec3d to = section.GetEndPosition(!enteredAtStart);
            double cx = to.X - from.X, cz = to.Z - from.Z;
            length = Math.Sqrt(cx * cx + cz * cz);
            if (length < 1e-9)
            {
                sideways = 0;
                return 0;
            }

            double px = p.X - from.X, pz = p.Z - from.Z;
            sideways = Math.Abs(px * cz - pz * cx) / length;
            return (px * cx + pz * cz) / length;
        }

        // Після зміни висот ухил дотичних має відповідати новим висотам. Напрямок у плані лишається
        private static void RecomputeSlopes(List<PointOnBezierCurve> points)
        {
            int count = points.Count;
            var slopes = new double[count];

            for (int i = 0; i < count; i++)
            {
                Vec3d a = points[Math.Max(i - 1, 0)].position;
                Vec3d b = points[Math.Min(i + 1, count - 1)].position;
                double run = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
                slopes[i] = run > 1e-9 ? (b.Y - a.Y) / run : 0;
            }

            for (int i = 0; i < count; i++)
            {
                PointOnBezierCurve point = points[i];
                Vec3f t = point.tangent;
                double length = Math.Sqrt(t.X * t.X + t.Z * t.Z);
                if (length < 1e-6) continue;

                Vec3d tangent = new Vec3d(t.X / length, slopes[i], t.Z / length).Normalize();
                point.tangent = new Vec3f((float)tangent.X, (float)tangent.Y, (float)tangent.Z);
                points[i] = point;
            }
        }

        // Поверх основної колії нахил полотна такий, як у неї; далі він переходить у власний нахил маршруту
        // з тією самою вагою, що й висота. Нормаль точки будується так само, як у ModMath.ApplyCant
        private static void BlendCant(List<PointOnBezierCurve> points, double[] weight, double[] trackCant)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (weight[i] <= 0) continue;

                PointOnBezierCurve point = points[i];
                Vec3f t = point.tangent;
                double length = Math.Sqrt(t.X * t.X + t.Z * t.Z);
                if (length < 1e-6) continue;

                double own = Math.Asin(GameMath.Clamp(point.normal.Y, -1, 1));
                double cant = own + (trackCant[i] - own) * weight[i];

                double nx = -t.Z / length, nz = t.X / length;
                double ux = -nz * t.Y, uy = nz * t.X - nx * t.Z, uz = nx * t.Y;
                double cos = Math.Cos(cant), sin = Math.Sin(cant);

                point.normal = new Vec3f((float)(nx * cos + ux * sin), (float)(uy * sin), (float)(nz * cos + uz * sin));
                points[i] = point;
            }
        }
    }
}
