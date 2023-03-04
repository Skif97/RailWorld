using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrainWorld
{
    public class BresenHam
    {

        public delegate void PlotDelegate2DAA(double x, double z, double aa);

        public static void BresenHamPlotLineWidth(double x0, double y0, double x1, double y1, double th, PlotDelegate2DAA onPlot)
        {
            double num = Math.Abs(x1 - x0);
            double num2 = (x0 < x1) ? 1 : -1;
            double num3 = -Math.Abs(y1 - y0);
            double num4 = (y0 < y1) ? 1 : -1;
            double num5 = num + num3;
            for (; ; )
            {
                onPlot(x0, y0, 1);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }
                double num6 = 2 * num5;
                if (num6 >= num3)
                {
                    num5 += num3;
                    x0 += num2;
                }
                if (num6 <= num)
                {
                    num5 += num;
                    y0 += num4;
                }
            }
        }

        static void PlotQuadRationalBezierWidthSeg(double x0, double y0, double x1, double y1, double x2, double y2, double w, double th, PlotDelegate2DAA onPlot)
        { /* plot a limited rational Bezier segment of thickness th, squared weight */
            double sx = x2 - x1;
            double sy = y2 - y1;  /* relative values for checks */
            double dx = x0 - x2;
            double dy = y0 - y2;
            double xx = x0 - x1;
            double yy = y0 - y1;
            double xy = xx * sy + yy * sx;
            double cur = xx * sy - yy * sx;
            double err, e2, ed; /* curvature */
            bool fullBreak = false;

            if (cur != 0.0 && w > 0.0)
            { /* no straight line */
                if (sx * sx + sy * sy > xx * xx + yy * yy)
                { /* begin with longer part */
                    x2 = x0;
                    x0 -= dx;
                    y2 = y0;
                    y0 -= dy;
                    cur = -cur; /* swap P0 P2 */
                }
                xx = (2.0 * (4.0 * w * sx * xx + dx * dx)); /* differences 2nd degree */
                yy = (2.0 * (4.0 * w * sy * yy + dy * dy));
                sx = x0 < x2 ? 1 : -1; /* x step direction */
                sy = y0 < y2 ? 1 : -1; /* y step direction */
                xy = -2.0 * sx * sy * (2.0 * w * xy + dx * dy);

                if (cur * sx * sy < 0)
                { /* negated curvature? */
                    xx = -xx;
                    yy = -yy;
                    cur = -cur;
                    xy = -xy;
                }
                dx = (4.0 * w * (x1 - x0) * sy * cur + xx / 2.0); /* differences 1st degree */
                dy = (4.0 * w * (y0 - y1) * sx * cur + yy / 2.0);

                if (w < 0.5 && (dx + xx <= 0 || dy + yy >= 0))
                { /* flat ellipse, algo fails */
                    cur = (w + 1.0) / 2.0;
                    w = Math.Sqrt(w);
                    xy = 1.0 / (w + 1.0);
                    sx = Math.Floor((x0 + 2.0 * w * x1 + x2) * xy / 2.0 + 0.5); /* subdivide curve  */
                    sy = Math.Floor((y0 + 2.0 * w * y1 + y2) * xy / 2.0 + 0.5); /* plot separately */
                    dx = Math.Floor((w * x1 + x0) * xy + 0.5);
                    dy = Math.Floor((y1 * w + y0) * xy + 0.5);
                    PlotQuadRationalBezierWidthSeg(x0, y0, dx, dy, sx, sy, cur, th, onPlot);
                    dx = Math.Floor((w * x1 + x2) * xy + 0.5);
                    dy = Math.Floor((y1 * w + y2) * xy + 0.5);
                    PlotQuadRationalBezierWidthSeg(sx, sy, dx, dy, x2, y2, cur, th, onPlot);
                    return;
                }

                for (err = 0; dy + 2 * yy < 0 && dx + 2 * xx > 0;) /* loop of steep/flat curve */
                    if (dx + dy + xy < 0)
                    {               /* steep curve */
                        do
                        {
                            ed = -dy - 2 * dy * dx * dx / (4.0 * dy * dy + dx * dx); /* approximate sqrt */
                            w = ((th - 1) * ed);                 /* scale line width */
                            x1 = Math.Floor((err - ed - w / 2) / dy); /* start offset */
                            e2 = err - x1 * dy - w / 2; /* error value at offset */
                            x1 = x0 - x1 * sx;          /* start point */
                            onPlot(x1, y0, (255 * e2 / ed)); /* aliasing pre-pixel */
                            for (e2 = -w - dy - e2; e2 - dy < ed; e2 -= dy)
                            {
                                onPlot(x1 += sx, y0, 1); /* pixel on thick line */
                            }
                            onPlot(x1 + sx, y0, (255 * e2 / ed)); /* aliasing post-pixel */
                            if (y0 == y2) return; /* last pixel -> curve finished */
                            y0 += sy;
                            dy += xy;
                            err += dx;
                            dx += xx;  /* y step */
                            if (2 * err + dy > 0)
                            { /* e_x+e_xy > 0 */
                                x0 += sx;
                                dx += xy;
                                err += dy;
                                dy += yy; /* x step */
                            }
                            if (x0 != x2 && (dx + 2 * xx <= 0 || dy + 2 * yy >= 0))
                            {
                                if (Math.Abs(y2 - y0) > Math.Abs(x2 - x0))
                                {
                                    fullBreak = true;
                                    break;
                                }
                                else
                                {
                                    break;          /* other curve near */
                                }
                            }

                        } while (dx + dy + xy < 0); /* gradient still steep? */

                        if (fullBreak)
                        {
                            break;
                        }

                        /* change from steep to flat curve */
                        for (cur = err - dy - w / 2, y1 = y0; cur < ed; y1 += sy, cur += dx)
                        {
                            for (e2 = cur, x1 = x0; e2 - dy < ed; e2 -= dy)
                            {
                                onPlot(x1 -= sx, y1, 1); /* pixel on thick line */
                            }
                            onPlot(x1 - sx, y1, (255 * e2 / ed)); /* aliasing post-pixel */
                        }
                    }
                    else
                    { /* flat curve */
                        do
                        {
                            ed = dx + 2 * dx * dy * dy / (4.0 * dx * dx + dy * dy); /* approximate sqrt */
                            w = ((th - 1) * ed);                 /* scale line width */
                            y1 = Math.Floor((err + ed + w / 2) / dx); /* start offset */
                            e2 = y1 * dx - w / 2 - err; /* error value at offset */
                            y1 = y0 - y1 * sy;          /* start point */
                            onPlot(x0, y1, (255 * e2 / ed)); /* aliasing pre-pixel */
                            for (e2 = dx - e2 - w; e2 + dx < ed; e2 += dx)
                            {
                                onPlot(x0, y1 += sy, 1); /* pixel on thick line */
                            }
                            onPlot(x0, y1 + sy, (255 * e2 / ed)); /* aliasing post-pixel */
                            if (x0 == x2) return; /* last pixel -> curve finished */
                            x0 += sx;
                            dx += xy;
                            err += dy;
                            dy += yy;               /* x step */
                            if (2 * err + dx < 0)
                            { /* e_y+e_xy < 0 */
                                y0 += sy;
                                dy += xy;
                                err += dx;
                                dx += xx; /* y step */
                            }
                            if (y0 != y2 && (dx + 2 * xx <= 0 || dy + 2 * yy >= 0))
                            {
                                if (Math.Abs(y2 - y0) <= Math.Abs(x2 - x0))
                                {
                                    fullBreak = true;
                                    break;
                                }
                                else
                                {
                                    break; /* other curve near */
                                }
                            }
                        } while (dx + dy + xy >= 0); /* gradient still flat? */

                        if (fullBreak) break;

                        /* change from flat to steep curve */
                        for (cur = -err + dx - w / 2, x1 = x0; cur < ed; x1 += sx, cur -= dy)
                        {
                            for (e2 = cur, y1 = y0; e2 + dx < ed; e2 += dx)
                            {
                                onPlot(x1, y1 -= sy, 1); /* pixel on thick line */
                            }
                            onPlot(x1, y1 - sy, (255 * e2 / ed)); /* aliasing post-pixel */
                        }
                    }
            }
            BresenHamPlotLineWidth(x0, y0, x2, y2, th, onPlot);
        }

        public static void PlotQuadRationalBezierWidth(double x0, double y0, double x1, double y1, double x2, double y2, double w, double th, PlotDelegate2DAA onPlot)
        { /* plot any anti-aliased quadratic rational Bezier curve */
            double x = x0 - 2 * x1 + x2;
            double y = y0 - 2 * y1 + y2;
            double xx = x0 - x1;
            double yy = y0 - y1;
            double ww, t, q;

            if (xx * (x2 - x1) > 0)
            {   /* horizontal cut at P4? */
                if (yy * (y2 - y1) > 0) /* vertical cut at P6 too? */
                    if (Math.Abs(xx * y) > Math.Abs(yy * x))
                    { /* which first? */
                        x0 = x2;
                        x2 = xx + x1;
                        y0 = y2;
                        y2 = yy + y1; /* swap points */
                    }                 /* now horizontal cut at P4 comes first */
                if (x0 == x2 || w == 1.0)
                {
                    t = (x0 - x1) / x;
                }
                else
                { /* non-rational or rational case */
                    q = Math.Sqrt(4.0 * w * w * (x0 - x1) * (x2 - x1) + (x2 - x0) * (x2 - x0));
                    if (x1 < x0)
                    {
                        q = -q;
                    }
                    t = (2.0 * w * (x0 - x1) - x0 + x2 + q) / (2.0 * (1.0 - w) * (x2 - x0)); /* t at P4 */
                }
                q = 1.0 / (2.0 * t * (1.0 - t) * (w - 1.0) + 1.0); /* sub-divide at t */
                xx = (t * t * (x0 - 2.0 * w * x1 + x2) + 2.0 * t * (w * x1 - x0) + x0) * q; /* = P4 */
                yy = (t * t * (y0 - 2.0 * w * y1 + y2) + 2.0 * t * (w * y1 - y0) + y0) * q;
                ww = t * (w - 1.0) + 1.0;
                ww *= ww * q; /* squared weight P3 */
                w = (((1.0 - t) * (w - 1.0) + 1.0) * Math.Sqrt(q)); /* weight P8 */
                x = Math.Floor(xx + 0.5);
                y = Math.Floor(yy + 0.5);                    /* P4 */
                yy = (xx - x0) * (y1 - y0) / (x1 - x0) + y0; /* intersect P3 | P0 P1 */
                PlotQuadRationalBezierWidthSeg(x0, y0, x, Math.Floor(yy + 0.5), x, y, ww, th, onPlot);
                yy = (xx - x2) * (y1 - y2) / (x1 - x2) + y2; /* intersect P4 | P1 P2 */
                y1 = Math.Floor(yy + 0.5);
                x0 = x1 = x;
                y0 = y; /* P0 = P4, P1 = P8 */
            }
            if ((y0 - y1) * (y2 - y1) > 0)
            { /* vertical cut at P6? */
                if (y0 == y2 || w == 1.0)
                {
                    t = (y0 - y1) / (y0 - 2.0 * y1 + y2);
                }
                else
                { /* non-rational or rational case */
                    q = Math.Sqrt(4.0 * w * w * (y0 - y1) * (y2 - y1) + (y2 - y0) * (y2 - y0));
                    if (y1 < y0)
                    {
                        q = -q;
                    }
                    t = (2.0 * w * (y0 - y1) - y0 + y2 + q) / (2.0 * (1.0 - w) * (y2 - y0)); /* t at P6 */
                }
                q = 1.0 / (2.0 * t * (1.0 - t) * (w - 1.0) + 1.0); /* sub-divide at t */
                xx = (t * t * (x0 - 2.0 * w * x1 + x2) + 2.0 * t * (w * x1 - x0) + x0) * q; /* = P6 */
                yy = (t * t * (y0 - 2.0 * w * y1 + y2) + 2.0 * t * (w * y1 - y0) + y0) * q;
                ww = t * (w - 1.0) + 1.0;
                ww *= ww * q; /* squared weight P5 */
                w = (((1.0 - t) * (w - 1.0) + 1.0) * Math.Sqrt(q)); /* weight P7 */
                x = Math.Floor(xx + 0.5);
                y = Math.Floor(yy + 0.5);                    /* P6 */
                xx = (x1 - x0) * (yy - y0) / (y1 - y0) + x0; /* intersect P6 | P0 P1 */
                PlotQuadRationalBezierWidthSeg(x0, y0, Math.Floor(xx + 0.5), y, x, y, ww, th, onPlot);
                xx = (x1 - x2) * (yy - y2) / (y1 - y2) + x2; /* intersect P7 | P1 P2 */
                x1 = Math.Floor(xx + 0.5);
                x0 = x;
                y0 = y1 = y; /* P0 = P6, P1 = P7 */
            }
            PlotQuadRationalBezierWidthSeg(x0, y0, x1, y1, x2, y2, w * w, th, onPlot);
        }

        public static void PlotCubicBezierWidth(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double th, PlotDelegate2DAA onPlot)
        {                                              /* plot any cubic Bezier curve */
            x0 = Math.Floor(x0);
            y0 = Math.Floor(y0);
            x1 = Math.Floor(x1);
            y1 = Math.Floor(y1);
            x2 = Math.Floor(x2);
            y2 = Math.Floor(y2);
            x3 = Math.Floor(x3);
            y3 = Math.Floor(y3);

            int n = 0;
            double i;
            double xc = x0 + x1 - x2 - x3, xa = xc - 4 * (x1 - x2);
            double xb = x0 - x1 - x2 + x3, xd = xb + 4 * (x1 + x2);
            double yc = y0 + y1 - y2 - y3, ya = yc - 4 * (y1 - y2);
            double yb = y0 - y1 - y2 + y3, yd = yb + 4 * (y1 + y2);
            double fx0 = x0, fx1, fx2, fx3, fy0 = y0, fy1, fy2, fy3;
            double t1 = xb * xb - xa * xc, t2;
            double[] t = new double[7];

            /* sub-divide curve at gradient sign changes */
            if (xa == 0)                               /* horizontal */
            {
                if (Math.Abs(xc) < 2 * Math.Abs(xb))   /* one change */
                {
                    t[n++] = xc / (2.0 * xb);
                }
            }
            else if (t1 > 0.0)                          /* two changes */
            {
                t2 = Math.Sqrt(t1);
                t1 = (xb - t2) / xa;
                if (Math.Abs(t1) < 1.0)
                {
                    t[n++] = t1;
                }
                t1 = (xb + t2) / xa;
                if (Math.Abs(t1) < 1.0)
                {
                    t[n++] = t1;
                }
            }

            t1 = yb * yb - ya * yc;
            if (ya == 0)                                      /* vertical */
            {
                if (Math.Abs(yc) < 2 * Math.Abs(yb))          /* one change */
                {
                    t[n++] = yc / (2.0 * yb);
                }
            }
            else if (t1 > 0.0)
            {                                      /* two changes */
                t2 = Math.Sqrt(t1);
                t1 = (yb - t2) / ya;
                if (Math.Abs(t1) < 1.0)
                {
                    t[n++] = t1;
                }
                t1 = (yb + t2) / ya;
                if (Math.Abs(t1) < 1.0)
                {
                    t[n++] = t1;
                }
            }
            t1 = 2 * (xa * yb - xb * ya);    /* divide at inflection point */
            t2 = xa * yc - xc * ya;
            i = t2 * t2 - 2 * t1 * (xb * yc - xc * yb);
            if (i > 0)
            {
                i = Math.Sqrt(i);
                t[n] = (t2 + i) / t1;
                if (Math.Abs(t[n]) < 1.0)
                {
                    n++;
                }
                t[n] = (t2 - i) / t1;
                if (Math.Abs(t[n]) < 1.0)
                {
                    n++;
                }
            }
            for (int ii = 1; ii < n; ii++)  /* bubble sort of 4 points */
            {
                if ((t1 = t[ii - 1]) > t[ii])
                {
                    t[ii - 1] = t[ii];
                    t[ii] = t1;
                    ii = 0;
                }
            }

            t1 = -1.0;
            t[n] = 1.0;                               /* begin / end points */
            for (int ii = 0; ii <= n; ii++)
            {                 /* plot each segment separately */
                t2 = t[ii];                                /* sub-divide at t[i-1], t[i] */
                fx1 = (t1 * (t1 * xb - 2 * xc) - t2 * (t1 * (t1 * xa - 2 * xb) + xc) + xd) / 8 - fx0;
                fy1 = (t1 * (t1 * yb - 2 * yc) - t2 * (t1 * (t1 * ya - 2 * yb) + yc) + yd) / 8 - fy0;
                fx2 = (t2 * (t2 * xb - 2 * xc) - t1 * (t2 * (t2 * xa - 2 * xb) + xc) + xd) / 8 - fx0;
                fy2 = (t2 * (t2 * yb - 2 * yc) - t1 * (t2 * (t2 * ya - 2 * yb) + yc) + yd) / 8 - fy0;
                fx0 -= fx3 = (t2 * (t2 * (3 * xb - t2 * xa) - 3 * xc) + xd) / 8;
                fy0 -= fy3 = (t2 * (t2 * (3 * yb - t2 * ya) - 3 * yc) + yd) / 8;
                x3 = Math.Floor(fx3 + 0.5);
                y3 = Math.Floor(fy3 + 0.5);     /* scale bounds */
                if (fx0 != 0.0) { fx1 *= fx0 = (x0 - x3) / fx0; fx2 *= fx0; }
                if (fy0 != 0.0) { fy1 *= fy0 = (y0 - y3) / fy0; fy2 *= fy0; }
                if (x0 != x3 || y0 != y3)
                { /* segment t1 - t2 */
                    PlotCubicBezierSegWidth(x0, y0, x0 + fx1, y0 + fy1, x0 + fx2, y0 + fy2, x3, y3, th, onPlot);
                }
                x0 = x3; y0 = y3; fx0 = fx3; fy0 = fy3; t1 = t2;
            }
        }

        public static void PlotCubicBezierSegWidth(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double th, PlotDelegate2DAA onPlot)
        {                     /* split cubic Bezier segment in two quadratic segments */
            double x = Math.Floor((x0 + 3 * x1 + 3 * x2 + x3 + 4) / 8);
            double y = Math.Floor((y0 + 3 * y1 + 3 * y2 + y3 + 4) / 8);
            PlotQuadRationalBezierWidthSeg(x0, y0, Math.Floor((x0 + 3 * x1 + 2) / 4), Math.Floor((y0 + 3 * y1 + 2) / 4), x, y, 1, th, onPlot);
            PlotQuadRationalBezierWidthSeg(x, y, Math.Floor((3 * x2 + x3 + 2) / 4), Math.Floor((3 * y2 + y3 + 2) / 4), x3, y3, 1, th, onPlot);
        }
    }
}
