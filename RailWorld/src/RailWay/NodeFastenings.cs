using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>Рейка однієї секції у вузлі: три точки її осі, від початку секції через середину до кінця.</summary>
    public class RailRun
    {
        public Section Section;
        public SectionPart Part;
        public Vec3d From, Middle, To;
    }

    /// <summary>Чим рейка прикріплена до бруса.</summary>
    public enum FasteningKind
    {
        /// <summary>Підкладка з костилями, як на звичайній колії.</summary>
        Plate,
        /// <summary>Лише костилі, по два з кожного боку підошви: підкладці тут немає місця.</summary>
        Spikes,
        /// <summary>
        /// Поруч із рейкою стоїть контррейка: костилі із зовнішнього боку ходової рейки і з боку контррейки,
        /// а між двома рейками їх немає.
        /// </summary>
        GuardSpikes,
        /// <summary>
        /// Костилі лише із зовнішнього боку рейки. Так на пластині під гостряки, де зсередини до рейки
        /// притискається гостряк, і біля відігнутого кінця контррейки, який до бруса не кріпиться.
        /// </summary>
        OuterSpikes
    }

    /// <summary>Скріплення рейки з брусом вузла: де воно стоїть і куди йде рейка в цьому місці.</summary>
    public class NodeFastening
    {
        public Vec3d Position;
        /// <summary>Напрямок рейки. Одиничний.</summary>
        public Vec3d Along;
        public Section Section;
        public SectionPart Part;
        public FasteningKind Kind;
    }

    /// <summary>
    /// Розставляє скріплення рейок на брусах стрілки чи перетину. У вузлі шпал секцій немає, є бруси, і лежать
    /// вони не під серединами секцій. Тому скріплення стоїть там, де рейка перетинає брус.
    /// Буває і так, що рейка лежить уздовж брусів: друга колія на щиті перетину під кутом, близьким до прямого.
    /// Перетинів з брусами в неї немає, вона лежить на них усім своїм довгим боком. Тоді скріплення стоять, як
    /// на звичайній колії, посередині кожної секції, якщо під цим місцем є дерево.
    /// Рейки у вузлі сходяться, і підкладка, ширша за підошву рейки, поміщається не скрізь. Дві підкладки одна
    /// на одну не лягають: де вони налізли б, обидві рейки тримаються самими костилями. Так само там, де поруч
    /// із рейкою стоїть контррейка.
    /// Чиста геометрія.
    /// </summary>
    public static class NodeFastenings
    {
        /// <summary>
        /// Гостріше за цей кут між рейкою і брусом вважається, що рейка лежить уздовж бруса, а не перетинає його.
        /// </summary>
        public const double AlongTimberDeg = 45;

        // Розміри підкладки з моделі shapes/part/fastening.json, блоків: пів довжини вздовж рейки і пів ширини
        // впоперек. Якщо підкладку в моделі змінено, ці числа треба змінити теж
        public const double PlateHalfLength = 1.6 / 16;
        public const double PlateHalfWidth = 2.4 / 16;

        /// <summary>
        /// guardBeside каже, чи біля рейки в цьому місці стоїть контррейка і як тоді рейку кріпити: GuardSpikes,
        /// якщо костилі потрібні і з боку контррейки, OuterSpikes, якщо лише ззовні (відігнутий кінець контррейки
        /// до бруса не кріпиться). null, якщо контррейки поруч немає або їх у вузлі немає зовсім.
        /// </summary>
        public static List<NodeFastening> Lay(IList<Timber> timbers, IList<RailRun> rails, Func<RailRun, Vec3d, FasteningKind?> guardBeside = null)
        {
            var found = new List<NodeFastening>();
            double along = Math.Cos(AlongTimberDeg * Math.PI / 180);

            foreach (RailRun rail in rails)
            {
                Vec3d[] points = { rail.From, rail.Middle, rail.To };
                for (int piece = 0; piece < 2; piece++)
                {
                    Vec3d from = points[piece], to = points[piece + 1];
                    double rx = to.X - from.X, rz = to.Z - from.Z;
                    double length = Math.Sqrt(rx * rx + rz * rz);
                    if (length < 1e-9) continue;

                    foreach (Timber timber in timbers)
                    {
                        // Рейка майже вздовж бруса: перетином це не вважається
                        if (Math.Abs(rx * timber.Across.X + rz * timber.Across.Z) / length > along) continue;
                        if (!Crosses(from, to, timber, out Vec3d position)) continue;

                        found.Add(Fastening(rail, position, Direction(from, to), guardBeside, timber));
                    }
                }

                // Рейка вздовж брусів: посередині секції, якщо під цим місцем лежить брус
                Vec3d direction = Direction(rail.From, rail.To);
                foreach (Timber timber in timbers)
                {
                    if (Math.Abs(direction.X * timber.Across.X + direction.Z * timber.Across.Z) <= along) continue;
                    if (!Under(timber, rail.Middle)) continue;

                    found.Add(Fastening(rail, rail.Middle.Clone(), direction, guardBeside, timber));
                    break;
                }
            }

            // Скріплення, костилі якого не потрапляють у дерево, не ставиться зовсім: воно висіло б у повітрі
            found.RemoveAll(fastening => !NailsOnWood(fastening.Position, fastening.Along, timbers, 0));

            // Підкладки, що налізли б одна на одну, не кладуться: рахується місце кожної, хоч чим рейка там
            // зрештою прикріплена, бо місце займає сама рейка з костилями
            var crowded = new bool[found.Count];
            for (int a = 0; a < found.Count; a++)
            {
                for (int b = a + 1; b < found.Count; b++)
                {
                    if (!PlatesOverlap(found[a], found[b])) continue;
                    crowded[a] = crowded[b] = true;
                }
            }
            for (int n = 0; n < found.Count; n++)
            {
                if (crowded[n] && found[n].Kind == FasteningKind.Plate) found[n].Kind = FasteningKind.Spikes;
            }
            return found;
        }

        private static NodeFastening Fastening(RailRun rail, Vec3d position, Vec3d along, Func<RailRun, Vec3d, FasteningKind?> guardBeside, Timber timber)
        {
            FasteningKind kind = guardBeside?.Invoke(rail, position) ?? FasteningKind.Plate;
            if (kind == FasteningKind.Plate && timber.SlidePlate) kind = FasteningKind.OuterSpikes;

            return new NodeFastening { Position = position, Along = along, Section = rail.Section, Part = rail.Part, Kind = kind };
        }

        // Де в моделі стоять костилі відносно середини скріплення, блоків: уздовж рейки і вбік від її осі
        // (shapes/part/fastening.json і spikepair.json). Якщо костилі в моделі пересунуто, змінити й тут
        public const double SpikeAlong = 0.75 / 16;
        public const double SpikeAside = 1.15 / 16;

        /// <summary>
        /// Де відрізок рейки від from до to перетинає в плані вісь бруса. Кінець відрізка належить наступному
        /// відрізку: на стику скріплення одне.
        /// </summary>
        public static bool Crosses(Vec3d from, Vec3d to, Timber timber, out Vec3d at)
        {
            at = null;
            double rx = to.X - from.X, rz = to.Z - from.Z;
            double half = timber.Length / 2;
            double ax = timber.Position.X - timber.Across.X * half, az = timber.Position.Z - timber.Across.Z * half;
            double sx = timber.Across.X * timber.Length, sz = timber.Across.Z * timber.Length;
            if (!PlanLines.Meet(from.X, from.Z, rx, rz, ax, az, sx, sz, out double t, out double u)) return false;
            if (t < 0 || t >= 1 || u < 0 || u > 1) return false;

            at = new Vec3d(from.X + rx * t, from.Y + (to.Y - from.Y) * t, from.Z + rz * t);
            return true;
        }

        /// <summary>
        /// Чи костилі скріплення потрапляють у дерево. Сама підкладка може трохи звисати з бруса, але місця,
        /// куди забито костилі, мають лежати над ним: інакше костиль б'ється в повітря. Площу не рахуємо,
        /// дивимося лише на ці місця. Вони можуть лежати на різних брусах: на щиті перетину бруси йдуть упритул.
        /// side каже, з якого боку рейки стоять костилі: 1 з боку (z, -x) її напрямку (x, z), -1 з протилежного, 0 з обох.
        /// </summary>
        public static bool NailsOnWood(Vec3d position, Vec3d along, IList<Timber> timbers, int side)
        {
            double ax = along.X, az = along.Z;
            double length = Math.Sqrt(ax * ax + az * az);
            if (length < 1e-9) return false;
            ax /= length;
            az /= length;

            for (int lengthwise = -1; lengthwise <= 1; lengthwise += 2)
            {
                for (int sideways = -1; sideways <= 1; sideways += 2)
                {
                    if (side != 0 && sideways != side) continue;

                    // Бік, який рахується додатним: (az, -ax)
                    double forward = SpikeAlong * lengthwise, aside = SpikeAside * sideways;
                    var nail = new Vec3d(position.X + ax * forward + az * aside, position.Y, position.Z + az * forward - ax * aside);

                    bool onWood = false;
                    foreach (Timber timber in timbers)
                    {
                        if (!Under(timber, nail)) continue;
                        onWood = true;
                        break;
                    }
                    if (!onWood) return false;
                }
            }
            return true;
        }

        private static bool PlatesOverlap(NodeFastening a, NodeFastening b)
        {
            // Далі за дві діагоналі підкладки вони точно не зачепляться
            double reach = 2 * (PlateHalfLength + PlateHalfWidth);
            if (Math.Abs(a.Position.X - b.Position.X) > reach || Math.Abs(a.Position.Z - b.Position.Z) > reach) return false;

            return Footprint.Overlap(End(a, -1), End(a, 1), PlateHalfWidth * 2, End(b, -1), End(b, 1), PlateHalfWidth * 2, out _, out _);
        }

        private static Vec3d End(NodeFastening fastening, int sign)
        {
            return new Vec3d(fastening.Position.X + fastening.Along.X * PlateHalfLength * sign, fastening.Position.Y,
                fastening.Position.Z + fastening.Along.Z * PlateHalfLength * sign);
        }

        private static Vec3d Direction(Vec3d from, Vec3d to)
        {
            var direction = new Vec3d(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
            return direction.Length() < 1e-9 ? new Vec3d(1, 0, 0) : direction.Normalize();
        }

        // Чи точка лежить у плані над брусом
        private static bool Under(Timber timber, Vec3d p)
        {
            double dx = p.X - timber.Position.X, dz = p.Z - timber.Position.Z;
            double lengthwise = dx * timber.Across.X + dz * timber.Across.Z;
            double sideways = -dx * timber.Across.Z + dz * timber.Across.X;
            return Math.Abs(lengthwise) <= timber.Length / 2 && Math.Abs(sideways) <= SectionBox.SleeperHalfWidth;
        }
    }
}
