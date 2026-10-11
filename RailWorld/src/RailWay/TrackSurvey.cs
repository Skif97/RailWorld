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
        private static readonly List<double> NoAngles = new List<double>();

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

        /// <summary>Кути, під якими вісь секції перетинають осі покладених колій на близькій висоті.</summary>
        public List<double> Crossed = NoAngles;

        internal void AddInPlane(Section other)
        {
            if (InPlane == NoSections) InPlane = new List<Section>();
            InPlane.Add(other);
        }

        internal void AddCrossed(double angle)
        {
            if (Crossed == NoAngles) Crossed = new List<double>();
            Crossed.Add(angle);
        }
    }

    /// <summary>
    /// Місце, де шматок маршруту перетинає вісь покладеної секції: котру і на якій частці шматка.
    /// Тут же лежить те, що про цей перетин з'ясовується обходом покладеної колії навколо нього. Воно
    /// залежить лише від самого перетину і від колії, тож рахується один раз, коли вперше знадобилося;
    /// null означає, що ще не рахувалося.
    /// </summary>
    public class SegmentHit
    {
        public Section Section;
        public double T;

        /// <summary>Перетнута секція лежить у тілі стрілки.</summary>
        internal bool? InSwitchBody;
        /// <summary>Вузол цього перетину дістає до тіла стрілки на перетнутій колії.</summary>
        internal bool? ReachesSwitchBody;
        /// <summary>На перетнутій колії поруч уже є перетин з іншою колією, і два вузли наклалися б.</summary>
        internal bool? NearAnotherCrossing;
    }

    /// <summary>
    /// Огляд покладеної колії навколо нової секції чи шматка маршруту.
    /// </summary>
    public static class TrackSurvey
    {
        /// <summary>
        /// Як далеко від середини секції лежить те, що може її стосуватися, блоків у плані. Найдальше це
        /// важіль стрілки сусідньої колії: пів секції, пів шпали й сам важіль з одного боку і стільки ж
        /// з другого, близько 2.6 блока. Габарити двох секцій перекриваються ще ближче, до 2.5.
        /// </summary>
        public const double Reach = 3;

        /// <summary>
        /// Як далеко від початку шматка завдовжки із секцію лежить середина секції, вісь якої він перетинає.
        /// </summary>
        public const double CrossReach = 1;

        /// <summary>
        /// Наскільки висоти двох колій можуть розходитися там, де їхні габарити перекриваються, щоб це ще
        /// вважалося однією площиною: перетином або стрілкою.
        /// </summary>
        public const double SamePlane = 0.15;

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
                    if (other.HasSwitch(atStart)) facts.CoversLever = CoversLever(section, SectionPicker.LeverPosition(other, atStart));
                }

                bool near = Math.Abs(other.GetGlobalPos().Y - middle.Y) < RouteCurve.CrossingClearance;
                if (near && RouteCurve.Crosses(start, end, other, out _, out double angle)) facts.AddCrossed(angle);

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
            double rx = b.X - a.X, rz = b.Z - a.Z;

            foreach (Section section in track.Around(a, CrossReach, RouteCurve.CrossingClearance + 1))
            {
                Vec3d c = section.FullStartPosition, d = section.FullEndPosition;
                double sx = d.X - c.X, sz = d.Z - c.Z;
                double denominator = rx * sz - rz * sx;
                if (Math.Abs(denominator) < 1e-12) continue;

                double t = ((c.X - a.X) * sz - (c.Z - a.Z) * sx) / denominator;
                double u = ((c.X - a.X) * rz - (c.Z - a.Z) * rx) / denominator;
                if (t < 0 || t >= 1 || u < 0 || u >= 1) continue;

                if (hits == null) hits = new List<SegmentHit>();
                hits.Add(new SegmentHit { Section = section, T = t });
            }
            return hits;
        }

        /// <summary>Чи важіль у цьому місці опинився б під колією: під шпалами секції, на близькій до неї висоті.</summary>
        public static bool CoversLever(Section section, Vec3d lever)
        {
            if (Math.Abs(lever.Y - section.GetGlobalPos().Y) >= RouteCurve.CrossingClearance) return false;

            // Важіль під колією, якщо він ближче до її осі, ніж край шпали
            double along = RouteCurve.Along(section, true, lever, out double length, out double sideways);
            return along >= 0 && along <= length && sideways < section.SleeperLength / 2 + SectionBox.LeverHalfWidth;
        }

        // Ті самі кінці, в будь-якому з двох напрямків
        private static bool SameEnds(Vec3d start, Vec3d end, Vec3d otherStart, Vec3d otherEnd)
        {
            double tolerance = SectionLinker.DuplicateTolerance;
            return (otherStart.DistanceTo(start) <= tolerance && otherEnd.DistanceTo(end) <= tolerance)
                || (otherStart.DistanceTo(end) <= tolerance && otherEnd.DistanceTo(start) <= tolerance);
        }
    }
}
