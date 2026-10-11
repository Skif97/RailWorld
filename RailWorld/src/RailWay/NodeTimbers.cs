using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>Шпала, яку кладе стрілка: брус під обома коліями.</summary>
    public class Timber
    {
        /// <summary>Середина на рівні підошви рейки.</summary>
        public Vec3d Position;
        /// <summary>Напрямок, уздовж якого вона лежить.</summary>
        public Vec3d Across;
        public double Length;
        /// <summary>Секція, на місці шпали якої вона лежить: звідти дерево й ухил.</summary>
        public Section Owner;
    }

    /// <summary>Розкладка шпал стрілки: що вона кладе сама і секції, чиї власні шпали це заміняє.</summary>
    public class TimberLayout
    {
        public List<Timber> Timbers = new List<Timber>();
        public HashSet<Section> Hidden = new HashSet<Section>();
    }

    /// <summary>
    /// Розкладає шпали стрілки, як це роблять на залізниці.
    /// У звичайній стрілці основна колія, пряміша з двох, тримає свої шпали рівно, звичайним кроком. Поки
    /// відгалуження лежить на ній, власних шпал воно не має: кожна шпала основної колії подовжується в його
    /// бік до зовнішнього краю його шпал. Це довгі бруси, що тримають обидві колії.
    /// У розвилці, де гілки розходяться в різні боки приблизно однаково, основної колії немає: бруси лежать
    /// упоперек середньої лінії і тягнуться до зовнішніх країв шпал обох гілок.
    /// Гілка кладе власні шпали, повні й упоперек себе, щойно вони перестають зачіпати шпали другої колії.
    /// Бруси біля них вкорочені: рівно настільки, щоб на них не лягти.
    /// Чиста геометрія.
    /// </summary>
    public static class NodeTimbers
    {
        // Проміжок, ближче за який брус і власна шпала гілки одне до одного не лягають
        public const double ClipGap = 0.06;
        // На скільки брус має виступати за вісь рейки, щоб її тримати: підошва рейки і місце на кріплення
        public const double RailTail = 0.1;
        // У скільки разів одна гілка може відвертати крутіше за другу, щоб це ще була розвилка без основної колії
        public const double ForkTurnRatio = 1.5;
        // Коротший за це брус не кладеться
        private const double MinLength = 0.3;
        // На скільки блоків брус виступає за місце, де стоїть важіль стрілки
        public const double LeverBase = 0.35;
        // Скільки брусів, найближчих до важеля, подовжуються в його бік: на них стоятиме механізм стрілки
        public const int LeverTimbers = 2;

        // Шпала секції як вона лежить: середина, напрямок упоперек колії і пів довжини
        private struct Place
        {
            public Vec3d Middle, Across;
            public double Half;
        }

        /// <summary>
        /// first і second це секції двох гілок по порядку від стику. lever це місце важеля стрілки або null:
        /// бруси біля нього подовжуються в його бік.
        /// </summary>
        public static TimberLayout Lay(IList<Section> first, IList<Section> second, Vec3d lever = null)
        {
            var layout = new TimberLayout();
            if (first.Count == 0 || second.Count == 0) return layout;

            int compared = Math.Min(first.Count, second.Count);
            double firstTurn = Turn(first, compared), secondTurn = Turn(second, compared);
            double gentle = Math.Min(Math.Abs(firstTurn), Math.Abs(secondTurn)), steep = Math.Max(Math.Abs(firstTurn), Math.Abs(secondTurn));

            List<Timber> beams;
            if (firstTurn * secondTurn < 0 && steep <= gentle * ForkTurnRatio) beams = LayFork(first, second, layout);
            // Основна колія та, що менше відвертає від спільного напрямку
            else if (Math.Abs(firstTurn) <= Math.Abs(secondTurn)) beams = LayTurnout(first, second, layout);
            else beams = LayTurnout(second, first, layout);

            if (lever != null) ExtendToLever(beams, lever);
            layout.Timbers.AddRange(beams);
            return layout;
        }

        // Звичайна стрілка: бруси це шпали основної колії, подовжені під відгалуження
        private static List<Timber> LayTurnout(IList<Section> main, IList<Section> branch, TimberLayout layout)
        {
            var beams = new List<Timber>();
            Place[] mainPlaces = Places(main), branchPlaces = Places(branch);

            // Перша власна шпала відгалуження: та, що вже не зачіпає звичайних шпал основної колії
            int own = 0;
            for (; own < branch.Count; own++)
            {
                bool clear = true;
                for (int j = 0; j < main.Count && clear; j++) clear = !Touches(branchPlaces[own], Along(mainPlaces[j], -mainPlaces[j].Half), Along(mainPlaces[j], mainPlaces[j].Half));
                if (clear) break;
            }
            if (own == 0) return beams;
            for (int i = 0; i < own; i++) layout.Hidden.Add(branch[i]);

            var ownPlaces = new List<Place>();
            for (int i = own; i < branch.Count; i++) ownPlaces.Add(branchPlaces[i]);

            for (int j = 0; j < main.Count; j++)
            {
                Place place = mainPlaces[j];
                double axis = 0, cos = 1;
                bool positive = true;
                int crossed = -1;
                for (int i = 0; i < branch.Count && crossed < 0; i++)
                {
                    if (Reach(place, main[j], branch[i], out axis, out cos, out positive)) crossed = i;
                }
                if (crossed < 0) continue;

                // До зовнішнього краю шпал відгалуження, але не далі, ніж дозволяють його власні шпали
                double side = positive ? 1 : -1;
                var outward = new Vec3d(place.Across.X * side, place.Across.Y * side, place.Across.Z * side);
                double want = Math.Max(axis + branch[crossed].SleeperLength / 2 / cos, place.Half);
                double reach = Clip(place.Middle, outward, place.Half, want, ownPlaces);

                // Там, де відгалуження вже має свої шпали, подовжувати шпалу є сенс, лише якщо вона дістає
                // до його ближньої рейки: інакше це звичайна шпала з обрубком збоку
                if (crossed >= own && reach < axis - branch[crossed].TrackWidth / 2 / cos + RailTail) continue;

                // Подовжується лише кінець з боку відгалуження
                beams.Add(new Timber { Position = Along(place, (reach - place.Half) / 2 * side), Across = place.Across, Length = reach + place.Half, Owner = main[j] });
                layout.Hidden.Add(main[j]);
            }
            return beams;
        }

        // Розвилка: бруси лежать упоперек середньої лінії, від зовнішнього краю шпал однієї гілки до другої
        private static List<Timber> LayFork(IList<Section> first, IList<Section> second, TimberLayout layout)
        {
            var beams = new List<Timber>();
            Place[] firstPlaces = Places(first), secondPlaces = Places(second);
            int count = Math.Min(first.Count, second.Count);

            // Перші власні шпали гілок: ті, що вже не зачіпають шпал сусідньої гілки
            int own = 0;
            for (; own < count; own++)
            {
                bool clear = true;
                for (int i = own; i < count && clear; i++)
                {
                    clear = !Touches(firstPlaces[own], Along(secondPlaces[i], -secondPlaces[i].Half), Along(secondPlaces[i], secondPlaces[i].Half))
                        && !Touches(secondPlaces[own], Along(firstPlaces[i], -firstPlaces[i].Half), Along(firstPlaces[i], firstPlaces[i].Half));
                }
                if (clear) break;
            }

            var ownPlaces = new List<Place>();
            for (int i = own; i < first.Count; i++) ownPlaces.Add(firstPlaces[i]);
            for (int i = own; i < second.Count; i++) ownPlaces.Add(secondPlaces[i]);

            for (int k = 0; k < own; k++)
            {
                layout.Hidden.Add(first[k]);
                layout.Hidden.Add(second[k]);

                // Зовнішній кінець шпали кожної гілки: той, що далі від сусідньої
                Vec3d onFirst = OuterEnd(firstPlaces[k], secondPlaces[k].Middle), onSecond = OuterEnd(secondPlaces[k], firstPlaces[k].Middle);
                var middle = new Vec3d((firstPlaces[k].Middle.X + secondPlaces[k].Middle.X) / 2, (firstPlaces[k].Middle.Y + secondPlaces[k].Middle.Y) / 2, (firstPlaces[k].Middle.Z + secondPlaces[k].Middle.Z) / 2);
                Vec3d across = new Vec3d(onSecond.X - onFirst.X, onSecond.Y - onFirst.Y, onSecond.Z - onFirst.Z).Normalize();
                var back = new Vec3d(-across.X, -across.Y, -across.Z);

                // Від середини в кожен бік, не далі, ніж дозволяють власні шпали гілок
                double toSecond = Clip(middle, across, 0, Project(onSecond, middle, across), ownPlaces);
                double toFirst = Clip(middle, back, 0, Project(onFirst, middle, back), ownPlaces);
                if (toFirst + toSecond < MinLength) continue;

                double center = (toSecond - toFirst) / 2;
                beams.Add(new Timber
                {
                    Position = new Vec3d(middle.X + across.X * center, middle.Y + across.Y * center, middle.Z + across.Z * center),
                    Across = across, Length = toFirst + toSecond, Owner = first[k]
                });
            }
            return beams;
        }

        // На скільки колія відвернула від свого початкового напрямку за перші count секцій, радіани зі знаком
        private static double Turn(IList<Section> track, int count)
        {
            Vec3d a = track[0].FullStartPosition, b = track[0].FullEndPosition;
            Vec3d c = track[count - 1].FullStartPosition, d = track[count - 1].FullEndPosition;
            double ax = b.X - a.X, az = b.Z - a.Z, cx = d.X - c.X, cz = d.Z - c.Z;
            return Math.Atan2(ax * cz - az * cx, ax * cx + az * cz);
        }

        private static Place[] Places(IList<Section> track)
        {
            var places = new Place[track.Count];
            for (int i = 0; i < track.Count; i++) places[i] = PlaceOf(track[i]);
            return places;
        }

        private static Place PlaceOf(Section section)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            Vec3f normal = section.CenterNormal;
            return new Place
            {
                Middle = new Vec3d((start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2),
                Across = new Vec3d(normal.X, normal.Y, normal.Z).Normalize(),
                Half = section.SleeperLength / 2
            };
        }

        // Точка на шпалі за offset блоків від її середини
        private static Vec3d Along(Place place, double offset)
        {
            return new Vec3d(place.Middle.X + place.Across.X * offset, place.Middle.Y + place.Across.Y * offset, place.Middle.Z + place.Across.Z * offset);
        }

        // Той кінець шпали, що далі від точки away
        private static Vec3d OuterEnd(Place place, Vec3d away)
        {
            Vec3d a = Along(place, -place.Half), b = Along(place, place.Half);
            return a.HorizontalSquareDistanceTo(away.X, away.Z) >= b.HorizontalSquareDistanceTo(away.X, away.Z) ? a : b;
        }

        // Відстань від origin до точки p уздовж напрямку direction
        private static double Project(Vec3d p, Vec3d origin, Vec3d direction)
        {
            return (p.X - origin.X) * direction.X + (p.Y - origin.Y) * direction.Y + (p.Z - origin.Z) * direction.Z;
        }

        // Чи шпала лежить ближче за ClipGap до бруса, що тягнеться від from до to
        private static bool Touches(Place sleeper, Vec3d from, Vec3d to)
        {
            double width = SectionBox.SleeperHalfWidth * 2;
            return Footprint.Overlap(
                Along(sleeper, -sleeper.Half - ClipGap), Along(sleeper, sleeper.Half + ClipGap), width + ClipGap * 2,
                from, to, width, out _, out _);
        }

        // На скільки брус може тягнутися від origin у бік direction, щоб не лягти на жодну з власних шпал
        // гілок: не менше за min і не більше за want
        private static double Clip(Vec3d origin, Vec3d direction, double min, double want, List<Place> sleepers)
        {
            if (want <= min || !TouchesAny(origin, direction, want, sleepers)) return Math.Max(want, min);

            double free = min, taken = want;
            for (int step = 0; step < 24; step++)
            {
                double middle = (free + taken) / 2;
                if (TouchesAny(origin, direction, middle, sleepers)) taken = middle;
                else free = middle;
            }
            return free;
        }

        private static bool TouchesAny(Vec3d origin, Vec3d direction, double reach, List<Place> sleepers)
        {
            var to = new Vec3d(origin.X + direction.X * reach, origin.Y + direction.Y * reach, origin.Z + direction.Z * reach);
            foreach (Place sleeper in sleepers)
            {
                if (Touches(sleeper, origin, to)) return true;
            }
            return false;
        }

        // Де брус на місці шпали own секції section перетинає вісь секції other. axis це відстань уздовж бруса
        // від осі основної колії до осі другої, cos це косинус кута між двома коліями, positive каже, з якого
        // боку від осі. false, якщо брус осі цієї секції не перетинає
        private static bool Reach(Place own, Section section, Section other, out double axis, out double cos, out bool positive)
        {
            axis = 0;
            cos = 1;
            positive = true;

            Vec3d c = other.FullStartPosition, d = other.FullEndPosition;
            double sx = d.X - c.X, sz = d.Z - c.Z;
            double otherLength = Math.Sqrt(sx * sx + sz * sz);
            double ax = own.Across.X, az = own.Across.Z;
            double acrossLength = Math.Sqrt(ax * ax + az * az);
            if (otherLength < 1e-9 || acrossLength < 1e-9) return false;
            ax /= acrossLength;
            az /= acrossLength;

            // Пряма бруса проти осі секції
            double denominator = ax * sz - az * sx;
            if (Math.Abs(denominator) < 1e-9) return false;
            double t = ((c.X - own.Middle.X) * sz - (c.Z - own.Middle.Z) * sx) / denominator;
            double u = ((c.X - own.Middle.X) * az - (c.Z - own.Middle.Z) * ax) / denominator;
            if (u < -1e-6 || u > 1 + 1e-6) return false;

            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            double mx = end.X - start.X, mz = end.Z - start.Z;
            double mainLength = Math.Sqrt(mx * mx + mz * mz);
            if (mainLength < 1e-9) return false;
            // Шпали й рейки другої колії брус перетинає навскоси: уздовж нього їхня смуга ширша в 1/cos разів
            cos = Math.Abs(mx * sx + mz * sz) / (mainLength * otherLength);
            if (cos < 1e-3) return false;

            axis = Math.Abs(t);
            positive = t >= 0;
            return true;
        }

        // Бруси, найближчі до важеля стрілки, тягнуться в його бік і трохи далі за нього
        private static void ExtendToLever(List<Timber> beams, Vec3d lever)
        {
            var nearest = new List<Timber>(beams);
            nearest.Sort((x, y) => DistanceToLine(x, lever).CompareTo(DistanceToLine(y, lever)));

            for (int n = 0; n < Math.Min(LeverTimbers, nearest.Count); n++)
            {
                Timber timber = nearest[n];
                double along = (lever.X - timber.Position.X) * timber.Across.X + (lever.Y - timber.Position.Y) * timber.Across.Y + (lever.Z - timber.Position.Z) * timber.Across.Z;
                double grow = Math.Abs(along) + LeverBase - timber.Length / 2;
                if (grow <= 0) continue;

                // Подовжується лише кінець з боку важеля: середина бруса зсувається на половину приросту
                double shift = grow / 2 * Math.Sign(along);
                timber.Length += grow;
                timber.Position = new Vec3d(timber.Position.X + timber.Across.X * shift, timber.Position.Y + timber.Across.Y * shift, timber.Position.Z + timber.Across.Z * shift);
            }
        }

        // Наскільки важіль осторонь від прямої, на якій лежить брус: уздовж колії
        private static double DistanceToLine(Timber timber, Vec3d lever)
        {
            double dx = lever.X - timber.Position.X, dz = lever.Z - timber.Position.Z;
            return Math.Abs(dx * timber.Across.Z - dz * timber.Across.X);
        }
    }
}
