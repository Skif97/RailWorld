using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;

namespace RailWorld.Tests
{
    /// <summary>
    /// Основа для тестів, яким потрібна покладена колія: нарізає маршрут на секції так само, як мод,
    /// і тримає їх у даних чанків (DataInChunk), як гра, тільки без самих чанків. На цьому схожість закінчується: сусідні секції тут зв'язані
    /// руками (Join), а не кодом мода (SectionLinker.LinkStretch), реєстри стрілок і перетинів не працюють,
    /// і маршрут кладеться без огляду на вже покладену колію. Тобто справжній шлях зв'язування і появи
    /// стрілок ці тести не перевіряють.
    /// </summary>
    public abstract class TrackTestBase
    {
        protected const double ArcRadius = 20;

        private readonly Dictionary<Vec3i, DataInChunk> chunks = new Dictionary<Vec3i, DataInChunk>();

        /// <summary>Секція за посиланням, або null, якщо її прибрано через Forget: ніби її чанк не завантажений.</summary>
        protected Section Resolve(SectionLink link)
        {
            Section section = null;
            if (chunks.TryGetValue(link.Chunk, out DataInChunk data)) data.RailWaySections.TryGetValue(link.Index, out section);
            return section;
        }

        /// <summary>Робить секцію недоступною, ніби її чанк не завантажений. Зв'язки на неї лишаються.</summary>
        protected void Forget(Section section)
        {
            if (chunks.TryGetValue(section.ChunkAddres, out DataInChunk data)) data.Remove(section.IndexInChunk);
        }

        /// <summary>Дані колії чанка, як їх тримає гра, або null, якщо в чанку нічого не покладено.</summary>
        protected DataInChunk DataIn(Vec3i chunk)
        {
            chunks.TryGetValue(chunk, out DataInChunk data);
            return data;
        }

        // Записує секцію в дані її чанка, як прокладання в грі: там вона отримує свій номер
        private void Store(Section section)
        {
            if (!chunks.TryGetValue(section.ChunkAddres, out DataInChunk data)) chunks[section.ChunkAddres.Clone()] = data = new DataInChunk();
            data.Add(section);
        }

        // Кладе колію по маршруту: точки нарізки, по три на секцію, сусідні секції зв'язані, рейки і шпали на місці
        protected List<Section> Lay(params RoutePoint[] route)
        {
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint>(route));
            var track = new List<Section>();
            for (int i = 0; i + 2 < points.Count; i += 2)
            {
                var section = new Section(points[i], points[i + 1], points[i + 2], TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength);
                section.InstallationSleeper("oak");
                section.InstallationRails("iron");
                Store(section);

                if (track.Count > 0) Join(track[track.Count - 1], section);
                track.Add(section);
            }
            return track;
        }

        /// <summary>Покладена колія, якою її бачать правила. Щоразу нова: між перевірками тести докладають колію.</summary>
        protected TrackIndex SectionsIn => new TrackIndex(GridIn);

        /// <summary>Секції чанка по клітинках: те саме, що гра дістає з даних чанка.</summary>
        protected ChunkTrackGrid GridIn(Vec3i chunk)
        {
            return chunks.TryGetValue(chunk, out DataInChunk data) ? data.Grid : null;
        }

        /// <summary>Секції маршруту, який лише задумано: нарізані так само, але ще не покладені.</summary>
        protected static List<Section> Plan(params RoutePoint[] route)
        {
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint>(route));
            var planned = new List<Section>();
            for (int i = 0; i + 2 < points.Count; i += 2)
            {
                planned.Add(new Section(points[i], points[i + 1], points[i + 2], TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength));
            }
            return planned;
        }

        /// <summary>
        /// Секції колії, що проходить точно через ці кінці секцій, як колія, знята з гри: кожна секція це
        /// відрізок між двома сусідніми кінцями, з нормаллю впоперек нього. Нічого не кладуть і не зв'язують.
        /// </summary>
        protected static List<Section> Through(params (double X, double Z)[] ends)
        {
            var sections = new List<Section>();
            for (int i = 0; i + 1 < ends.Length; i++)
            {
                double dx = ends[i + 1].X - ends[i].X, dz = ends[i + 1].Z - ends[i].Z, length = Math.Sqrt(dx * dx + dz * dz);
                var tangent = new Vec3f((float)(dx / length), 0, (float)(dz / length));
                var normal = new Vec3f(-tangent.Z, 0, tangent.X);
                PointOnBezierCurve At(double x, double z) => new PointOnBezierCurve { position = new Vec3d(x, 0, z), tangent = tangent, normal = normal };
                sections.Add(new Section(At(ends[i].X, ends[i].Z), At(ends[i].X + dx / 2, ends[i].Z + dz / 2), At(ends[i + 1].X, ends[i + 1].Z),
                    TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength));
            }
            return sections;
        }

        /// <summary>Кладе секції, як Lay: дає їм номери, ставить рейки й шпали і зв'язує сусідні між собою.</summary>
        protected List<Section> Lay(List<Section> track)
        {
            for (int i = 0; i < track.Count; i++)
            {
                Section section = track[i];
                section.InstallationSleeper("oak");
                section.InstallationRails("iron");
                Store(section);
                if (i > 0) Join(track[i - 1], section);
            }
            return track;
        }

        /// <summary>
        /// X стику колії, що йде на схід уздовж z = 0, найближчого до x. Маршрут у грі чіпляється до колії
        /// на її стику, а стики через нарізку стоять не на круглих числах.
        /// </summary>
        protected static double JointNear(List<Section> track, double x)
        {
            double best = track[0].FullStartPosition.X;
            foreach (Section section in track)
            {
                double end = section.FullEndPosition.X;
                if (Math.Abs(end - x) < Math.Abs(best - x)) best = end;
            }
            return best;
        }

        // Зв'язує кінець однієї секції з початком наступної в обидва боки
        protected static void Join(Section from, Section to)
        {
            from.AddLink(false, new SectionLink(to, true));
            to.AddLink(true, new SectionLink(from, false));
        }

        protected static RoutePoint Point(double x, double z, double tangentX, double tangentZ)
        {
            return new RoutePoint { Position = new Vec3d(x, 0, z), Tangent = new Vec3d(tangentX, 0, tangentZ).Normalize() };
        }

        /// <summary>Спільна колія, що приходить у точку стрілки із заходу.</summary>
        protected List<Section> Trunk()
        {
            return Lay(Point(-6, 0, 1, 0), Point(0, 0, 1, 0));
        }

        /// <summary>Пряма гілка від точки стрілки на схід.</summary>
        protected List<Section> Straight()
        {
            return Lay(Point(0, 0, 1, 0), Point(24, 0, 1, 0));
        }

        /// <summary>Гілка дугою з тієї самої точки в тому самому напрямку, що відходить убік, на 60 градусів.</summary>
        protected List<Section> Arc()
        {
            double angle = Math.PI / 3;
            return Lay(Point(0, 0, 1, 0),
                Point(ArcRadius * Math.Sin(angle), ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
        }
    }
}
