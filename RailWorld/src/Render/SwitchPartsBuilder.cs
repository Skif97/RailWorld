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
    /// Збирає деталі стрілки: гостряк із тягою, хрестовину і контррейку. Кожна це меш, натягнутий на контури
    /// перерізів уздовж рейки; готові частини віддаються рендеру. Де стоять рейки двох колій і де між ними
    /// хрестовина, рахує SwitchGeometry; тут лише форма деталей.
    /// </summary>
    public class SwitchPartsBuilder
    {
        private RailWayRendererSystem renderer;
        private ClientTrack track;
        private SwitchAnimator animator;

        public SwitchPartsBuilder(RailWayRendererSystem renderer, ClientTrack track, SwitchAnimator animator)
        {
            this.renderer = renderer;
            this.track = track;
            this.animator = animator;
        }

        // Хід вістря: на скільки блоків відведений гостряк відходить від рамної рейки
        private const double BladeThrow = 0.12;

        /// <summary>
        /// Ставить деталі стрілки, якщо секція це перша секція однієї з двох її колій: гостряк, вусовик і осердя.
        /// Усі три йдуть по лінії внутрішньої рейки колії, тому збираються з її перерізів.
        /// </summary>
        /// <param name="wood">
        /// Каже, яке дерево лежить під стрілкою із записом у цьому чанку: її бруси і шпали секцій. По ньому
        /// хрестовину прибивають костилями.
        /// </param>
        public void Build(Vec3i chunkCoord, Section section, System.Func<Vec3i, RailSwitch, IList<Timber>> wood = null)
        {
            // Секція має бути першою на гілці стрілки; з котрого боку від неї стрілка, перевіряємо окремо
            RailSwitch record = null;
            SwitchRef place = null;
            SectionLink partnerLink = null;
            Section partner = null;
            bool switchAtStart = true, found = false;
            for (int end = 0; end < 2 && !found; end++)
            {
                switchAtStart = end == 0;
                if (!track.FindSwitch(section, switchAtStart, out record, out place) || place.Number != 1) continue;

                partnerLink = record.Opposite(place);
                partner = partnerLink == null ? null : track.FindSection(partnerLink);
                found = partner != null;
            }
            if (!found) return;
            // Недобудована стрілка малюється звичайними рейками, без деталей стрілки
            if (!record.Complete) return;

            Vec3i switchChunk = place.Chunk;
            DataInChunk switchData = DataInChunk.GetClient(switchChunk);
            if (switchData == null || !switchData.RailWaySections.TryGetValue(record.TrunkIndex, out Section trunk)) return;

            // Внутрішня рейка, з боку другої гілки: її місце займають гостряк і хрестовина
            bool? innerFirst = record.BranchOf(place)?.InnerIsFirst(place.Number), partnerInnerFirst = record.Branches[1 - place.Branch].InnerIsFirst(place.Number);
            if (innerFirst == null || partnerInnerFirst == null) return;
            bool left = innerFirst.Value;
            SectionPart part = left ? SectionPart.FirstRail : SectionPart.SecondRail;
            if (!section.IsInstalled(part)) return;

            string material = section.GetMaterial(part) ?? "iron";
            // Хрестовину й тягу ставить перша гілка: вони одні на обидві колії
            bool primary = place.Branch == 0;
            // Перерізи внутрішньої рейки потрібні лише для меша, тому рахуються тут, а не зберігаються
            BranchGeometry own = SwitchGeometry.Branch(section, switchAtStart, left, partner, partnerLink.AtStart, partnerInnerFirst.Value, track.FindSection);

            var setup = new BladeSetup
            {
                Chunk = chunkCoord.Clone(), Section = section, Left = left, Material = material, Primary = primary, Own = own,
                BladeSections = record.BranchOf(place).BladeSections, SwitchChunk = switchChunk.Clone(), TrunkIndex = record.TrunkIndex,
                AtStart = record.AtStart, Branch = place.Branch
            };
            blades[SectionKey.Of(section)] = setup;
            PlaceBlade(setup);

            // Хрестовина одна на обидві колії: її ставить перша з них
            if (primary && own.Throat >= 0) AddFrog(chunkCoord, section, left, material, own, wood?.Invoke(switchChunk, record));

            // Контррейка в кожної колії своя
            if (own.Throat >= 0) AddCheckRail(chunkCoord, section, left, own);
        }

        // Що потрібно, щоб поставити гостряк гілки в будь-якому положенні стрілки. Геометрія гілки від положення не
        // залежить: її рахують, коли чанк перебудовується, а під час переведення лише перекладають гостряк і тягу
        private class BladeSetup
        {
            // Чанк, де малюється гілка, і її перша секція
            public Vec3i Chunk;
            public Section Section;
            // З котрого боку секції внутрішня рейка і з якого вона металу
            public bool Left;
            public string Material;
            // Тягу ставить перша гілка: вона одна на обидва гостряки
            public bool Primary;
            public BranchGeometry Own;
            public int BladeSections;
            // Де стрілка: чанк і кінець її спільної секції, і котра це з двох гілок
            public Vec3i SwitchChunk;
            public int TrunkIndex;
            public bool AtStart;
            public int Branch;
        }

        // Гілки стрілок, пораховані в перебудовах чанків, за першою секцією гілки
        private readonly Dictionary<SectionKey, BladeSetup> blades = new Dictionary<SectionKey, BladeSetup>();

        /// <summary>Забуває гілки, що малюються в чанку: чанк перебудовується або колія з нього зникла.</summary>
        public void Forget(Vec3i chunkCoord)
        {
            foreach (SectionKey key in blades.Where(pair => pair.Value.Chunk.Equals(chunkCoord)).Select(pair => pair.Key).ToList()) blades.Remove(key);
        }

        /// <summary>
        /// Ставить гостряк і тягу гілки, що починається секцією section, у теперішнє положення стрілки, за
        /// геометрією з останньої перебудови чанка. Хрестовину, контррейку і їхні болти не чіпає: вони не
        /// рухаються. Нічого не робить, якщо гілку ще не порахували.
        /// </summary>
        public void MoveBlade(Section section)
        {
            if (blades.TryGetValue(SectionKey.Of(section), out BladeSetup setup)) PlaceBlade(setup);
        }

        // Гостряк гілки і, у першої гілки, тяга, у положенні, де стрілка зараз
        private void PlaceBlade(BladeSetup setup)
        {
            // Наскільки гостряк відведений від рамної рейки: 0 притиснутий, 1 відведений на повний хід.
            // Під час переведення стрілки він між цими положеннями
            DataInChunk switchData = DataInChunk.GetClient(setup.SwitchChunk);
            if (switchData == null || !switchData.RailWaySections.TryGetValue(setup.TrunkIndex, out Section trunk)) return;
            bool active = trunk.GetSwitchPosition(setup.AtStart) == setup.Branch;
            double moved = animator.Progress(setup.SwitchChunk, setup.TrunkIndex, setup.AtStart);
            double open = active ? 1 - moved : moved;
            BranchGeometry own = setup.Own;
            Section section = setup.Section;

            // ── Гостряк ──
            int bladePoints = Math.Min(setup.BladeSections * 2 + 1, own.Frames.Count);
            if (bladePoints >= 2)
            {
                double[] stock = SwitchGeometry.Stock(own, bladePoints, section.TrackWidth);

                var blade = new LoftPart();
                LoftPoint[][] ends = null;
                // Ставить переріз гостряка за position перерізів колії від стрілки; position може бути дробом
                void AddStation(double position, bool withHead)
                {
                    int before = Math.Min((int)Math.Floor(position), bladePoints - 2);
                    double fraction = position - before;
                    RailFrame frame = fraction < 1e-6 ? own.Frames[before]
                        : fraction > 1 - 1e-6 ? own.Frames[before + 1] : RailLine.Between(own.Frames[before], own.Frames[before + 1], fraction);
                    double stockAt = stock[before] + (stock[before + 1] - stock[before]) * fraction;

                    // Відведений гостряк відходить на повний хід біля вістря і повертається на свою лінію
                    // біля кореня, який не рухається
                    double t = position / (bladePoints - 1);
                    double shift = open * BladeThrow * (1 - t * t * (3 - 2 * t));
                    blade.Stations.Add(BladeProfile(frame, stockAt, shift, withHead, out ends));
                    if (blade.Stations.Count == 1) blade.StartCaps.AddRange(ends);
                }

                // Тіло гостряка і його головка: див. SwitchGeometry.BladeBegin. Тіло починається не пізніше,
                // ніж за пів секції до тяги: тяга тримається за тіло обох гостряків
                double begin = Math.Min(SwitchGeometry.BladeBegin(stock, out int firstFull), TieBarAt - 1);
                double headFrom = Math.Max(begin, SwitchGeometry.HeadFrom(stock));

                if (headFrom < bladePoints - 1)
                {
                    if (headFrom - begin > 0.05)
                    {
                        AddStation(begin, false);
                        for (int i = (int)Math.Floor(begin) + 1; i < headFrom - 0.05; i++)
                        {
                            if (i - begin > 0.05) AddStation(i, false);
                        }
                        // Два перерізи в одному місці, без головки і з нею: між ними торець головки, сходинка
                        AddStation(headFrom, false);
                    }
                    AddStation(headFrom, true);
                    for (int i = (int)Math.Floor(headFrom) + 1; i < bladePoints; i++)
                    {
                        if (i - headFrom > 0.05) AddStation(i, true);
                    }
                }

                if (blade.Stations.Count >= 2)
                {
                    blade.EndCaps.AddRange(ends);
                    var parts = new List<LoftPart> { blade };

                    // Тяга між двома гостряками: планка, що тримає їх на сталій відстані, тому вони й рухаються
                    // разом. Одна на стрілку, її ставить перша гілка. Лежить між шпалами, біля вістря
                    if (setup.Primary && own.Other != null)
                    {
                        for (int bar = 0; bar < TieBars; bar++)
                        {
                            LoftPart tie = TieBar(section, own, bladePoints, stock, firstFull, open, bar);
                            if (tie != null) parts.Add(tie);
                        }
                    }

                    renderer.SetLoft(setup.Chunk, section.IndexInChunk, setup.Left, "", setup.Material, parts);
                }
            }
        }

        // На скільки кінці контррейки відігнуті від ходової рейки, блоків
        private const double CheckRailFlare = 0.04;
        // Вкладиш між шийками ходової рейки й контррейки: довжина вздовж колії і висота верху над підошвою, блоків.
        // Верх нижчий за головку рейки, щоб гребінь колеса проходив над ним
        private const double SpacerLength = 0.1;
        private const double SpacerTop = RailProfile.SpacerTop;

        /// <summary>
        /// Ставить контррейку: коротку рейку навпроти хрестовини, біля зовнішньої рейки колії, з боку її осі.
        /// Між нею і ходовою рейкою лишається жолоб для гребеня колеса. Вона притримує колесо з протилежного
        /// боку осі, поки інше проходить розрив біля вістря осердя. Кінці трохи відігнуті, щоб гребінь
        /// заходив у жолоб плавно. Між шийками двох рейок стоять вкладиші, що тримають ширину жолоба.
        /// Усе це один меш, зібраний так само, як хрестовина: повний профіль рейки в кожному перерізі,
        /// два торці і вкладиші окремими частинами.
        /// Стоїть вона на тих самих спільних перерізах двох колій, що й хрестовина, а не на перерізах своєї
        /// колії з тими самими номерами: колії порізані на секції кожна по-своєму, і за номерами дві
        /// контррейки з'їхали б уздовж колії одна відносно одної і відносно хрестовини.
        /// inner каже, з якого боку секції внутрішня рейка колії; контррейка стоїть біля протилежної.
        /// </summary>
        private void AddCheckRail(Vec3i chunkCoord, Section section, bool inner, BranchGeometry own)
        {
            SectionPart part = inner ? SectionPart.SecondRail : SectionPart.FirstRail;
            if (!section.IsInstalled(part)) return;
            string material = section.GetMaterial(part) ?? "iron";

            int from = (own.HideFrom - 1) * 2;
            int to = Math.Min(own.Across.Length - 1, own.HideTo * 2);
            if (to - from < 2) return;

            double half = BladeMesh.HeadHalfWidth;
            // Перерізи стоять на внутрішній рейці, бік дивиться від осі колії до неї. Зовнішня рейка лежить
            // за ширину колії в протилежний бік, контррейка ближче до осі на головку рейки і жолоб
            double gap = half * 2 + SwitchGeometry.FrogFlangeway;
            double running = -section.TrackWidth;
            double offset = running + gap;

            // Внутрішня рейка своєї колії в кожному спільному перерізі хрестовини і вісь контррейки там само
            var innerRail = new List<RailFrame>();
            var axis = new List<Vec3d>();
            for (int q = from; q <= to; q++)
            {
                RailFrame station = SwitchGeometry.CommonStation(own.Frames[q], own.Other[q]);
                double x = offset + (q == from || q == to ? CheckRailFlare : 0);
                RailFrame at = station == null ? null : RailLine.Cross(own.Frames, 0, station, q);
                RailFrame shifted = station == null ? null : RailLine.Cross(own.Frames, x, station, q);
                if (at == null || shifted == null) return;

                innerRail.Add(at);
                axis.Add(shifted.Center);
            }

            var rail = new LoftPart();
            for (int i = 0; i < axis.Count; i++)
            {
                RailFrame frame = innerRail[i];
                // Через відгин лінія контррейки не йде точно вздовж колії: напрямок веде вона сама
                Vec3d before = axis[Math.Max(i - 1, 0)], after = axis[Math.Min(i + 1, axis.Count - 1)];
                Vec3d along = new Vec3d(after.X - before.X, after.Y - before.Y, after.Z - before.Z);
                along = along.Length() < 1e-9 ? frame.Along : along.Normalize();

                RailFrame at = new RailFrame { Center = axis[i], Side = frame.Side, Up = frame.Up };
                rail.Stations.Add(RailContour(at, 0, along));

                // Торці: повний переріз рейки трьома прямокутниками, підошва, шийка і головка
                if (i == 0 || i == axis.Count - 1)
                {
                    List<LoftPoint[]> caps = i == 0 ? rail.StartCaps : rail.EndCaps;
                    caps.Add(Box(at, -RailFootHalf, RailFootHalf, 0, RailFootTop));
                    caps.Add(Box(at, -RailWebHalf, RailWebHalf, RailFootTop, FrogBodyTop));
                    caps.Add(Box(at, -half, half, FrogBodyTop, FrogRailTop));
                }
            }
            var parts = new List<LoftPart> { rail };

            // Вкладиші: посередині кожної секції, тобто між шпалами, крім відігнутих кінців
            for (int q = from + 1; q < to; q += 2)
            {
                RailFrame frame = innerRail[q - from];
                var spacer = new LoftPart();
                for (int end = -1; end <= 1; end += 2)
                {
                    double shift = end * SpacerLength / 2;
                    RailFrame at = new RailFrame
                    {
                        Center = new Vec3d(frame.Center.X + frame.Along.X * shift, frame.Center.Y + frame.Along.Y * shift, frame.Center.Z + frame.Along.Z * shift),
                        Side = frame.Side, Up = frame.Up
                    };
                    // Від шийки ходової рейки до шийки контррейки, над їхніми підошвами
                    LoftPoint[] box = Box(at, running + RailWebHalf, offset - RailWebHalf, RailFootTop, SpacerTop);
                    spacer.Stations.Add(new LoftStation { Along = frame.Along, Points = new[] { box[0], box[3], box[2], box[1] } });
                    (end < 0 ? spacer.StartCaps : spacer.EndCaps).Add(box);
                }
                parts.Add(spacer);

                // Вкладиш стягнутий з двома рейками наскрізним болтом: головка на ходовій рейці ззовні колії,
                // гайка на контррейці з боку осі
                var outward = new Vec3d(-frame.Side.X, -frame.Side.Y, -frame.Side.Z);
                renderer.AddBolt(chunkCoord, material, Along(frame.Center, frame.Side, running), outward, frame.Along);
                renderer.AddBolt(chunkCoord, material, Along(frame.Center, frame.Side, offset), frame.Side, frame.Along);
            }

            renderer.SetLoft(chunkCoord, section.IndexInChunk, !inner, "CR", material, parts);
        }

        // Тяга між гостряками: ширина вздовж колії, низ і верх над підошвою рейки, блоків
        private const int TieBars = 1;
        // На якому перерізі колії від стрілки стоїть тяга: 2 це кінець першої секції
        private const int TieBarAt = 2;
        private const double TieBarWidth = 0.09;
        private const double TieBarBottom = 0.5 * ModMath.Voxel;
        private const double TieBarTop = 1.0 * ModMath.Voxel;

        /// <summary>
        /// Тяга між гостряками: планка впоперек колії від шийки одного гостряка до шийки другого.
        /// Стоїть на стику першої і другої секцій стрілки, тобто між шпалами, навпроти важеля; number це
        /// котра вона за рахунком, наступні стоять через секцію. Обидва її кінці лежать в одній площині
        /// перерізу своєї колії, тож вона завжди впоперек колії, як би не були порізані на секції гілки. Кінці
        /// рахуються з того, де кожен гостряк стоїть саме зараз, тож під час переведення вона їде з ними.
        /// stock і firstFull це вже пораховане для свого гостряка: де рамна рейка і з якого перерізу він є.
        /// </summary>
        private LoftPart TieBar(Section section, BranchGeometry own, int bladePoints, double[] stock, int firstFull, double open, int number)
        {
            // Перша тяга стоїть рівно на стику першої і другої секцій стрілки, навпроти важеля;
            // наступні через секцію одна від одної
            int at = TieBarAt + number * 2;
            if (at > bladePoints - 1) return null;

            // Сусідній гостряк у тій самій площині перерізу і його рамна рейка: зовнішня рейка своєї колії
            RailFrame near = own.Frames[at];
            RailFrame far = RailLine.Cross(own.Other, 0, near, at);
            if (far == null || !SwitchGeometry.StockOffset(far, own.Frames, section.TrackWidth, at, out double farStock)) return null;

            // Де стоїть стінка шийки гостряка з боку колії: зміщення біля вістря плюс відведення
            double t = (double)at / (bladePoints - 1);
            double throwAt = BladeThrow * (1 - t * t * (3 - 2 * t));
            Vec3d WebFace(RailFrame frame, double stockAt, double opened)
            {
                double move = Math.Max(0, RailWebHalf - (stockAt - RailFootHalf)) + opened * throwAt;
                // Трохи всередину шийки, щоб між тягою і гостряком не було щілини
                double x = -(RailWebHalf * 0.5 + move);
                return new Vec3d(frame.Center.X + frame.Side.X * x, frame.Center.Y + frame.Side.Y * x, frame.Center.Z + frame.Side.Z * x);
            }

            Vec3d from = WebFace(near, stock[at], open);
            Vec3d to = WebFace(far, farStock, 1 - open);
            Vec3d along = to.SubCopy(from);
            if (along.Length() < 0.05) return null;
            along.Normalize();

            var bar = new LoftPart();
            foreach (Vec3d end in new[] { from, to })
            {
                // Переріз планки: упоперек неї це напрямок колії
                RailFrame frame = new RailFrame { Center = end, Side = near.Along, Up = near.Up };
                LoftPoint[] box = Box(frame, -TieBarWidth / 2, TieBarWidth / 2, TieBarBottom, TieBarTop);
                bar.Stations.Add(new LoftStation { Along = along, Points = new[] { box[0], box[3], box[2], box[1] } });
                (bar.Stations.Count == 1 ? bar.StartCaps : bar.EndCaps).Add(box);
            }
            return bar;
        }

        // За якого зміщення гостряка підпора під головкою сягає низу шийки, блоків. За меншого вона коротша
        private const double SupportFullOffset = 0.05;

        /// <summary>
        /// Контур гостряка в одному перерізі. Гостряк це звичайна рейка, оброблена так, як обробляють справжні:
        /// 1. З боку рамної рейки головка й підошва підрізані по формі рамної рейки, але не далі, ніж до
        ///    площини власної шийки: шийка лишається цілою, і з цього боку найвужчий гостряк це рівна стінка.
        /// 2. Де рамна рейка ближче, ніж дозволяє ця стінка, гостряк стоїть зі зміщенням: відсунутий від
        ///    своєї лінії до осі колії рівно настільки, щоб стінкою прилягати до підошви рамної рейки.
        ///    Підошва в рейки найширша, тому вона й визначає зміщення; головка гостряка при цьому
        ///    підрізана рівно по головці рамної рейки і прилягає до неї.
        /// 3. Через зміщення головка гостряка виступає за лінію, по якій котиться гребінь колеса. Усе, що
        ///    виступає, зрізане, ніби його зняли самі колеса: робоча грань головки стоїть на лінії рейки.
        ///    Тож біля вістря головка вузька, а шийка й підошва під нею ширші за неї з боку колії.
        /// stock це де вісь рамної рейки в цьому перерізі, блоків від лінії гостряка в бік Side.
        /// thrown це на скільки блоків гостряк відведений від рамної рейки.
        /// withHead: чи є в цьому перерізі головка. Біля самого вістря її немає, є лише тіло.
        /// ends це торець у цьому перерізі: підошва, шийка й головка.
        /// </summary>
        private static LoftStation BladeProfile(RailFrame frame, double stock, double thrown, bool withHead, out LoftPoint[][] ends)
        {
            double half = BladeMesh.HeadHalfWidth;
            const double floor = FrogBodyTop;
            // Без головки верх гостряка на рівні її низу
            double top = withHead ? FrogRailTop : floor;

            // Робоча грань головки рамної рейки, від лінії гостряка
            double face = stock - half;
            // Зміщення: стінка шийки не може зайти за край підошви рамної рейки
            double offset = Math.Max(0, RailWebHalf - (stock - RailFootHalf));

            // Далі все у власних координатах гостряка, від його осі
            // Головка: з боку рамної рейки до її грані, але не вужче за шийку; з боку колії до лінії рейки
            double headRight = GameMath.Clamp(face + offset, RailWebHalf, half);
            double headLeft = Math.Min(-half + offset, headRight);
            // Підошва: до підошви рамної рейки, але не вужче за шийку
            double footRight = GameMath.Clamp(stock - RailFootHalf + offset, RailWebHalf, RailFootHalf);

            // Біля вістря головка вузька і через зміщення стоїть не над шийкою, а збоку від неї, з боку
            // рамної рейки. Щоб вона не висіла в повітрі, під нею підпора: стінка від краю головки похило
            // спускається до низу шийки. Місце для неї є: це простір над підошвою рамної рейки, під її
            // головкою. Що менше зміщення, то коротша підпора; без зміщення це звичайний низ головки
            double support = floor - (floor - RailFootTop) * GameMath.Clamp(offset / SupportFullOffset, 0, 1);

            double move = offset + thrown;
            LoftPoint P(double x, double y) => Point(frame, x - move, y);

            ends = new[]
            {
                Box(frame, -RailFootHalf - move, footRight - move, 0, RailFootTop),
                Box(frame, -RailWebHalf - move, RailWebHalf - move, RailFootTop, floor),
                Box(frame, headLeft - move, headRight - move, floor, top),
                new[] { P(RailWebHalf, support), P(headRight, floor), P(RailWebHalf, floor), P(RailWebHalf, floor) }
            };
            return new LoftStation
            {
                Along = frame.Along,
                Points = new[]
                {
                    // Бік колії: підошва, шийка, головка від лінії рейки
                    P(-RailFootHalf, 0), P(-RailFootHalf, RailFootTop), P(-RailWebHalf, RailFootTop), P(-RailWebHalf, floor),
                    P(headLeft, floor), P(headLeft, top),
                    // Верх головки і бік рамної рейки
                    P(headRight, top), P(headRight, floor), P(RailWebHalf, support), P(RailWebHalf, RailFootTop),
                    P(footRight, RailFootTop), P(footRight, 0)
                }
            };
        }

        // Прямокутник у перерізі: кути по колу, починаючи з лівого нижнього, далі правий нижній
        private static LoftPoint[] Box(RailFrame at, double left, double right, double bottom, double top)
        {
            return new[] { Point(at, left, bottom), Point(at, right, bottom), Point(at, right, top), Point(at, left, top) };
        }

        // Хрестовина лита, один суцільний блок. Висоти його частин над підошвою рейки, блоків: тіло доходить
        // до низу головки рейки, поверхні кочення до її верху. Числа з моделі рейки (shapes/item/rail.json)
        private const double FrogBodyTop = RailProfile.HeadBottom;
        private const double FrogRailTop = RailProfile.Top;

        // Профіль звичайної рейки, блоків (shapes/item/rail.json): пів ширини підошви, її висота, пів товщини шийки
        private const double RailFootHalf = RailProfile.FootHalfWidth;
        private const double RailFootTop = RailProfile.FootTop;
        private const double RailWebHalf = RailProfile.WebHalfWidth;

        /// <summary>
        /// Збирає хрестовину: одну литу деталь. У кожному перерізі задано її контур, і меш це поверхня,
        /// натягнута на ці контури: усередині немає жодної грані.
        /// Контур зліва направо: зовнішній бік лівого вусовика, такий самий, як у звичайної рейки (підошва,
        /// шийка, головка); верх вусовика; жолоб із дном на рівні низу головки; осердя, яке далі розходиться
        /// на два промені; другий жолоб; правий вусовик і його зовнішній бік. Тож назовні хрестовина ніде
        /// не виступає за контур рейки, а між рейками суцільна.
        /// Перед вістрям осердя немає: його точки зведені в одну на дні жолоба, і така грань не малюється.
        /// Від перерізу без осердя до першого перерізу з ним воно піднімається схилом, як зістружене вістря.
        /// Усі перерізи тіла спільні для двох колій: посередині між двома рейками, упоперек бісектриси їхніх
        /// напрямків. Тому торці тіла пласкі й замкнені. Але рейки двох колій закінчуються кожна у своїй
        /// площині, упоперек себе, і з пласким торцем не збігаються. Тому крайній переріз тіла відсунутий
        /// углиб настільки, щоб жодна з двох рейок у нього не заходила, а від нього до стику з рейкою колії
        /// добудовано хвостик: повний профіль рейки, усі його точки. Стінка торця закриває проміжок між
        /// хвостиками.
        /// </summary>
        private void AddFrog(Vec3i chunkCoord, Section section, bool left, string material, BranchGeometry own, IList<Timber> wood)
        {
            // Осі двох вусовиків у кожному перерізі і бік, куди дивиться зовнішній бік лівого: по них хрестовину
            // прибивають до дерева
            var wingsLeft = new List<Vec3d>();
            var wingsRight = new List<Vec3d>();
            var wingSides = new List<Vec3d>();

            double half = BladeMesh.HeadHalfWidth;
            // Відстань між серединами рейок, за якої між їхніми головками лишається жолоб
            double clearance = half * 2 + SwitchGeometry.FrogFlangeway;
            const double floor = FrogBodyTop, top = FrogRailTop;

            int count = own.Across.Length;
            int from = (own.HideFrom - 1) * 2;
            int to = Math.Min(count - 1, own.HideTo * 2);
            if (to - from < 1) return;

            // Тіло вузла
            // З якого перерізу осердя розходиться на два промені: де щілина між їхніми головками стала ширша
            // за головку рейки. Вужча щілина залита, осердя там суцільне
            int splitFrom = to;
            for (int q = from; q <= to; q++)
            {
                if (own.Across[q] - half * 2 <= half * 2) continue;
                splitFrom = q;
                break;
            }

            var body = new LoftPart();
            List<LoftStation> stations = body.Stations;
            List<LoftPoint[]> startCaps = body.StartCaps, endCaps = body.EndCaps;
            // Хвостики: два на початку вузла, від рейок колій до вусовиків, і два в кінці, від променів осердя
            var parts = new List<LoftPart> { body };

            for (int q = from; q <= to; q++)
            {
                // Перерізи внутрішніх рейок двох колій у цьому місці: своєї і сусідньої. Сусідня повернута боком
                // у той самий бік, що й своя
                RailFrame mine = own.Frames[q], theirs = Mirrored(own.Other[q]);
                bool first = q == from, last = q == to;

                // Спільний переріз вузла
                RailFrame station = SwitchGeometry.CommonStation(mine, own.Other[q]);
                if (station == null) continue;
                Vec3d center = station.Center, along = station.Along;

                // Крайній переріз відсувається вглиб вузла, за найдальший кут підошви обох рейок
                if (first || last)
                {
                    double shift = 0;
                    foreach (RailFrame rail in new[] { mine, theirs })
                    {
                        for (int corner = -1; corner <= 1; corner += 2)
                        {
                            double reach = (rail.Center.X + rail.Side.X * RailFootHalf * corner - center.X) * along.X
                                + (rail.Center.Y + rail.Side.Y * RailFootHalf * corner - center.Y) * along.Y
                                + (rail.Center.Z + rail.Side.Z * RailFootHalf * corner - center.Z) * along.Z;
                            shift = first ? Math.Max(shift, reach) : Math.Min(shift, reach);
                        }
                    }
                    shift = GameMath.Clamp(shift, -0.2, 0.2);
                    station.Center = new Vec3d(center.X + along.X * shift, center.Y + along.Y * shift, center.Z + along.Z * shift);
                }

                // Де дві рейки перетинають цей переріз. До перетину рейок своя з додатного боку, сусідня
                // з від'ємного, після перетину навпаки: знак виходить сам
                if (!RailLine.Offset(own.Frames, 0, station, q, out double mineAt) || !RailLine.Offset(own.Other, 0, station, q, out double theirsAt)) continue;
                double across = theirsAt - mineAt;

                // На якій відстані від сусідньої рейки тримається вусовик. Далеко від перетину це просто його
                // рейка на своїй лінії; ближче за два жолоби відстань плавно перестає зменшуватися
                // і від перетину далі дорівнює жолобу
                double keep = clearance;
                if (across < 0)
                {
                    double near = Math.Min(-across / (2 * clearance), 1);
                    keep = near < 1 ? clearance * (1 + near * near) : -across;
                }
                // На початку вузла вусовики це самі рейки колій
                if (first) keep = Math.Abs(across);

                // Лівий вусовик на початку вузла це рейка сусідньої колії, далі він іде поруч зі своєю.
                // Правий навпаки
                double wingLeft = mineAt - keep;
                double wingRight = theirsAt + keep;

                // Хрестовина стягнута наскрізними болтами через шийки вусовиків: на кожному стику секцій,
                // тобто між брусами. Головка на одному вусовику, гайка на другому, обидві назовні
                if (!first && !last && q % 2 == 0)
                {
                    var outward = new Vec3d(-station.Side.X, -station.Side.Y, -station.Side.Z);
                    renderer.AddBolt(chunkCoord, material, Along(station.Center, station.Side, wingLeft), outward, station.Along);
                    renderer.AddBolt(chunkCoord, material, Along(station.Center, station.Side, wingRight), station.Side, station.Along);
                }

                wingsLeft.Add(Along(station.Center, station.Side, wingLeft));
                wingsRight.Add(Along(station.Center, station.Side, wingRight));
                wingSides.Add(station.Side);

                // Осердя: від робочої грані своєї рейки до робочої грані сусідньої. Поки рейки не розійшлися
                // на ширину головки, воно суцільне, далі це два промені з дном між ними
                // Осердя ділиться на два промені рівно посередині між двома рейками. Це не завжди середина
                // перерізу: точки двох колій з однаковим номером не стоять одна навпроти одної, і в перерізі
                // рейки лежать не дзеркально. Якби ділити по середині перерізу, один промінь відокремився б
                // раніше за другий, і ямка між ними вийшла б кривобокою
                double middle = (mineAt + theirsAt) / 2;
                double noseA, noseB, noseC, noseD, noseTop = floor;
                if (last)
                {
                    // У кінці вузла промені це самі рейки колій
                    noseTop = top;
                    noseA = mineAt - half;
                    noseB = mineAt + half;
                    noseC = theirsAt - half;
                    noseD = theirsAt + half;
                    if (noseB > noseC) noseB = noseC = (noseB + noseC) / 2;
                }
                else if (q >= own.Tip)
                {
                    noseTop = top;
                    noseA = mineAt - half;
                    noseB = Math.Min(mineAt + half, middle);
                    noseC = Math.Max(theirsAt - half, middle);
                    noseD = theirsAt + half;
                    noseA = Math.Min(Math.Max(noseA, wingLeft + half), noseB);
                    noseD = Math.Max(Math.Min(noseD, wingRight - half), noseC);
                }
                else
                {
                    noseA = noseB = noseC = noseD = GameMath.Clamp(middle, wingLeft + half, wingRight - half);
                    // Вістря осердя на повну висоту головки від самого початку: у перерізі перед першим
                    // широким воно вже стоїть, нульової ширини. Тож осердя це гострий клин, а не схил від дна
                    if (q >= own.Tip - 1) noseTop = top;
                }
                // Між променями дно, лише коли вони розійшлися ширше за головку рейки; доти осердя суцільне,
                // на всю висоту
                double between = q >= splitFrom ? floor : noseTop;

                LoftPoint P(double x, double y) => Point(station, x, y);

                // Контур перерізу з осердям висотою noseHeight і дном між променями на висоті gapHeight
                LoftStation Contour(double noseHeight, double gapHeight)
                {
                    return new LoftStation
                    {
                        Along = along,
                        Points = new[]
                        {
                            // Зовнішній бік лівого вусовика: профіль рейки
                            P(wingLeft - RailFootHalf, 0), P(wingLeft - RailFootHalf, RailFootTop), P(wingLeft - RailWebHalf, RailFootTop),
                            P(wingLeft - RailWebHalf, floor), P(wingLeft - half, floor), P(wingLeft - half, top),
                            // Його верх і стінка жолоба
                            P(wingLeft + half, top), P(wingLeft + half, floor),
                            // Осердя: лівий промінь, дно між променями, правий промінь
                            P(noseA, floor), P(noseA, noseHeight), P(noseB, noseHeight), P(noseB, gapHeight),
                            P(noseC, gapHeight), P(noseC, noseHeight), P(noseD, noseHeight), P(noseD, floor),
                            // Правий вусовик
                            P(wingRight - half, floor), P(wingRight - half, top),
                            P(wingRight + half, top), P(wingRight + half, floor), P(wingRight + RailWebHalf, floor),
                            P(wingRight + RailWebHalf, RailFootTop), P(wingRight + RailFootHalf, RailFootTop), P(wingRight + RailFootHalf, 0)
                        }
                    };
                }

                // Осердя і ямка між його променями починаються прямовисно. Щоб так вийшло, у місці, де
                // вони починаються, стоять два перерізи поспіль: один ще без них, другий уже з ними, нульової
                // ширини. Між такими перерізами поверхні немає, тож не виходить і грані нульової товщини,
                // що тяглася б від вістря назад до дна жолоба
                if (!last && q == own.Tip - 1) stations.Add(Contour(floor, floor));
                // Ямка починається одразу на свою ширину, прямовисною стінкою: той самий переріз ще без неї
                if (!last && q == splitFrom && noseTop > floor) stations.Add(Contour(noseTop, noseTop));
                stations.Add(Contour(noseTop, between));

                if (first)
                {
                    // Торець: стінка між шийками двох вусовиків. Самі вусовики продовжуються хвостиками до рейок
                    startCaps.Add(new[] { P(wingLeft + RailWebHalf, 0), P(wingRight - RailWebHalf, 0), P(wingRight - RailWebHalf, floor), P(wingLeft + RailWebHalf, floor) });
                    parts.Add(Tail( RailContour(theirs, 0, theirs.Along), RailContour(station, wingLeft, theirs.Along)));
                    parts.Add(Tail( RailContour(mine, 0, mine.Along), RailContour(station, wingRight, mine.Along)));
                }
                if (last)
                {
                    // Торець: вусовики тут обриваються, видно весь їхній переріз, і стінка між ними.
                    // Промені осердя продовжуються хвостиками до рейок
                    endCaps.Add(new[] { P(wingLeft - RailFootHalf, 0), P(wingLeft - RailWebHalf, 0), P(wingLeft - RailWebHalf, RailFootTop), P(wingLeft - RailFootHalf, RailFootTop) });
                    endCaps.Add(new[] { P(wingLeft - RailWebHalf, 0), P(wingRight + RailWebHalf, 0), P(wingRight + RailWebHalf, floor), P(wingLeft - RailWebHalf, floor) });
                    endCaps.Add(new[] { P(wingLeft - half, floor), P(wingLeft + half, floor), P(wingLeft + half, top), P(wingLeft - half, top) });
                    endCaps.Add(new[] { P(wingRight - half, floor), P(wingRight + half, floor), P(wingRight + half, top), P(wingRight - half, top) });
                    endCaps.Add(new[] { P(wingRight + RailWebHalf, 0), P(wingRight + RailFootHalf, 0), P(wingRight + RailFootHalf, RailFootTop), P(wingRight + RailWebHalf, RailFootTop) });
                    parts.Add(Tail( RailContour(station, mineAt, mine.Along), RailContour(mine, 0, mine.Along)));
                    parts.Add(Tail( RailContour(station, theirsAt, theirs.Along), RailContour(theirs, 0, theirs.Along)));
                }
            }

            // Тіло й хвостики це один меш
            renderer.SetLoft(chunkCoord, section.IndexInChunk, left, "F", material, parts);

            // До дерева хрестовина прибита костилями з двох зовнішніх боків, за підошви вусовиків: там, де
            // вусовик перетинає брус чи шпалу і костилі потрапляють у дерево
            if (wood == null) return;
            for (int n = 0; n + 1 < wingsLeft.Count; n++)
            {
                for (int wing = 0; wing < 2; wing++)
                {
                    List<Vec3d> line = wing == 0 ? wingsLeft : wingsRight;
                    Vec3d near = line[n], far = line[n + 1];
                    var along = new Vec3d(far.X - near.X, far.Y - near.Y, far.Z - near.Z);
                    if (along.Length() < 1e-9) continue;
                    along.Normalize();

                    // Лівий вусовик дивиться назовні проти боку перерізу, правий за ним
                    Vec3d side = wingSides[n];
                    Vec3d outward = wing == 0 ? new Vec3d(-side.X, -side.Y, -side.Z) : side;
                    // Бік, який NailsOnWood рахує додатним, це (z, -x) напрямку вусовика
                    int nails = outward.X * along.Z - outward.Z * along.X > 0 ? 1 : -1;

                    foreach (Timber timber in wood)
                    {
                        if (!NodeFastenings.Crosses(near, far, timber, out Vec3d at)) continue;
                        if (!NodeFastenings.NailsOnWood(at, along, wood, nails)) continue;
                        // Пара ставиться в місце самих костилів: за край підошви вусовика
                        renderer.AddOutward(chunkCoord, RailWayRendererSystem.SpikePairPart, material, Along(at, outward, NodeFastenings.SpikeAside), outward, along);
                    }
                }
            }
        }

        // Точка за distance блоків від point у напрямку direction
        private static Vec3d Along(Vec3d point, Vec3d direction, double distance)
        {
            return new Vec3d(point.X + direction.X * distance, point.Y + direction.Y * distance, point.Z + direction.Z * distance);
        }

        private static LoftPart Tail(LoftStation from, LoftStation to)
        {
            var part = new LoftPart();
            part.Stations.Add(from);
            part.Stations.Add(to);
            return part;
        }

        private static LoftPoint Point(RailFrame at, double x, double y)
        {
            return new LoftPoint { Center = at.Center, Side = at.Side, Up = at.Up, X = x, Y = y };
        }

        // Повний профіль звичайної рейки в перерізі at, з віссю за x блоків від його середини: усі дванадцять точок
        private static LoftStation RailContour(RailFrame at, double x, Vec3d along)
        {
            double half = BladeMesh.HeadHalfWidth;
            // Початок відліку ставимо на вісь рейки: лінію хвостика між двома перерізами веде крива
            // від початку відліку одного до початку відліку іншого, і вона має йти вздовж рейки
            RailFrame axis = new RailFrame
            {
                Center = new Vec3d(at.Center.X + at.Side.X * x, at.Center.Y + at.Side.Y * x, at.Center.Z + at.Side.Z * x),
                Side = at.Side, Up = at.Up
            };
            return new LoftStation
            {
                Along = along,
                Points = new[]
                {
                    Point(axis, -RailFootHalf, 0), Point(axis, -RailFootHalf, RailFootTop), Point(axis, -RailWebHalf, RailFootTop),
                    Point(axis, -RailWebHalf, FrogBodyTop), Point(axis, -half, FrogBodyTop), Point(axis, -half, FrogRailTop),
                    Point(axis, half, FrogRailTop), Point(axis, half, FrogBodyTop), Point(axis, RailWebHalf, FrogBodyTop),
                    Point(axis, RailWebHalf, RailFootTop), Point(axis, RailFootHalf, RailFootTop), Point(axis, RailFootHalf, 0)
                }
            };
        }

        // Переріз сусідньої рейки, повернутий боком у той самий бік, що й спільні перерізи вузла. Її власний бік
        // дивиться назустріч, і якби лишити його, поверхня між сусідніми перерізами вивернулася б навиворіт
        private static RailFrame Mirrored(RailFrame frame)
        {
            return new RailFrame { Center = frame.Center, Side = frame.Side.Clone().Mul(-1), Up = frame.Up, Along = frame.Along };
        }

    }
}
