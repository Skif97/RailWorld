using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>Шпала, яку кладе вузол, стрілка чи перетин: брус під обома коліями.</summary>
    public class Timber
    {
        /// <summary>Середина на рівні підошви рейки.</summary>
        public Vec3d Position;
        /// <summary>Напрямок, уздовж якого вона лежить.</summary>
        public Vec3d Across;
        public double Length;
        /// <summary>Секція, на місці шпали якої вона лежить: звідти дерево й ухил.</summary>
        public Section Owner;
        /// <summary>
        /// На брусі лежить металева пластина від рамної рейки до рамної: по ній ковзають гостряки, коли стрілку
        /// переводять. Такі два бруси, між якими стоїть важіль.
        /// </summary>
        public bool SlidePlate;
    }

    /// <summary>Розкладка шпал вузла: що він кладе сам і секції, чиї власні шпали це заміняє.</summary>
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
    /// Перетин під гострим кутом кладеться як розвилка: бруси впоперек бісектриси. Під кутом, близьким до
    /// прямого, основна колія тримає свої шпали, у вузлі вдвічі частіше: це щит, на якому лежить друга колія.
    /// Чиста геометрія.
    /// </summary>
    public static class NodeTimbers
    {
        // Проміжок, ближче за який брус і власна шпала гілки одне до одного не лягають
        public const double ClipGap = 0.06;
        // На скільки брус має виступати за вісь рейки, щоб її тримати: підошва рейки і місце на кріплення
        public const double RailTail = 0.1;
        /// <summary>Найменший кут між коліями перетину, за якого під ним кладеться щит, градуси.</summary>
        public const double DeckAngleDeg = 60;
        // Як далеко від свого кінця шпала щита шукає першу шпалу другої колії, щоб дотягнутися до неї
        public const double DeckReach = 1;
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
        /// first і second це секції двох гілок по порядку від стику, у порядку гілок запису стрілки; shape
        /// каже, котра з них основна, або що це розвилка (RailSwitch.Shape). lever це місце важеля стрілки
        /// або null: бруси біля нього подовжуються в його бік.
        /// </summary>
        public static TimberLayout Lay(IList<Section> first, IList<Section> second, SwitchShape shape, Vec3d lever = null)
        {
            var layout = new TimberLayout();
            if (first.Count == 0 || second.Count == 0) return layout;

            List<Timber> beams;
            if (shape == SwitchShape.Fork) beams = LayFork(first, second, layout);
            else if (shape == SwitchShape.MainSecond) beams = LayTurnout(second, first, layout);
            else beams = LayTurnout(first, second, layout);

            if (lever != null) ExtendToLever(beams, lever);
            layout.Timbers.AddRange(beams);
            return layout;
        }

        /// <summary>
        /// Шпали перетину. main і other це секції двох колій по порядку крізь вузол, mainCenter і otherCenter
        /// номери (з нуля) секцій, осі яких перетинаються. main це основна колія.
        /// </summary>
        public static TimberLayout LayCrossing(IList<Section> main, int mainCenter, IList<Section> other, int otherCenter)
        {
            var layout = new TimberLayout();
            if (mainCenter < 0 || mainCenter >= main.Count || otherCenter < 0 || otherCenter >= other.Count) return layout;

            Vec3d along = Chord(main[mainCenter]), across = Chord(other[otherCenter]);
            double cos = Math.Abs(along.X * across.X + along.Z * across.Z);
            double angle = Math.Acos(GameMath.Clamp(cos, 0, 1)) * 180 / Math.PI;

            if (angle >= DeckAngleDeg) LayDeck(main, other, otherCenter, layout);
            else LayAcross(main, mainCenter, other, otherCenter, layout);
            return layout;
        }

        // Перетин під кутом, близьким до прямого: щит зі шпал основної колії, у вузлі вдвічі частіших.
        // Друга колія лежить на ньому без власних шпал
        private static void LayDeck(IList<Section> main, IList<Section> other, int otherCenter, TimberLayout layout)
        {
            Place[] mainPlaces = Places(main), otherPlaces = Places(other);
            HiddenRange(otherPlaces, otherCenter, mainPlaces, out int from, out int to);

            var ownPlaces = new List<Place>();
            for (int i = 0; i < other.Count; i++)
            {
                if (i >= from && i <= to) layout.Hidden.Add(other[i]);
                else ownPlaces.Add(otherPlaces[i]);
            }

            for (int j = 0; j < main.Count; j++)
            {
                if (OnTrack(mainPlaces[j], other))
                {
                    layout.Hidden.Add(main[j]);
                    layout.Timbers.Add(DeckTimber(mainPlaces[j], main[j], ownPlaces));
                }

                // Ще одна шпала посередині між цією і наступною
                if (j + 1 >= main.Count) continue;
                Place between = Between(mainPlaces[j], mainPlaces[j + 1]);
                if (OnTrack(between, other) && !TouchesAny(Along(between, -between.Half), between.Across, between.Half * 2, ownPlaces))
                    layout.Timbers.Add(DeckTimber(between, main[j], ownPlaces));
            }
        }

        // Шпала щита: у кожен бік тягнеться до першої шпали другої колії, якщо та лежить поруч
        private static Timber DeckTimber(Place place, Section owner, List<Place> ownPlaces)
        {
            var back = new Vec3d(-place.Across.X, -place.Across.Y, -place.Across.Z);
            double limit = place.Half + DeckReach;
            double forward = Clip(place.Middle, place.Across, place.Half, limit, ownPlaces);
            double backward = Clip(place.Middle, back, place.Half, limit, ownPlaces);
            // Нічого не зустріла: звичайної довжини
            if (forward >= limit - 1e-6) forward = place.Half;
            if (backward >= limit - 1e-6) backward = place.Half;

            return new Timber { Position = Along(place, (forward - backward) / 2), Across = place.Across, Length = forward + backward, Owner = owner };
        }

        // Перетин під гострим кутом: бруси впоперек бісектриси, від зовнішнього краю шпал однієї колії до другої
        private static void LayAcross(IList<Section> main, int mainCenter, IList<Section> other, int otherCenter, TimberLayout layout)
        {
            Place[] mainPlaces = Places(main), otherPlaces = Places(other);
            HiddenRange(mainPlaces, mainCenter, otherPlaces, out int mainFrom, out int mainTo);
            HiddenRange(otherPlaces, otherCenter, mainPlaces, out int otherFrom, out int otherTo);

            // Бісектриса гострого кута між коліями
            Vec3d a = Chord(main[mainCenter]), b = Chord(other[otherCenter]);
            if (a.X * b.X + a.Z * b.Z < 0) b = new Vec3d(-b.X, 0, -b.Z);
            Vec3d middleLine = new Vec3d(a.X + b.X, 0, a.Z + b.Z).Normalize();
            var sideways = new Vec3d(-middleLine.Z, 0, middleLine.X);
            Vec3d crossAt = AxesCross(main[mainCenter], other[otherCenter]);

            var ownPlaces = new List<Place>();
            var ends = new List<Vec3d>();
            double reach = 0;
            for (int k = 0; k < 2; k++)
            {
                IList<Section> track = k == 0 ? main : other;
                Place[] places = k == 0 ? mainPlaces : otherPlaces;
                int from = k == 0 ? mainFrom : otherFrom, to = k == 0 ? mainTo : otherTo;
                for (int i = 0; i < places.Length; i++)
                {
                    ends.Add(Along(places[i], -places[i].Half));
                    ends.Add(Along(places[i], places[i].Half));
                    if (i < from || i > to)
                    {
                        ownPlaces.Add(places[i]);
                        continue;
                    }
                    layout.Hidden.Add(track[i]);
                    reach = Math.Max(reach, Math.Abs(Project(places[i].Middle, crossAt, middleLine)));
                }
            }

            // Бруси кроком звичайних шпал уздовж бісектриси, в обидва боки від місця, де перетнулися осі.
            // Усе вище пораховано в плані, а крок шпал міряється вздовж колії: на схилі в плані він коротший
            double step = main[mainCenter].FullStartPosition.DistanceTo(main[mainCenter].FullEndPosition);
            if (step < 1e-6) return;
            const double probe = 0.25;
            if (Deck(main, mainPlaces, Shifted(crossAt, middleLine, -probe), out double below) && Deck(main, mainPlaces, Shifted(crossAt, middleLine, probe), out double above))
            {
                double rise = (above - below) / (2 * probe);
                step /= Math.Sqrt(1 + rise * rise);
            }
            var back = new Vec3d(-sideways.X, 0, -sideways.Z);
            for (double at = step / 2; at <= reach + step / 2; at += step)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var middle = new Vec3d(crossAt.X + middleLine.X * at * sign, 0, crossAt.Z + middleLine.Z * at * sign);
                    int nearest = Nearest(mainPlaces, middle);
                    middle.Y = mainPlaces[nearest].Middle.Y;

                    // У кожен бік до зовнішнього краю шпал, не далі, ніж дозволяють власні шпали колій
                    double right = Clip(middle, sideways, 0, OuterEdge(middle, middleLine, sideways, ends, step), ownPlaces);
                    double left = Clip(middle, back, 0, OuterEdge(middle, middleLine, back, ends, step), ownPlaces);
                    if (left + right < MinLength) continue;

                    // Кінці бруса знайдено в плані. Лежить він на полотні основної колії: на схилі воно під
                    // брусом піднімається, і рівний брус одним кінцем задерся б над рейками, а другим провалився
                    Vec3d from = Shifted(middle, sideways, -left), to = Shifted(middle, sideways, right);
                    // Брус тут, щоб тримати рейки секцій, шпали яких він заміняє. Обрізаний об власні шпали колій
                    // так, що під ним жодної такої рейки, він лише лежав би між коліями
                    if (!CarriesRail(from, to, layout.Hidden)) continue;
                    if (Deck(main, mainPlaces, from, out double fromY)) from.Y = fromY;
                    if (Deck(main, mainPlaces, to, out double toY)) to.Y = toY;

                    var span = new Vec3d(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
                    double length = span.Length();
                    layout.Timbers.Add(new Timber
                    {
                        Position = new Vec3d((from.X + to.X) / 2, (from.Y + to.Y) / 2, (from.Z + to.Z) / 2),
                        Across = span.Normalize(), Length = length, Owner = main[nearest]
                    });
                }
            }
        }

        // Точка за distance блоків від point у напрямку direction, на тій самій висоті
        private static Vec3d Shifted(Vec3d point, Vec3d direction, double distance)
        {
            return new Vec3d(point.X + direction.X * distance, point.Y, point.Z + direction.Z * distance);
        }

        // Висота полотна колії над точкою. track це секції колії по порядку, places їхні шпали.
        // false, якщо полотна над точкою немає
        private static bool Deck(IList<Section> track, Place[] places, Vec3d p, out double height)
        {
            int nearest = Nearest(places, p);
            Section section = track[nearest];

            // Сусіди з боку початку й кінця секції: у списку вони йдуть підряд, але секції колії можуть бути
            // покладені в різні боки
            Section before = null, after = null;
            for (int n = nearest - 1; n <= nearest + 1; n += 2)
            {
                if (n < 0 || n >= track.Count) continue;
                Vec3d joint = track[n].FullStartPosition.SquareDistanceTo(section.GetGlobalPos()) < track[n].FullEndPosition.SquareDistanceTo(section.GetGlobalPos())
                    ? track[n].FullStartPosition : track[n].FullEndPosition;
                if (joint.SquareDistanceTo(section.FullStartPosition) < joint.SquareDistanceTo(section.FullEndPosition)) before = track[n];
                else after = track[n];
            }
            return TrackSurface.At(section, before, after, p, 0, 0, out height, out _);
        }

        // Котрі шпали колії навколо перетнутої секції не кладуться: від неї в обидва боки, поки шпала ще
        // зачіпає шпали другої колії
        private static void HiddenRange(Place[] places, int center, Place[] others, out int from, out int to)
        {
            from = to = center;
            while (from - 1 >= 0 && TouchesTrack(places[from - 1], others)) from--;
            while (to + 1 < places.Length && TouchesTrack(places[to + 1], others)) to++;
        }

        private static bool TouchesTrack(Place sleeper, Place[] others)
        {
            foreach (Place other in others)
            {
                if (Touches(sleeper, Along(other, -other.Half), Along(other, other.Half))) return true;
            }
            return false;
        }

        // Чи шпала на цьому місці лежить під другою колією: у смузі її шпал
        private static bool OnTrack(Place sleeper, IList<Section> track)
        {
            Vec3d from = Along(sleeper, -sleeper.Half), to = Along(sleeper, sleeper.Half);
            foreach (Section section in track)
            {
                if (Footprint.Overlap(from, to, SectionBox.SleeperHalfWidth * 2, section.FullStartPosition, section.FullEndPosition, section.SleeperLength, out _, out _)) return true;
            }
            return false;
        }

        // Місце шпали посередині між двома сусідніми. Секції колії можуть бути покладені в різні боки, тоді
        // їхні нормалі дивляться назустріч і в сумі дали б нуль: другу спершу розвертаємо в бік першої
        private static Place Between(Place a, Place b)
        {
            int same = a.Across.X * b.Across.X + a.Across.Z * b.Across.Z < 0 ? -1 : 1;
            return new Place
            {
                Middle = new Vec3d((a.Middle.X + b.Middle.X) / 2, (a.Middle.Y + b.Middle.Y) / 2, (a.Middle.Z + b.Middle.Z) / 2),
                Across = new Vec3d(a.Across.X + b.Across.X * same, a.Across.Y + b.Across.Y * same, a.Across.Z + b.Across.Z * same).Normalize(),
                Half = (a.Half + b.Half) / 2
            };
        }

        // Напрямок секції в плані
        private static Vec3d Chord(Section section)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            var chord = new Vec3d(end.X - start.X, 0, end.Z - start.Z);
            return chord.Length() < 1e-9 ? new Vec3d(1, 0, 0) : chord.Normalize();
        }

        // Де в плані перетинаються осі двох секцій
        private static Vec3d AxesCross(Section first, Section second)
        {
            Vec3d a = first.FullStartPosition, c = second.FullStartPosition;
            Vec3d r = Chord(first), s = Chord(second);
            if (!PlanLines.Meet(a.X, a.Z, r.X, r.Z, c.X, c.Z, s.X, s.Z, out double t, out _)) return first.GetGlobalPos();
            return new Vec3d(a.X + r.X * t, a.Y, a.Z + r.Z * t);
        }

        // Чи в плані над брусом від from до to проходить рейка котроїсь із секцій
        private static bool CarriesRail(Vec3d from, Vec3d to, IEnumerable<Section> sections)
        {
            double sx = to.X - from.X, sz = to.Z - from.Z;
            foreach (Section section in sections)
            {
                Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
                Vec3f normal = section.CenterNormal;
                for (int side = -1; side <= 1; side += 2)
                {
                    double offset = section.TrackWidth / 2 * side;
                    if (PlanLines.Meet(start.X + normal.X * offset, start.Z + normal.Z * offset, end.X - start.X, end.Z - start.Z,
                            from.X, from.Z, sx, sz, out double t, out double u) && t >= 0 && t <= 1 && u >= 0 && u <= 1) return true;
                }
            }
            return false;
        }

        // Номер місця, найближчого в плані до точки
        private static int Nearest(Place[] places, Vec3d p)
        {
            int best = 0;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < places.Length; i++)
            {
                double distance = places[i].Middle.HorizontalSquareDistanceTo(p.X, p.Z);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        // Як далеко в бік direction від точки на бісектрисі лежить зовнішній край шпал: найдальший кінець
        // шпали серед тих, що стоять навпроти цієї точки
        private static double OuterEdge(Vec3d middle, Vec3d middleLine, Vec3d direction, List<Vec3d> ends, double step)
        {
            double edge = 0;
            foreach (Vec3d end in ends)
            {
                double dx = end.X - middle.X, dz = end.Z - middle.Z;
                if (Math.Abs(dx * middleLine.X + dz * middleLine.Z) > step / 2) continue;
                edge = Math.Max(edge, dx * direction.X + dz * direction.Z);
            }
            return edge;
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
            if (!PlanLines.Meet(own.Middle.X, own.Middle.Z, ax, az, c.X, c.Z, sx, sz, out double t, out double u)) return false;
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
                timber.SlidePlate = true;
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
