using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Деталь під прицілом: промінь іде по сітці колії і перевіряє бокси лише секцій поруч із собою.
    /// </summary>
    public class SectionRayPickerTests : TrackTestBase
    {
        // Бокси секцій, як їх збирає клієнт: усі одним списком і місце кожної секції в ньому
        private readonly List<SectionBox> all = new List<SectionBox>();
        private readonly Dictionary<Section, (int First, int Count)> places = new Dictionary<Section, (int First, int Count)>();
        private double reach;
        private int asked;

        private void Boxes(List<Section> track)
        {
            foreach (Section section in track)
            {
                int first = all.Count;
                SectionPicker.AddSectionBoxes(all, section.ChunkAddres, section.IndexInChunk, section);
                places[section] = (first, all.Count - first);
                for (int i = first; i < all.Count; i++) reach = System.Math.Max(reach, ClientTrack.Reach(all[i], section));
            }
        }

        private bool BoxesOf(Section section, out List<SectionBox> boxes, out int first, out int count)
        {
            asked++;
            boxes = all;
            first = count = 0;
            if (!places.TryGetValue(section, out var place)) return false;
            (first, count) = place;
            return true;
        }

        private SectionSelection Pick(Vec3d origin, Vec3d dir, double range)
        {
            asked = 0;
            return new SectionRayPicker(GridIn).Pick(origin, dir.Normalize(), range, reach, BoxesOf, box => box.Part == SectionPart.Whole);
        }

        [Fact]
        public void FindsTheSectionUnderTheRayAndOnlyLooksNearIt()
        {
            List<Section> track = Lay(Point(2, 4, 1, 0), Point(60, 4, 1, 0));
            Boxes(track);
            Section target = track.Find(section => System.Math.Abs(section.Center.X - 10) < 0.3);

            // Дивимося згори вниз на секцію біля x = 10
            var origin = new Vec3d(target.Center.X, 2, target.Center.Z);
            SectionSelection sel = Pick(origin, new Vec3d(0, -1, 0), 4.5);

            Assert.NotNull(sel);
            Assert.Same(target, sel.Section);
            // Колія завдовжки майже 60 блоків, а питали лише секції поруч із променем, і кожну раз
            Assert.True(asked < 20, "перевірено секцій: " + asked);
        }

        [Fact]
        public void NearestSectionWinsAlongALongRay()
        {
            List<Section> track = Lay(Point(2, 4, 1, 0), Point(60, 4, 1, 0));
            Boxes(track);

            // Промінь уздовж колії трохи згори під малим кутом: першою влучає найближча секція, а не та,
            // що перевірялася першою
            var origin = new Vec3d(5, 0.5, 4);
            SectionSelection sel = Pick(origin, new Vec3d(1, -0.1, 0), 20);

            Assert.NotNull(sel);
            Assert.InRange(sel.HitPosition.X, 5, 11);
            foreach (Section section in track)
            {
                (int first, int count) = places[section];
                for (int i = first; i < first + count; i++)
                {
                    if (all[i].Part == SectionPart.Whole && all[i].Intersect(origin, new Vec3d(1, -0.1, 0).Normalize(), 20, out double dist))
                        Assert.True(dist >= sel.Distance - 1e-9);
                }
            }
        }

        [Fact]
        public void FarSectionIsFoundWhenTheRangeIsLong()
        {
            List<Section> track = Lay(Point(2, 4, 1, 0), Point(60, 4, 1, 0));
            Boxes(track);
            Section far = track.Find(section => System.Math.Abs(section.Center.X - 50) < 0.3);

            // Дальність виділення можна збільшити командою: секція за 70 блоків від ока, через чанк від нього, теж
            // знаходиться. Промінь іде впоперек колії, тож влучити він може лише в неї
            var origin = new Vec3d(far.Center.X, far.Center.Y + 0.05, far.Center.Z + 70);
            SectionSelection sel = Pick(origin, new Vec3d(0, 0, -1), 100);

            Assert.NotNull(sel);
            Assert.Same(far, sel.Section);
            Assert.InRange(sel.Distance, 68, 70);
        }

        [Fact]
        public void NothingWhenTheRayMissesTheTrack()
        {
            List<Section> track = Lay(Point(2, 4, 1, 0), Point(60, 4, 1, 0));
            Boxes(track);

            Assert.Null(Pick(new Vec3d(10, 2, 12), new Vec3d(0, -1, 0), 4.5));
        }
    }
}
