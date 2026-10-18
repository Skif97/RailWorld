using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Розміри профілю рейки, блоків (shapes/item/rail.json). Одне місце на всі деталі, що повторюють рейку
    /// або прилягають до неї: гостряк, хрестовина, контррейка.
    /// </summary>
    public static class RailProfile
    {
        /// <summary>Пів ширини головки.</summary>
        public const double HeadHalfWidth = 0.6 * ModMath.Voxel;
        /// <summary>Пів ширини підошви.</summary>
        public const double FootHalfWidth = 1.0 * ModMath.Voxel;
        /// <summary>Пів товщини шийки.</summary>
        public const double WebHalfWidth = 0.2 * ModMath.Voxel;
        /// <summary>Верх підошви над її низом.</summary>
        public const double FootTop = 0.3 * ModMath.Voxel;
        /// <summary>Низ головки над низом підошви.</summary>
        public const double HeadBottom = 1.8 * ModMath.Voxel;
        /// <summary>Верх головки над низом підошви.</summary>
        public const double Top = 2.5 * ModMath.Voxel;
        /// <summary>
        /// Товщина підкладки, на якій рейка лежить на шпалі (shapes/part/fastening.json). Рейка стоїть на ній,
        /// тому шпала лежить нижче за підошву рейки саме на стільки.
        /// </summary>
        public const double PlateThickness = 0.25 * ModMath.Voxel;
        /// <summary>
        /// Верх вкладиша між шийками ходової рейки й контррейки стрілки над низом підошви. Нижчий за головку
        /// рейки, щоб гребінь колеса проходив над ним.
        /// </summary>
        public const double SpacerTop = 1.5 * ModMath.Voxel;
    }

    /// <summary>
    /// Переріз рейки колії в одній точці вздовж неї: де вона, і куди дивляться її осі.
    /// </summary>
    public class RailFrame
    {
        /// <summary>Середина рейки в цій точці.</summary>
        public Vec3d Center;
        /// <summary>Упоперек колії, у бік сусідньої колії стрілки. Одиничний.</summary>
        public Vec3d Side;
        /// <summary>Угору від полотна. Одиничний.</summary>
        public Vec3d Up;
        /// <summary>Уздовж колії, від стрілки. Одиничний.</summary>
        public Vec3d Along;
    }

    /// <summary>
    /// Усе про внутрішню рейку однієї з двох колій стрілки: її лінія і де на ній хрестовина.
    /// </summary>
    public class BranchGeometry
    {
        // Перерізи внутрішньої рейки від стрілки, через чверть блока
        public List<RailFrame> Frames;
        // Ті самі перерізи внутрішньої рейки сусідньої колії
        public List<RailFrame> Other;
        // Де сусідня внутрішня рейка відносно своєї в кожному перерізі, блоків у бік сусідньої колії.
        // Біля стрілки вона з іншого боку, на ширину колії, далі рейки сходяться, перетинаються і розходяться
        public double[] Across;
        // Номери перерізів: де рейка підійшла до сусідньої на жолоб, де починається осердя,
        // де рейки перетинаються і де осердя набрало повну ширину. -1, якщо хрестовини немає
        public int Throat = -1, Tip, Crossing, Full;
        // Секції, на яких звичайна рейка не малюється, бо там хрестовина. Номери від стрілки, з одиниці
        public int HideFrom, HideTo;
    }

    /// <summary>
    /// Геометрія стрілки: де йдуть внутрішні рейки двох її гілок, де вони сходяться в хрестовину і скільки
    /// секцій займає гостряк. Чиста геометрія з даних секцій: нею однаково користуються сервер і клієнт,
    /// від рендеру вона не залежить. Сусідні секції дістає через resolve.
    /// </summary>
    public static class SwitchGeometry
    {
        // Гостряк стрілки: початок внутрішньої рейки кожної з двох колій, окрема деталь зі своїм мешем.
        // Розміри взято зі справжніх стрілок і перераховано на нашу рейку, головка якої 0.075 блока.
        // Довжина: до кореня (HeelAt), але не коротше і не довше за ці межі в секціях: від 3.5 до 8 ширин
        // колії, як на справжній залізниці. При колії 0.78 це від 3 до 6 блоків
        public static readonly int MinBladeSections = (int)Math.Ceiling(TrackGauge.StandardWidth * 3.5 / 0.5);
        public static readonly int MaxBladeSections = (int)Math.Floor(TrackGauge.StandardWidth * 8.0 / 0.5);

        /// <summary>
        /// На скільки рамна рейка має відійти від лінії гостряка, щоб гостряк скінчився, блоків між осями рейок:
        /// дві ширини головки. Одна це дві половинки головок, гостряка і рамної рейки; друга це зазор між
        /// ними, де проходить гребінь колеса. Так визначається довжина гостряка і на справжній залізниці:
        /// що пологіше розходяться колії, то далі це місце і то довший гостряк.
        /// </summary>
        public const double HeelGap = RailProfile.HeadHalfWidth * 4;

        /// <summary>
        /// Де корінь гостряка гілки: за скільки блоків від стрілки, уздовж його лінії, рамна рейка відійшла від
        /// неї на HeelGap. double.MaxValue, якщо в межах перерізів гілки так і не відійшла.
        /// </summary>
        public static double HeelAt(BranchGeometry own, double trackWidth)
        {
            double[] stock = Stock(own, own.Frames.Count, trackWidth);
            double at = Reaches(stock, HeelGap, out _);
            if (at >= stock.Length) return double.MaxValue;

            double along = 0;
            int whole = (int)Math.Floor(at);
            for (int q = 1; q <= whole; q++) along += own.Frames[q - 1].Center.DistanceTo(own.Frames[q].Center);
            if (whole + 1 < own.Frames.Count) along += (at - whole) * own.Frames[whole].Center.DistanceTo(own.Frames[whole + 1].Center);
            return along;
        }

        // Скільки секцій завдовжки гостряк, якщо його корінь за heelAt блоків від стрілки
        public static int BladeSections(double heelAt)
        {
            return GameMath.Clamp((int)Math.Ceiling(heelAt / 0.5 - 1e-6), MinBladeSections, MaxBladeSections);
        }

        // Те саме, але гостряк не заходить у хрестовину. На крутій стрілці вона починається ближче, ніж
        // найменша довжина гостряка, і тоді гостряк коротший: закінчується там, де починається хрестовина
        public static int BladeSections(double heelAt, BranchGeometry side)
        {
            int sections = BladeSections(heelAt);
            if (side != null && side.Throat >= 0) sections = Math.Min(sections, Math.Max(side.HideFrom - 1, 1));
            return sections;
        }

        /// <summary>
        /// Де в перерізі frame гостряка лежить рамна рейка, до якої він прилягає: зовнішня рейка сусідньої
        /// колії. Відстань від лінії гостряка в бік сусідньої колії, блоків. Біля стрілки це нуль, рамна
        /// рейка лежить на лінії гостряка; далі вона відходить убік.
        /// otherInner це внутрішня рейка сусідньої колії, near номер її перерізу, біля якого шукати.
        /// false, якщо сусідня колія до цього перерізу не доходить.
        /// </summary>
        public static bool StockOffset(RailFrame frame, IList<RailFrame> otherInner, double trackWidth, int near, out double offset)
        {
            offset = 0;
            // Зовнішня рейка лежить за ширину колії від внутрішньої, з протилежного від нас боку
            if (!RailLine.Offset(otherInner, -trackWidth, frame, near, out offset)) return false;

            offset = Math.Max(0, offset);
            return true;
        }

        /// <summary>
        /// Спільний переріз двох рейок, що сходяться: посередині між їхніми перерізами mine і theirs, упоперек
        /// бісектриси їхніх напрямків. Бік дивиться туди ж, куди бік mine; боки двох рейок дивляться назустріч.
        /// З котрої з двох рейок не будуй, площина та сама, а бік рівно протилежний: тому все, що в цьому
        /// перерізі виміряно, виходить однаковим для обох колій. null, якщо рейки йдуть назустріч.
        /// </summary>
        public static RailFrame CommonStation(RailFrame mine, RailFrame theirs)
        {
            Vec3d along = new Vec3d(mine.Along.X + theirs.Along.X, mine.Along.Y + theirs.Along.Y, mine.Along.Z + theirs.Along.Z);
            if (along.Length() < 1e-6) return null;
            along.Normalize();

            Vec3d side = new Vec3d(mine.Side.X - theirs.Side.X, mine.Side.Y - theirs.Side.Y, mine.Side.Z - theirs.Side.Z);
            side.Sub(along.Clone().Mul(side.Dot(along)));
            if (side.Length() < 1e-6) return null;
            side.Normalize();

            Vec3d up = side.Cross(along);
            if (up.Y < 0) up.Mul(-1);

            return new RailFrame
            {
                Center = new Vec3d((mine.Center.X + theirs.Center.X) / 2, (mine.Center.Y + theirs.Center.Y) / 2, (mine.Center.Z + theirs.Center.Z) / 2),
                Side = side,
                Up = up,
                Along = along
            };
        }

        // Хрестовина. Розміри зі справжніх стрілок, перераховані на нашу рейку.
        // Жолоб для гребеня колеса між вусовиком і осердям, блоків
        public const double FrogFlangeway = 0.05;

        // Ширина головки гостряка, блоків, від якої рахується його початок, і на скільки перерізів колії
        // ближче до стрілки він починається насправді. Два перерізи це одна секція
        private const double BladeMinHead = 0.01;
        private const double BladeLead = 2;
        // Найменша ширина головки гостряка, блоків: з такої вона починається, сходинкою
        private const double BladeHeadFrom = 0.005;

        /// <summary>
        /// Де в кожному з перших points перерізів гілки лежить рамна рейка: зовнішня рейка сусідньої колії,
        /// до якої прилягає гостряк. Відстань від лінії гостряка, блоків. Біля стрілки вона лежить на лінії
        /// гостряка, далі відходить убік. Якщо сусідньої колії не знайдено, вважаємо, що вона відходить
        /// рівномірно і біля кореня гостряк уже повний.
        /// </summary>
        public static double[] Stock(BranchGeometry own, int points, double trackWidth)
        {
            double[] stock = new double[points];
            for (int i = 0; i < points; i++)
            {
                stock[i] = points > 1 ? (double)i / (points - 1) * RailProfile.HeadHalfWidth * 4 : 0;
                if (own.Other != null && StockOffset(own.Frames[i], own.Other, trackWidth, i, out double offset)) stock[i] = offset;
            }
            return stock;
        }

        /// <summary>
        /// Де починається тіло гостряка, підошва з шийкою, у перерізах колії від стрілки, дробом: на секцію
        /// ближче до стрілки, ніж місце, де головка набирає ширину BladeMinHead. Далі за сам стик стрілки воно
        /// не йде: там рейка ще одна на обидві колії. firstFull це перший переріз, де головка вже така.
        /// </summary>
        public static double BladeBegin(double[] stock, out int firstFull)
        {
            return Math.Max(0, Reaches(stock, BladeMinHead, out firstFull) - BladeLead);
        }

        /// <summary>
        /// Де починається головка гостряка: там, де від неї лишається ширина BladeHeadFrom; вужча була б лезом.
        /// Сходинкою, а до цього місця гостряк це лише тіло. Номер перерізу дробом; stock.Length, якщо
        /// рамна рейка так далеко й не відійшла.
        /// </summary>
        public static double HeadFrom(double[] stock)
        {
            return Reaches(stock, BladeHeadFrom, out _);
        }

        /// <summary>
        /// Чи гостряк гілки довжиною bladeSections секцій доходить до свого кореня: рамна рейка встигає
        /// відійти від його лінії на HeelGap. Ні, коли гостряк коротший, ніж треба, бо далі вже хрестовина
        /// або найбільша довжина: тоді біля його кінця рамна рейка лежить на ньому. Так буває, коли гілки
        /// довго лежать одна на одній або розходяться дуже полого.
        /// </summary>
        public static bool BladeFits(BranchGeometry own, int bladeSections, double trackWidth)
        {
            int points = Math.Min(bladeSections * 2 + 1, own.Frames.Count);
            if (points < 2) return false;

            double[] stock = Stock(own, points, trackWidth);
            return stock[points - 1] >= HeelGap - 1e-6;
        }

        // Де рамна рейка відійшла на needed блоків, у перерізах колії від стрілки, дробом; stock.Length, якщо ніде
        private static double Reaches(double[] stock, double needed, out int index)
        {
            index = 0;
            while (index < stock.Length && stock[index] < needed) index++;
            if (index == 0 || index >= stock.Length) return index;
            return index - 1 + (needed - stock[index - 1]) / (stock[index] - stock[index - 1]);
        }
        // Як далеко від стрілки шукати хрестовину, у секціях. Стрілку, для якої хрестовини не знайшлося,
        // збудувати не можна: див. SectionLinker.FindSwitchProblem
        public const int MaxFrogSections = 40;

        /// <summary>
        /// Перерізи внутрішньої рейки колії від стрілки. first це перша секція колії, switchAtStart котрим кінцем
        /// вона до стрілки, toward з якого боку її нормалі лежить сусідня колія.
        /// </summary>
        public static List<RailFrame> RailFrames(Section first, bool switchAtStart, double toward, int sections, Func<SectionLink, Section> resolve)
        {
            var frames = new List<RailFrame>();
            TrackWalker walk = TrackWalker.Entering(first, switchAtStart, resolve);
            Vec3d side = null;

            for (int n = 0; n < sections; n++)
            {
                Section current = walk.Section;
                bool enteredAtStart = walk.Entered;

                // Три точки секції в порядку від стрілки. Перша збігається з останньою точкою попередньої секції
                Vec3d[] axis = { current.FullStartPosition, current.GetGlobalPos(), current.FullEndPosition };
                Vec3f[] normals = { current.StartNormal, current.CenterNormal, current.EndNormal };
                Vec3f[] tangents = { current.StartTangent, current.CenterTangent, current.EndTangent };
                double direction = enteredAtStart ? 1 : -1;
                if (!enteredAtStart)
                {
                    Array.Reverse(axis);
                    Array.Reverse(normals);
                    Array.Reverse(tangents);
                }

                for (int k = n == 0 ? 0 : 1; k < 3; k++)
                {
                    // Бік: нормаль секції, спрямована до сусідньої колії. У першій точці це відомо, далі
                    // тримаємося того самого боку: нормалі сусідніх секцій можуть дивитися в різні боки
                    Vec3d normal = new Vec3d(normals[k].X, normals[k].Y, normals[k].Z).Normalize();
                    if (side == null) normal.Mul(toward);
                    else if (normal.X * side.X + normal.Z * side.Z < 0) normal.Mul(-1);
                    side = normal;

                    Vec3d along = new Vec3d(tangents[k].X * direction, tangents[k].Y * direction, tangents[k].Z * direction);
                    along.Sub(side.Clone().Mul(along.Dot(side)));
                    if (along.Length() < 1e-6) along = new Vec3d(-side.Z, 0, side.X);
                    along.Normalize();

                    Vec3d up = side.Cross(along);
                    if (up.Y < 0) up.Mul(-1);

                    frames.Add(new RailFrame
                    {
                        Center = new Vec3d(axis[k].X + side.X * current.TrackWidth / 2, axis[k].Y + side.Y * current.TrackWidth / 2, axis[k].Z + side.Z * current.TrackWidth / 2),
                        Side = side.Clone(),
                        Up = up,
                        Along = along
                    });
                }

                // Наступна секція колії. Якщо колія скінчилася, перерізи закінчуються там само
                if (!walk.Next()) break;
            }

            return frames;
        }

        /// <summary>
        /// Розраховує внутрішню рейку гілки стрілки і місце хрестовини на ній. first це перша секція гілки,
        /// switchAtStart котрим кінцем вона до стрілки; partnerFirst це перша секція другої гілки.
        /// partnerSwitchAtStart котрим кінцем до стрілки друга гілка; null, якщо другу гілку не знайдено:
        /// тоді є лише лінія своєї рейки, без хрестовини.
        /// Результат однаковий, з котрої з двох гілок не рахуй: межі хрестовини в обох ті самі.
        /// </summary>
        public static BranchGeometry Branch(Section first, bool switchAtStart, bool innerFirst, Section partnerFirst, bool? partnerSwitchAtStart,
            bool partnerInnerFirst, Func<SectionLink, Section> resolve)
        {
            BranchGeometry result = new BranchGeometry();
            result.Frames = RailFrames(first, switchAtStart, innerFirst ? 1 : -1, MaxFrogSections, resolve);
            if (partnerSwitchAtStart == null) return result;

            result.Other = RailFrames(partnerFirst, partnerSwitchAtStart.Value, partnerInnerFirst ? 1 : -1, MaxFrogSections, resolve);
            int count = Math.Min(result.Frames.Count, result.Other.Count);
            result.Across = new double[count];

            double head = RailProfile.HeadHalfWidth * 2;
            // Відстань між серединами рейок, за якої між їхніми головками лишається жолоб
            double clearance = head + FrogFlangeway;
            int tip = -1, crossing = -1, full = -1;

            // Відстань між рейками в кожному їхньому спільному перерізі. Міряється там, де рейки справді
            // проходять крізь переріз, а не між точками з однаковим номером: ті не стоять одна навпроти одної,
            // бо колії порізані на шматки трохи різної довжини і відгалуження йде дугою.
            // Відстань зі знаком: до перетину сусідня рейка з боку осі своєї колії, це від'ємний бік, після
            // з протилежного. Число однакове, з якої колії не дивись, тому й межі хрестовини в обох ті самі
            double[] apart = new double[count];
            for (int q = 0; q < count; q++)
            {
                RailFrame station = CommonStation(result.Frames[q], result.Other[q]);
                if (station != null && RailLine.Offset(result.Frames, 0, station, q, out double mineAt)
                    && RailLine.Offset(result.Other, 0, station, q, out double theirsAt))
                {
                    apart[q] = theirsAt - mineAt;
                    continue;
                }

                // Спільного перерізу тут немає: вважаємо, що рейки стоять, як у попередньому
                apart[q] = q > 0 ? apart[q - 1] : -first.TrackWidth;
            }

            for (int q = 0; q < count; q++)
            {
                double across = apart[q];
                result.Across[q] = across;

                // Вусовик починає відходити від своєї лінії заздалегідь, за два жолоби до сусідньої рейки
                if (result.Throat < 0 && across > -2 * clearance) result.Throat = q;
                // Осердя починається там, де сходяться робочі грані двох рейок: за ширину головки до перетину осей
                if (tip < 0 && across > -head * 0.8) tip = q;
                if (crossing < 0 && across >= 0) crossing = q;
                // Хрестовина закінчується там, де рейки двох колій розійшлися так само далеко, як на її
                // початку: на два жолоби. Доти між променями осердя лежить лите дно, а не вузька щілина
                // до самої землі між двома звичайними рейками
                if (full < 0 && across >= 2 * clearance) full = q;
            }

            // Рейки так і не зійшлися в межах пройденого: хрестовини тут немає
            if (result.Throat < 0 || tip < 0 || crossing < 0)
            {
                result.Throat = -1;
                return result;
            }
            if (full < 0) full = count - 1;

            result.Tip = tip;
            result.Crossing = crossing;
            result.Full = full;
            // Переріз q лежить у секції з номером (q - 1) / 2 + 1
            result.HideFrom = Math.Max(result.Throat - 1, 0) / 2 + 1;
            result.HideTo = Math.Max(full - 1, 0) / 2 + 1;
            return result;
        }
    }
}
