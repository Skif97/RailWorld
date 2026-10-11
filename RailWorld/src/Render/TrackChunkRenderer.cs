using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Вигляд колії чанка: розкладає по рендерах шпали, рейки, важелі й деталі стрілок із даних, які
    /// клієнт знає про колію, і перекладає деталі стрілки, поки вона переводиться.
    /// </summary>
    public class TrackChunkRenderer
    {
        private ICoreClientAPI capi;
        private RailWayRendererSystem renderer;
        private ClientTrack track;
        private SwitchAnimator animator;
        private SwitchPartsBuilder builder;
        private bool disposed;

        public TrackChunkRenderer(ICoreClientAPI capi, RailWayRendererSystem renderer, ClientTrack track, SwitchAnimator animator)
        {
            this.capi = capi;
            this.renderer = renderer;
            this.track = track;
            this.animator = animator;
            builder = new SwitchPartsBuilder(renderer, track, animator);
        }

        /// <summary>Після цього відкладені перебудови вже нічого не малюють: рендера більше немає.</summary>
        public void Dispose()
        {
            disposed = true;
        }

        /// <summary>Збирає заново все, що малюється для колії чанка: шпали, важелі стрілок, рейки і деталі стрілок.</summary>
        public void Rebuild(Vec3i chunkCoord, IDictionary<int, Section> sections)
        {
            renderer.RemoveChunkParts(chunkCoord);
            renderer.ClearRails(chunkCoord);
            // Деталі стрілок не викидаються наперед: незмінні лишаються з готовим мешем
            renderer.BeginParts(chunkCoord);

            var railSections = new List<Section>();
            var bladeSections = new List<Section>();
            // Розкладки брусів стрілок, які знадобилися цій перебудові: стрілка може лежати і в сусідньому чанку
            var layouts = new Dictionary<string, TimberLayout>();

            foreach (var kvp in sections)
            {
                Section section = kvp.Value;

                // У стрілці шпали двох гілок перетиналися б, тому власні шпали секцій там не кладуться:
                // стрілка розкладає свої бруси сама
                bool inNode = false;
                for (int e = 0; e < 2 && !inNode; e++)
                {
                    if (track.FindSwitch(section, e == 0, out RailSwitch record, out SwitchRef place))
                        inNode = Layout(layouts, place.Chunk, record).Hidden.Contains(section);
                }

                if (section.SleeperInstalled && !inNode)
                {
                    // Шпала лежить посередині секції, під серединою рейок, і повернута так само, як вони:
                    // поворот і ухил з хорди секції, крен середній між початком і кінцем
                    Vec3d start = section.FullStartPosition;
                    Vec3d end = section.FullEndPosition;
                    Vec3d globalPos = new Vec3d((start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2);
                    double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
                    float yaw = (float)Math.Atan2(dx, dz);
                    float pitch = (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
                    float roll = (float)((Math.Asin(GameMath.Clamp(section.StartNormal.Y, -1, 1)) + Math.Asin(GameMath.Clamp(section.EndNormal.Y, -1, 1))) / 2);
                    renderer.AddSleeper(chunkCoord, section.SleeperMaterial ?? "oak", globalPos, new Vec3f(pitch, yaw, roll), section.SleeperLength);
                }

                // Важіль стрілки на кожному кінці, з якого колія розходиться. Стоїть прямо, а нахилом убік
                // показує, котра з двох гілок увімкнена
                for (int e = 0; e < 2; e++)
                {
                    bool atStart = e == 0;
                    if (!section.HasSwitch(atStart)) continue;

                    animator.Track(chunkCoord, section, atStart);
                    AddLever(chunkCoord, section, atStart);
                }

                if (section.FirstRailInstalled || section.SecondRailInstalled)
                {
                    railSections.Add(section);
                    bladeSections.Add(section);
                }
            }

            // Бруси стрілок, записи яких лежать у цьому чанку
            DataInChunk data = DataInChunk.GetClient(chunkCoord);
            if (data != null)
            {
                foreach (RailSwitch record in data.Switches.Values)
                {
                    foreach (Timber timber in Layout(layouts, chunkCoord, record).Timbers) AddTimber(chunkCoord, timber);
                }
            }

            if (railSections.Count > 0)
                renderer.RebuildRails(chunkCoord, railSections, BladeShape);

            foreach (Section section in bladeSections) builder.Build(chunkCoord, section);
            renderer.EndParts(chunkCoord);
        }

        // Чанки, вигляд колії в яких треба зібрати заново, бо змінилася колія в сусідньому
        private HashSet<Vec3i> pendingRender = new HashSet<Vec3i>();
        private bool pendingRenderScheduled;

        /// <summary>
        /// Вигляд колії в чанку залежить і від сусідніх чанків: спільна шпала стрілки малюється з двох секцій,
        /// які можуть лежати в різних чанках, а дані чанків приходять окремо і в довільному порядку.
        /// Тому після зміни колії в чанку його сусіди перемальовуються теж, трохи згодом і всі разом.
        /// </summary>
        public void QueueNeighbours(Vec3i chunkCoord)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        Vec3i neighbour = new Vec3i(chunkCoord.X + dx, chunkCoord.Y + dy, chunkCoord.Z + dz);
                        if (DataInChunk.GetClient(neighbour) != null) pendingRender.Add(neighbour);
                    }
                }
            }

            if (pendingRender.Count == 0 || pendingRenderScheduled) return;
            pendingRenderScheduled = true;
            capi.Event.RegisterCallback(dt =>
            {
                pendingRenderScheduled = false;
                var coords = new List<Vec3i>(pendingRender);
                pendingRender.Clear();

                foreach (Vec3i coord in coords)
                {
                    DataInChunk data = DataInChunk.GetClient(coord);
                    if (!disposed && data != null) Rebuild(coord, data.RailWaySections);
                }
            }, 100);
        }

        // На скільки радіан важіль стрілки нахилений убік у кожному з двох положень
        private const float LeverLean = 0.45f;

        // Важіль стрілки. Стоїть прямо, а нахилом убік показує, котра з двох гілок увімкнена; під час
        // переведення перекидається з одного боку на інший
        private void AddLever(Vec3i chunkCoord, Section section, bool atStart)
        {
            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            float yaw = (float)Math.Atan2(dx, dz);
            float pitch = (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));

            float target = section.GetSwitchPosition(atStart) == 0 ? LeverLean : -LeverLean;
            double moved = animator.Progress(chunkCoord, section.IndexInChunk, atStart);
            float lean = (float)(target * (2 * moved - 1));
            renderer.AddLever(chunkCoord, SectionPicker.LeverPosition(section, atStart), new Vec3f(pitch, yaw, lean));
        }

        /// <summary>
        /// Щокадру, поки котрась стрілка рухається: ставить її важіль і гостряки в нове проміжне положення.
        /// Решту чанка не чіпає; хрестовина й контррейки не змінюються і лишаються з готовим мешем.
        /// </summary>
        public void AnimateSwitches()
        {
            animator.Advance((chunk, index, atStart) =>
            {
                DataInChunk data = DataInChunk.GetClient(chunk);
                if (data == null || !data.RailWaySections.TryGetValue(index, out Section trunk) || !trunk.HasSwitch(atStart)) return;

                AddLever(chunk, trunk, atStart);
                foreach (SectionLink link in trunk.GetLinks(atStart))
                {
                    Section branch = track.FindSection(link);
                    if (branch != null) builder.Build(link.Chunk, branch);
                }
            });
        }

        // Розкладка брусів стрілки із записом record, що лежить у чанку chunk. known це вже пораховані
        // за цю перебудову. У незібраної стрілки, і якщо стрілку прочитати не вдається, розкладка порожня:
        // кожна секція лишається зі своєю шпалою
        private TimberLayout Layout(Dictionary<string, TimberLayout> known, Vec3i chunk, RailSwitch record)
        {
            string key = chunk.X + "_" + chunk.Y + "_" + chunk.Z + "_" + record.Key;
            if (known.TryGetValue(key, out TimberLayout layout)) return layout;
            known[key] = layout = new TimberLayout();

            DataInChunk data = DataInChunk.GetClient(chunk);
            if (data == null || !record.Complete || record.Branches.Count != 2 || !data.RailWaySections.TryGetValue(record.TrunkIndex, out Section trunk)) return layout;

            var branches = new List<Section>[2];
            for (int k = 0; k < 2; k++)
            {
                SwitchBranch branch = record.Branches[k];
                branches[k] = new List<Section>();
                // Гілка вся, скільки її знає запис: тіло стрілки і кілька секцій за ним. Шпала одразу за тілом
                // теж може тягнутися під сусідню гілку. Далі першої секції без шпали розкладка не йде
                foreach (SectionLink member in branch.Members)
                {
                    Section section = track.FindSection(member);
                    if (section == null || !section.SleeperInstalled) break;
                    branches[k].Add(section);
                }
                if (branches[k].Count == 0) return layout;
            }

            layout = NodeTimbers.Lay(branches[0], branches[1], SectionPicker.LeverPosition(trunk, record.AtStart));
            known[key] = layout;
            return layout;
        }

        // Кладе брус стрілки: з того самого дерева, що й шпала секції, на місці якої він лежить
        private void AddTimber(Vec3i chunkCoord, Timber timber)
        {
            if (timber.Length < 1e-6) return;

            // Брус лежить уздовж across: шпала лежить уздовж нормалі колії, ліворуч від її напрямку, тож поворот
            // відновлюється з неї так само
            Vec3d across = timber.Across;
            float yaw = (float)Math.Atan2(across.Z, -across.X);
            float roll = (float)Math.Asin(GameMath.Clamp(across.Y, -1, 1));

            // Ухил як у колії на цьому місці: дві гілки лежать в одній площині
            float pitch = Pitch(timber.Owner);

            renderer.AddSleeper(chunkCoord, timber.Owner.SleeperMaterial ?? "oak", timber.Position, new Vec3f(pitch, yaw, roll), (float)timber.Length);
        }

        // Ухил секції: підйом її хорди
        private static float Pitch(Section section)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            return (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
        }

        /// <summary>
        /// Каже рендеру рейок не малювати ту рейку, місце якої займає деталь стрілки: внутрішню рейку колії
        /// на секціях гостряка і на секціях хрестовини. Для решти рейок повертає null.
        /// </summary>
        private RailShape BladeShape(Section section, bool left)
        {
            // Стрілка може бути з будь-якого боку секції, і з обох одразу, якщо стрілки стоять густо:
            // перевіряємо кожен бік окремо
            for (int end = 0; end < 2; end++)
            {
                if (!track.FindSwitch(section, end == 0, out RailSwitch record, out SwitchRef place)) continue;
                // Недобудована стрілка: звичайні рейки
                if (!record.Complete || !record.BranchOf(place).ReplacesInnerRail(place.Number)) continue;

                // Внутрішня рейка це та, що з боку сусідньої гілки
                Section partner = track.Opposite(record, place);
                if (partner == null) continue;
                if (left == SwitchGeometry.TowardPartner(section, partner) > 0) return new RailShape { Hidden = true };
            }
            return null;
        }
    }
}
