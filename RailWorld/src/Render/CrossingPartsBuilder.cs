using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Збирає рейки перетину колій в один меш: ходові з жолобами для реборд, проставки і контррейки.
    /// Як і хрестовина стрілки, це поверхня, натягнута на контури перерізів уздовж рейки, і всередині неї немає
    /// жодної грані. Рейка, проставка і контррейка, що лежать поруч, у перерізі це одна фігура: її контур
    /// обходить їх усі зовні. Уздовж рейки склад фігури міняється: десь немає головки, десь кінчається
    /// контррейка. Тому рейка ділиться на відрізки між сусідніми краями, і в кожного свій контур.
    /// Де що лежить і де обривається, рахує CrossingGeometry; тут лише форма.
    /// </summary>
    public class CrossingPartsBuilder
    {
        private RailWayRendererSystem renderer;

        public CrossingPartsBuilder(RailWayRendererSystem renderer)
        {
            this.renderer = renderer;
        }

        // Ближчі за це місця вздовж рейки вважаються одним краєм, у перерізах лінії
        private const double SameEdge = 1e-6;

        /// <summary>
        /// Ставить рейки перетину із записом record, що лежить у чанку chunkCoord. Викликати між BeginParts
        /// і EndParts цього чанка.
        /// </summary>
        public void Build(Vec3i chunkCoord, RailCrossing record, CrossingRails rails)
        {
            if (rails == null) return;

            // Один меш на весь вузол. Якщо рейки двох колій з різного металу, то по одному на метал:
            // текстура в меша одна
            var byMaterial = new Dictionary<string, List<LoftPart>>();
            var order = new List<string>();
            foreach (CrossingRail rail in rails.Rails)
            {
                string material = rail.Owner.GetMaterial(rail.OwnerPart) ?? "iron";
                if (!byMaterial.TryGetValue(material, out List<LoftPart> parts))
                {
                    byMaterial[material] = parts = new List<LoftPart>();
                    order.Add(material);
                }
                parts.AddRange(Parts(rail));
                AddBolts(chunkCoord, rail, material);
            }

            for (int n = 0; n < order.Count; n++)
                renderer.SetLoft(chunkCoord, record.Key, true, "X" + n, order[n], byMaterial[order[n]]);
        }

        // Вузол стягнутий наскрізними болтами: крізь шийку ходової рейки, проставку і шийку контррейки, а де
        // контррейки немає, крізь рейку з проставкою. Стоять уздовж усієї проставки, через чверть блока.
        // Головка на ходовій рейці ззовні колії, гайка з боку осі: на контррейці або на самій проставці
        private void AddBolts(Vec3i chunkCoord, CrossingRail rail, string material)
        {
            double web = RailProfile.WebHalfWidth;
            foreach (RailSpan span in rail.Of(RailPiece.Spacer))
            {
                // Не ближче ніж за пів перерізу від кінців проставки
                int first = (int)Math.Ceiling(span.From.Farthest + 0.5), last = (int)Math.Floor(span.To.Nearest - 0.5);
                for (int at = first; at <= last; at++)
                {
                    if (at < 0 || at >= rail.Frames.Count) continue;
                    // Де шийки ходової рейки немає, вона там обірвана на рейці іншої колії: стягувати нічого
                    if (!Lies(rail, RailPiece.Web, at)) continue;

                    RailFrame frame = rail.Frames[at];
                    var outer = new Vec3d(frame.Side.X * rail.Side, frame.Side.Y * rail.Side, frame.Side.Z * rail.Side);
                    var inner = new Vec3d(-outer.X, -outer.Y, -outer.Z);
                    renderer.AddBolt(chunkCoord, material, frame.Center, outer, frame.Along);

                    // Модель болта стоїть на пів шийки від точки, куди її поставлено. Гайка сидить на шийці
                    // контррейки, а де її немає, на дальньому боці проставки
                    double nut = Lies(rail, RailPiece.GuardWeb, at) ? CrossingGeometry.GuardAxis : CrossingGeometry.GuardAxis - 2 * web;
                    renderer.AddBolt(chunkCoord, material, new Vec3d(frame.Center.X + inner.X * nut, frame.Center.Y + inner.Y * nut, frame.Center.Z + inner.Z * nut), inner, frame.Along);
                }
            }
        }

        // Чи складова рейки лежить у цьому місці вздовж неї
        private static bool Lies(CrossingRail rail, RailPiece piece, double at)
        {
            foreach (RailSpan span in rail.Of(piece))
            {
                if (at >= span.From.Middle && at <= span.To.Middle) return true;
            }
            return false;
        }

        // Рейка з усім, що до неї прилягає, відрізками між сусідніми краями
        internal static List<LoftPart> Parts(CrossingRail rail)
        {
            var parts = new List<LoftPart>();

            // Усі краї вздовж рейки, по порядку, без повторів
            var edges = new List<RailEdge>();
            for (int piece = 0; piece < CrossingGeometry.PieceCount; piece++)
            {
                foreach (RailSpan span in rail.Of((RailPiece)piece))
                {
                    edges.Add(span.From);
                    edges.Add(span.To);
                }
            }
            edges.Sort((x, y) => x.Middle.CompareTo(y.Middle));
            for (int n = edges.Count - 1; n > 0; n--)
            {
                if (edges[n].Middle - edges[n - 1].Middle < SameEdge) edges.RemoveAt(n);
            }

            for (int n = 0; n + 1 < edges.Count; n++)
            {
                RailEdge from = edges[n], to = edges[n + 1];
                double middle = (from.Middle + to.Middle) / 2;

                // Що лежить на цьому відрізку і що з того тут починається чи кінчається торцем
                var boxes = new List<double[]>();
                var startCaps = new List<double[]>();
                var endCaps = new List<double[]>();
                for (int piece = 0; piece < CrossingGeometry.PieceCount; piece++)
                {
                    foreach (RailSpan span in rail.Of((RailPiece)piece))
                    {
                        if (span.From.Middle > middle || span.To.Middle < middle) continue;

                        double[] box = CrossingGeometry.Box((RailPiece)piece, rail.Side);
                        boxes.Add(box);
                        if (span.CapFrom && Math.Abs(span.From.Middle - from.Middle) < SameEdge) startCaps.Add(box);
                        if (span.CapTo && Math.Abs(span.To.Middle - to.Middle) < SameEdge) endCaps.Add(box);
                        break;
                    }
                }
                if (boxes.Count == 0) continue;

                List<List<double[]>> outlines = CrossingGeometry.Outline(boxes);
                for (int o = 0; o < outlines.Count; o++)
                {
                    List<double[]> outline = outlines[o];
                    double left = double.MaxValue, right = double.MinValue;
                    foreach (double[] point in outline)
                    {
                        left = Math.Min(left, point[0]);
                        right = Math.Max(right, point[0]);
                    }

                    var part = new LoftPart();
                    part.Stations.Add(Station(rail.Frames, from, outline));
                    // Перерізи лінії, що лежать цілком між двома краями
                    int first = (int)Math.Floor(Math.Max(from.At(left), from.At(right))) + 1;
                    int last = (int)Math.Ceiling(Math.Min(to.At(left), to.At(right))) - 1;
                    for (int at = first; at <= last; at++) part.Stations.Add(Station(rail.Frames, RailEdge.Square(at), outline));
                    part.Stations.Add(Station(rail.Frames, to, outline));

                    // Торці належать відрізку, а не контуру: кладемо їх разом із першим
                    if (o == 0)
                    {
                        foreach (double[] box in startCaps) part.StartCaps.Add(Cap(rail.Frames, from, box));
                        foreach (double[] box in endCaps) part.EndCaps.Add(Cap(rail.Frames, to, box));
                    }
                    parts.Add(part);
                }
            }
            return parts;
        }

        // Контур на краї: кожна його точка стоїть там, де край проходить на її відстані від осі рейки
        private static LoftStation Station(List<RailFrame> frames, RailEdge edge, List<double[]> outline)
        {
            var points = new LoftPoint[outline.Count];
            for (int n = 0; n < outline.Count; n++) points[n] = Point(frames, edge.At(outline[n][0]), outline[n][0], outline[n][1]);
            return new LoftStation { Along = FrameAt(frames, edge.Middle).Along, Points = points };
        }

        // Торець: прямокутник складової на краї. Кути по колу, починаючи з лівого нижнього, далі правий нижній
        private static LoftPoint[] Cap(List<RailFrame> frames, RailEdge edge, double[] box)
        {
            double left = box[0], right = box[1], bottom = box[2], top = box[3];
            return new[]
            {
                Point(frames, edge.At(left), left, bottom), Point(frames, edge.At(right), right, bottom),
                Point(frames, edge.At(right), right, top), Point(frames, edge.At(left), left, top)
            };
        }

        private static LoftPoint Point(List<RailFrame> frames, double at, double x, double y)
        {
            RailFrame frame = FrameAt(frames, at);
            return new LoftPoint { Center = frame.Center, Side = frame.Side, Up = frame.Up, X = x, Y = y };
        }

        // Переріз лінії в місці at: номер перерізу з дробовою частиною
        private static RailFrame FrameAt(List<RailFrame> frames, double at)
        {
            int index = GameMath.Clamp((int)Math.Floor(at), 0, frames.Count - 2);
            return RailLine.Between(frames[index], frames[index + 1], GameMath.Clamp(at - index, 0, 1));
        }
    }
}
