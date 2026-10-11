using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// З чого складається рейка у вузлі перетину. Ходова рейка: підошва, шийка, головка. Проставка між її
    /// шийкою і шийкою контррейки. Контррейка: підошва, шийка, головка.
    /// </summary>
    public enum RailPiece
    {
        Foot = 0,
        Web = 1,
        Head = 2,
        Spacer = 3,
        GuardFoot = 4,
        GuardWeb = 5,
        GuardHead = 6
    }

    /// <summary>
    /// Косий край шматка рейки: місце вздовж лінії рейки, де він проходить за EdgeHalf ліворуч і праворуч від
    /// її осі. Місце це номер перерізу лінії з дробовою частиною. Край іде вздовж рейки іншої колії, тому
    /// з двох боків рейки він на різній відстані.
    /// </summary>
    public class RailEdge
    {
        public double Minus, Plus;

        public double Middle => (Minus + Plus) / 2;

        /// <summary>Де край проходить за x блоків від осі рейки.</summary>
        public double At(double x)
        {
            return Minus + (Plus - Minus) * (x + CrossingGeometry.EdgeHalf) / (2 * CrossingGeometry.EdgeHalf);
        }

        /// <summary>Найближче до початку лінії і найдальше місце краю в межах усього, що лежить біля рейки.</summary>
        public double Nearest => Math.Min(At(-CrossingGeometry.Reach), At(CrossingGeometry.Reach));
        public double Farthest => Math.Max(At(-CrossingGeometry.Reach), At(CrossingGeometry.Reach));

        public static RailEdge Square(double at)
        {
            return new RailEdge { Minus = at, Plus = at };
        }

        /// <summary>Такий самий край, зсунутий уздовж лінії.</summary>
        public RailEdge Moved(double by)
        {
            return new RailEdge { Minus = Minus + by, Plus = Plus + by };
        }
    }

    /// <summary>
    /// Шматок від краю до краю. Торець потрібен там, де шматок обривається у повітрі; де він упирається
    /// в сусідню рейку або продовжується звичайною рейкою, торця немає.
    /// </summary>
    public class RailSpan
    {
        public RailEdge From, To;
        public bool CapFrom, CapTo;
    }

    /// <summary>
    /// Один бік однієї колії у вузлі перетину: ходова рейка з усім, що до неї прилягає. Кожну складову задано
    /// окремо, шматками вздовж рейки, бо обриваються вони в різних місцях: жолоб для реборди вирізаний лише
    /// в головці, а підошва кінчається раніше за шийку.
    /// </summary>
    public class CrossingRail
    {
        /// <summary>Котра з двох колій: 0 це основна.</summary>
        public int Track;
        /// <summary>З якого боку осі колії: +1 чи -1 уздовж Side її перерізів.</summary>
        public int Side;
        /// <summary>Перерізи осі ходової рейки вздовж колії, через чверть блока.</summary>
        public List<RailFrame> Frames;
        private List<RailSpan>[] pieces = new List<RailSpan>[CrossingGeometry.PieceCount];
        /// <summary>Перетнута секція колії і котра з двох її рейок лежить із цього боку: звідти метал.</summary>
        public Section Owner;
        public SectionPart OwnerPart;

        public CrossingRail()
        {
            for (int n = 0; n < pieces.Length; n++) pieces[n] = new List<RailSpan>();
        }

        /// <summary>Шматки складової вздовж рейки.</summary>
        public List<RailSpan> Of(RailPiece piece)
        {
            return pieces[(int)piece];
        }
    }

    /// <summary>Рейки перетину і звичайні рейки секцій, які вони заміняють.</summary>
    public class CrossingRails
    {
        public List<CrossingRail> Rails = new List<CrossingRail>();
        private HashSet<string> hidden = new HashSet<string>();

        public void Hide(Section section, SectionPart part)
        {
            hidden.Add(Key(section, part));
        }

        /// <summary>Чи цю рейку секції заміняє рейка перетину.</summary>
        public bool Hides(Section section, SectionPart part)
        {
            return hidden.Contains(Key(section, part));
        }

        private static string Key(Section section, SectionPart part)
        {
            Vec3i chunk = section.ChunkAddres;
            return chunk.X + "_" + chunk.Y + "_" + chunk.Z + "_" + section.IndexInChunk + "_" + (int)part;
        }
    }

    /// <summary>
    /// Рахує рейки глухого перетину двох колій. Рейки обох колій ідуть наскрізь; щоб реборди коліс проходили,
    /// у головках вирізані жолоби: реборда їде вздовж внутрішнього боку рейки, тож кожна рейка має жолоб
    /// там, де її перетинає внутрішній бік кожної рейки іншої колії. Під гострим кутом жолоби йдуть навскоси,
    /// і між ними лишається вістря, як осердя хрестовини стрілки.
    /// Рейки основної колії суцільні, жолоб у них лише в головці. Рейка другої колії на рейці основної
    /// обривається і підрізана по ній: головка впирається в головку, шийка доходить до шийки, підошва
    /// до підошви.
    /// Уздовж внутрішнього боку кожної ходової рейки через увесь вузол лежить проставка до низу головки: рейка
    /// там масивніша, а дно жолоба суцільне.
    /// Контррейки, як на залізниці, залежать від кута. Під кутом, близьким до прямого, вздовж внутрішнього
    /// боку кожної ходової рейки йде контррейка на відстані жолоба: починається перед ромбом, проходить
    /// крізь нього і кінчається за ним, а на рейках іншої колії обривається. На кожному плечі це дає рамку
    /// з двох контррейок і ходової рейки, без проміжків між ними, а всередині ромба раму. У ромбі під гострим
    /// кутом контррейки стоять лише у двох тупих кутах, зігнуті вздовж обох рейок: у гострих рейки сходяться
    /// вістрям.
    /// Чиста геометрія.
    /// </summary>
    public static class CrossingGeometry
    {
        public const int PieceCount = 7;

        /// <summary>На якій відстані від осі рейки заміряно її косий край: пів ширини підошви.</summary>
        public const double EdgeHalf = RailProfile.FootHalfWidth;

        /// <summary>Довжина контррейки від місця, де вона починається біля рейки іншої колії, блоків.</summary>
        public const double GuardLeg = 0.5;

        // Від осі ходової рейки до осі контррейки: головка, жолоб і ще пів головки з кожного боку
        private const double GuardAxis = 2 * RailProfile.HeadHalfWidth + SwitchGeometry.FrogFlangeway;

        /// <summary>Як далеко від осі ходової рейки сягає те, що лежить біля неї: до краю підошви контррейки.</summary>
        public const double Reach = GuardAxis + RailProfile.FootHalfWidth;

        /// <summary>
        /// Прямокутник складової в перерізі: лівий і правий край уздовж Side від осі ходової рейки, низ і верх
        /// над низом підошви, блоків. side це з якого боку осі колії лежить рейка: проставка і контррейка
        /// стоять з протилежного, внутрішнього.
        /// </summary>
        public static double[] Box(RailPiece piece, int side)
        {
            double foot = RailProfile.FootHalfWidth, web = RailProfile.WebHalfWidth, head = RailProfile.HeadHalfWidth;
            // Від осі рейки в бік осі колії
            double from, to, bottom, top;
            switch (piece)
            {
                case RailPiece.Foot: from = -foot; to = foot; bottom = 0; top = RailProfile.FootTop; break;
                case RailPiece.Web: from = -web; to = web; bottom = RailProfile.FootTop; top = RailProfile.HeadBottom; break;
                case RailPiece.Head: from = -head; to = head; bottom = RailProfile.HeadBottom; top = RailProfile.Top; break;
                case RailPiece.Spacer: from = web; to = GuardAxis - web; bottom = RailProfile.FootTop; top = RailProfile.HeadBottom; break;
                case RailPiece.GuardFoot: from = GuardAxis - foot; to = GuardAxis + foot; bottom = 0; top = RailProfile.FootTop; break;
                case RailPiece.GuardWeb: from = GuardAxis - web; to = GuardAxis + web; bottom = RailProfile.FootTop; top = RailProfile.HeadBottom; break;
                default: from = GuardAxis - head; to = GuardAxis + head; bottom = RailProfile.HeadBottom; top = RailProfile.Top; break;
            }
            return side > 0 ? new[] { -to, -from, bottom, top } : new[] { from, to, bottom, top };
        }

        /// <summary>
        /// Зовнішній контур фігури, складеної з прямокутників (лівий край, правий, низ, верх): те, що видно
        /// в перерізі, без жодної лінії всередині. Кожен контур замкнений, остання точка повторює першу; іде
        /// від лівого нижнього кута вгору, далі верхом праворуч і низом назад. Окремих фігур і порожнин
        /// усередині може бути кілька, тоді й контурів кілька.
        /// </summary>
        public static List<List<double[]>> Outline(IList<double[]> boxes)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (double[] box in boxes)
            {
                Mark(xs, box[0]);
                Mark(xs, box[1]);
                Mark(ys, box[2]);
                Mark(ys, box[3]);
            }
            xs.Sort();
            ys.Sort();

            int columns = xs.Count - 1, rows = ys.Count - 1;
            var filled = new bool[Math.Max(columns, 0), Math.Max(rows, 0)];
            for (int i = 0; i < columns; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    double x = (xs[i] + xs[i + 1]) / 2, y = (ys[j] + ys[j + 1]) / 2;
                    foreach (double[] box in boxes)
                    {
                        if (x > box[0] && x < box[1] && y > box[2] && y < box[3]) filled[i, j] = true;
                    }
                }
            }

            // Межа між заповненою клітинкою і порожньою, спрямована так, щоб фігура лишалася праворуч
            var next = new Dictionary<long, List<long>>();
            long Corner(int i, int j) => (long)i * 100000 + j;
            void Side(int fromI, int fromJ, int toI, int toJ)
            {
                long from = Corner(fromI, fromJ);
                if (!next.TryGetValue(from, out List<long> ends)) next[from] = ends = new List<long>();
                ends.Add(Corner(toI, toJ));
            }
            bool Filled(int i, int j) => i >= 0 && j >= 0 && i < columns && j < rows && filled[i, j];

            var starts = new List<long>();
            for (int i = 0; i < columns; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    if (!filled[i, j]) continue;
                    if (!Filled(i - 1, j))
                    {
                        Side(i, j, i, j + 1);
                        starts.Add(Corner(i, j));
                    }
                    if (!Filled(i, j + 1)) Side(i, j + 1, i + 1, j + 1);
                    if (!Filled(i + 1, j)) Side(i + 1, j + 1, i + 1, j);
                    if (!Filled(i, j - 1)) Side(i + 1, j, i, j);
                }
            }

            var loops = new List<List<double[]>>();
            // Починаємо з лівого краю, знизу: клітинки перебиралися зліва направо і знизу вгору
            foreach (long start in starts)
            {
                if (!next.TryGetValue(start, out List<long> first) || first.Count == 0) continue;

                var corners = new List<long> { start };
                long current = start;
                for (int guard = 0; guard < 10000; guard++)
                {
                    List<long> ends = next[current];
                    long to = ends[ends.Count - 1];
                    ends.RemoveAt(ends.Count - 1);
                    if (to == start) break;
                    corners.Add(to);
                    current = to;
                    if (!next.TryGetValue(current, out List<long> onward) || onward.Count == 0) break;
                }

                // Точка посеред прямої сторони контуру нічого не додає
                var loop = new List<double[]>();
                for (int n = 0; n < corners.Count; n++)
                {
                    long before = corners[(n + corners.Count - 1) % corners.Count], at = corners[n], after = corners[(n + 1) % corners.Count];
                    bool straight = n > 0 && ((before / 100000 == at / 100000 && at / 100000 == after / 100000) || (before % 100000 == at % 100000 && at % 100000 == after % 100000));
                    if (!straight) loop.Add(new[] { xs[(int)(at / 100000)], ys[(int)(at % 100000)] });
                }
                loop.Add(new[] { loop[0][0], loop[0][1] });
                loops.Add(loop);
            }
            return loops;
        }

        private static void Mark(List<double> values, double value)
        {
            foreach (double known in values)
            {
                if (Math.Abs(known - value) < 1e-9) return;
            }
            values.Add(value);
        }

        // Вісь колії: перерізи через чверть блока
        private class Axis
        {
            public List<RailFrame> Frames = new List<RailFrame>();
            // Чи перша рейка секції лежить з боку Side перерізів: нормалі сусідніх секцій можуть дивитися в різні боки
            public List<bool> FirstOnSide = new List<bool>();
        }

        /// <summary>
        /// tracks це секції двох колій по порядку крізь вузол, перша основна; entered каже, котрим кінцем
        /// у кожну секцію заїжджають, рухаючись у цьому порядку; centers номери (з нуля) перетнутих секцій.
        /// null, якщо рейки двох колій у межах цих секцій не перетинаються як слід.
        /// </summary>
        public static CrossingRails Build(IList<Section>[] tracks, IList<bool>[] entered, int[] centers)
        {
            var axes = new Axis[2];
            for (int k = 0; k < 2; k++)
            {
                if (tracks[k].Count == 0 || centers[k] < 0 || centers[k] >= tracks[k].Count) return null;
                axes[k] = AxisOf(tracks[k], entered[k]);
            }

            double gauge = tracks[0][centers[0]].TrackWidth / 2;
            double head = RailProfile.HeadHalfWidth, foot = RailProfile.FootHalfWidth, web = RailProfile.WebHalfWidth, groove = SwitchGeometry.FrogFlangeway;
            // Місця впоперек колії, від її осі: вісь контррейки і дальній від ходової рейки бік проставки,
            // він же ближчий до ходової рейки бік шийки контррейки
            double line = gauge - GuardAxis, spacerEdge = line + web;

            RailFrame a = axes[0].Frames[centers[0] * 2 + 1], b = axes[1].Frames[centers[1] * 2 + 1];
            double cos = Math.Abs(a.Along.X * b.Along.X + a.Along.Z * b.Along.Z);
            // Під кутом, близьким до прямого, контррейки йдуть уздовж усіх рейок; у ромбі лише в тупих кутах
            bool square = Math.Acos(GameMath.Clamp(cos, 0, 1)) * 180 / Math.PI >= NodeTimbers.DeckAngleDeg;

            var result = new CrossingRails();
            for (int k = 0; k < 2; k++)
            {
                int other = 1 - k;
                double near = centers[k] * 2 + 1;

                for (int side = -1; side <= 1; side += 2)
                {
                    var rail = new CrossingRail
                    {
                        Track = k, Side = side, Frames = Shifted(axes[k].Frames, side * gauge),
                        Owner = tracks[k][centers[k]], OwnerPart = axes[k].FirstOnSide[centers[k]] == side > 0 ? SectionPart.FirstRail : SectionPart.SecondRail
                    };
                    // Край, що йде вздовж іншої колії за across блоків від її осі
                    RailEdge At(double across) => Edge(axes[k], side * gauge, axes[other], across, near);
                    RailSpan Cut(double from, bool capFrom, double to, bool capTo)
                    {
                        RailEdge first = At(from), second = At(to);
                        if (first == null || second == null) return null;
                        return first.Middle <= second.Middle
                            ? new RailSpan { From = first, To = second, CapFrom = capFrom, CapTo = capTo }
                            : new RailSpan { From = second, To = first, CapFrom = capTo, CapTo = capFrom };
                    }

                    // Розриви ходової рейки біля кожної з двох рейок іншої колії. Торець потрібен лише стінці
                    // жолоба; де рейка впирається в рейку основної колії, його немає
                    var headCuts = new List<RailSpan>();
                    var webCuts = new List<RailSpan>();
                    var footCuts = new List<RailSpan>();
                    for (int across = -1; across <= 1; across += 2)
                    {
                        if (k == 0)
                        {
                            headCuts.Add(Cut(across * (gauge - head - groove), true, across * (gauge - head), true));
                            continue;
                        }
                        headCuts.Add(Cut(across * (gauge - head - groove), true, across * (gauge + head), false));
                        // Зсередини ромба шийка доходить до проставки рейки основної колії, ззовні до її шийки
                        webCuts.Add(Cut(across * spacerEdge, false, across * (gauge + web), false));
                        footCuts.Add(Cut(across * (gauge - foot), false, across * (gauge + foot), false));
                    }
                    if (headCuts.Contains(null) || webCuts.Contains(null) || footCuts.Contains(null)) return null;

                    // Звичайна рейка ховається на секціях, де в ній є розрив, цілком: вирізати шматок рендер не вміє
                    double first = double.MaxValue, last = double.MinValue;
                    foreach (List<RailSpan> cuts in new[] { headCuts, webCuts, footCuts })
                    {
                        foreach (RailSpan cut in cuts)
                        {
                            first = Math.Min(first, cut.From.Nearest);
                            last = Math.Max(last, cut.To.Farthest);
                        }
                    }
                    int sectionFrom = GameMath.Clamp((int)Math.Floor(first / 2), 0, tracks[k].Count - 1);
                    int sectionTo = GameMath.Clamp((int)Math.Floor(last / 2), 0, tracks[k].Count - 1);
                    RailEdge start = RailEdge.Square(sectionFrom * 2), end = RailEdge.Square(sectionTo * 2 + 2);

                    rail.Of(RailPiece.Head).AddRange(Between(start, end, headCuts, false));
                    rail.Of(RailPiece.Web).AddRange(Between(start, end, webCuts, false));
                    rail.Of(RailPiece.Foot).AddRange(Between(start, end, footCuts, false));

                    for (int n = sectionFrom; n <= sectionTo; n++)
                        result.Hide(tracks[k][n], axes[k].FirstOnSide[n] == side > 0 ? SectionPart.FirstRail : SectionPart.SecondRail);

                    // Проставка лежить від першого розриву до останнього: в основної колії це жолоби в головці,
                    // у другої місця, де її шийка впирається в рейки основної. Де є контррейки на плечах, до їхніх кінців
                    List<RailSpan> reach = k == 0 ? headCuts : webCuts;
                    reach.Sort((x, y) => x.From.Middle.CompareTo(y.From.Middle));
                    RailEdge zoneFrom = reach[0].From, zoneTo = reach[reach.Count - 1].To;

                    double? middle = Meet(axes[k].Frames, side * gauge, axes[other].Frames, 0, near);
                    if (middle != null)
                    {
                        if (square) SquareGuards(rail, At, k, gauge, line, spacerEdge, middle.Value, ref zoneFrom, ref zoneTo);
                        else DiamondGuard(rail, At, axes, k, side, gauge, line, spacerEdge, near, middle.Value);
                    }

                    // Проставка основної колії суцільна; другої на рейці основної обривається, як і її шийка
                    rail.Of(RailPiece.Spacer).AddRange(Between(zoneFrom, zoneTo, k == 0 ? new List<RailSpan>() : webCuts, true));

                    result.Rails.Add(rail);
                }
            }
            return result;
        }

        // Контррейка під кутом, близьким до прямого: всередині ромба і на обох плечах. Яку частину не вдається
        // порахувати, тієї просто немає
        private static void SquareGuards(CrossingRail rail, Func<double, RailEdge> at, int k, double gauge, double line, double spacerEdge, double middle,
            ref RailEdge zoneFrom, ref RailEdge zoneTo)
        {
            double head = RailProfile.HeadHalfWidth, foot = RailProfile.FootHalfWidth, web = RailProfile.WebHalfWidth, groove = SwitchGeometry.FrogFlangeway;

            // Усередині ромба. Контррейка основної колії йде на всю раму: головкою до жолоба рейки іншої колії,
            // шийкою і підошвою до її проставки. Контррейка другої колії впирається в неї всіма трьома
            double headReach = k == 0 ? gauge - head - groove : line - head;
            double webReach = k == 0 ? spacerEdge : line - web, footReach = k == 0 ? spacerEdge : line - foot;
            Add(rail.Of(RailPiece.GuardHead), Span(at(-headReach), k == 0, at(headReach), k == 0));
            Add(rail.Of(RailPiece.GuardWeb), Span(at(-webReach), false, at(webReach), false));
            Add(rail.Of(RailPiece.GuardFoot), Span(at(-footReach), false, at(footReach), false));

            // На обох плечах: від рейки іншої колії назовні. Упирається в неї впритул, підрізана по ній: із
            // зовнішнього боку рейки реборда не ходить, жолоб там не потрібен
            for (int across = -1; across <= 1; across += 2)
            {
                RailEdge headFrom = at(across * (gauge + head));
                if (headFrom == null) continue;
                RailEdge far = RailEdge.Square(Advance(rail.Frames, headFrom.Middle, headFrom.Middle > middle ? GuardLeg : -GuardLeg));

                Add(rail.Of(RailPiece.GuardHead), Span(headFrom, false, far, true));
                Add(rail.Of(RailPiece.GuardWeb), Span(at(across * (gauge + web)), false, far, true));
                Add(rail.Of(RailPiece.GuardFoot), Span(at(across * (gauge + foot)), false, far, true));

                if (far.Middle < zoneFrom.Middle) zoneFrom = far;
                if (far.Middle > zoneTo.Middle) zoneTo = far;
            }
        }

        // Контррейка в ромбі: одна ніжка від тупого кута вглиб ромба
        private static void DiamondGuard(CrossingRail rail, Func<double, RailEdge> at, Axis[] axes, int k, int side, double gauge, double line, double spacerEdge,
            double near, double middle)
        {
            double head = RailProfile.HeadHalfWidth, foot = RailProfile.FootHalfWidth, web = RailProfile.WebHalfWidth, groove = SwitchGeometry.FrogFlangeway;

            // Тупий кут ромба з цього боку колії: той із двох, що ближче до середини
            int corner = 0;
            double best = double.MaxValue;
            for (int across = -1; across <= 1; across += 2)
            {
                double? found = Meet(axes[k].Frames, side * gauge, axes[1 - k].Frames, across * gauge, near);
                if (found == null || Math.Abs(found.Value - near) >= best) continue;
                best = Math.Abs(found.Value - near);
                corner = across;
            }
            if (corner == 0) return;

            double headReach = k == 0 ? gauge - head - groove : line - head;
            double webReach = k == 0 ? spacerEdge : line - web, footReach = k == 0 ? spacerEdge : line - foot;
            RailEdge headFrom = at(corner * headReach);
            if (headFrom == null) return;

            // Дальній кінець зрізано так само навскоси, як і всі краї довкола: уздовж рейки іншої колії
            double length = Advance(rail.Frames, headFrom.Middle, headFrom.Middle > middle ? -GuardLeg : GuardLeg) - headFrom.Middle;
            RailEdge far = headFrom.Moved(length);

            Add(rail.Of(RailPiece.GuardHead), Span(headFrom, k == 0, far, true));
            Add(rail.Of(RailPiece.GuardWeb), Span(at(corner * webReach), false, far, true));
            Add(rail.Of(RailPiece.GuardFoot), Span(at(corner * footReach), false, far, true));
        }

        private static void Add(List<RailSpan> spans, RailSpan span)
        {
            if (span != null) spans.Add(span);
        }

        // Шматок між двома краями, у порядку вздовж лінії; торці йдуть за своїми краями. null, якщо краю
        // немає або вони зійшлися
        private static RailSpan Span(RailEdge a, bool capA, RailEdge b, bool capB)
        {
            if (a == null || b == null || Math.Abs(a.Middle - b.Middle) < 1e-9) return null;
            return a.Middle < b.Middle
                ? new RailSpan { From = a, To = b, CapFrom = capA, CapTo = capB }
                : new RailSpan { From = b, To = a, CapFrom = capB, CapTo = capA };
        }

        // Шматки між початком і кінцем, що лишаються поза розривами. Торець біля розриву такий, як каже розрив;
        // на початку й у кінці такий, як capEnds
        private static List<RailSpan> Between(RailEdge start, RailEdge end, List<RailSpan> cuts, bool capEnds)
        {
            var sorted = new List<RailSpan>(cuts);
            sorted.Sort((x, y) => x.From.Middle.CompareTo(y.From.Middle));

            var spans = new List<RailSpan>();
            RailEdge from = start;
            bool capFrom = capEnds;
            foreach (RailSpan cut in sorted)
            {
                if (cut.To.Middle <= from.Middle || cut.From.Middle >= end.Middle) continue;
                if (cut.From.Middle > from.Middle + 1e-9) spans.Add(new RailSpan { From = from, To = cut.From, CapFrom = capFrom, CapTo = cut.CapFrom });
                from = cut.To;
                capFrom = cut.CapTo;
            }
            if (end.Middle > from.Middle + 1e-9) spans.Add(new RailSpan { From = from, To = end, CapFrom = capFrom, CapTo = capEnds });
            return spans;
        }

        // Край на рейці own (зсунутій на shift від осі своєї колії) там, де її перетинає лінія іншої колії,
        // що йде за across блоків від її осі
        private static RailEdge Edge(Axis own, double shift, Axis other, double across, double near)
        {
            double? minus = Meet(own.Frames, shift - EdgeHalf, other.Frames, across, near);
            double? plus = Meet(own.Frames, shift + EdgeHalf, other.Frames, across, near);
            if (minus == null || plus == null) return null;
            return new RailEdge { Minus = minus.Value, Plus = plus.Value };
        }

        /// <summary>
        /// Де в плані лінія a, зсунута на shiftA вбік, перетинає лінію b, зсунуту на shiftB: номер перерізу
        /// лінії a з дробовою частиною. Якщо перетинів кілька, береться найближчий до near. null, якщо жодного.
        /// </summary>
        public static double? Meet(IList<RailFrame> a, double shiftA, IList<RailFrame> b, double shiftB, double near)
        {
            double? best = null;
            for (int i = 0; i + 1 < a.Count; i++)
            {
                double px = a[i].Center.X + a[i].Side.X * shiftA, pz = a[i].Center.Z + a[i].Side.Z * shiftA;
                double rx = a[i + 1].Center.X + a[i + 1].Side.X * shiftA - px, rz = a[i + 1].Center.Z + a[i + 1].Side.Z * shiftA - pz;

                for (int j = 0; j + 1 < b.Count; j++)
                {
                    double qx = b[j].Center.X + b[j].Side.X * shiftB, qz = b[j].Center.Z + b[j].Side.Z * shiftB;
                    double sx = b[j + 1].Center.X + b[j + 1].Side.X * shiftB - qx, sz = b[j + 1].Center.Z + b[j + 1].Side.Z * shiftB - qz;

                    double denominator = rx * sz - rz * sx;
                    if (Math.Abs(denominator) < 1e-12) continue;
                    double t = ((qx - px) * sz - (qz - pz) * sx) / denominator;
                    double u = ((qx - px) * rz - (qz - pz) * rx) / denominator;
                    // Стик двох відрізків належить обом: щілини між ними бути не повинно
                    if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) continue;

                    double at = i + GameMath.Clamp(t, 0, 1);
                    if (best == null || Math.Abs(at - near) < Math.Abs(best.Value - near)) best = at;
                }
            }
            return best;
        }

        // Місце на лінії за distance блоків уздовж неї від місця from; від'ємна відстань веде назад.
        // Далі за край лінії не йде
        private static double Advance(List<RailFrame> frames, double from, double distance)
        {
            int step = distance >= 0 ? 1 : -1;
            double left = Math.Abs(distance), at = GameMath.Clamp(from, 0, frames.Count - 1);

            while (left > 1e-9)
            {
                // Відрізок лінії, на якому стоїмо, і його кінець у бік руху
                int index = step > 0 ? (int)Math.Floor(at) : (int)Math.Ceiling(at) - 1;
                if (index < 0 || index + 1 >= frames.Count) break;
                double length = frames[index].Center.DistanceTo(frames[index + 1].Center);
                if (length < 1e-9) break;

                double toEnd = (step > 0 ? index + 1 - at : at - index) * length;
                if (toEnd >= left) return at + step * left / length;
                left -= toEnd;
                at = step > 0 ? index + 1 : index;
            }
            return at;
        }

        // Та сама лінія, зсунута вбік на shift блоків
        private static List<RailFrame> Shifted(List<RailFrame> frames, double shift)
        {
            var shifted = new List<RailFrame>(frames.Count);
            foreach (RailFrame frame in frames)
            {
                shifted.Add(new RailFrame
                {
                    Center = new Vec3d(frame.Center.X + frame.Side.X * shift, frame.Center.Y + frame.Side.Y * shift, frame.Center.Z + frame.Side.Z * shift),
                    Side = frame.Side, Up = frame.Up, Along = frame.Along
                });
            }
            return shifted;
        }

        // Перерізи осі колії: по три на секцію, спільний кінець сусідніх секцій один раз
        private static Axis AxisOf(IList<Section> track, IList<bool> entered)
        {
            var axis = new Axis();
            Vec3d side = null;

            for (int n = 0; n < track.Count; n++)
            {
                Section current = track[n];
                Vec3d[] points = { current.FullStartPosition, current.GetGlobalPos(), current.FullEndPosition };
                Vec3f[] normals = { current.StartNormal, current.CenterNormal, current.EndNormal };
                Vec3f[] tangents = { current.StartTangent, current.CenterTangent, current.EndTangent };
                double direction = entered[n] ? 1 : -1;
                if (!entered[n])
                {
                    Array.Reverse(points);
                    Array.Reverse(normals);
                    Array.Reverse(tangents);
                }

                for (int k = n == 0 ? 0 : 1; k < 3; k++)
                {
                    // Бік тримається той самий уздовж усієї колії, хоч куди дивиться нормаль чергової секції
                    Vec3d normal = new Vec3d(normals[k].X, normals[k].Y, normals[k].Z).Normalize();
                    if (side != null && normal.X * side.X + normal.Z * side.Z < 0) normal.Mul(-1);
                    side = normal;

                    Vec3d along = new Vec3d(tangents[k].X * direction, tangents[k].Y * direction, tangents[k].Z * direction);
                    along.Sub(side.Clone().Mul(along.Dot(side)));
                    if (along.Length() < 1e-6) along = new Vec3d(-side.Z, 0, side.X);
                    along.Normalize();

                    Vec3d up = side.Cross(along);
                    if (up.Y < 0) up.Mul(-1);

                    axis.Frames.Add(new RailFrame { Center = points[k], Side = side.Clone(), Up = up, Along = along });

                    if (k == 1)
                    {
                        Vec3f own = current.CenterNormal;
                        axis.FirstOnSide.Add(own.X * side.X + own.Y * side.Y + own.Z * side.Z > 0);
                    }
                }
            }
            return axis;
        }
    }
}
