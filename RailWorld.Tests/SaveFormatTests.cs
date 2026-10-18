using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ProtoBuf;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Формат, у якому колія лежить у збереженні світу: дані чанка (DataInChunk) з секціями, записами стрілок і
    /// перетинів. Поля читаються за номерами ProtoMember, тож змінений номер поля робить уже збережені світи
    /// нечитаними, і жоден інший тест цього не помітив би. До першого випуску номери можна міняти, але свідомо,
    /// разом із таблицею тут; після випуску вже ні.
    /// </summary>
    public class SaveFormatTests : TrackTestBase
    {
        // Поля кожного класу, що лежить у збереженні: номер і назва. Нове поле дістає наступний номер і дописується
        // сюди
        private static readonly Dictionary<Type, string> Fields = new Dictionary<Type, string>
        {
            [typeof(DataInChunk)] = "1 index, 2 sections, 3 Switches, 4 Revision, 5 Crossings",
            [typeof(Section)] = "1 _chunkAddres, 2 _trackWidth, 3 _startPosition, 4 _startTangent, 5 _startNormal, "
                + "6 _centerPosition, 7 _centerTangent, 8 _centerNormal, 9 _endPosition, 10 _endTangent, 11 _endNormal, "
                + "12 _firstRailInstalled, 13 _secondRailInstalled, 14 _sleeperInstalled, 15 _sleeperMaterial, "
                + "16 _railMaterial, 17 _secondRailMaterial, 18 _sleeperType, 19 _firstRailType, 20 _secondRailType, "
                + "21 _startLinks, 22 _endLinks, 23 _activeStartLink, 24 _activeEndLink, 25 _sleeperLength, "
                + "26 _ballastInstalled, 27 _ballastMaterial, 28 _blocked, 29 _startLeverSide, 30 _endLeverSide, "
                + "31 _startLeverManual, 32 _endLeverManual, 33 _startSwitchRef, 34 _endSwitchRef, 35 _railJoint, "
                + "36 _crossingRef",
            [typeof(SectionLink)] = "1 ChunkX, 2 ChunkY, 3 ChunkZ, 4 Index, 5 AtStart",
            [typeof(SwitchRef)] = "1 ChunkX, 2 ChunkY, 3 ChunkZ, 4 Key, 5 Branch, 6 Number",
            [typeof(CrossingRef)] = "1 ChunkX, 2 ChunkY, 3 ChunkZ, 4 Key, 5 Track, 6 Number",
            [typeof(RailSwitch)] = "1 TrunkIndex, 2 AtStart, 3 Branches, 4 Complete, 5 Resolved, 6 Shape, 7 Formula",
            [typeof(SwitchBranch)] = "1 Members, 2 PairSections, 3 OverlapSections, 4 BladeSections, 5 FrogFrom, 6 FrogTo, 7 InnerFirst",
            [typeof(RailCrossing)] = "1 Key, 2 Tracks, 3 Complete, 4 Resolved, 5 Formula",
            [typeof(CrossingTrack)] = "1 Members, 2 Center, 3 NodeFrom, 4 NodeTo",
        };

        private static string FieldsOf(Type type)
        {
            return string.Join(", ", type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(member => (member, attribute: member.GetCustomAttribute<ProtoMemberAttribute>()))
                .Where(pair => pair.attribute != null)
                .OrderBy(pair => pair.attribute.Tag)
                .Select(pair => pair.attribute.Tag + " " + pair.member.Name));
        }

        [Fact]
        public void FieldNumbersOfSavedClassesStayTheSame()
        {
            foreach (var pair in Fields)
            {
                Assert.True(pair.Value == FieldsOf(pair.Key),
                    "Змінилися поля збереження " + pair.Key.Name + ": збережені світи прочитаються криво. Якщо так задумано, "
                    + "онови таблицю цього тесту.\nЗараз: " + FieldsOf(pair.Key));
            }
        }

        [Fact]
        public void SwitchShapeIsSavedAsTheseNumbers()
        {
            Assert.Equal(0, (int)SwitchShape.Unknown);
            Assert.Equal(1, (int)SwitchShape.MainFirst);
            Assert.Equal(2, (int)SwitchShape.MainSecond);
            Assert.Equal(3, (int)SwitchShape.Fork);
        }

        private static byte[] Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                Serializer.Serialize(stream, value);
                return stream.ToArray();
            }
        }

        private static T Read<T>(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes)) return Serializer.Deserialize<T>(stream);
        }

        [Fact]
        public void ChunkWithSwitchAndCrossingIsReadBackAsItWasWritten()
        {
            // Спільна колія і дві гілки стрілки, а пряму гілку перетинає ще одна колія. Усе в одному чанку
            List<Section> trunk = Lay(Point(2, 4, 1, 0), Point(10, 4, 1, 0));
            List<Section> straight = Lay(Point(10, 4, 1, 0), Point(28, 4, 1, 0));
            double angle = Math.PI / 4;
            List<Section> arc = Lay(Point(10, 4, 1, 0),
                Point(10 + ArcRadius * Math.Sin(angle), 4 + ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            Section last = trunk[trunk.Count - 1];
            Join(last, straight[0]);
            Join(last, arc[0]);
            List<Section> across = Lay(Point(22.1, -4, 0, 1), Point(22.1, 12, 0, 1));

            Vec3i chunk = last.ChunkAddres;
            DataInChunk data = DataIn(chunk);
            Assert.All(trunk.Concat(straight).Concat(arc).Concat(across), section => Assert.Equal(chunk, section.ChunkAddres));

            // Запис стрілки з посиланнями в секціях гілок, важіль, положення і деталі, як їх ставить гра
            RailSwitch record = SwitchBuilder.Build(last, false, Resolve);
            data.Switches[record.Key] = record;
            last.SetLeverSide(false, 1, true);
            last.ToggleSwitch(false, 0);
            straight[0].SetSwitchRef(true, new SwitchRef { ChunkX = chunk.X, ChunkY = chunk.Y, ChunkZ = chunk.Z, Key = record.Key, Branch = 0, Number = 1 });
            straight[3].InstallPart(SectionPart.Ballast, "normal", "gravel-granite");
            arc[2].RemovePart(SectionPart.SecondRail);

            // Запис перетину і посилання на нього
            Section main = straight.First(s => across.Exists(o => AxisCrossing.OfLaid(s, o)));
            Section other = across.First(o => AxisCrossing.OfLaid(main, o));
            RailCrossing crossing = CrossingBuilder.Build(main, other, Resolve, out _);
            data.Crossings[crossing.Key] = crossing;
            main.SetCrossingRef(new CrossingRef { ChunkX = chunk.X, ChunkY = chunk.Y, ChunkZ = chunk.Z, Key = crossing.Key, Track = 0, Number = 1 });

            byte[] written = Write(data);
            DataInChunk read = Read<DataInChunk>(written);

            // Записане знову дає ті самі байти: нічого не загубилося і не змінилося
            Assert.Equal(written, Write(read));

            // Номер секції не зберігається, він ключ словника: після читання його роздано заново
            Assert.Equal(data.RailWaySections.Count, read.RailWaySections.Count);
            foreach (var pair in read.RailWaySections) Assert.Equal(pair.Key, pair.Value.IndexInChunk);

            Section readLast = read.RailWaySections[last.IndexInChunk];
            Assert.Equal(1, readLast.GetLeverSide(false));
            Assert.True(readLast.IsLeverManual(false));
            Assert.Equal(1, readLast.GetSwitchPosition(false));
            Assert.Equal(2, readLast.GetLinks(false).Count);
            Assert.Equal(0, readLast.FullEndPosition.DistanceTo(last.FullEndPosition), 9);
            Assert.True(read.RailWaySections[straight[3].IndexInChunk].BallastInstalled);
            Assert.False(read.RailWaySections[arc[2].IndexInChunk].SecondRailInstalled);
            Assert.Equal(record.Key, read.RailWaySections[straight[0].IndexInChunk].GetSwitchRef(true).Key);
            Assert.Equal(crossing.Key, read.RailWaySections[main.IndexInChunk].GetCrossingRef().Key);

            Assert.True(record.SameAs(read.Switches[record.Key]));
            Assert.True(crossing.SameAs(read.Crossings[crossing.Key]));
            Assert.False(read.Switches[record.Key].Outdated);

            // Секції розкладено по клітинках заново, а нова секція дістає номер, якого ще не було
            Assert.Contains(read.RailWaySections[main.IndexInChunk], new TrackIndex(c => c.Equals(chunk) ? read.Grid : null).Around(main.GetGlobalPos(), 1, 1));
            int next = read.Add(new Section(new PointOnBezierCurve { position = new Vec3d(5, 10, 5), tangent = new Vec3f(1, 0, 0), normal = new Vec3f(0, 0, 1) },
                new PointOnBezierCurve { position = new Vec3d(5.25, 10, 5), tangent = new Vec3f(1, 0, 0), normal = new Vec3f(0, 0, 1) },
                new PointOnBezierCurve { position = new Vec3d(5.5, 10, 5), tangent = new Vec3f(1, 0, 0), normal = new Vec3f(0, 0, 1) },
                TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength));
            Assert.True(next > data.RailWaySections.Keys.Max());
        }

        [Fact]
        public void RecordComputedByAnotherFormulaIsOutdated()
        {
            // Запис, пораховано іншою версією розрахунку: після читання він застарілий і перерахується
            RailSwitch record = new RailSwitch { TrunkIndex = 7, AtStart = true, Resolved = true, Formula = RailSwitch.CurrentFormula + 1 };
            Assert.True(Read<RailSwitch>(Write(record)).Outdated);

            RailCrossing crossing = new RailCrossing { Key = 3, Resolved = true, Formula = RailCrossing.CurrentFormula + 1 };
            Assert.True(Read<RailCrossing>(Write(crossing)).Outdated);
        }
    }
}
