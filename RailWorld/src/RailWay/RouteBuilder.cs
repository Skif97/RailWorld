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
        /// Ставить стрілки маршруту туди, де вони справді починаються. Маршрут, що відходить від колії (або
        /// вливається в неї), часто ще якийсь час лежить на ній: гілки біля стику йдуть одна по одній і
        /// розходяться далі. Стрілка ж починається там, звідки вони лише розходяться (SwitchSides.DivergesFrom).
        /// Тоді перша (остання) точка маршруту переїжджає на стик колії перед цим місцем, а шматок до нього,
        /// що лежить на самій колії, вдруге не кладеться; точки маршруту на ньому відпадають. Повертає нові
        /// точки або ті самі, якщо переносити нічого. Клієнт і сервер роблять це однаково, тож показ
        /// маршруту одразу показує стрілку там, де її поставить сервер.
        /// </summary>
        public static List<RouteMarker> SettleSwitches(List<RouteMarker> markers, Func<SectionLink, Section> resolve)
        {
            if (resolve == null || markers == null || markers.Count < 2) return markers;
            List<RouteMarker> result = Settle(markers, true, resolve);
            return Settle(result, false, resolve);
        }

        private static List<RouteMarker> Settle(List<RouteMarker> markers, bool first, Func<SectionLink, Section> resolve)
        {
            RouteMarker marker = first ? markers[0] : markers[markers.Count - 1];
            if (marker.Track == null) return markers;

            var sources = new List<int>();
            List<RoutePoint> route = Build(markers, sources);
            if (route == null) return markers;
            if (!first)
            {
                route.Reverse();
                sources.Reverse();
            }

            // Маршрут від стрілки, точками нарізки, і де в них стоїть кожна точка маршруту
            var points = new List<PointOnBezierCurve>();
            var at = new List<int> { 0 };
            for (int i = 0; i + 1 < route.Count && points.Count < SwitchSides.MaxSections * 2; i++)
            {
                RoutePoint a = route[i], b = route[i + 1];
                // Маршрут розвернуто, щоб іти від стрілки; сама крива відрізка та сама, що й у маршруті, лише навпаки
                List<PointOnBezierCurve> leg = first ? RouteCurve.Leg(a, b) : RouteCurve.Leg(b, a);
                if (leg == null) return markers;
                if (!first) leg.Reverse();
                if (points.Count > 0) leg.RemoveAt(0);
                points.AddRange(leg);
                at.Add(points.Count - 1);
            }

            // Гілка тієї самої ширини, що й колія, до якої чіпляється: колії різної ширини не з'єднуються
            TrackGauge gauge = TrackGauge.Of(marker.Track);
            var branch = new List<Section>();
            for (int i = 0; i + 2 < points.Count && branch.Count < SwitchSides.MaxSections; i += 2)
            {
                branch.Add(gauge.NewSection(points[i], points[i + 1], points[i + 2]));
            }
            var track = new List<Section>();
            var entered = new List<bool>();
            TrackWalker walk = TrackWalker.Entering(marker.Track, marker.TrackJointAtStart, resolve);
            do
            {
                track.Add(walk.Section);
                entered.Add(walk.Entered);
            }
            while (track.Count < SwitchSides.MaxSections && walk.Next());

            int from = SwitchSides.DivergesFrom(branch, track);
            if (from <= 0) return markers;

            // Стрілка стає на стик колії. Гілка до місця, де стрілка мала б початися, лежить на колії, тож відстань
            // до нього вздовж гілки це відстань і вздовж колії. Береться останній стик, що не далі за це місце:
            // справжній початок тоді лежить у першій секції за ним
            double reach = 0;
            for (int n = 0; n < from; n++) reach += branch[n].FullStartPosition.DistanceTo(branch[n].FullEndPosition);
            int joint = 0;
            double run = 0;
            for (int j = 0; j + 1 < track.Count; j++)
            {
                run += track[j].FullStartPosition.DistanceTo(track[j].FullEndPosition);
                if (run > reach + 1e-6) break;
                joint = j + 1;
            }
            if (joint == 0) return markers;

            // Точки, поставлені гравцем, усі точки маршруту з яких лежать до нового стику, відпадають;
            // протилежний кінець маршруту лишається завжди
            int dropped = 0;
            for (int m = 1; m < markers.Count - 1; m++)
            {
                int placed = first ? m : markers.Count - 1 - m;
                int last = -1;
                for (int k = 0; k < sources.Count && k < at.Count; k++)
                {
                    if (sources[k] == placed) last = at[k];
                }
                if (last < 0 || last > from * 2) break;
                dropped++;
            }

            RouteMarker moved = RouteMarker.OnJoint(track[joint - 1], !entered[joint - 1], first ? 1 : -1, first, resolve);
            var result = new List<RouteMarker>(markers);
            if (first)
            {
                result.RemoveRange(0, dropped + 1);
                result.Insert(0, moved);
            }
            else
            {
                result.RemoveRange(result.Count - 1 - dropped, dropped + 1);
                result.Add(moved);
            }
            return result.Count >= 2 ? result : markers;
        }

        /// <summary>
        /// З точок розмітки робить маршрут: кінці виносить на край блока, проміжні точки вирівнює по сусідах
        /// і скруглює кути. null, якщо точок менше двох.
        /// </summary>
        public static List<RoutePoint> Build(List<RouteMarker> points, TrackGauge gauge = null)
        {
            return Build(points, null, gauge);
        }

        /// <summary>
        /// Те саме, але ще каже про кожну точку маршруту, з якої точки гравця (номер у points) її зроблено:
        /// кут маршрут заміняє двома точками, початком і кінцем дуги, і додає свої точки на сході з колії.
        /// </summary>
        internal static List<RoutePoint> Build(List<RouteMarker> points, List<int> sources, TrackGauge gauge = null)
        {
            int count = points.Count;
            if (count < 2) return null;

            EndsOf(points, out Vec3d[] ends, out Vec3d[] headings);

            var route = new List<RoutePoint>();
            for (int i = 0; i < count; i++)
            {
                RouteMarker marker = points[i];
                Vec3d position = marker.Position.Clone();
                Vec3d tangent = marker.Heading.Clone();

                if (!marker.Snapped)
                {
                    if (i == 0 || i == count - 1)
                    {
                        position = ends[i == 0 ? 0 : 1];
                        tangent = headings[i == 0 ? 0 : 1];
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
                    Exact = i == 0 || i == count - 1 || marker.Exact || marker.Snapped,
                    Position = position, Tangent = tangent, Cant = marker.Cant,
                    Track = marker.Track, TrackJointAtStart = marker.TrackJointAtStart
                });
            }

            return SplitIntoArcs(RoundCorners(points, route, sources, gauge ?? TrackGauge.Narrow), sources);
        }

        // Напрямки на кінцях ділянки лягають на одну дугу, якщо вони дзеркальні відносно хорди, з таким допуском, рад
        private const double OneArcTolerance = 1e-3;

        /// <summary>
        /// Кожна ділянка маршруту між двома сусідніми точками стає однією дугою кола (або прямою): крива ділянки
        /// (RouteCurve.Leg) повторює дугу, а коли напрямки на кінцях на одну дугу не лягають, вона збирає вигин біля
        /// одного кінця, і там радіус менший за дозволений. Так було з перемичкою між двома кінцями колії на змійці
        /// (у змійки найменшого радіуса 2.7 при межі 3.5). Такі ділянки діляться точкою на дві дуги, що в ній
        /// стикуються з однаковим напрямком (біарк). Обидві дуги мають однакові дотичні відрізки: для змійки з двох
        /// однакових дуг точка стає на її перегин, і перемичка повторює змійку. Висота й ухил у новій точці з тієї
        /// самої кубічної кривої висоти, якою йшла б ділянка. sources, якщо є, отримує номер точки гравця і для неї.
        /// </summary>
        private static List<RoutePoint> SplitIntoArcs(List<RoutePoint> route, List<int> sources)
        {
            if (route == null) return null;
            for (int k = 0; k + 1 < route.Count; k++)
            {
                RoutePoint joint = ArcJoint(route[k], route[k + 1]);
                if (joint == null) continue;
                route.Insert(k + 1, joint);
                sources?.Insert(k + 1, sources[k]);
                k++;
            }
            return route;
        }

        // Точка, у якій дві дуги біарка між a і b стикуються; null, якщо ділянка вже одна дуга чи пряма, або дуги
        // виходять коротшими за найменшу ділянку
        private static RoutePoint ArcJoint(RoutePoint a, RoutePoint b)
        {
            double vx = b.Position.X - a.Position.X, vz = b.Position.Z - a.Position.Z;
            double chord = Math.Sqrt(vx * vx + vz * vz);
            if (chord < 2 * RouteCurve.MinSpan) return null;
            if (!Flat(a.Tangent, out double ax, out double az) || !Flat(b.Tangent, out double bx, out double bz)) return null;

            // Кути напрямків від хорди: на одній дузі вони рівні й протилежні
            double fromChord = Math.Atan2(vx * az - vz * ax, vx * ax + vz * az);
            double toChord = Math.Atan2(vx * bz - vz * bx, vx * bx + vz * bz);
            if (Math.Abs(fromChord + toChord) < OneArcTolerance) return null;

            // Дотичні відрізки однакової довжини d: від a вздовж її напрямку і до b вздовж її напрямку. Відрізок між
            // їхніми кінцями має довжину 2d, і його середина це точка стику, а сам він дотична в ній
            double tx = ax + bx, tz = az + bz;
            double vt = vx * tx + vz * tz, vv = vx * vx + vz * vz;
            double twoMinusCos = 2 * (1 - (ax * bx + az * bz));
            double d = twoMinusCos < 1e-9 ? vv / (2 * vt) : (-vt + Math.Sqrt(vt * vt + twoMinusCos * vv)) / twoMinusCos;
            if (double.IsNaN(d) || d <= 0) return null;

            double q0x = a.Position.X + ax * d, q0z = a.Position.Z + az * d;
            double q1x = b.Position.X - bx * d, q1z = b.Position.Z - bz * d;
            double jx = (q0x + q1x) / 2, jz = (q0z + q1z) / 2;
            double first = Math.Sqrt((jx - a.Position.X) * (jx - a.Position.X) + (jz - a.Position.Z) * (jz - a.Position.Z));
            double second = Math.Sqrt((b.Position.X - jx) * (b.Position.X - jx) + (b.Position.Z - jz) * (b.Position.Z - jz));
            if (first < RouteCurve.MinSpan || second < RouteCurve.MinSpan) return null;

            // Висота: кубічна крива між висотами й ухилами кінців, як у висоти між вузлами маршруту
            double span = first + second, u = first / span;
            double fromSlope = Slope(a.Tangent), toSlope = Slope(b.Tangent), fromY = a.Position.Y, toY = b.Position.Y;
            double u2 = u * u, u3 = u2 * u;
            double y = (2 * u3 - 3 * u2 + 1) * fromY + (u3 - 2 * u2 + u) * span * fromSlope
                + (-2 * u3 + 3 * u2) * toY + (u3 - u2) * span * toSlope;
            double slope = ((6 * u2 - 6 * u) * fromY + (3 * u2 - 4 * u + 1) * span * fromSlope
                + (-6 * u2 + 6 * u) * toY + (3 * u2 - 2 * u) * span * toSlope) / span;

            return new RoutePoint { Position = new Vec3d(jx, y, jz), Tangent = WithSlope(q1x - q0x, q1z - q0z, slope), ArcJoint = true, Exact = false };
        }

        // Одиничний напрямок у плані; false, якщо напрямок вертикальний
        private static bool Flat(Vec3d tangent, out double x, out double z)
        {
            double length = Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z);
            x = length < 1e-9 ? 0 : tangent.X / length;
            z = length < 1e-9 ? 0 : tangent.Z / length;
            return length >= 1e-9;
        }

        /// <summary>
        /// Положення й напрямок двох крайніх точок маршруту, що стоять на блоках (для точки на колії вони беруться
        /// з колії, і тут не потрібні). Крайня точка стоїть на краю свого блока, щоб колія займала блок цілком:
        /// початок на краю позаду, кінець на краю попереду. Напрямок, якщо його задано (Aimed), береться як є.
        /// Інакше маршрут приходить у точку найплавніше: з двох точок, одна з яких має напрямок, маршрут іде однією
        /// дугою сталого радіуса, і в другій точці напрямок дзеркальний відносно хорди; в інших випадках
        /// крайня ділянка пряма, уздовж хорди до сусідньої точки. Так та сама точка дає той самий маршрут, куди б
        /// гравець не дивився, ставлячи її.
        /// </summary>
        private static void EndsOf(List<RouteMarker> points, out Vec3d[] ends, out Vec3d[] headings)
        {
            int count = points.Count;
            RouteMarker[] markers = { points[0], points[count - 1] };
            ends = new Vec3d[2];
            headings = new Vec3d[2];
            for (int end = 0; end < 2; end++)
            {
                headings[end] = markers[end].Heading.Clone();
                ends[end] = Edge(markers[end], headings[end], end == 0);
            }

            // Напрямок вільного кінця залежить від положення кінців, а положення від напрямку (край блока, на
            // якому стоїть кінець). Перший прохід рахує від середин блоків, другий уточнює від країв
            for (int pass = 0; pass < 2; pass++)
            {
                for (int end = 0; end < 2; end++)
                {
                    if (markers[end].Snapped || markers[end].Aimed) continue;

                    bool first = end == 0;
                    // Сусідня точка маршруту: друга крайня, якщо точок дві, інакше проміжна
                    Vec3d near = count == 2 ? ends[1 - end] : points[first ? 1 : count - 2].Position;
                    Vec3d from = first ? ends[0] : near, to = first ? near : ends[1];

                    RouteMarker other = markers[1 - end];
                    Vec3d otherHeading = count == 2 && (other.Snapped || other.Aimed) ? headings[1 - end] : null;
                    Vec3d heading = Smoothest(from, to, otherHeading);
                    if (heading == null) continue;

                    headings[end] = heading;
                    ends[end] = Edge(markers[end], heading, first);
                }
            }
        }

        // Точка на краю свого блока в напрямку руху: початок позаду, кінець попереду. Точка на колії стоїть, де стоїть
        private static Vec3d Edge(RouteMarker marker, Vec3d heading, bool first)
        {
            if (marker.Snapped) return marker.Position.Clone();
            return ModMath.DirectionVectorPositionCorrection(marker.Position.Clone(), first ? heading.Clone() : heading.Clone().Mul(-1));
        }

        // Напрямок, у якому маршрут від from до to найплавніше проходить свій вільний кінець. other це напрямок
        // другого кінця, якщо він заданий і між кінцями нічого немає: тоді маршрут це одна дуга, і напрямки на її
        // кінцях дзеркальні відносно хорди. Інакше пряма вздовж хорди. null, якщо кінці в одній точці плану
        private static Vec3d Smoothest(Vec3d from, Vec3d to, Vec3d other)
        {
            double cx = to.X - from.X, cz = to.Z - from.Z, length = Math.Sqrt(cx * cx + cz * cz);
            if (length < 1e-6) return null;
            cx /= length;
            cz /= length;
            if (other == null) return new Vec3d(cx, 0, cz);

            double ox = other.X, oz = other.Z, flat = Math.Sqrt(ox * ox + oz * oz);
            if (flat < 1e-6) return new Vec3d(cx, 0, cz);
            ox /= flat;
            oz /= flat;
            double along = ox * cx + oz * cz;
            return new Vec3d(2 * along * cx - ox, 0, 2 * along * cz - oz).Normalize();
        }

        // Скільки прямої лишається під перехідну криву другого проходу там, де крива сходиться з тим, що не рухається,
        // блоків: половина найкоротшої перехідної кривої (RouteCurve.MinTransition), бо перехід лягає посередині стику
        private const double StraightPerTransition = RouteCurve.MinTransition / 2;
        // Найменший поворот, з якого від кінця маршруту чи точки з Ctrl колія вже вигинається, рад
        private const double NoBend = 0.01;

        // Кінець прямого підходу від кінця маршруту end (direction 1 для початку, -1 для кінця), якщо від нього колія
        // одразу вигинається до сусідньої точки маршруту toward; null, якщо вона й так іде прямо, або кінець біля стрілки
        // (там перехідних кривих немає: гілка одразу йде своєю кривою, і від неї залежить, чи вміщається гостряк), або
        // кінець на колії, що сама вигинається: тоді маршрут продовжує її кривизну, і пряма на стику її б зламала
        private static Node ApproachOf(RouteMarker marker, Node end, Vec3d toward, int direction)
        {
            if (marker.Track != null || !Flat(end.Tangent, out double tx, out double tz)) return null;
            if (marker.Snapped && (marker.Joint == null || !Straight(marker.Joint))) return null;
            double cx = (toward.X - end.Position.X) * direction, cz = (toward.Z - end.Position.Z) * direction;
            double chord = Math.Sqrt(cx * cx + cz * cz);
            if (chord < 1e-6 || Math.Acos(GameMath.Clamp((tx * cx + tz * cz) / chord, -1, 1)) < NoBend) return null;
            var at = new Vec3d(end.Position.X + tx * StraightPerTransition * direction, end.Position.Y, end.Position.Z + tz * StraightPerTransition * direction);
            return new Node
            {
                Position = at, Tangent = end.Tangent, Marker = end.Marker, Approach = true,
                Point = new RoutePoint { Position = at.Clone(), Tangent = end.Tangent.Clone(), Exact = false }
            };
        }

        // Чи секція покладеної колії пряма в плані: напрямок від початку до середини той самий, що від середини до кінця
        private static bool Straight(Section section)
        {
            Vec3d start = section.FullStartPosition, middle = section.GetGlobalPos(), end = section.FullEndPosition;
            double ax = middle.X - start.X, az = middle.Z - start.Z, bx = end.X - middle.X, bz = end.Z - middle.Z;
            return Math.Abs(Math.Atan2(ax * bz - az * bx, ax * bx + az * bz)) < NoBend;
        }

        // Маршрут з двох точок, від яких колія одразу вигинається: прямі підходи від обох кінців (ApproachOf), а між ними
        // крива. null, якщо підходи не потрібні. Висота й ухил у кінцях підходів з кубічної кривої між кінцями
        private static List<RoutePoint> Approaches(List<RouteMarker> markers, RoutePoint a, RoutePoint b, List<int> sources)
        {
            var start = new Node { Position = a.Position, Tangent = a.Tangent, Point = a, Marker = 0 };
            var end = new Node { Position = b.Position, Tangent = b.Tangent, Point = b, Marker = 1 };
            Node first = ApproachOf(markers[0], start, b.Position, 1), last = ApproachOf(markers[1], end, a.Position, -1);
            if (first == null && last == null) return null;

            double span = HorizontalDistance(a.Position, b.Position);
            if (span < 2 * StraightPerTransition + RouteCurve.MinSpan) return null;
            double fromSlope = Slope(a.Tangent), toSlope = Slope(b.Tangent);
            RoutePoint Lift(Node node, double share)
            {
                double u = share, u2 = u * u, u3 = u2 * u;
                double y = (2 * u3 - 3 * u2 + 1) * a.Position.Y + (u3 - 2 * u2 + u) * span * fromSlope + (-2 * u3 + 3 * u2) * b.Position.Y + (u3 - u2) * span * toSlope;
                double slope = ((6 * u2 - 6 * u) * a.Position.Y + (3 * u2 - 4 * u + 1) * span * fromSlope + (-6 * u2 + 6 * u) * b.Position.Y + (3 * u2 - 2 * u) * span * toSlope) / span;
                node.Point.Position = new Vec3d(node.Position.X, y, node.Position.Z);
                node.Point.Tangent = WithSlope(node.Tangent.X, node.Tangent.Z, slope);
                return node.Point;
            }

            var result = new List<RoutePoint> { a };
            sources?.Add(0);
            if (first != null) { result.Add(Lift(first, StraightPerTransition / span)); sources?.Add(0); }
            if (last != null) { result.Add(Lift(last, 1 - StraightPerTransition / span)); sources?.Add(1); }
            result.Add(b);
            sources?.Add(1);
            return result;
        }

        /// <summary>
        /// На скільки від точки з Ctrl p уздовж її напрямку (tx, tz) у бік sign ставити кут її прямого шматка: якнайближче
        /// до точки, щоб маршрут ішов майже так, як ішов би через неї вигином, але так, щоб дуга кута вмістилася з радіусом
        /// не меншим за smallest. Ближче до точки на кут лишається мало прямої (пряма до точки щонайменше
        /// StraightPerTransition), далі менше місця з боку сусіда neighbour і крутіший поворот. Перебираються відстані
        /// через чверть блока; якщо такого радіуса не дає жодна, береться та, з якою дуга найпросторіша (тісну відхилять
        /// правила). 0, якщо до сусіда надто близько.
        /// </summary>
        private static double CtrlReach(Vec3d p, double tx, double tz, Node neighbour, int sign, double smallest)
        {
            double distance = HorizontalDistance(p, neighbour.Position);
            double best = 0, widest = -1;
            for (double reach = StraightPerTransition + RouteCurve.MinSpan; reach <= distance - RouteCurve.MinSpan; reach += RouteCurve.PieceLength)
            {
                double vx = p.X + tx * reach * sign, vz = p.Z + tz * reach * sign;
                double ox = (neighbour.Position.X - vx) * sign, oz = (neighbour.Position.Z - vz) * sign;
                double other = Math.Sqrt(ox * ox + oz * oz);
                if (other < RouteCurve.MinSpan) break;
                // Поворот у куті: від прямої з боку сусіда до прямого шматка
                double turn = Math.Acos(GameMath.Clamp(-(ox * tx + oz * tz) / other, -1, 1));
                // Місце на дугу з кожного боку кута: до точки за вирахуванням прямої під перехід, до сусіда половина, якщо
                // там теж крива, інакше все за вирахуванням його прямої
                double toPoint = reach - StraightPerTransition;
                double toNeighbour = neighbour.Corner || neighbour.Lead ? other / 2 : other - StraightPerTransition;
                double radius = turn < NoBend ? double.MaxValue : Math.Min(toPoint, toNeighbour) / Math.Tan(turn / 2);
                if (radius >= smallest) return reach;
                if (radius > widest + 1e-9) { widest = radius; best = reach; }
            }
            return best;
        }

        /// <summary>
        /// Яку пряму лишить планувальник з кожного боку кута на повороті turn радіан, якщо дуга має бути радіуса radius
        /// і на прямій до кута більше нічого немає: готова форма (RouteTemplate) додає її до своїх прямих, щоб дуга
        /// вийшла саме того радіуса, а перехідним кривим було місце. Між кутом і кінцем форми планувальник лишає
        /// StraightPerTransition (RoundCorners), решту прямої бере дуга.
        /// </summary>
        public static double StraightFor(double radius, double turn, TrackGauge gauge = null)
        {
            return StraightPerTransition;
        }

        // Дуга, якою маршрут скруглює кут, не тісніша за найменший радіус із невеликим запасом: крива Безьє
        // повторює дугу кола не точно і місцями вийшла б на волосину крутішою
        internal const double CornerRadiusFactor = 1.05;

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
            // Кінець прямого підходу від кінця маршруту (Approach): крива від кінця починається лише за ним, щоб на стику
            // з колією чи з напрямком кінця перехідній кривій було місце. Висоти не задає
            public bool Approach;
            public RoutePoint Point;
            // З якої точки гравця вузол: її номер. Кінець дуги сходу належить крайній точці, від якої вона
            public int Marker;
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
        ///
        /// Висота веде окремо від плану. Точки, які поставив гравець, це вузли: через їхні висоти проходить
        /// одна гладка крива вздовж усього маршруту, і початок та кінець кожної дуги беруть висоту й ухил із неї.
        /// Якби висота йшла прямими між точками, увесь перехід від рівного кінця маршруту до ухилу дістався б
        /// короткому шматку прямої перед першою дугою: там виходив злам, а за ним горбок.
        /// </summary>
        private static List<RoutePoint> RoundCorners(List<RouteMarker> markers, List<RoutePoint> route, List<int> sources, TrackGauge gauge)
        {
            int count = route.Count;
            if (count < 3)
            {
                List<RoutePoint> straightened = count == 2 ? Approaches(markers, route[0], route[1], sources) : null;
                if (straightened != null) return straightened;
                for (int i = 0; i < count; i++) sources?.Add(i);
                return route;
            }

            var nodes = new List<Node>();
            for (int i = 0; i < count; i++)
            {
                nodes.Add(new Node
                {
                    Position = route[i].Position, Tangent = route[i].Tangent, Point = route[i], Marker = i,
                    Corner = i > 0 && i < count - 1 && !markers[i].Exact && !markers[i].Snapped
                });
            }

            // Точка з Ctrl: маршрут іде через неї точно. Якби колія вигиналася в самій точці, перехідній кривій біля неї
            // не було б місця, а зсунути вигин заради неї не можна, бо він мусить пройти через точку. Тож через точку
            // лягає прямий шматок уздовж її напрямку, а вигини до неї й від неї стають звичайними кутами на його кінцях
            // (CtrlReach): вони скруглюються дугами, другий прохід дає їм перехідні криві, а сама точка лишається на
            // прямій точно там, де її поставили
            for (int i = nodes.Count - 2; i >= 1; i--)
            {
                Node node = nodes[i];
                RouteMarker marker = markers[node.Marker];
                if (node.Corner || !marker.Exact || marker.Snapped || !Flat(node.Tangent, out double tx, out double tz)) continue;
                double smallest = gauge.MinRadius * CornerRadiusFactor;
                double before = CtrlReach(node.Position, tx, tz, nodes[i - 1], -1, smallest), after = CtrlReach(node.Position, tx, tz, nodes[i + 1], 1, smallest);
                if (before <= 0 || after <= 0) continue;

                Node Around(int sign)
                {
                    double reach = sign < 0 ? before : after;
                    var at = new Vec3d(node.Position.X + tx * reach * sign, node.Position.Y, node.Position.Z + tz * reach * sign);
                    return new Node
                    {
                        Position = at, Tangent = node.Tangent, Corner = true, Marker = node.Marker,
                        Point = new RoutePoint { Position = at.Clone(), Tangent = node.Tangent.Clone(), Exact = false }
                    };
                }
                nodes.Insert(i + 1, Around(1));
                nodes.Insert(i, Around(-1));
            }
            // Вільний кінець (напрямок не задано) дивиться вздовж хорди до сусідньої точки маршруту (EndsOf). Якщо
            // сусідньою стала точка з Ctrl, перед нею тепер кут її прямого шматка: кінець дивиться на нього, і крайня
            // ділянка лишається прямою
            for (int end = 0; end < 2; end++)
            {
                int index = end == 0 ? 0 : nodes.Count - 1, neighbour = end == 0 ? 1 : nodes.Count - 2;
                RouteMarker marker = markers[nodes[index].Marker];
                // Кут прямого шматка має номер точки з Ctrl, від якої він
                bool ctrlCorner = nodes[neighbour].Corner && markers[nodes[neighbour].Marker].Exact;
                if (marker.Snapped || marker.Aimed || !ctrlCorner) continue;
                Vec3d near = nodes[neighbour].Position, here = nodes[index].Position;
                double dx = end == 0 ? near.X - here.X : here.X - near.X, dz = end == 0 ? near.Z - here.Z : here.Z - near.Z;
                double flat = Math.Sqrt(dx * dx + dz * dz);
                if (flat < 1e-6) continue;
                nodes[index].Tangent = new Vec3d(dx / flat, nodes[index].Tangent.Y, dz / flat).Normalize();
                nodes[index].Point.Tangent = nodes[index].Tangent;
            }

            // Кінець, від якого колія одразу вигинається, спершу йде прямо (Approach): перехідна крива на стику бере
            // половину своєї довжини з прямої
            Node startApproach = ApproachOf(markers[0], nodes[0], nodes[1].Position, 1);
            if (startApproach != null) nodes.Insert(1, startApproach);
            Node endApproach = ApproachOf(markers[count - 1], nodes[nodes.Count - 1], nodes[nodes.Count - 2].Position, -1);
            if (endApproach != null) nodes.Insert(nodes.Count - 1, endApproach);

            // Схід із напрямку першої точки і захід у напрямок останньої, від кінця прямого підходу, якщо він є
            int first = startApproach != null ? 1 : 0;
            if (nodes[first + 1].Corner)
            {
                Node lead = LeadIn(nodes[first], nodes[first + 1].Position, 1, gauge);
                if (lead != null)
                {
                    lead.Marker = 0;
                    nodes.Insert(first + 1, lead);
                }
            }
            int last = nodes.Count - 1 - (endApproach != null ? 1 : 0);
            if (nodes[last - 1].Corner)
            {
                Node lead = LeadIn(nodes[last], nodes[last - 1].Position, -1, gauge);
                if (lead != null)
                {
                    lead.Marker = count - 1;
                    nodes.Insert(last, lead);
                }
            }

            int n = nodes.Count;
            double[] leg = new double[n];
            for (int i = 0; i + 1 < n; i++) leg[i] = HorizontalDistance(nodes[i].Position, nodes[i + 1].Position);

            // Скільки кут може взяти від прямої до сусіда, якщо сусід не кут: до точки, чий напрямок уже лежить
            // на цій прямій, усю; до точної точки трохи менше, їй потрібне місце на власний вигин
            double Limit(int neighbour, double length)
            {
                Node near = nodes[neighbour];
                return near.Corner || near.Lead || near.Approach ? length : Math.Max(length - 1, length / 2);
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

                need[i] = gauge.MinRadius * CornerRadiusFactor * Math.Tan(node.Turn / 2);
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

            // Пряма, яку ділянка ламаної лишає під перехідну криву: там, де крива (кут з дугою чи дуга сходу) сходиться з
            // точкою, що не рухається (кінець маршруту, точка з Ctrl, точка на колії), StraightPerTransition. Між двома
            // кривими прямої не треба: другий прохід (RouteCurve.SmoothCurves) згладжує криві, що йдуть одна за одною,
            // разом, як одну. Запас є завжди: якщо з ним дуги найменшого радіуса не вміщаються, кути поступаються прямій,
            // дуги стають тіснішими за дозволене, і правила такий маршрут відхиляють: гравець зсуває точку
            bool Curved(int index) => nodes[index].Lead || nodes[index].Corner && need[index] > 0;
            bool Fixed(int index) => !nodes[index].Corner && !nodes[index].Lead && !nodes[index].Approach;
            double[] reserve = new double[n];
            for (int i = 0; i + 1 < n; i++)
            {
                bool meets = Curved(i) && Fixed(i + 1) || Fixed(i) && Curved(i + 1);
                reserve[i] = Math.Min(leg[i], meets ? StraightPerTransition : 0);
                double room = leg[i] - reserve[i], taken = Cut(nodes[i]) + Cut(nodes[i + 1]);
                if (taken <= room || taken < 1e-9) continue;
                double scale = room / taken;
                if (nodes[i].Corner) nodes[i].Cut *= scale;
                if (nodes[i + 1].Corner) nodes[i + 1].Cut *= scale;
            }

            // Решту місця кути розбирають порівну: кожен бере половину того, що ще вільне з тіснішого боку, не чіпаючи
            // запасу прямої
            double Room(int neighbour, int legIndex)
            {
                double length = leg[legIndex] - reserve[legIndex];
                return nodes[neighbour].Corner ? length - nodes[neighbour].Cut : Math.Min(Limit(neighbour, leg[legIndex]), length);
            }
            for (int pass = 0; pass < 12; pass++)
            {
                for (int i = 1; i + 1 < n; i++)
                {
                    if (!nodes[i].Corner || need[i] <= 0) continue;
                    double inFree = Room(i - 1, i - 1) - nodes[i].Cut;
                    double outFree = Room(i + 1, i) - nodes[i].Cut;
                    double free = Math.Min(inFree, outFree);
                    if (free > 1e-6) nodes[i].Cut += free / 2;
                }
            }

            // Висота вздовж маршруту. Вузли це всі точки, крім доданих кінців дуг сходу: ті самі лежать на кривій.
            // Ухил у вузлі: на кінцях і в точках на колії свій, з дотичної; у куті такий, як між його сусідами, у
            // точній точці з дотичної. Ухил у куті й точній точці ще обмежується так, щоб висота між вузлами не
            // виходила за висоти самих вузлів (ModMath.MonotoneSlope): інакше перед підйомом колія спершу пірнала
            // нижче за початок. На кінцях і на колії ухил не змінюється, бо там колія має зійтися з наявною
            // Відстань уздовж справжнього шляху, а не ламаної: у куті колія йде дугою, коротшою за дві його частини
            // ламаної (2 · cut), і якби висота розкладалася по ламаній, на дузі ухил був би крутіший, ніж на прямих
            // поруч (на 90° на 27%). Вузол кута стоїть посередині своєї дуги
            double[] arc = new double[n];
            for (int i = 0; i < n; i++) arc[i] = ArcLength(nodes[i], need[i]);
            double[] at = new double[n];
            for (int i = 1; i < n; i++) at[i] = at[i - 1] + leg[i - 1] - Cut(nodes[i - 1]) - Cut(nodes[i]) + arc[i - 1] / 2 + arc[i] / 2;

            // Вузли кривої висоти: кінці, точні точки і точки на колії. Кут задає лише поворот: якби крива висоти
            // проходила висоту кожного кута, то там, де кути стоять не на одній лінії, ухил ішов би хвилею
            var knots = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!nodes[i].Lead && !nodes[i].Corner && !nodes[i].Approach) knots.Add(i);
            }
            // Підйом на блок шляху між двома вузлами
            double Secant(int a, int b)
            {
                double span = at[b] - at[a];
                return span > 1e-6 ? (nodes[b].Position.Y - nodes[a].Position.Y) / span : 0;
            }

            double[] knotSlope = new double[knots.Count];
            for (int k = 0; k < knots.Count; k++)
            {
                Node node = nodes[knots[k]];
                bool own = k == 0 || k == knots.Count - 1 || markers[node.Marker].Snapped;
                if (own)
                {
                    knotSlope[k] = Slope(node.Tangent);
                    continue;
                }
                int a = knots[k - 1], i = knots[k], b = knots[k + 1];
                double wanted = Secant(a, b);
                knotSlope[k] = ModMath.MonotoneSlope(wanted, Secant(a, i), Secant(i, b));
                // Точна точка входить у маршрут як є, тож обмежений ухил іде і в її напрямок
                node.Tangent = WithSlope(node.Tangent.X, node.Tangent.Z, knotSlope[k]);
                if (node.Point != null) node.Point.Tangent = node.Tangent;
            }

            // Висота й ухил кривої за run блоків від початку маршруту: кубічна крива між двома сусідніми вузлами
            // із заданими висотою та ухилом на кінцях
            double Height(double run, out double slope)
            {
                int k = 0;
                while (k + 2 < knots.Count && run > at[knots[k + 1]]) k++;

                double from = at[knots[k]], length = at[knots[k + 1]] - from;
                double fromY = nodes[knots[k]].Position.Y, toY = nodes[knots[k + 1]].Position.Y;
                slope = knotSlope[k];
                if (length < 1e-6) return fromY;

                double u = GameMath.Clamp((run - from) / length, 0, 1);
                double u2 = u * u, u3 = u2 * u;
                slope = ((6 * u2 - 6 * u) * fromY + (3 * u2 - 4 * u + 1) * length * knotSlope[k]
                    + (-6 * u2 + 6 * u) * toY + (3 * u2 - 2 * u) * length * knotSlope[k + 1]) / length;
                return (2 * u3 - 3 * u2 + 1) * fromY + (u3 - 2 * u2 + u) * length * knotSlope[k]
                    + (-2 * u3 + 3 * u2) * toY + (u3 - u2) * length * knotSlope[k + 1];
            }

            for (int i = 0; i < n; i++)
            {
                Node node = nodes[i];
                if (!node.Lead && !node.Approach) continue;
                double y = Height(at[i], out double slope);
                node.Position = new Vec3d(node.Position.X, y, node.Position.Z);
                node.Tangent = WithSlope(node.Tangent.X, node.Tangent.Z, slope);
                if (node.Point != null)
                {
                    node.Point.Position = node.Position.Clone();
                    node.Point.Tangent = node.Tangent.Clone();
                }
            }

            var result = new List<RoutePoint>();
            var from = new List<int>();
            // Точки, через які маршрут має пройти точно: їх не можна ні зсувати, ні зливати з сусідами
            var keep = new HashSet<RoutePoint>();

            for (int i = 0; i < n; i++)
            {
                Node node = nodes[i];
                if (node.Hidden) continue;
                if (!node.Corner)
                {
                    RoutePoint point = node.Point ?? new RoutePoint { Position = node.Position, Tangent = node.Tangent, Exact = false };
                    result.Add(point);
                    from.Add(node.Marker);
                    keep.Add(point);
                    continue;
                }

                // Точка на прямій: кута немає, і дуги теж. Сама точка лишається, але висоти не задає: висота й ухил з
                // кривої висоти. Напрямок уздовж прямої
                if (node.Turn < 0.01)
                {
                    Vec3d next = nodes[i + 1].Position;
                    double alongX = next.X - node.Position.X, alongZ = next.Z - node.Position.Z;
                    if (leg[i] < 1e-6)
                    {
                        // Наступна точка в тому самому стовпчику: напрямок з попередньої
                        alongX = node.Position.X - nodes[i - 1].Position.X;
                        alongZ = node.Position.Z - nodes[i - 1].Position.Z;
                    }
                    double y = Height(at[i], out double slope);
                    result.Add(new RoutePoint { Position = new Vec3d(node.Position.X, y, node.Position.Z), Tangent = WithSlope(alongX, alongZ, slope), Exact = false });
                    from.Add(node.Marker);
                    continue;
                }
                // Розворот назад, або дуга виходить надто короткою для нарізки: точка лишається в плані як є, а висоту й
                // ухил бере з кривої висоти
                double cut = node.Cut;
                if (need[i] <= 0 || 2 * cut / Math.Tan(node.Turn / 2) * Math.Sin(node.Turn / 2) < RouteCurve.MinSpan)
                {
                    double y = Height(at[i], out double slope);
                    node.Point.Position = new Vec3d(node.Point.Position.X, y, node.Point.Position.Z);
                    node.Point.Tangent = WithSlope(node.Point.Tangent.X, node.Point.Tangent.Z, slope);
                    node.Point.Exact = false;
                    result.Add(node.Point);
                    from.Add(node.Marker);
                    continue;
                }

                Vec3d before = nodes[i - 1].Position, corner = node.Position, after = nodes[i + 1].Position;
                double inX = corner.X - before.X, inZ = corner.Z - before.Z, outX = after.X - corner.X, outZ = after.Z - corner.Z;
                double inLength = leg[i - 1], outLength = leg[i];
                double inPart = cut / inLength, outPart = cut / outLength;

                double inY = Height(at[i] - arc[i] / 2, out double inSlope);
                double outY = Height(at[i] + arc[i] / 2, out double outSlope);

                result.Add(new RoutePoint
                {
                    Position = new Vec3d(corner.X - inX * inPart, inY, corner.Z - inZ * inPart),
                    Tangent = WithSlope(inX, inZ, inSlope),
                    Exact = false
                });
                result.Add(new RoutePoint
                {
                    Position = new Vec3d(corner.X + outX * outPart, outY, corner.Z + outZ * outPart),
                    Tangent = WithSlope(outX, outZ, outSlope),
                    Exact = false
                });
                from.Add(node.Marker);
                from.Add(node.Marker);
            }

            // Дві точки ближче одна до одної, ніж дозволяє нарізка: між ними не вийшло б жодної секції.
            // Кінець однієї дуги і початок наступної на спільній прямій зливаються в одну точку посередині;
            // поруч із точною точкою лишається вона
            for (int k = 0; k + 1 < result.Count; k++)
            {
                RoutePoint a = result[k], b = result[k + 1];
                if (HorizontalDistance(a.Position, b.Position) >= RouteCurve.MinSpan) continue;

                if (keep.Contains(a) && keep.Contains(b)) continue;
                if (keep.Contains(b))
                {
                    result.RemoveAt(k);
                    from.RemoveAt(k);
                }
                else if (keep.Contains(a))
                {
                    result.RemoveAt(k + 1);
                    from.RemoveAt(k + 1);
                }
                else
                {
                    // Точка посередині між кінцем однієї дуги і початком наступної: вона вже на шляху до другої
                    result[k] = new RoutePoint
                    {
                        Position = new Vec3d((a.Position.X + b.Position.X) / 2, (a.Position.Y + b.Position.Y) / 2, (a.Position.Z + b.Position.Z) / 2),
                        Tangent = new Vec3d(a.Tangent.X + b.Tangent.X, a.Tangent.Y + b.Tangent.Y, a.Tangent.Z + b.Tangent.Z).Normalize(),
                        Exact = false
                    };
                    from[k] = from[k + 1];
                    result.RemoveAt(k + 1);
                    from.RemoveAt(k + 1);
                }
                k--;
            }

            if (result.Count < 2)
            {
                for (int i = 0; i < count; i++) sources?.Add(i);
                return route;
            }
            sources?.AddRange(from);
            return result;
        }

        /// <summary>
        /// Дуга, якою маршрут сходить із заданого напрямку крайньої точки end на пряму до першого кута target.
        /// Коло торкається напрямку в самій точці; з кута до кола ведеться дотична, і точка дотику це кінець
        /// дуги. Повертає цю точку з напрямком уздовж дотичної, або null, якщо дуга не потрібна (кут і так
        /// майже прямо по курсу) чи не вміщається (кут усередині кола найменшого радіуса).
        /// direction це 1 для початку маршруту і -1 для кінця: там маршрут іде навпаки, з кута в точку.
        /// </summary>
        private static Node LeadIn(Node end, Vec3d target, int direction, TrackGauge gauge)
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
                double radius = gauge.MinRadius * factor;
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

        // Скільки ламаної забирає дуга кута з кожного боку; 0 для вузла, що не кут
        private static double Cut(Node node)
        {
            return node.Corner ? node.Cut : 0;
        }

        // Довжина дуги кута в плані: cut · θ / tan(θ/2). Кут без дуги (маршрут іде крізь нього, бо дуга не потрібна чи
        // не вміщається) лишає ламану як є, і його «дуга» це ті самі 2 · cut; так само для вузла, що не кут
        private static double ArcLength(Node node, double need)
        {
            if (!node.Corner) return 0;
            double cut = node.Cut, turn = node.Turn;
            if (need <= 0 || turn < 1e-6 || 2 * cut / Math.Tan(turn / 2) * Math.Sin(turn / 2) < RouteCurve.MinSpan) return 2 * cut;
            return cut * turn / Math.Tan(turn / 2);
        }

        // Ухил напрямку: на скільки блоків він піднімається на блок шляху в плані
        private static double Slope(Vec3d tangent)
        {
            double flat = Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z);
            return flat < 1e-9 ? 0 : tangent.Y / flat;
        }

        // Одиничний напрямок, що в плані дивиться вздовж (x, z) і має заданий ухил
        private static Vec3d WithSlope(double x, double z, double slope)
        {
            double flat = Math.Sqrt(x * x + z * z);
            return flat < 1e-9 ? new Vec3d(x, 0, z) : new Vec3d(x / flat, slope, z / flat).Normalize();
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
