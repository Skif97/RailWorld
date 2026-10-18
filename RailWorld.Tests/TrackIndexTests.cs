using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Покладена колія, розкладена по клітинках: правила питають у неї, які секції лежать біля точки.
    /// </summary>
    public class TrackIndexTests : TrackTestBase
    {
        [Fact]
        public void FindsEverySectionNearThePointAndNothingFarAway()
        {
            // Довга колія через кілька чанків, у тому числі через межу чанка на x = 32 і x = 64
            List<Section> laid = Lay(Point(10, 5, 1, 0), Point(90, 5, 1, 0));
            TrackIndex track = SectionsIn;

            foreach (double x in new[] { 12.0, 31.5, 32.5, 50, 63.9, 64.1, 88 })
            {
                var point = new Vec3d(x, 0, 5);
                List<Section> near = track.Around(point, 3, 2);

                foreach (Section section in laid)
                {
                    double away = Math.Abs(section.GetGlobalPos().X - x);
                    // Усе, що в межах запитаних трьох блоків, знайдено
                    if (away <= 3) Assert.Contains(section, near);
                    // А далекого немає: інакше це знову перебір усієї колії
                    if (away > 4) Assert.DoesNotContain(section, near);
                }
            }
        }

        [Fact]
        public void TrackOnAnotherLevelIsFoundOnlyWhenAskedThatFar()
        {
            // Колія на 40 блоків вище: інший чанк по висоті
            Lay(new RoutePoint { Position = new Vec3d(10, 40, 5), Tangent = new Vec3d(1, 0, 0) },
                new RoutePoint { Position = new Vec3d(20, 40, 5), Tangent = new Vec3d(1, 0, 0) });

            Assert.Empty(SectionsIn.Around(new Vec3d(15, 0, 5), 3, 2));
            Assert.NotEmpty(SectionsIn.Around(new Vec3d(15, 38, 5), 3, 2));
        }

        [Fact]
        public void RemovedSectionIsNotFoundAnyMore()
        {
            List<Section> laid = Lay(Point(10, 5, 1, 0), Point(20, 5, 1, 0));
            Section gone = laid[laid.Count / 2];
            Vec3d middle = gone.GetGlobalPos();
            Assert.Contains(gone, SectionsIn.Around(middle, 1, 1));

            Forget(gone);

            List<Section> near = SectionsIn.Around(middle, 1, 1);
            Assert.DoesNotContain(gone, near);
            // Сусіди на місці
            Assert.Contains(laid[laid.Count / 2 - 1], near);
        }

        [Fact]
        public void GridIsRebuiltAfterReading()
        {
            // Сітка не зберігається: прочитані дані чанка розкладають секції по клітинках заново
            var data = new DataInChunk();
            List<Section> laid = Plan(Point(10, 5, 1, 0), Point(20, 5, 1, 0));
            foreach (Section section in laid) data.Add(section);

            DataInChunk read = ProtoBuf.Serializer.DeepClone(data);
            var track = new TrackIndex(chunk => chunk.Equals(laid[0].ChunkAddres) ? read.Grid : null);

            List<Section> near = track.Around(laid[0].GetGlobalPos(), 30, 2);
            Assert.Equal(read.RailWaySections.Count, near.Count);
            foreach (Section section in read.RailWaySections.Values) Assert.Contains(section, near);
        }

        [Fact]
        public void MarkerNearTrackFindsTheClosestPointOfItsAxis()
        {
            Lay(Point(0, 0, 1, 0), Point(20, 0, 1, 0));

            // На 2,4 блока збоку від колії: стає на її вісь навпроти себе
            Assert.True(TrackSurvey.NearestAxis(new Vec3d(10.3, 0, 2.4), TrackSurvey.MarkerReach, SectionsIn, out Section near, out Vec3d at));
            Assert.Equal(10.3, at.X, 6);
            Assert.Equal(0, at.Z, 6);
            Assert.InRange(10.3, Math.Min(near.FullStartPosition.X, near.FullEndPosition.X), Math.Max(near.FullStartPosition.X, near.FullEndPosition.X));

            // За краєм колії, на 2,4 блока від нього: найближча точка це сам край
            Assert.True(TrackSurvey.NearestAxis(new Vec3d(22.4, 0, 0), TrackSurvey.MarkerReach, SectionsIn, out _, out at));
            Assert.Equal(0, at.DistanceTo(new Vec3d(20, 0, 0)), 6);
        }

        [Fact]
        public void MarkerFarFromTrackOrOnAnotherLevelStaysFree()
        {
            Lay(Point(0, 0, 1, 0), Point(20, 0, 1, 0));

            Assert.False(TrackSurvey.NearestAxis(new Vec3d(10, 0, 2.6), TrackSurvey.MarkerReach, SectionsIn, out _, out _));
            Assert.False(TrackSurvey.NearestAxis(new Vec3d(10, 3, 1), TrackSurvey.MarkerReach, SectionsIn, out _, out _));
            Assert.False(TrackSurvey.NearestAxis(new Vec3d(10, -3, 1), TrackSurvey.MarkerReach, SectionsIn, out _, out _));
        }

        [Fact]
        public void MarkerAboveOrBelowTrackWithinReachFindsIt()
        {
            Lay(Point(0, 0, 1, 0), Point(20, 0, 1, 0));

            // На два блоки вище і нижче, навпроти середини колії і за її краєм
            Assert.True(TrackSurvey.NearestAxis(new Vec3d(10, 2, 1), TrackSurvey.MarkerReach, SectionsIn, out _, out Vec3d at));
            Assert.Equal(10, at.X, 6);
            Assert.True(TrackSurvey.NearestAxis(new Vec3d(22, -2, 0), TrackSurvey.MarkerReach, SectionsIn, out _, out at));
            Assert.Equal(0, at.DistanceTo(new Vec3d(20, 0, 0)), 6);
        }

        [Fact]
        public void TrackOnTheMarkerLevelWinsOverTrackAboveIt()
        {
            List<Section> level = Lay(Point(0, 0, 1, 0), Point(20, 0, 1, 0));
            // Колія на два блоки вище, у плані ближча до точки
            Lay(new RoutePoint { Position = new Vec3d(0, 2, 1.5), Tangent = new Vec3d(1, 0, 0) },
                new RoutePoint { Position = new Vec3d(20, 2, 1.5), Tangent = new Vec3d(1, 0, 0) });

            Assert.True(TrackSurvey.NearestAxis(new Vec3d(10, 0, 1.2), TrackSurvey.MarkerReach, SectionsIn, out Section near, out _));
            Assert.Contains(near, level);
        }

        [Fact]
        public void MarkerBetweenTwoTracksTakesTheCloserOne()
        {
            List<Section> south = Lay(Point(0, 0, 1, 0), Point(20, 0, 1, 0));
            Lay(Point(0, 4, 1, 0), Point(20, 4, 1, 0));

            Assert.True(TrackSurvey.NearestAxis(new Vec3d(10, 0, 1.8), TrackSurvey.MarkerReach, SectionsIn, out Section near, out _));
            Assert.Contains(near, south);
        }

        [Fact]
        public void MarkerNearTrackOnASlopeFindsIt()
        {
            // Колія на схилі: висота береться в найближчій точці осі, а не в середині секції
            Lay(new RoutePoint { Position = new Vec3d(0, 0, 0), Tangent = new Vec3d(1, 0.15, 0).Normalize() },
                new RoutePoint { Position = new Vec3d(20, 3, 0), Tangent = new Vec3d(1, 0.15, 0).Normalize() });

            Assert.True(TrackSurvey.NearestAxis(new Vec3d(10, 1.5, 2), TrackSurvey.MarkerReach, SectionsIn, out _, out Vec3d at));
            Assert.Equal(10, at.X, 1);
        }
    }
}
