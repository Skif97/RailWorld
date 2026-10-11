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

            foreach (var kvp in sections)
            {
                Section section = kvp.Value;

                // У стрілці шпали двох гілок перетинаються, тому замість двох малюється одна спільна.
                // Малює її секція першої гілки; секція другої свою шпалу пропускає
                bool sharedSleeper = false;
                if (section.SleeperInstalled && track.FindSleeperPair(section, out Section partner, out bool primary, out int fromEnd, out int pairs)
                    && partner.SleeperInstalled)
                {
                    sharedSleeper = true;
                    if (primary) AddSharedSleeper(chunkCoord, section, partner, pairs < StaggerMinPairs ? 2 : Math.Min(fromEnd, 2));
                }

                if (section.SleeperInstalled && !sharedSleeper)
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

        // У стрілці, де спільних шпал менше за це, дві останні не робляться коротшими в шаховому порядку:
        // на короткій стрілці вони займали б половину її довжини
        private const int StaggerMinPairs = 6;

        /// <summary>
        /// Спільна шпала двох секцій у стрілці: одна суцільна на обидві колії.
        /// Лежить упоперек середнього напрямку двох колій, серединою між ними, тож сусідні спільні шпали
        /// майже паралельні й ідуть рівним кроком, навіть коли колії розходяться круто. Завдовжки вона така,
        /// щоб дістати до зовнішніх кінців власних шпал обох колій: за рейки виступає так само, як звичайна.
        /// Дві останні шпали стрілки коротші і лежать у шаховому порядку: передостання тримає три рейки
        /// з боку першої колії (обидві її рейки і ближню рейку другої), остання три рейки з боку другої,
        /// і за третьою рейкою виступає на звичайний хвостик. fromEnd це номер пари від краю стрілки.
        /// </summary>
        private void AddSharedSleeper(Vec3i chunkCoord, Section a, Section b, int fromEnd)
        {
            Vec3d centerA = Middle(a), centerB = Middle(b);
            Vec3d sideA = new Vec3d(a.CenterNormal.X, a.CenterNormal.Y, a.CenterNormal.Z).Normalize();
            Vec3d sideB = new Vec3d(b.CenterNormal.X, b.CenterNormal.Y, b.CenterNormal.Z).Normalize();

            // З якого боку від кожної колії лежить сусідня: зовнішній край шпали з протилежного
            Vec3d between = centerB - centerA;
            double towardB = between.X * sideA.X + between.Z * sideA.Z >= 0 ? 1 : -1;
            double towardA = between.X * sideB.X + between.Z * sideB.Z >= 0 ? -1 : 1;

            // Точки, до яких шпала має дістати з кожного боку
            Vec3d from = centerA - sideA * (towardB * a.SleeperLength / 2);
            Vec3d to = centerB - sideB * (towardA * b.SleeperLength / 2);

            // Край за третьою рейкою: ближня до сусіда рейка лежить за пів ширини колії від осі в його бік,
            // а хвостик це те, на скільки звичайна шпала виступає за рейку. Разом це зсув від осі колії
            if (fromEnd == 1) to = centerB + sideB * (towardA * (b.TrackWidth - b.SleeperLength / 2));
            else if (fromEnd == 0) from = centerA + sideA * (towardB * (a.TrackWidth - a.SleeperLength / 2));

            // Напрямок шпали: середній між нормалями двох колій, тобто впоперек середнього напрямку самих колій.
            // Нормаль другої секції може дивитися в інший бік, тоді береться з протилежним знаком
            Vec3d sameB = sideA.X * sideB.X + sideA.Z * sideB.Z >= 0 ? sideB : sideB * -1;
            Vec3d across = sideA + sameB;
            if (across.Length() < 1e-6) return;
            across.Normalize();

            // Шпала лежить на прямій через середину між коліями. Її кінці це проєкції потрібних точок на цю пряму
            Vec3d middle = new Vec3d((centerA.X + centerB.X) / 2, (centerA.Y + centerB.Y) / 2, (centerA.Z + centerB.Z) / 2);
            double first = (from.X - middle.X) * across.X + (from.Y - middle.Y) * across.Y + (from.Z - middle.Z) * across.Z;
            double second = (to.X - middle.X) * across.X + (to.Y - middle.Y) * across.Y + (to.Z - middle.Z) * across.Z;
            double low = Math.Min(first, second), high = Math.Max(first, second);
            double length = high - low;
            if (length < 1e-6) return;

            // Уздовж колії: нормаль секції лежить ліворуч від її напрямку, тож напрямок відновлюється з неї
            float yaw = (float)Math.Atan2(across.Z, -across.X);
            float roll = (float)Math.Asin(GameMath.Clamp(across.Y, -1, 1));

            Vec3d start = a.FullStartPosition, end = a.FullEndPosition;
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            float pitch = (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));

            Vec3d position = middle + across * ((low + high) / 2);
            renderer.AddSleeper(chunkCoord, a.SleeperMaterial ?? "oak", position, new Vec3f(pitch, yaw, roll), (float)length);
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

        private static Vec3d Middle(Section section)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            return new Vec3d((start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2);
        }
    }
}
