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
        private CrossingPartsBuilder crossingBuilder;
        // Рейки перетинів, пораховані за поточну перебудову чанка: їх питає і розкладка рейок, і збирач деталей
        private Dictionary<string, CrossingRails> crossingRails = new Dictionary<string, CrossingRails>();
        private bool disposed;

        public TrackChunkRenderer(ICoreClientAPI capi, RailWayRendererSystem renderer, ClientTrack track, SwitchAnimator animator)
        {
            this.capi = capi;
            this.renderer = renderer;
            this.track = track;
            this.animator = animator;
            builder = new SwitchPartsBuilder(renderer, track, animator);
            crossingBuilder = new CrossingPartsBuilder(renderer);
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
            crossingRails.Clear();

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

                if (!inNode && track.FindCrossing(section, out RailCrossing crossing, out CrossingRef at))
                    inNode = CrossingLayout(layouts, at.Chunk, crossing).Hidden.Contains(section);

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
                    // Між шпалою і рейкою лежить підкладка, тож шпала нижча за підошву рейки на її товщину
                    var sleeperPos = new Vec3d(globalPos.X, globalPos.Y - RailProfile.PlateThickness, globalPos.Z);
                    renderer.AddSleeper(chunkCoord, section.SleeperMaterial ?? "oak", sleeperPos, new Vec3f(pitch, yaw, roll), section.SleeperLength);

                    // Скріплення: під кожною рейкою, там де вона лежить на шпалі. Друге повернуте на пів оберта,
                    // тож костилі двох рейок стоять дзеркально відносно осі колії. Де на секції стик рейок,
                    // там само стоїть і стикова накладка
                    Vec3f normal = section.CenterNormal;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        SectionPart rail = side > 0 ? SectionPart.FirstRail : SectionPart.SecondRail;
                        if (!section.IsInstalled(rail)) continue;

                        double offset = section.TrackWidth / 2 * side;
                        var onRail = new Vec3d(globalPos.X + normal.X * offset, globalPos.Y + normal.Y * offset, globalPos.Z + normal.Z * offset);
                        Vec3f turned = side > 0 ? new Vec3f(pitch, yaw, roll) : new Vec3f(-pitch, yaw + GameMath.PI, -roll);
                        string metal = section.GetMaterial(rail) ?? "iron";
                        renderer.AddRailPart(chunkCoord, RailWayRendererSystem.FasteningPart, metal, onRail, turned);
                        if (section.RailJoint) renderer.AddRailPart(chunkCoord, RailWayRendererSystem.JointPart, metal, onRail, turned);
                    }
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
                    List<Timber> timbers = Layout(layouts, chunkCoord, record).Timbers;
                    if (timbers.Count == 0) continue;
                    foreach (Timber timber in timbers) AddTimber(chunkCoord, timber);

                    List<Section>[] branches = SwitchBranches(chunkCoord, record, out _);
                    if (branches != null) AddNodeFastenings(chunkCoord, timbers, branches, (rail, at) => CheckRailBeside(rail.Section, rail.Part == SectionPart.FirstRail), null);
                }
                foreach (RailCrossing record in data.Crossings.Values)
                {
                    List<Timber> timbers = CrossingLayout(layouts, chunkCoord, record).Timbers;
                    if (timbers.Count == 0) continue;
                    foreach (Timber timber in timbers) AddTimber(chunkCoord, timber);

                    CrossingRails crossingRails = RailsOf(chunkCoord, record);
                    if (CrossingTracks(record, out List<Section>[] tracks, out _, out _))
                        AddNodeFastenings(chunkCoord, timbers, tracks, (rail, at) => crossingRails != null && crossingRails.GuardBeside(at) ? FasteningKind.GuardSpikes : (FasteningKind?)null, crossingRails);
                }
            }

            if (railSections.Count > 0)
                renderer.RebuildRails(chunkCoord, railSections, BladeShape);

            var wood = new Dictionary<string, IList<Timber>>();
            foreach (Section section in bladeSections) builder.Build(chunkCoord, section, (chunk, record) => SwitchWood(layouts, wood, chunk, record));
            if (data != null)
            {
                foreach (RailCrossing record in data.Crossings.Values) crossingBuilder.Build(chunkCoord, record, RailsOf(chunkCoord, record));
            }
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

            List<Section>[] branches = SwitchBranches(chunk, record, out Section trunk);
            if (branches == null) return layout;

            layout = NodeTimbers.Lay(branches[0], branches[1], SectionPicker.LeverPosition(trunk, record.AtStart));
            known[key] = layout;
            return layout;
        }

        // Усе дерево під стрілкою: її бруси і шпали тих секцій гілок, що лежать на власних шпалах. known це вже
        // зібране за цю перебудову
        private IList<Timber> SwitchWood(Dictionary<string, TimberLayout> layouts, Dictionary<string, IList<Timber>> known, Vec3i chunk, RailSwitch record)
        {
            string key = chunk.X + "_" + chunk.Y + "_" + chunk.Z + "_" + record.Key;
            if (known.TryGetValue(key, out IList<Timber> wood)) return wood;

            TimberLayout layout = Layout(layouts, chunk, record);
            var all = new List<Timber>(layout.Timbers);
            List<Section>[] branches = SwitchBranches(chunk, record, out _);
            if (branches != null)
            {
                foreach (List<Section> branch in branches)
                {
                    foreach (Section section in branch)
                    {
                        if (layout.Hidden.Contains(section) || !section.SleeperInstalled) continue;
                        Vec3f normal = section.CenterNormal;
                        all.Add(new Timber
                        {
                            Position = section.GetGlobalPos(), Across = new Vec3d(normal.X, normal.Y, normal.Z).Normalize(),
                            Length = section.SleeperLength, Owner = section
                        });
                    }
                }
            }

            known[key] = all;
            return all;
        }

        // Секції двох гілок зібраної стрілки по порядку від стику, trunk її спільна секція. null у незібраної
        // стрілки і якщо її прочитати не вдається
        private List<Section>[] SwitchBranches(Vec3i chunk, RailSwitch record, out Section trunk)
        {
            trunk = null;
            DataInChunk data = DataInChunk.GetClient(chunk);
            if (data == null || !record.Complete || record.Branches.Count != 2 || !data.RailWaySections.TryGetValue(record.TrunkIndex, out trunk)) return null;

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
                if (branches[k].Count == 0) return null;
            }
            return branches;
        }

        // Ставить скріплення рейок вузла на його бруси, там де рейка лежить на брусі. Скріплення складене
        // з частин: підкладка, а де їй немає місця або до рейки щось прилягає, прокладка під підошву; і пари
        // костилів окремо з кожного боку рейки. На підкладці їх усі чотири. Немає їх з того боку, де до рейки
        // прилягає гостряк чи контррейка, де костиль опинився б під іншою рейкою перетину, і де він не
        // потрапляє в дерево. guardBeside каже про контррейку, crossing це рейки перетину або null у стрілки.
        // Під гостряком і хрестовиною стрілки скріплень немає: гостряк рухається, а хрестовина прибита окремо
        private void AddNodeFastenings(Vec3i chunkCoord, List<Timber> timbers, List<Section>[] tracks, System.Func<RailRun, Vec3d, FasteningKind?> guardBeside,
            CrossingRails crossing)
        {
            var rails = new List<RailRun>();
            foreach (List<Section> sections in tracks)
            {
                foreach (Section section in sections)
                {
                    Vec3d start = section.FullStartPosition, middle = section.GetGlobalPos(), end = section.FullEndPosition;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        SectionPart part = side > 0 ? SectionPart.FirstRail : SectionPart.SecondRail;
                        if (!section.IsInstalled(part) || ReplacedBySwitch(section, side > 0)) continue;

                        double offset = section.TrackWidth / 2 * side;
                        rails.Add(new RailRun
                        {
                            Section = section, Part = part,
                            From = Shifted(start, section.StartNormal, offset), Middle = Shifted(middle, section.CenterNormal, offset), To = Shifted(end, section.EndNormal, offset)
                        });
                    }
                }
            }

            foreach (NodeFastening fastening in NodeFastenings.Lay(timbers, rails, guardBeside))
            {
                // На перетині рейка другої колії обривається на рейці основної: де рейки немає, кріпити нічого
                if (crossing != null && !crossing.HasRail(fastening.Position, fastening.Along)) continue;

                Section section = fastening.Section;

                Vec3d along = fastening.Along;
                float yaw = (float)Math.Atan2(along.X, along.Z);
                float pitch = (float)Math.Atan2(along.Y, Math.Sqrt(along.X * along.X + along.Z * along.Z));
                float roll = (float)((Math.Asin(GameMath.Clamp(section.StartNormal.Y, -1, 1)) + Math.Asin(GameMath.Clamp(section.EndNormal.Y, -1, 1))) / 2);

                // Як і на звичайній колії, під другою рейкою скріплення повернуте на пів оберта
                bool first = fastening.Part == SectionPart.FirstRail;
                var straight = new Vec3f(pitch, yaw, roll);
                var reversed = new Vec3f(-pitch, yaw + GameMath.PI, -roll);
                string metal = section.GetMaterial(fastening.Part) ?? "iron";

                // Підкладка, якщо їй є місце і до рейки нічого не прилягає; інакше прокладка завширшки з підошву
                bool plate = fastening.Kind == FasteningKind.Plate;
                string under = plate ? RailWayRendererSystem.PlatePart : RailWayRendererSystem.PadPart;
                renderer.AddRailPart(chunkCoord, under, metal, fastening.Position, first ? straight : reversed);

                // Бік рейки, який NodeFastenings.NailsOnWood рахує додатним: для напрямку (x, z) це (z, -x).
                // Нормаль секції лежить з протилежного боку: у секції, що йде на схід, вона дивиться на південь
                // (перевірено на покладеній секції). Перша рейка лежить з боку нормалі, тож назовні від колії
                // в неї від'ємний бік, у другої додатний
                var right = new Vec3d(along.Z, 0, -along.X);
                if (right.Length() < 1e-9) continue;
                right.Normalize();
                int outer = first ? -1 : 1;

                for (int side = -1; side <= 1; side += 2)
                {
                    // З боку осі колії до рейки може прилягати гостряк чи контррейка
                    bool inner = side != outer;
                    if (inner && (fastening.Kind == FasteningKind.GuardSpikes || fastening.Kind == FasteningKind.OuterSpikes)) continue;

                    var away = new Vec3d(right.X * side, 0, right.Z * side);
                    AddSpikePair(chunkCoord, metal, fastening.Position, along, away, side, timbers, crossing);
                }

                // Біля контррейки ще одна пара з її боку, що дивиться на вісь колії
                if (fastening.Kind == FasteningKind.GuardSpikes)
                {
                    var inward = new Vec3d(-right.X * outer, 0, -right.Z * outer);
                    var guard = new Vec3d(fastening.Position.X + inward.X * CrossingGeometry.GuardAxis, fastening.Position.Y,
                        fastening.Position.Z + inward.Z * CrossingGeometry.GuardAxis);
                    // Лише якщо контррейка справді лежить у цьому місці: костилі мають щось тримати
                    if (crossing == null || crossing.Covers(guard)) AddSpikePair(chunkCoord, metal, guard, along, inward, -outer, timbers, crossing);
                }
            }
        }

        // Пара костилів з одного боку рейки: axis точка на осі рейки, away бік, куди вони стоять від неї, side
        // той самий бік числом, як його рахує NodeFastenings.NailsOnWood. Не ставиться, якщо костилі не
        // потрапляють у дерево або опинилися б під металом перетину
        private void AddSpikePair(Vec3i chunkCoord, string metal, Vec3d axis, Vec3d along, Vec3d away, int side, List<Timber> timbers, CrossingRails crossing)
        {
            if (!NodeFastenings.NailsOnWood(axis, along, timbers, side)) return;

            var at = new Vec3d(axis.X + away.X * NodeFastenings.SpikeAside, axis.Y, axis.Z + away.Z * NodeFastenings.SpikeAside);
            if (crossing != null)
            {
                double length = Math.Sqrt(along.X * along.X + along.Z * along.Z);
                if (length < 1e-9) return;
                for (int end = -1; end <= 1; end += 2)
                {
                    double forward = NodeFastenings.SpikeAlong * end / length;
                    if (crossing.Covers(new Vec3d(at.X + along.X * forward, at.Y, at.Z + along.Z * forward))) return;
                }
            }
            renderer.AddOutward(chunkCoord, RailWayRendererSystem.SpikePairPart, metal, at, away);
        }

        // Чи біля цієї рейки секції стоїть контррейка стрілки (навпроти хрестовини, біля зовнішньої рейки гілки),
        // і як тоді кріпити рейку. На першій і останній секції контррейки лежать її відігнуті кінці: вони до
        // бруса не кріпляться, костилі там лише із зовнішнього боку ходової рейки. null, якщо контррейки немає
        private FasteningKind? CheckRailBeside(Section section, bool left)
        {
            for (int end = 0; end < 2; end++)
            {
                if (!track.FindSwitch(section, end == 0, out RailSwitch record, out SwitchRef place) || !record.Complete) continue;

                SwitchBranch branch = record.BranchOf(place);
                if (branch.FrogFrom <= 0 || place.Number < branch.FrogFrom || place.Number > branch.FrogTo) continue;

                // Зовнішня рейка це та, що не з боку сусідньої гілки
                Section partner = track.Opposite(record, place);
                if (partner == null || left == SwitchGeometry.TowardPartner(section, partner) > 0) continue;

                bool flared = place.Number == branch.FrogFrom || place.Number == branch.FrogTo;
                return flared ? FasteningKind.OuterSpikes : FasteningKind.GuardSpikes;
            }
            return null;
        }

        private static Vec3d Shifted(Vec3d point, Vec3f normal, double offset)
        {
            return new Vec3d(point.X + normal.X * offset, point.Y + normal.Y * offset, point.Z + normal.Z * offset);
        }

        // Розкладка шпал перетину із записом record, що лежить у чанку chunk. У незібраного перетину, і якщо
        // його прочитати не вдається, розкладка порожня: кожна секція лишається зі своєю шпалою
        private TimberLayout CrossingLayout(Dictionary<string, TimberLayout> known, Vec3i chunk, RailCrossing record)
        {
            string key = chunk.X + "_" + chunk.Y + "_" + chunk.Z + "_x" + record.Key;
            if (known.TryGetValue(key, out TimberLayout layout)) return layout;
            known[key] = layout = new TimberLayout();
            if (!CrossingTracks(record, out List<Section>[] tracks, out _, out int[] centers)) return layout;

            layout = NodeTimbers.LayCrossing(tracks[0], centers[0], tracks[1], centers[1]);
            known[key] = layout;
            return layout;
        }

        // Рейки перетину із записом record, що лежить у чанку chunk. null у незібраного перетину і якщо його
        // прочитати не вдається: тоді кожна секція малює власні рейки
        private CrossingRails RailsOf(Vec3i chunk, RailCrossing record)
        {
            string key = chunk.X + "_" + chunk.Y + "_" + chunk.Z + "_" + record.Key;
            if (crossingRails.TryGetValue(key, out CrossingRails rails)) return rails;

            rails = CrossingTracks(record, out List<Section>[] tracks, out List<bool>[] entered, out int[] centers)
                ? CrossingGeometry.Build(tracks, entered, centers) : null;
            crossingRails[key] = rails;
            return rails;
        }

        // Секції двох колій зібраного перетину по порядку крізь вузол: від перетнутої секції в обидва боки,
        // до першої секції без шпали чи рейок. entered каже, котрим кінцем у секцію заїжджають у цьому
        // порядку, centers номери перетнутих секцій. false, якщо перетин не зібрано або прочитати його не вдається
        private bool CrossingTracks(RailCrossing record, out List<Section>[] tracks, out List<bool>[] entered, out int[] centers)
        {
            tracks = new List<Section>[2];
            entered = new List<bool>[2];
            centers = new int[2];
            if (!record.Complete || record.Tracks.Count != 2) return false;

            for (int k = 0; k < 2; k++)
            {
                CrossingTrack crossed = record.Tracks[k];
                if (crossed.Center < 1 || crossed.Center > crossed.Members.Count) return false;
                if (track.FindSection(crossed.Members[crossed.Center - 1]) == null) return false;

                int from = crossed.Center - 1, to = crossed.Center - 1;
                while (from - 1 >= 0 && Fitted(crossed.Members[from - 1])) from--;
                while (to + 1 < crossed.Members.Count && Fitted(crossed.Members[to + 1])) to++;

                tracks[k] = new List<Section>();
                entered[k] = new List<bool>();
                for (int n = from; n <= to; n++)
                {
                    tracks[k].Add(track.FindSection(crossed.Members[n]));
                    entered[k].Add(crossed.Members[n].AtStart);
                }
                centers[k] = crossed.Center - 1 - from;
            }
            return true;
        }

        // Чи секція за посиланням відома клієнту і має шпалу й обидві рейки
        private bool Fitted(SectionLink link)
        {
            Section section = track.FindSection(link);
            return section != null && section.SleeperInstalled && section.RailsInstalled;
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

            // Між брусом і рейкою лежить підкладка, тож брус нижчий за підошву рейки на її товщину, як і шпала
            var position = new Vec3d(timber.Position.X, timber.Position.Y - RailProfile.PlateThickness, timber.Position.Z);
            renderer.AddSleeper(chunkCoord, timber.Owner.SleeperMaterial ?? "oak", position, new Vec3f(pitch, yaw, roll), (float)timber.Length);

            // Пластина під гостряки: на осі колії, упоперек неї, як сам брус. З металу рейки над нею
            if (timber.SlidePlate)
            {
                Section owner = timber.Owner;
                string metal = owner.GetMaterial(SectionPart.FirstRail) ?? owner.GetMaterial(SectionPart.SecondRail) ?? "iron";
                renderer.AddRailPart(chunkCoord, RailWayRendererSystem.SlidePlatePart, metal, owner.GetGlobalPos(), new Vec3f(pitch, yaw, roll));
            }
        }

        // Ухил секції: підйом її хорди
        private static float Pitch(Section section)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            return (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
        }

        // Чи місце цієї рейки секції займає деталь стрілки: гостряк або хрестовина
        private bool ReplacedBySwitch(Section section, bool left)
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
                if (left == SwitchGeometry.TowardPartner(section, partner) > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Каже рендеру рейок не малювати ту рейку, місце якої займає деталь стрілки чи перетину: внутрішню
        /// рейку колії на секціях гостряка і хрестовини, рейку з жолобами у вузлі перетину. Для решти рейок
        /// повертає null.
        /// </summary>
        private RailShape BladeShape(Section section, bool left)
        {
            if (ReplacedBySwitch(section, left)) return new RailShape { Hidden = true };

            // Рейку секції у вузлі перетину заміняє рейка перетину, з жолобами для реборд
            if (track.FindCrossing(section, out RailCrossing crossing, out CrossingRef at))
            {
                CrossingRails rails = RailsOf(at.Chunk, crossing);
                if (rails != null && rails.Hides(section, left ? SectionPart.FirstRail : SectionPart.SecondRail)) return new RailShape { Hidden = true };
            }
            return null;
        }
    }
}
