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
        public static List<PointOnBezierCurve> BuildPoints(List<RoutePoint> route, Func<SectionLink, Section> resolve, out string conflict)
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

                double span = a.Position.DistanceTo(b.Position);
                if (span < MinSpan) continue;

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
                if (points.Count < 3) continue;

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
            if (followed) RecomputeSlopes(all);

            ModMath.ApplyCant(all, first.Cant, last.Cant);
            if (followed) BlendCant(all, weight, trackCant);
            return all;
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
        ///    ближча до осі основної колії, ніж ширина колії, хоч одна рейка маршруту лежить між рейками
        ///    основної; точка бере висоту з площини тієї секції, над якою вона опинилася, і її нахил полотна.
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

            // Справжні точки маршруту у вільній частині: найближча до кожної стрілки
            int nearStart = -1, nearEnd = -1;
            foreach (int joint in joints)
            {
                if (joint < freeFrom || joint > freeTo) continue;
                if (start != null && joint == 0) continue;
                if (end != null && joint == count - 1) continue;
                if (nearStart < 0 || joint < nearStart) nearStart = joint;
                if (joint > nearEnd) nearEnd = joint;
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
                    current = next;
                    enteredAtStart = link.AtStart;
                }

                Along(current, enteredAtStart, p, out _, out double sideways);
                if (ended || sideways >= current.TrackWidth)
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

        // Кладе точку на площину секції основної колії: висота з площини, нахил полотна як у секції
        private static bool PutOnPlane(List<PointOnBezierCurve> points, int i, Section section, double[] weight, double[] trackCant, ref string conflict)
        {
            Vec3d p = points[i].position;
            if (!TrackPlane(section, p, points[i].tangent, out double planeY, out double cant)) return false;

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

        // Висота площини секції над точкою і нахил полотна секції в знаку маршруту, що йде в напрямку travel.
        // Площина та сама, якою секція займає блоки: через середину хорди, з ухилом хорди і нахилом полотна
        private static bool TrackPlane(Section section, Vec3d p, Vec3f travel, out double planeY, out double cant)
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

            // Нормаль маршруту дивиться ліворуч від напрямку руху, а нормаль секції може дивитися і в інший бік
            double sameSide = normal.X * -travel.Z + normal.Z * travel.X >= 0 ? 1 : -1;
            cant = Math.Asin(GameMath.Clamp(sameSide * normal.Y, -1, 1));
            return true;
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
