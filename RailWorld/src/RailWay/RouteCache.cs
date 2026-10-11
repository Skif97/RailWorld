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
        // Коли запам'ятованого стає більше за це, викидається те, що останньому перерахунку не знадобилося
        private const int MaxEntries = 40000;

        // Запам'ятоване і номер перерахунку, якому воно востаннє знадобилося
        private class Entry<T>
        {
            public T Value;
            public int UsedAt;
        }

        private readonly Dictionary<((double, double, double), (double, double, double), (double, double, double), (double, double, double)), Entry<List<PointOnBezierCurve>>> legs
            = new Dictionary<((double, double, double), (double, double, double), (double, double, double), (double, double, double)), Entry<List<PointOnBezierCurve>>>();
        private readonly Dictionary<((double, double, double), (double, double, double)), Entry<List<SegmentHit>>> hits
            = new Dictionary<((double, double, double), (double, double, double)), Entry<List<SegmentHit>>>();
        private readonly Dictionary<((double, double, double), (double, double, double), (double, double, double), (double, double, double)), Entry<SectionFacts>> facts
            = new Dictionary<((double, double, double), (double, double, double), (double, double, double), (double, double, double)), Entry<SectionFacts>>();

        private int rebuild;

        /// <summary>Скільки разів довелося рахувати, бо готового не було: для перевірки, що пам'ять працює.</summary>
        public int Misses { get; private set; }

        public void Clear()
        {
            legs.Clear();
            hits.Clear();
            facts.Clear();
        }

        /// <summary>
        /// Починається новий перерахунок маршруту. Якщо запам'ятованого назбиралося забагато, лишається тільки
        /// те, що знадобилося попередньому перерахунку: це і є маршрут, який зараз на екрані. Викидати все
        /// не можна: наступний перерахунок довгого маршруту знову рахував би його цілком.
        /// </summary>
        public void Begin()
        {
            if (legs.Count + hits.Count + facts.Count > MaxEntries)
            {
                Keep(legs, rebuild);
                Keep(hits, rebuild);
                Keep(facts, rebuild);
            }
            rebuild++;
        }

        /// <summary>Крива між двома точками маршруту, нарізана на точки; null, якщо точки надто близько.</summary>
        public List<PointOnBezierCurve> Leg(RoutePoint a, RoutePoint b)
        {
            var key = (Key(a.Position), Key(a.Tangent), Key(b.Position), Key(b.Tangent));
            if (!legs.TryGetValue(key, out Entry<List<PointOnBezierCurve>> entry))
            {
                Misses++;
                legs[key] = entry = new Entry<List<PointOnBezierCurve>> { Value = RouteCurve.Leg(a, b) };
            }
            entry.UsedAt = rebuild;
            if (entry.Value == null) return null;

            // Той, хто питає, далі міняє точки: віддаємо копію, запам'ятоване лишається як було
            var copy = new List<PointOnBezierCurve>(entry.Value.Count);
            foreach (PointOnBezierCurve point in entry.Value)
            {
                copy.Add(new PointOnBezierCurve { position = point.position.Clone(), tangent = point.tangent.Clone(), normal = point.normal.Clone() });
            }
            return copy;
        }

        /// <summary>Де шматок маршруту перетинає осі покладених секцій; null, якщо ніде.</summary>
        public List<SegmentHit> Hits(Vec3d a, Vec3d b, TrackIndex track)
        {
            var key = (Key(a), Key(b));
            if (!hits.TryGetValue(key, out Entry<List<SegmentHit>> entry))
            {
                Misses++;
                hits[key] = entry = new Entry<List<SegmentHit>> { Value = TrackSurvey.Hits(a, b, track) };
            }
            entry.UsedAt = rebuild;
            return entry.Value;
        }

        /// <summary>Факти про нову секцію, нарізану з цих трьох точок.</summary>
        public SectionFacts Facts(PointOnBezierCurve start, PointOnBezierCurve center, PointOnBezierCurve end, Section section, TrackIndex track)
        {
            // Усе, від чого залежать факти: три точки осі й нахил полотна посередині
            var key = (Key(start.position), Key(center.position), Key(end.position), ((double)center.normal.X, (double)center.normal.Y, (double)center.normal.Z));
            if (!facts.TryGetValue(key, out Entry<SectionFacts> entry))
            {
                Misses++;
                facts[key] = entry = new Entry<SectionFacts> { Value = TrackSurvey.Examine(section, track) };
            }
            entry.UsedAt = rebuild;
            return entry.Value;
        }

        private static (double, double, double) Key(Vec3d v)
        {
            return (v.X, v.Y, v.Z);
        }

        // Лишає тільки те, що знадобилося перерахунку з таким номером
        private static void Keep<TKey, TValue>(Dictionary<TKey, Entry<TValue>> entries, int usedAt)
        {
            var stale = new List<TKey>();
            foreach (KeyValuePair<TKey, Entry<TValue>> pair in entries)
            {
                if (pair.Value.UsedAt != usedAt) stale.Add(pair.Key);
            }
            foreach (TKey key in stale) entries.Remove(key);
        }
    }
}
