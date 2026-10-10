using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;

namespace RailWorld.Tests
{
    /// <summary>
    /// Основа для тестів, яким потрібна покладена колія: кладе секції так само, як це робить мод,
    /// тільки без гри, і тримає їх у словнику замість чанків.
    /// </summary>
    public abstract class TrackTestBase
    {
        protected const double ArcRadius = 20;

        private readonly Dictionary<string, Section> sections = new Dictionary<string, Section>();
        private int nextIndex;

        private static string Key(Vec3i chunk, int index)
        {
            return chunk.X + "_" + chunk.Y + "_" + chunk.Z + "_" + index;
        }

        /// <summary>Секція за посиланням, або null, якщо її прибрано через Forget: ніби її чанк не завантажений.</summary>
        protected Section Resolve(SectionLink link)
        {
            sections.TryGetValue(Key(link.Chunk, link.Index), out Section section);
            return section;
        }

        /// <summary>Робить секцію недоступною, ніби її чанк не завантажений. Зв'язки на неї лишаються.</summary>
        protected void Forget(Section section)
        {
            sections.Remove(Key(section.ChunkAddres, section.IndexInChunk));
        }

        // Кладе колію по маршруту: точки нарізки, по три на секцію, сусідні секції зв'язані, рейки і шпали на місці
        protected List<Section> Lay(params RoutePoint[] route)
        {
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint>(route));
            var track = new List<Section>();
            for (int i = 0; i + 2 < points.Count; i += 2)
            {
                var section = new Section(points[i], points[i + 1], points[i + 2], TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength);
                section.IndexInChunk = nextIndex++;
                section.InstallationSleeper("oak");
                section.InstallationRails("iron");
                sections[Key(section.ChunkAddres, section.IndexInChunk)] = section;

                if (track.Count > 0) Join(track[track.Count - 1], section);
                track.Add(section);
            }
            return track;
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
