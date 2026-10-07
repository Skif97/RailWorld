using System;
using System.Collections.Generic;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Посилання з кінця однієї секції на сусідню: де вона лежить і яким кінцем приєднана.
    /// </summary>
    [ProtoContract]
    public class SectionLink
    {
        [ProtoMember(1)] public int ChunkX;
        [ProtoMember(2)] public int ChunkY;
        [ProtoMember(3)] public int ChunkZ;
        [ProtoMember(4)] public int Index;

        /// <summary>true, якщо сусід приєднаний своїм початком, false якщо кінцем.</summary>
        [ProtoMember(5)] public bool AtStart;

        public SectionLink() { }

        public SectionLink(Section target, bool atStart)
        {
            ChunkX = target.ChunkAddres.X;
            ChunkY = target.ChunkAddres.Y;
            ChunkZ = target.ChunkAddres.Z;
            Index = target.IndexInChunk;
            AtStart = atStart;
        }

        public Vec3i Chunk => new Vec3i(ChunkX, ChunkY, ChunkZ);

        public bool PointsTo(Section section)
        {
            Vec3i chunk = section.ChunkAddres;
            return Index == section.IndexInChunk && ChunkX == chunk.X && ChunkY == chunk.Y && ChunkZ == chunk.Z;
        }
    }

    /// <summary>
    /// Куди веде кінець секції: сусідня секція і кінець, через який у неї заїжджають.
    /// </summary>
    public class SectionStep
    {
        public Section Section;
        public bool EnterAtStart;
    }

    /// <summary>
    /// Дані колії кількох чанків на час однієї операції на сервері: кожен чанк читається один раз,
    /// а змінені записуються і розсилаються клієнтам наприкінці.
    /// </summary>
    public class RailDataSession
    {
        private IWorldAccessor world;
        private Dictionary<Vec3i, DataInChunk> loaded = new Dictionary<Vec3i, DataInChunk>();
        private HashSet<Vec3i> dirty = new HashSet<Vec3i>();

        public RailDataSession(IWorldAccessor world)
        {
            this.world = world;
        }

        public bool HasChanges => dirty.Count > 0;

        /// <summary>
        /// Чи завантажений сам чанк. Якщо ні, про колію в ньому зараз нічого сказати не можна.
        /// </summary>
        public bool IsChunkLoaded(Vec3i chunkCoord)
        {
            return world.BlockAccessor.GetChunk(chunkCoord.X, chunkCoord.Y, chunkCoord.Z) != null;
        }

        /// <summary>
        /// Дані колії чанка або null, якщо чанк не завантажений чи колії в ньому немає і create == false.
        /// </summary>
        public DataInChunk Get(Vec3i chunkCoord, bool create = false)
        {
            if (loaded.TryGetValue(chunkCoord, out DataInChunk data)) return data;
            if (!IsChunkLoaded(chunkCoord)) return null;

            data = DataInChunk.Get(world, chunkCoord);
            if (data == null)
            {
                if (!create) return null;
                data = new DataInChunk();
            }

            loaded[chunkCoord.Clone()] = data;
            return data;
        }

        public Section GetSection(SectionLink link)
        {
            DataInChunk data = Get(link.Chunk);
            if (data == null) return null;
            data.RailWaySections.TryGetValue(link.Index, out Section section);
            return section;
        }

        public void MarkDirty(Vec3i chunkCoord)
        {
            dirty.Add(chunkCoord.Clone());
        }

        /// <summary>
        /// Записує змінені чанки і розсилає їх клієнтам.
        /// </summary>
        public void SaveAll()
        {
            foreach (Vec3i chunkCoord in dirty)
            {
                if (!loaded.TryGetValue(chunkCoord, out DataInChunk data)) continue;
                DataInChunk.Save(world, chunkCoord, data);
                RailWorld.SendChunkDataToClients(chunkCoord, data);
            }
            dirty.Clear();
        }
    }

    /// <summary>
    /// З'єднання між секціями колії. Працює тільки на сервері.
    /// </summary>
    public static class SectionLinker
    {
        // Кінці ділянок лягають на край блока з похибкою нарізки, тому збіг шукаємо з допуском
        public const double PositionTolerance = 0.1;

        // Наскільки напрямки двох кінців мають бути протилежні (косинус кута між ними).
        // Напрямки колії кратні 22.5°, тож сусідній напрямок сюди вже не проходить
        public const double DirectionTolerance = -0.95;

        /// <summary>
        /// Наскільки близько мають лежати обидва кінці двох секцій, щоб вважати їх однією і тією самою.
        /// Навмисно набагато тісніше за PositionTolerance: перші секції відгалуження відходять від основної
        /// колії на частки сантиметра, і з грубішою міркою їх викидало як повтори, а стрілка зсувалася далі.
        /// Справжній повтор (той самий маршрут прокладено вдруге) збігається точно.
        /// </summary>
        public const double DuplicateTolerance = 0.001;

        /// <summary>
        /// Скільки сусідів може бути в одного кінця секції. Один це звичайна колія, два це стрілка:
        /// основна колія і відгалуження. Потрійних стрілок не буває.
        /// </summary>
        public const int MaxLinksPerEnd = 2;

        /// <summary>
        /// Чи лежить у чанку секція з тими самими кінцями, в будь-якому з двох напрямків.
        /// </summary>
        public static bool HasSameSection(DataInChunk data, Section section)
        {
            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;

            foreach (Section other in data.RailWaySections.Values)
            {
                Vec3d otherStart = other.FullStartPosition;
                Vec3d otherEnd = other.FullEndPosition;

                bool same = otherStart.DistanceTo(start) <= DuplicateTolerance && otherEnd.DistanceTo(end) <= DuplicateTolerance;
                bool reversed = otherStart.DistanceTo(end) <= DuplicateTolerance && otherEnd.DistanceTo(start) <= DuplicateTolerance;
                if (same || reversed) return true;
            }
            return false;
        }

        /// <summary>
        /// З'єднує щойно прокладену ділянку: сусідні секції між собою, а два крайні кінці з тим, що вже лежить поруч.
        /// </summary>
        public static void LinkStretch(RailDataSession session, List<Section> stretch)
        {
            for (int i = 0; i + 1 < stretch.Count; i++)
            {
                // Перевіряємо збіг, бо секція між ними могла не записатися в незавантажений чанк
                if (stretch[i].FullEndPosition.DistanceTo(stretch[i + 1].FullStartPosition) > PositionTolerance) continue;
                Link(session, stretch[i], false, stretch[i + 1], true);
            }

            // Кінці, що лишилися без сусіда: краї ділянки і місця, де нова секція не будувалася,
            // бо там уже лежала така сама
            foreach (Section section in stretch)
            {
                if (section.GetLinks(true).Count == 0) LinkToNeighbours(session, section, true);
                if (section.GetLinks(false).Count == 0) LinkToNeighbours(session, section, false);
            }
        }

        /// <summary>
        /// Шукає секції, кінець яких збігається з цим кінцем і дивиться назустріч, і з'єднує з ними.
        /// </summary>
        public static void LinkToNeighbours(RailDataSession session, Section section, bool atStart)
        {
            Vec3d pos = section.GetEndPosition(atStart);
            Vec3f dir = section.GetOutwardDirection(atStart);

            // Секція зберігається в чанку свого центру, а центр не далі ніж за блок від кінця
            var chunks = new HashSet<Vec3i>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        chunks.Add(ModMath.FindChunk(pos.X + dx, pos.Y + dy, pos.Z + dz));

            foreach (Vec3i chunkCoord in chunks)
            {
                DataInChunk data = session.Get(chunkCoord);
                if (data == null) continue;

                foreach (Section other in data.RailWaySections.Values)
                {
                    if (other == section) continue;

                    for (int end = 0; end < 2; end++)
                    {
                        bool otherAtStart = end == 0;
                        if (other.GetEndPosition(otherAtStart).DistanceTo(pos) > PositionTolerance) continue;

                        Vec3f otherDir = other.GetOutwardDirection(otherAtStart);
                        if (dir.X * otherDir.X + dir.Y * otherDir.Y + dir.Z * otherDir.Z > DirectionTolerance) continue;

                        // До стрілки третю гілку не приєднуємо. Зв'язок, який уже є, це не зачіпає
                        bool known = other.GetLinks(otherAtStart).Exists(link => link.PointsTo(section));
                        if (!known && (other.GetLinks(otherAtStart).Count >= MaxLinksPerEnd
                            || section.GetLinks(atStart).Count >= MaxLinksPerEnd)) continue;

                        Link(session, section, atStart, other, otherAtStart);
                    }
                }
            }
        }

        private static void Link(RailDataSession session, Section a, bool aAtStart, Section b, bool bAtStart)
        {
            if (a.AddLink(aAtStart, new SectionLink(b, bAtStart))) session.MarkDirty(a.ChunkAddres);
            if (b.AddLink(bAtStart, new SectionLink(a, aAtStart))) session.MarkDirty(b.ChunkAddres);
        }

        /// <summary>
        /// Прибирає посилання на секцію в усіх її сусідів. Викликати перед видаленням секції.
        /// </summary>
        public static void Unlink(RailDataSession session, Section section)
        {
            for (int end = 0; end < 2; end++)
            {
                foreach (SectionLink link in section.GetLinks(end == 0))
                {
                    Section other = session.GetSection(link);
                    if (other != null && other.RemoveLink(link.AtStart, section)) session.MarkDirty(other.ChunkAddres);
                }
            }
        }

        /// <summary>
        /// Куди веде кінець секції. Якщо зв'язку ще немає (колія прокладена до появи зв'язків
        /// або сусідній чанк тоді не був завантажений), шукає сусіда зараз і запам'ятовує.
        /// Повертає null, якщо далі колії немає або сусідній чанк не завантажений.
        /// Після виклику треба зберегти сесію, якщо в ній з'явилися зміни.
        /// </summary>
        public static SectionStep GetNext(RailDataSession session, Section section, bool atStart)
        {
            // Посилання на секції, яких уже немає, прибираємо. Якщо чанк сусіда не завантажений, не чіпаємо
            List<SectionLink> links = new List<SectionLink>(section.GetLinks(atStart));
            foreach (SectionLink link in links)
            {
                if (session.GetSection(link) != null || !session.IsChunkLoaded(link.Chunk)) continue;
                section.RemoveLink(atStart, link);
                session.MarkDirty(section.ChunkAddres);
            }

            if (section.GetLinks(atStart).Count == 0) LinkToNeighbours(session, section, atStart);

            SectionLink active = section.GetActiveLink(atStart);
            if (active == null) return null;

            Section next = session.GetSection(active);
            if (next == null) return null;

            return new SectionStep { Section = next, EnterAtStart = active.AtStart };
        }
    }
}
