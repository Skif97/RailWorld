using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Пам'ять розмітки маршруту між перерахунками. Поки гравець веде останню точку, маршрут перераховується
    /// на кожен її рух, але міняється лише його хвіст: решта до останнього біта та сама. Тут лежить те,
    /// що для тієї самої геометрії щоразу виходить однаковим: нарізані криві між точками маршруту, перетини
    /// шматків маршруту з покладеними коліями і факти про кожну нову секцію.
    /// Готове береться лише за точним збігом геометрії; будь-яка відмінність означає звичайний розрахунок,
    /// тому результат із пам'яттю і без неї той самий.
    /// Факти посилаються на секції покладеної колії: щойно вона змінилася, пам'ять треба очистити (Clear).
    /// Користується нею лише клієнт для показу; сервер рахує все сам.
    /// </summary>
    public class RouteCache
    {
        // Більше за це пам'ять не росте: довгий сеанс розмітки не має збирати все, що колись показав
        private const int MaxEntries = 40000;

        private readonly Dictionary<((double, double, double), (double, double, double), (double, double, double), (double, double, double)), List<PointOnBezierCurve>> legs
            = new Dictionary<((double, double, double), (double, double, double), (double, double, double), (double, double, double)), List<PointOnBezierCurve>>();
        private readonly Dictionary<((double, double, double), (double, double, double)), List<SegmentHit>> hits
            = new Dictionary<((double, double, double), (double, double, double)), List<SegmentHit>>();
        private readonly Dictionary<((double, double, double), (double, double, double), (double, double, double), (double, double, double)), SectionFacts> facts
            = new Dictionary<((double, double, double), (double, double, double), (double, double, double), (double, double, double)), SectionFacts>();

        /// <summary>Скільки разів довелося рахувати, бо готового не було: для перевірки, що пам'ять працює.</summary>
        public int Misses { get; private set; }

        public void Clear()
        {
            legs.Clear();
            hits.Clear();
            facts.Clear();
        }

        /// <summary>Крива між двома точками маршруту, нарізана на точки; null, якщо точки надто близько.</summary>
        public List<PointOnBezierCurve> Leg(RoutePoint a, RoutePoint b)
        {
            var key = (Key(a.Position), Key(a.Tangent), Key(b.Position), Key(b.Tangent));
            if (!legs.TryGetValue(key, out List<PointOnBezierCurve> cut))
            {
                Misses++;
                Trim();
                legs[key] = cut = RouteCurve.Leg(a, b);
            }
            if (cut == null) return null;

            // Той, хто питає, далі міняє точки: віддаємо копію, запам'ятоване лишається як було
            var copy = new List<PointOnBezierCurve>(cut.Count);
            foreach (PointOnBezierCurve point in cut)
            {
                copy.Add(new PointOnBezierCurve { position = point.position.Clone(), tangent = point.tangent.Clone(), normal = point.normal.Clone() });
            }
            return copy;
        }

        /// <summary>Де шматок маршруту перетинає осі покладених секцій; null, якщо ніде.</summary>
        public List<SegmentHit> Hits(Vec3d a, Vec3d b, TrackIndex track)
        {
            var key = (Key(a), Key(b));
            if (!hits.TryGetValue(key, out List<SegmentHit> found))
            {
                Misses++;
                Trim();
                hits[key] = found = TrackSurvey.Hits(a, b, track);
            }
            return found;
        }

        /// <summary>Факти про нову секцію, нарізану з цих трьох точок.</summary>
        public SectionFacts Facts(PointOnBezierCurve start, PointOnBezierCurve center, PointOnBezierCurve end, Section section, TrackIndex track)
        {
            // Усе, від чого залежать факти: три точки осі й нахил полотна посередині
            var key = (Key(start.position), Key(center.position), Key(end.position), ((double)center.normal.X, (double)center.normal.Y, (double)center.normal.Z));
            if (!facts.TryGetValue(key, out SectionFacts found))
            {
                Misses++;
                Trim();
                facts[key] = found = TrackSurvey.Examine(section, track);
            }
            return found;
        }

        private static (double, double, double) Key(Vec3d v)
        {
            return (v.X, v.Y, v.Z);
        }

        private void Trim()
        {
            if (legs.Count + hits.Count + facts.Count > MaxEntries) Clear();
        }
    }
}
