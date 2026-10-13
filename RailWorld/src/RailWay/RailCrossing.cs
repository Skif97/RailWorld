using ProtoBuf;
using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Посилання секції на перетин, у вузлі якого вона лежить: де запис про перетин, котра це з двох колій
    /// і котра за рахунком секція на ній, з одиниці.
    /// </summary>
    [ProtoContract]
    public class CrossingRef
    {
        [ProtoMember(1)] public int ChunkX;
        [ProtoMember(2)] public int ChunkY;
        [ProtoMember(3)] public int ChunkZ;
        /// <summary>Ключ перетину в його чанку, RailCrossing.Key.</summary>
        [ProtoMember(4)] public int Key;
        [ProtoMember(5)] public int Track;
        [ProtoMember(6)] public int Number;

        /// <summary>Чанк, у якому лежить запис про перетин.</summary>
        public Vec3i Chunk => new Vec3i(ChunkX, ChunkY, ChunkZ);

        public bool SameAs(CrossingRef other)
        {
            return other != null && ChunkX == other.ChunkX && ChunkY == other.ChunkY && ChunkZ == other.ChunkZ
                && Key == other.Key && Track == other.Track && Number == other.Number;
        }

        public bool PointsTo(Vec3i chunk, int key)
        {
            return ChunkX == chunk.X && ChunkY == chunk.Y && ChunkZ == chunk.Z && Key == key;
        }
    }

    /// <summary>
    /// Одна з двох колій перетину: її секції по порядку крізь вузол. Номери секцій рахуються з одиниці.
    /// </summary>
    [ProtoContract]
    public class CrossingTrack
    {
        /// <summary>
        /// Секції колії по порядку: кілька перед вузлом, вузол, кілька за ним. Напрямок задає перетнута секція,
        /// від свого початку до кінця; AtStart у кожного посилання каже, котрим кінцем у секцію заїжджають,
        /// рухаючись у цьому напрямку.
        /// </summary>
        [ProtoMember(1)]
        public List<SectionLink> Members = new List<SectionLink>();

        /// <summary>Номер секції, вісь якої перетинає вісь другої колії.</summary>
        [ProtoMember(2)]
        public int Center;

        /// <summary>Перша і остання секції вузла: ті, що габаритом лежать на другій колії.</summary>
        [ProtoMember(3)]
        public int NodeFrom;
        [ProtoMember(4)]
        public int NodeTo;

        public bool InNode(int number)
        {
            return number >= NodeFrom && number <= NodeTo;
        }

        public bool SameAs(CrossingTrack other)
        {
            if (other == null || Center != other.Center || NodeFrom != other.NodeFrom || NodeTo != other.NodeTo
                || Members.Count != other.Members.Count) return false;
            for (int n = 0; n < Members.Count; n++)
            {
                SectionLink mine = Members[n], theirs = other.Members[n];
                if (mine.ChunkX != theirs.ChunkX || mine.ChunkY != theirs.ChunkY || mine.ChunkZ != theirs.ChunkZ
                    || mine.Index != theirs.Index || mine.AtStart != theirs.AtStart) return false;
            }
            return true;
        }

        /// <summary>Чи перетнута секція та сама, що й в other.</summary>
        public bool SameCenter(CrossingTrack other)
        {
            if (other == null || Center < 1 || Center > Members.Count || other.Center < 1 || other.Center > other.Members.Count) return false;
            SectionLink mine = Members[Center - 1], theirs = other.Members[other.Center - 1];
            return mine.ChunkX == theirs.ChunkX && mine.ChunkY == theirs.ChunkY && mine.ChunkZ == theirs.ChunkZ && mine.Index == theirs.Index;
        }

        [ProtoAfterDeserialization]
        private void OnDeserialized()
        {
            if (Members == null) Members = new List<SectionLink>();
        }
    }

    /// <summary>
    /// Перетин двох колій в одному рівні. Зв'язків між двома коліями немає, вагонетка з однієї на іншу не
    /// переходить; запис потрібен, щоб вузол малювався вузлом: зі спільними шпалами замість двох рядів,
    /// покладених один на один. Зберігається в чанку перетнутої секції основної колії.
    /// Рахує його CrossingBuilder, коли перетин з'являється або колія у вузлі змінюється.
    /// </summary>
    [ProtoContract]
    public class RailCrossing
    {
        /// <summary>Номер у чанку перетнутої секції основної колії: він же ключ запису.</summary>
        [ProtoMember(1)]
        public int Key;

        /// <summary>Дві колії. Перша основна: та, що лежала тут раніше; друга покладена через неї.</summary>
        [ProtoMember(2)]
        public List<CrossingTrack> Tracks = new List<CrossingTrack>();

        /// <summary>
        /// Перетин зібрано: у вузлі на кожній секції обох колій стоять обидві рейки і шпала. Лише тоді він
        /// малюється вузлом. Доки бракує хоч однієї деталі, це дві колії, покладені одна на одну.
        /// </summary>
        [ProtoMember(3)]
        public bool Complete;

        /// <summary>
        /// Усі секції вдалося прочитати. Якщо ні (чанк однієї з колій не був завантажений), запис треба
        /// перерахувати, коли чанк з'явиться.
        /// </summary>
        [ProtoMember(4)]
        public bool Resolved;

        public bool SameAs(RailCrossing other)
        {
            return other != null && Key == other.Key && Complete == other.Complete && Resolved == other.Resolved
                && Tracks.Count == 2 && other.Tracks.Count == 2 && Tracks[0].SameAs(other.Tracks[0]) && Tracks[1].SameAs(other.Tracks[1]);
        }

        /// <summary>Колія, на яку вказує посилання секції, або null, якщо такої в записі немає.</summary>
        public CrossingTrack TrackOf(CrossingRef place)
        {
            return Tracks.Count == 2 && place.Track >= 0 && place.Track < 2 ? Tracks[place.Track] : null;
        }

        [ProtoAfterDeserialization]
        private void OnDeserialized()
        {
            if (Tracks == null) Tracks = new List<CrossingTrack>();
        }
    }

    /// <summary>
    /// Рахує перетин з колії. Чиста геометрія і зв'язки секцій: сусідні секції дістає через resolve,
    /// тому працює однаково на сервері й у тестах.
    /// </summary>
    public static class CrossingBuilder
    {
        /// <summary>
        /// На скільки секцій далі за вузол запис тримає кожну колію: розкладці шпал вузла треба знати перші
        /// власні шпали колій за ним.
        /// </summary>
        public const int MarginSections = 4;

        // Запобіжник: скільки секцій у кожен бік від перетнутої вузол може займати
        private const int MaxWalk = 64;

        // Секція колії разом із кінцем, яким у неї заїжджають
        private class Step
        {
            public Section Section;
            public bool Entered;
        }

        // Колія в один бік від перетнутої секції
        private class Side
        {
            public List<Step> Steps = new List<Step>();
            // Колія далі є, але прочитати її не вдалося
            public bool Cut;
        }

        /// <summary>
        /// Чи вісь секції a перетинає вісь секції b в одному рівні. Колії, що проходять одна над одною,
        /// і сусідні секції однієї колії це не перетин.
        /// </summary>
        public static bool Cross(Section a, Section b)
        {
            // Прокладання лишає лише два випадки: колії в одній площині або щонайменше за CrossingClearance
            // одна над одною. Межа посередині між ними відрізняє їх із запасом
            if (Math.Abs(a.GetGlobalPos().Y - b.GetGlobalPos().Y) >= RouteCurve.CrossingClearance / 2) return false;
            return AxisCrossing.OfLaid(a, b);
        }

        /// <summary>
        /// Перетин секції main із секцією other, осі яких перетинаються. partsInPlace: у прочитаній частині
        /// вузла на кожній секції стоять обидві рейки і шпала.
        /// </summary>
        public static RailCrossing Build(Section main, Section other, Func<SectionLink, Section> resolve, out bool partsInPlace)
        {
            partsInPlace = true;
            var result = new RailCrossing { Key = main.IndexInChunk, Resolved = true };

            Section[] centers = { main, other };
            var sides = new Side[2][];
            var whole = new List<Section>[2];
            for (int k = 0; k < 2; k++)
            {
                sides[k] = new[] { Walk(centers[k], true, resolve), Walk(centers[k], false, resolve) };
                whole[k] = new List<Section> { centers[k] };
                foreach (Side side in sides[k])
                {
                    foreach (Step step in side.Steps) whole[k].Add(step.Section);
                }
            }

            for (int k = 0; k < 2; k++)
            {
                var track = new CrossingTrack();
                // Скільки секцій у кожен бік лежить габаритом на другій колії
                int[] inNode = new int[2];
                for (int end = 0; end < 2; end++)
                {
                    List<Step> steps = sides[k][end].Steps;
                    while (inNode[end] < steps.Count && OnTrack(steps[inNode[end]].Section, whole[1 - k])) inNode[end]++;
                }

                // Назад від перетнутої секції, у зворотному порядку: рух уперед заходить у секцію з іншого кінця
                int before = Math.Min(sides[k][0].Steps.Count, inNode[0] + MarginSections);
                for (int n = before - 1; n >= 0; n--)
                {
                    Step step = sides[k][0].Steps[n];
                    track.Members.Add(new SectionLink(step.Section, !step.Entered));
                }
                track.Members.Add(new SectionLink(centers[k], true));
                track.Center = track.Members.Count;
                int after = Math.Min(sides[k][1].Steps.Count, inNode[1] + MarginSections);
                for (int n = 0; n < after; n++)
                {
                    Step step = sides[k][1].Steps[n];
                    track.Members.Add(new SectionLink(step.Section, step.Entered));
                }

                track.NodeFrom = track.Center - inNode[0];
                track.NodeTo = track.Center + inNode[1];

                // Колія обірвалася на непрочитаному чанку раніше, ніж запис набрав потрібну довжину
                if ((sides[k][0].Cut && before < inNode[0] + MarginSections) || (sides[k][1].Cut && after < inNode[1] + MarginSections)) result.Resolved = false;

                if (!Fitted(centers[k])) partsInPlace = false;
                for (int end = 0; end < 2; end++)
                {
                    for (int n = 0; n < inNode[end]; n++)
                    {
                        if (!Fitted(sides[k][end].Steps[n].Section)) partsInPlace = false;
                    }
                }

                result.Tracks.Add(track);
            }

            result.Complete = partsInPlace && result.Resolved;
            return result;
        }

        /// <summary>
        /// Рахує перетин заново, маючи його попередній запис. Якщо всю колію прочитати не вдалося, а попередній
        /// запис описує той самий перетин, його дані лишаються: колія в незавантаженому чанку змінитися
        /// не могла. Запис при цьому позначений як недорахований.
        /// </summary>
        public static RailCrossing Rebuild(RailCrossing previous, Section main, Section other, Func<SectionLink, Section> resolve)
        {
            RailCrossing built = Build(main, other, resolve, out bool partsInPlace);
            if (built.Resolved || previous == null || previous.Tracks.Count != 2
                || !previous.Tracks[0].SameCenter(built.Tracks[0]) || !previous.Tracks[1].SameCenter(built.Tracks[1])) return built;

            built.Tracks = previous.Tracks;
            built.Complete = previous.Complete && partsInPlace;
            return built;
        }

        private static bool Fitted(Section section)
        {
            return section.RailsInstalled && section.SleeperInstalled;
        }

        // Чи секція лежить габаритом на котрійсь із секцій другої колії
        private static bool OnTrack(Section section, List<Section> other)
        {
            foreach (Section candidate in other)
            {
                if (Footprint.Overlap(section, candidate)) return true;
            }
            return false;
        }

        // Іде по колії від секції center через її кінець exitAtStart, доки колія одна і не далі, ніж може
        // тягнутися вузол
        private static Side Walk(Section center, bool exitAtStart, Func<SectionLink, Section> resolve)
        {
            var side = new Side();
            TrackWalker walk = TrackWalker.Leaving(center, exitAtStart, resolve);
            double run = 0;
            // Колія скінчилася або розходиться стрілкою: далі це вже не вона
            while (side.Steps.Count < MaxWalk && run <= RouteCurve.MaxNodeReach + MarginSections && walk.Next())
            {
                side.Steps.Add(new Step { Section = walk.Section, Entered = walk.Entered });
                run += walk.Section.FullStartPosition.DistanceTo(walk.Section.FullEndPosition);
            }
            side.Cut = walk.Stop == WalkStop.Unread;
            return side;
        }
    }
}
