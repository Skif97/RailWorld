using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Що оточує секцію в уже покладеній колії: усе, що правила прокладання хочуть про неї знати.
    /// Збирається за один огляд сусідів, а правила далі працюють із готовими фактами.
    /// Факти залежать лише від геометрії секції і від покладеної колії, тож для тієї самої секції
    /// їх можна взяти готовими (RouteCache).
    /// </summary>
    public class SectionFacts
    {
        private static readonly List<Section> NoSections = new List<Section>();

        /// <summary>Така сама секція вже лежить: удруге вона не кладеться.</summary>
        public bool AlreadyLaid;

        /// <summary>Габарит секції перекривається з габаритом іншої колії на близькій висоті.</summary>
        public bool LiesOnTrack;

        /// <summary>
        /// Секція проходить над чи під іншою колією ближче, ніж можна: не в її площині, але й не на просвіті.
        /// </summary>
        public bool TooClose;

        /// <summary>Під секцією опинився б важіль стрілки, що вже стоїть.</summary>
        public bool CoversLever;

        /// <summary>
        /// Покладені секції іншої колії, на габарит яких секція лягає в тій самій площині.
        /// Секції, з якими вона стикується кінцем, сюди не потрапляють: це продовження тієї самої колії.
        /// </summary>
        public List<Section> InPlane = NoSections;

        /// <summary>Вісь секції перетинає вісь покладеної колії на близькій висоті.</summary>
        public bool Crossed;

        internal void AddInPlane(Section other)
        {
            if (InPlane == NoSections) InPlane = new List<Section>();
            InPlane.Add(other);
        }

    }

    /// <summary>
    /// Місце, де шматок маршруту перетинає вісь покладеної секції: котру і на якій частці шматка.
    /// Тут же лежить те, що про цей перетин з'ясовується обходом покладеної колії навколо нього; null означає,
    /// що ще не рахувалося. Чи секція в тілі стрілки, залежить лише від неї. Решта залежить від вузла перетину,
    /// тобто й від того, як маршрут іде по обидва боки від цього шматка: воно дійсне, доки маршрут у вузлі
    /// той самий до останнього числа (NodeSignature).
    /// </summary>
    public class SegmentHit
    {
        public Section Section;
        public double T;

        /// <summary>Перетнута секція лежить у тілі стрілки.</summary>
        internal bool? InSwitchBody;
        /// <summary>Точки маршруту у вузлі, для яких пораховано наступні два.</summary>
        internal double[] NodeSignature;
        /// <summary>Вузол цього перетину дістає до тіла стрілки на перетнутій колії.</summary>
        internal bool? ReachesSwitchBody;
        /// <summary>У вузлі на перетнутій колії вже лежить третя колія: два вузли наклалися б.</summary>
        internal bool? NearAnotherCrossing;
    }

    /// <summary>
    /// Огляд покладеної колії навколо нової секції чи шматка маршруту.
    /// </summary>
    public static class TrackSurvey
    {
        /// <summary>
        /// Як далеко від середини секції лежить середина іншої, яка може її стосуватися, блоків у плані.
        /// Найдальше це секція зі стрілкою, у місце важеля якої заходить ця. Упоперек: пів габариту цієї секції
        /// (0.98), хід вантажу важеля (SectionBox.LeverReachAcross) і відстань від важеля до осі його колії
        /// (пів шпали і SectionBox.LeverGap), разом 3.2. Уздовж: важіль стоїть на SectionBox.LeverAhead за стиком,
        /// середина секції зі стрілкою на чверть блока перед ним, плюс хід уздовж (LeverReachAlong) і пів цієї
        /// секції, разом 1.5. По діагоналі близько 3.5. Габарити двох секцій перекриваються значно ближче.
        /// </summary>
        public const double Reach = 4;

        /// <summary>
        /// Як далеко від початку шматка завдовжки із секцію лежить середина секції, вісь якої він перетинає.
        /// </summary>
        public const double CrossReach = 1;

        /// <summary>
        /// Наскільки висоти двох колій можуть розходитися там, де їхні габарити перекриваються, щоб це ще
        /// вважалося однією площиною: перетином або стрілкою.
        /// </summary>
        public const double SamePlane = 0.15;

        /// <summary>
        /// Як далеко від осі покладеної колії точка розмітки, поставлена на блок, стає на саму колію, блоків у
        /// плані: маршрут, що починається чи закінчується поруч із колією, до неї й приєднується. Колію впритул
        /// до іншої так не покладеш, і це навмисно.
        /// </summary>
        public const double MarkerReach = 2.5;

        /// <summary>
        /// Секція покладеної колії, вісь якої проходить у плані найближче до точки p, не далі за reach, на тій
        /// самій висоті (SamePlane), і найближча до p точка її осі. Вісь секції це ламана через її початок,
        /// середину й кінець. false, якщо такої секції немає.
        /// </summary>
        public static bool NearestAxis(Vec3d p, double reach, TrackIndex track, out Section nearest, out Vec3d at)
        {
            nearest = null;
            at = null;
            double best = reach;

            // Середина секції не далі ніж за блок від будь-якої точки її осі
            foreach (Section section in track.Around(p, reach + 1, SamePlane + 1))
            {
                Vec3d start = section.FullStartPosition, middle = section.GetGlobalPos(), end = section.FullEndPosition;
                for (int half = 0; half < 2; half++)
                {
                    Vec3d point = Closest(half == 0 ? start : middle, half == 0 ? middle : end, p);
                    if (Math.Abs(point.Y - p.Y) > SamePlane) continue;

                    double dx = point.X - p.X, dz = point.Z - p.Z, distance = Math.Sqrt(dx * dx + dz * dz);
                    if (distance > best) continue;

                    best = distance;
                    nearest = section;
                    at = point;
                }
            }
            return nearest != null;
        }

        // Найближча до p у плані точка відрізка від a до b, з висотою на відрізку
        private static Vec3d Closest(Vec3d a, Vec3d b, Vec3d p)
        {
            double rx = b.X - a.X, rz = b.Z - a.Z, length = rx * rx + rz * rz;
            double t = length < 1e-12 ? 0 : GameMath.Clamp(((p.X - a.X) * rx + (p.Z - a.Z) * rz) / length, 0, 1);
            return new Vec3d(a.X + rx * t, a.Y + (b.Y - a.Y) * t, a.Z + rz * t);
        }

        /// <summary>Оглядає покладену колію навколо секції.</summary>
        public static SectionFacts Examine(Section section, TrackIndex track)
        {
            var facts = new SectionFacts();
            Vec3d middle = section.GetGlobalPos(), start = section.FullStartPosition, end = section.FullEndPosition;

            foreach (Section other in track.Around(middle, Reach, RouteCurve.CrossingClearance))
            {
                if (other == section) continue;
                Vec3d otherStart = other.FullStartPosition, otherEnd = other.FullEndPosition;

                if (!facts.AlreadyLaid) facts.AlreadyLaid = SameEnds(start, end, otherStart, otherEnd);

                for (int side = 0; side < 2 && !facts.CoversLever; side++)
                {
                    bool atStart = side == 0;
                    if (other.HasSwitch(atStart)) facts.CoversLever = CoversLever(section, other, atStart, other.GetLeverSide(atStart));
                }

                bool near = Math.Abs(other.GetGlobalPos().Y - middle.Y) < RouteCurve.CrossingClearance;
                if (near && !facts.Crossed) facts.Crossed = AxisCrossing.OfPlanned(section, other);

                if (!Footprint.Overlap(section, other, out double x, out double z)) continue;

                // Секція, з якою ця стикується кінцем, це та сама колія: на кривій їхні габарити заходять
                // один на одного кутами
                bool joined = Footprint.Joined(section, other);
                if (!joined && near) facts.LiesOnTrack = true;

                // Висоти порівнюються там, де габарити перекриваються
                Vec3d where = new Vec3d(x, 0, z);
                if (!RouteCurve.CrossPlane(section, where, 0, 0, out double ownY, out _) || !RouteCurve.CrossPlane(other, where, 0, 0, out double otherY, out _)) continue;
                double gap = Math.Abs(ownY - otherY);

                if (gap > SamePlane && gap < RouteCurve.CrossingClearance) facts.TooClose = true;
                if (!joined && gap <= SamePlane) facts.AddInPlane(other);
            }
            return facts;
        }

        /// <summary>
        /// Де шматок маршруту від a до b перетинає в плані осі покладених секцій. null, якщо ніде:
        /// так буває майже завжди.
        /// </summary>
        public static List<SegmentHit> Hits(Vec3d a, Vec3d b, TrackIndex track)
        {
            List<SegmentHit> hits = null;

            foreach (Section section in track.Around(a, CrossReach, RouteCurve.CrossingClearance + 1))
            {
                if (!AxisCrossing.Of(a, b, section, out double t)) continue;

                if (hits == null) hits = new List<SegmentHit>();
                hits.Add(new SegmentHit { Section = section, T = t });
            }
            return hits;
        }

        /// <summary>
        /// Чи колія заходить у місце важеля стрілки trunk, якби той стояв з боку side: чи габарит секції
        /// перекривається з місцем, яке важіль займає разом з ходом ручки й вантажу, на близькій до нього висоті.
        /// </summary>
        public static bool CoversLever(Section section, Section trunk, bool atStart, int side)
        {
            SectionPicker.LeverFootprint(trunk, atStart, side, out Vec3d from, out Vec3d to);
            if (Math.Abs(from.Y - section.GetGlobalPos().Y) >= RouteCurve.CrossingClearance) return false;

            return Footprint.Overlap(from, to, SectionBox.LeverReachAcross * 2, section.FullStartPosition, section.FullEndPosition, Footprint.Width(section), out _, out _);
        }

        // Ті самі кінці, в будь-якому з двох напрямків
        private static bool SameEnds(Vec3d start, Vec3d end, Vec3d otherStart, Vec3d otherEnd)
        {
            double tolerance = SectionLinker.PositionTolerance;
            return (otherStart.DistanceTo(start) <= tolerance && otherEnd.DistanceTo(end) <= tolerance)
                || (otherStart.DistanceTo(end) <= tolerance && otherEnd.DistanceTo(start) <= tolerance);
        }
    }
}
