using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Складає маршрут із точок, які поставив гравець. Чиста геометрія: нею однаково користуються
    /// клієнт, щоб показати маршрут, і сервер, щоб його побудувати.
    /// </summary>
    public static class RouteBuilder
    {
        /// <summary>
        /// З точок розмітки робить маршрут: кінці виносить на край блока, проміжні точки вирівнює по сусідах
        /// і скруглює кути. null, якщо точок менше двох.
        /// </summary>
        public static List<RoutePoint> Build(List<RouteMarker> points)
        {
            int count = points.Count;
            if (count < 2) return null;

            var route = new List<RoutePoint>();
            for (int i = 0; i < count; i++)
            {
                RouteMarker marker = points[i];
                Vec3d position = marker.Position.Clone();
                Vec3d tangent = marker.Heading.Clone();

                if (!marker.Snapped)
                {
                    if (i == 0)
                    {
                        // Початок маршруту на краю блока позаду, щоб колія займала блок цілком
                        position = ModMath.DirectionVectorPositionCorrection(position, tangent.Clone(), true);
                    }
                    else if (i == count - 1)
                    {
                        position = ModMath.DirectionVectorPositionCorrection(position, tangent.Clone().Mul(-1), true);
                    }
                    else
                    {
                        // Проміжна точка задає лише місце, а не напрямок. Напрямок це бісектриса між тим, звідки
                        // маршрут прийшов, і тим, куди піде далі: поворот ділиться порівну на обидва боки точки
                        // і не залежить від того, яка з двох ділянок довша
                        Vec3d prev = points[i - 1].Position;
                        Vec3d next = points[i + 1].Position;
                        double before = HorizontalDistance(prev, marker.Position);
                        double after = HorizontalDistance(marker.Position, next);

                        if (before > 1e-6 && after > 1e-6)
                        {
                            double inX = (marker.Position.X - prev.X) / before, inZ = (marker.Position.Z - prev.Z) / before;
                            double outX = (next.X - marker.Position.X) / after, outZ = (next.Z - marker.Position.Z) / after;
                            double x = inX + outX, z = inZ + outZ;
                            double length = Math.Sqrt(x * x + z * z);

                            // Розворот назад або майже назад: бісектриса зникає або стрибає з боку на бік
                            // від найменшого зсуву точок. Тоді дотична стає впоперек, у той бік, куди маршрут
                            // уже закручувався на попередній ділянці: так точки на двох колонах по черзі
                            // дають спіраль, а не зигзаг, що лягає сам на себе
                            if (length < UTurnSum)
                            {
                                Vec3d arriving = route[i - 1].Tangent;
                                double sense = arriving.X * inZ - arriving.Z * inX;
                                // Попередня ділянка пряма: бік підказує сама геометрія точок, якщо підказує
                                if (Math.Abs(sense) < 0.05) sense = inX * outZ - inZ * outX;

                                double side = sense < 0 ? -1 : 1;
                                x = -side * inZ;
                                z = side * inX;
                                length = 1;
                            }

                            // На кінцях маршрут горизонтальний. У проміжній точці дотична нахилена так,
                            // щоб плавно вести від висоти попередньої точки до висоти наступної
                            tangent = new Vec3d(x / length, (next.Y - prev.Y) / (before + after), z / length).Normalize();
                        }
                    }
                }

                route.Add(new RoutePoint
                {
                    Position = position, Tangent = tangent, Cant = marker.Cant,
                    Track = marker.Track, TrackJointAtStart = marker.TrackJointAtStart
                });
            }

            return RoundCorners(points, route);
        }

        // Дуга, якою маршрут скруглює кут, не тісніша за найменший радіус із невеликим запасом: крива Безьє
        // повторює дугу кола не точно і місцями вийшла б на волосину крутішою
        private const double CornerRadiusFactor = 1.05;

        // Радіус дуги, якою маршрут сходить з напрямку першої точки і заходить у напрямок останньої, у найменших
        // радіусах: спершу пробуємо просторішу, і лише якщо вона не вміщається, найтіснішу дозволену
        private static readonly double[] LeadRadiusFactors = { 1.6, CornerRadiusFactor };

        // Вузол маршруту під час скруглення
        private class Node
        {
            public Vec3d Position;
            public Vec3d Tangent;
            // Кут ламаної, який маршрут скруглює; решта вузлів це точки, через які він проходить
            public bool Corner;
            // Точка, додана між крайньою точкою маршруту і першим кутом: кінець дуги, якою маршрут сходить
            // із заданого напрямку. Її напрямок уже збігається з прямою до кута
            public bool Lead;
            // Дуга сходу вийшла надто короткою, щоб стати окремою ділянкою. Точка лише задає пряму до кута,
            // а в маршрут не потрапляє: кут з'єднується з крайньою точкою однією кривою
            public bool Hidden;
            public RoutePoint Point;
            // Скільки дуга кута забирає від кожної з двох прямих
            public double Cut;
            public double Turn;
        }

        /// <summary>
        /// Скруглює кути маршруту. route це по одній точці на кожну позначку, як їх зібрав BuildRoute.
        /// Перша, остання, точні й узяті з колії точки лишаються як є: маршрут проходить через них.
        /// Решта проміжних це кути ламаної. Маршрут через такий кут не проходить, а обходить його зсередини
        /// дугою, що торкається прямих до сусідніх точок: замість кута стають дві точки, початок і кінець дуги.
        /// Між дугами маршрут іде прямо, і це справді пряма: напрямок на обох її кінцях той самий.
        ///
        /// Місце на прямих ділиться так. Спершу кожен кут отримує стільки, скільки треба для дуги найменшого
        /// радіуса; якщо двом сусіднім кутам разом не вистачає прямої між ними, вони діляться нею порівну
        /// до потреби, і там поворот лишиться крутішим за дозволений. Те, що лишилося після цього, кути
        /// розбирають порівну, і дуги стають ширшими. Тому тісний кут не страждає через просторого сусіда.
        ///
        /// На початку й у кінці маршруту напрямок заданий: поглядом або колією, з якою маршрут стикується.
        /// Там першим ділом ставиться дуга, якою маршрут плавно сходить із цього напрямку на пряму до першого
        /// кута (і так само заходить з останнього кута): це важливіше, ніж рівно поділити поворот між точками.
        /// </summary>
        private static List<RoutePoint> RoundCorners(List<RouteMarker> markers, List<RoutePoint> route)
        {
            int count = route.Count;
            if (count < 3) return route;

            var nodes = new List<Node>();
            for (int i = 0; i < count; i++)
            {
                nodes.Add(new Node
                {
                    Position = route[i].Position, Tangent = route[i].Tangent, Point = route[i],
                    Corner = i > 0 && i < count - 1 && !markers[i].Exact && !markers[i].Snapped
                });
            }

            // Схід із напрямку першої точки і захід у напрямок останньої
            if (nodes[1].Corner)
            {
                Node lead = LeadIn(nodes[0], nodes[1].Position, 1);
                if (lead != null) nodes.Insert(1, lead);
            }
            if (nodes[nodes.Count - 2].Corner)
            {
                Node lead = LeadIn(nodes[nodes.Count - 1], nodes[nodes.Count - 2].Position, -1);
                if (lead != null) nodes.Insert(nodes.Count - 1, lead);
            }

            int n = nodes.Count;
            double[] leg = new double[n];
            for (int i = 0; i + 1 < n; i++) leg[i] = HorizontalDistance(nodes[i].Position, nodes[i + 1].Position);

            // Скільки кут може взяти від прямої до сусіда, якщо сусід не кут: до точки, чий напрямок уже лежить
            // на цій прямій, усю; до точної точки трохи менше, їй потрібне місце на власний вигин
            double Limit(int neighbour, double length)
            {
                return nodes[neighbour].Corner || nodes[neighbour].Lead ? length : Math.Max(length - 1, length / 2);
            }

            // Потреба кожного кута: дуга найменшого радіуса
            double[] need = new double[n];
            for (int i = 1; i + 1 < n; i++)
            {
                Node node = nodes[i];
                if (!node.Corner) continue;

                Vec3d before = nodes[i - 1].Position, after = nodes[i + 1].Position;
                double inX = node.Position.X - before.X, inZ = node.Position.Z - before.Z;
                double outX = after.X - node.Position.X, outZ = after.Z - node.Position.Z;
                if (leg[i - 1] < 1e-6 || leg[i] < 1e-6) continue;

                node.Turn = Math.Acos(GameMath.Clamp((inX * outX + inZ * outZ) / (leg[i - 1] * leg[i]), -1, 1));
                // Точка на прямій або розворот назад: дуги тут немає
                if (node.Turn < 0.01 || node.Turn > Math.PI - 0.05) continue;

                need[i] = RoutePlanner.MinRadius * CornerRadiusFactor * Math.Tan(node.Turn / 2);
                node.Cut = Math.Min(need[i], Math.Min(Limit(i - 1, leg[i - 1]), Limit(i + 1, leg[i])));
            }

            // Двом сусіднім кутам не вистачає прямої між ними: ділять її відповідно до потреб
            for (int i = 1; i + 2 < n; i++)
            {
                if (!nodes[i].Corner || !nodes[i + 1].Corner) continue;
                double both = nodes[i].Cut + nodes[i + 1].Cut;
                if (both <= leg[i] || both < 1e-9) continue;
                double scale = leg[i] / both;
                nodes[i].Cut *= scale;
                nodes[i + 1].Cut *= scale;
            }

            // Решту місця кути розбирають порівну: кожен бере половину того, що ще вільне з тіснішого боку
            for (int pass = 0; pass < 12; pass++)
            {
                for (int i = 1; i + 1 < n; i++)
                {
                    if (!nodes[i].Corner || need[i] <= 0) continue;
                    double inFree = (nodes[i - 1].Corner ? leg[i - 1] - nodes[i - 1].Cut : Limit(i - 1, leg[i - 1])) - nodes[i].Cut;
                    double outFree = (nodes[i + 1].Corner ? leg[i] - nodes[i + 1].Cut : Limit(i + 1, leg[i])) - nodes[i].Cut;
                    double free = Math.Min(inFree, outFree);
                    if (free > 1e-6) nodes[i].Cut += free / 2;
                }
            }

            var result = new List<RoutePoint>();
            // Точки, через які маршрут має пройти точно: їх не можна ні зсувати, ні зливати з сусідами
            var keep = new HashSet<RoutePoint>();

            for (int i = 0; i < n; i++)
            {
                Node node = nodes[i];
                if (node.Hidden) continue;
                if (!node.Corner)
                {
                    RoutePoint point = node.Point ?? new RoutePoint { Position = node.Position, Tangent = node.Tangent };
                    result.Add(point);
                    keep.Add(point);
                    continue;
                }

                // Точка на прямій: кута немає
                if (node.Turn < 0.01) continue;
                // Розворот назад, або дуга виходить надто короткою для нарізки: точка лишається як є
                double cut = node.Cut;
                if (need[i] <= 0 || 2 * cut / Math.Tan(node.Turn / 2) * Math.Sin(node.Turn / 2) < RouteCurve.MinSpan)
                {
                    result.Add(node.Point);
                    continue;
                }

                Vec3d before = nodes[i - 1].Position, corner = node.Position, after = nodes[i + 1].Position;
                double inX = corner.X - before.X, inZ = corner.Z - before.Z, outX = after.X - corner.X, outZ = after.Z - corner.Z;
                double inLength = leg[i - 1], outLength = leg[i];
                double inPart = cut / inLength, outPart = cut / outLength;

                result.Add(new RoutePoint
                {
                    Position = new Vec3d(corner.X - inX * inPart, corner.Y - (corner.Y - before.Y) * inPart, corner.Z - inZ * inPart),
                    Tangent = new Vec3d(inX / inLength, (corner.Y - before.Y) / inLength, inZ / inLength).Normalize()
                });
                result.Add(new RoutePoint
                {
                    Position = new Vec3d(corner.X + outX * outPart, corner.Y + (after.Y - corner.Y) * outPart, corner.Z + outZ * outPart),
                    Tangent = new Vec3d(outX / outLength, (after.Y - corner.Y) / outLength, outZ / outLength).Normalize()
                });
            }

            // Дві точки ближче одна до одної, ніж дозволяє нарізка: між ними не вийшло б жодної секції.
            // Кінець однієї дуги і початок наступної на спільній прямій зливаються в одну точку посередині;
            // поруч із точною точкою лишається вона
            for (int k = 0; k + 1 < result.Count; k++)
            {
                RoutePoint a = result[k], b = result[k + 1];
                if (HorizontalDistance(a.Position, b.Position) >= RouteCurve.MinSpan) continue;

                if (keep.Contains(a) && keep.Contains(b)) continue;
                if (keep.Contains(b)) result.RemoveAt(k);
                else if (keep.Contains(a)) result.RemoveAt(k + 1);
                else
                {
                    result[k] = new RoutePoint
                    {
                        Position = new Vec3d((a.Position.X + b.Position.X) / 2, (a.Position.Y + b.Position.Y) / 2, (a.Position.Z + b.Position.Z) / 2),
                        Tangent = new Vec3d(a.Tangent.X + b.Tangent.X, a.Tangent.Y + b.Tangent.Y, a.Tangent.Z + b.Tangent.Z).Normalize()
                    };
                    result.RemoveAt(k + 1);
                }
                k--;
            }

            return result.Count < 2 ? route : result;
        }

        /// <summary>
        /// Дуга, якою маршрут сходить із заданого напрямку крайньої точки end на пряму до першого кута target.
        /// Коло торкається напрямку в самій точці; з кута до кола ведеться дотична, і точка дотику це кінець
        /// дуги. Повертає цю точку з напрямком уздовж дотичної, або null, якщо дуга не потрібна (кут і так
        /// майже прямо по курсу) чи не вміщається (кут усередині кола найменшого радіуса).
        /// direction це 1 для початку маршруту і -1 для кінця: там маршрут іде навпаки, з кута в точку.
        /// </summary>
        private static Node LeadIn(Node end, Vec3d target, int direction)
        {
            double tx = end.Tangent.X * direction, tz = end.Tangent.Z * direction;
            double length = Math.Sqrt(tx * tx + tz * tz);
            if (length < 1e-6) return null;
            tx /= length;
            tz /= length;

            Vec3d p = end.Position;
            double dx = target.X - p.X, dz = target.Z - p.Z;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (distance < 2) return null;

            double cross = tx * dz - tz * dx, dot = tx * dx + tz * dz;
            if (Math.Atan2(Math.Abs(cross), dot) < 0.05) return null;

            // Бік, у який треба повертати, і напрямок обходу кола
            double side = cross > 0 ? 1 : -1;
            double nx = -tz * side, nz = tx * side;

            foreach (double factor in LeadRadiusFactors)
            {
                double radius = RoutePlanner.MinRadius * factor;
                double ox = p.X + nx * radius, oz = p.Z + nz * radius;
                double vx = target.X - ox, vz = target.Z - oz;
                double far = Math.Sqrt(vx * vx + vz * vz);
                if (far <= radius * 1.02) continue;

                double angle = Math.Acos(radius / far);
                double ux = vx / far, uz = vz / far;
                // Обхід кола: від точки в напрямку руху
                double sense = (-nx) * tz - (-nz) * tx >= 0 ? 1 : -1;

                for (int sign = -1; sign <= 1; sign += 2)
                {
                    double cos = Math.Cos(angle * sign), sin = Math.Sin(angle * sign);
                    double rx = ux * cos - uz * sin, rz = ux * sin + uz * cos;
                    double qx = ox + rx * radius, qz = oz + rz * radius;

                    double hx = target.X - qx, hz = target.Z - qz;
                    double straight = Math.Sqrt(hx * hx + hz * hz);
                    if (straight < 0.5) continue;
                    hx /= straight;
                    hz /= straight;

                    // Дотична має вести далі в той самий бік, у який маршрут іде по колу
                    if ((-rz * sense) * hx + (rx * sense) * hz < 0.99) continue;

                    double chord = Math.Sqrt((qx - p.X) * (qx - p.X) + (qz - p.Z) * (qz - p.Z));

                    // Висота міняється рівномірно від крайньої точки до кута
                    double total = chord + straight;
                    double slope = (target.Y - p.Y) / total;
                    return new Node
                    {
                        Position = new Vec3d(qx, p.Y + slope * chord, qz),
                        Tangent = new Vec3d(hx * direction, slope * direction, hz * direction).Normalize(),
                        Lead = true,
                        // Напрямок міняється зовсім трохи, і дуга коротша за найменшу ділянку. Якби її просто
                        // відкинути, пряма від кута цілила б у саму крайню точку, а не по дотичній до дуги,
                        // і весь поворот зібрався б перегином біля кінця
                        Hidden = chord < RouteCurve.MinSpan
                    };
                }
            }
            return null;
        }

        // Сума одиничних напрямків «прийшли» і «підемо», менша за це, означає розворот більш як на 150°
        private const double UTurnSum = 0.52;

        /// <summary>Відстань між двома точками в плані, без висоти.</summary>
        public static double HorizontalDistance(Vec3d a, Vec3d b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
