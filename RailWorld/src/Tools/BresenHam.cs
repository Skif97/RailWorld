using System;

namespace RailWorld
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
    }
}
