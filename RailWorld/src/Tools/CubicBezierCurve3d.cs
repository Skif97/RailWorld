using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    public struct PointOnBezierCurve
    {
        public Vec3d position;
        public Vec3f tangent;
        public Vec3f normal;

    }

    public class CubicBezierCurve3d
    {
        public readonly double x0, y0, z0, x1, y1, z1, x2, y2, z2, x3, y3, z3, w0, w1, w2, w3;

        public CubicBezierCurve3d(double p0x, double p0y, double p0z, double p1x, double p1y, double p1z, double p2x, double p2y, double p2z, double p3x, double p3y, double p3z, double p0w = 1f, double p1w = 1f, double p2w = 1f, double p3w = 1f)
        {
            x0 = p0x;
            y0 = p0y;
            z0 = p0z;
            w0 = p0w;

            x1 = p1x;
            y1 = p1y;
            z1 = p1z;
            w1 = p1w;

            x2 = p2x;
            y2 = p2y;
            z2 = p2z;
            w2 = p2w;

            x3 = p3x;
            y3 = p3y;
            z3 = p3z;
            w3 = p3w;
        }

        double InterpolationX(double t)
        {
            return Interpolation(t, x0, x1, x2, x3);
        }

        double InterpolationY(double t)
        {
            return Interpolation(t, y0, y1, y2, y3);
        }

        double InterpolationZ(double t)
        {

            return Interpolation(t, z0, z1, z2, z3);
        }

        private double Interpolation(double t, double p0, double p1, double p2, double p3)
        {
            double t2 = t * t;
            double t3 = t2 * t;
            double mt = 1 - t;
            double mt2 = mt * mt;
            double mt3 = mt2 * mt;

            double c0 = p0 * mt3;
            double c1 = 3 * p1 * mt2 * t;
            double c2 = 3 * p2 * mt * t2;
            double c3 = p3 * t3;
            return c0 + c1 + c2 + c3;
        }

        public Vec3d Interpolation(double t)
        {
            return new Vec3d(InterpolationX(t), InterpolationY(t), InterpolationZ(t));
        }


        double InterpolationX_WithWeight(double t)
        {
            return InterpolationWithWeight(t, x0, x1, x2, x3);
        }

        double InterpolationY_WithWeight(double t)
        {
            return InterpolationWithWeight(t, y0, y1, y2, y3);
        }

        double InterpolationZ_WithWeight(double t)
        {
            return InterpolationWithWeight(t, z0, z1, z2, z3);
        }

        private double InterpolationWithWeight(double t, double p0, double p1, double p2, double p3)
        {
            double t2 = t * t;
            double t3 = t2 * t;
            double mt = 1 - t;
            double mt2 = mt * mt;
            double mt3 = mt2 * mt;

            double c0 = w0 * mt3;
            double c1 = 3 * w1 * mt2 * t;
            double c2 = 3 * w2 * mt * t2;
            double c3 = w3 * t3;
            double basis = c0 + c1 + c2 + c3;
            return (c0 * p0 + c1 * p1 + c2 * p2 + c3 * p3) / basis;
        }

        public Vec3d InterpolationWithWeight(double t)
        {
            return new Vec3d(InterpolationX_WithWeight(t), InterpolationY_WithWeight(t), InterpolationZ_WithWeight(t));
        }

        private double Derivative(double t, double p0, double p1, double p2, double p3)
        {
            // Крива раціональна: P(t) = N(t) / D(t), тому P' = (N' * D - N * D') / D^2.
            // Без урахування ваг дотична не збігається з кривою, яку дає InterpolationWithWeight
            double t2 = t * t;
            double mt = 1 - t;
            double mt2 = mt * mt;

            double b0 = mt2 * mt;
            double b1 = 3 * mt2 * t;
            double b2 = 3 * mt * t2;
            double b3 = t2 * t;

            double n0 = w0 * p0, n1 = w1 * p1, n2 = w2 * p2, n3 = w3 * p3;

            double numerator   = b0 * n0 + b1 * n1 + b2 * n2 + b3 * n3;
            double denominator = b0 * w0 + b1 * w1 + b2 * w2 + b3 * w3;

            double dNumerator   = 3 * (mt2 * (n1 - n0) + 2 * mt * t * (n2 - n1) + t2 * (n3 - n2));
            double dDenominator = 3 * (mt2 * (w1 - w0) + 2 * mt * t * (w2 - w1) + t2 * (w3 - w2));

            return (dNumerator * denominator - numerator * dDenominator) / (denominator * denominator);
        }

        double DerivativeX(double t)
        {
            return Derivative(t, x0, x1, x2, x3);
        }

        double DerivativeY(double t)
        {
            return Derivative(t, y0, y1, y2, y3);
        }

        double DerivativeZ(double t)
        {
            return Derivative(t, z0, z1, z2, z3);
        }

        public Vec3d Derivative(double t)
        {
            return new Vec3d(DerivativeX(t), DerivativeY(t), DerivativeZ(t)).Normalize();
        }


        public double LenghtLine3D(double x0, double y0, double z0, double x1, double y1, double z1) 
        {
            return Math.Sqrt(Math.Pow(x1 - x0, 2) + Math.Pow(y1 - y0, 2) + Math.Pow(z1 - z0, 2));
        }


        public double MinimumTimeStepSearcher(double minSegmentLength = 0.25f) 
        {
            double minimumTimeStep = 1f;
            while (DistanceBetweenTwoTimePoints(0,minimumTimeStep) > minSegmentLength) 
            {
                minimumTimeStep /= 2f;
            }
            return minimumTimeStep / 20f;
        }

        public double DistanceBetweenTwoTimePoints(double t1, double t2)
        {
            double dx = InterpolationX_WithWeight(t2) - InterpolationX_WithWeight(t1);
            double dy = InterpolationY_WithWeight(t2) - InterpolationY_WithWeight(t1);
            double dz = InterpolationZ_WithWeight(t2) - InterpolationZ_WithWeight(t1);
            return Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2) + Math.Pow(dz, 2));
        }

        public List<PointOnBezierCurve> CutIntoEqualPieces(double approximatelyPieceLength)
        {
            List<PointOnBezierCurve> pointsList = new List<PointOnBezierCurve>();
            PointOnBezierCurve point = new PointOnBezierCurve();

            double minTimeStep = MinimumTimeStepSearcher(approximatelyPieceLength);

            // Крива проходиться дрібними кроками один раз: запам'ятовуємо, скільки пройдено на кожному кроці.
            // З цього виходить і повна довжина, і місця, де ставити точки. Раніше крива проходилася тричі
            // (двічі заради довжини і ще раз заради точок); числа виходять ті самі до останнього біта
            List<double> times = new List<double>();
            List<double> lengths = new List<double>();
            double xPrevious = x0, yPrevious = y0, zPrevious = z0;
            double xNext = x0, yNext = y0, zNext = z0;
            double walked = 0;
            for (double time = 0; time >= 0 && time <= 1; time += minTimeStep)
            {
                xNext = InterpolationX_WithWeight(time);
                yNext = InterpolationY_WithWeight(time);
                zNext = InterpolationZ_WithWeight(time);

                walked += LenghtLine3D(xNext, yNext, zNext, xPrevious, yPrevious, zPrevious);
                xPrevious = xNext;
                yPrevious = yNext;
                zPrevious = zNext;
                times.Add(time);
                lengths.Add(walked);
            }
            double fullLength = walked + LenghtLine3D(xNext, yNext, zNext, x3, y3, z3);

            int approximateQuantityPiece = (int)Math.Floor(fullLength / approximatelyPieceLength);
            int quntityPoints = 0;
            //if (approximateQuantityPiece % 2 == 0) { quntityPoints = approximateQuantityPiece - 1; }
           // else { quntityPoints = approximateQuantityPiece; }
            quntityPoints = ((int)Math.Floor(approximateQuantityPiece / 2f) * 2)+1;
            double pieceLength = fullLength / (quntityPoints - 1f);

            point.position = InterpolationWithWeight(0f);
            point.tangent = Derivative(0f).ToVec3f();
            point.normal = new Vec3f(-point.tangent.Z, 0, point.tangent.X).Normalize();
            pointsList.Add(point);

            // Між першою і останньою точкою їх рівно quntityPoints - 2: кожна там, де пройдено ще один шматок,
            // на першому кроці обходу, що його перейшов. Остання точка це сам кінець кривої, тож тут її не шукаємо:
            // інакше похибка суми довжин інколи ставила ще одну впритул до кінця або й за ним, і точок ставало парно
            int walkStep = 0;
            for (int step = 1; step < quntityPoints - 1; step++)
            {
                double target = pieceLength * step;
                while (walkStep < times.Count - 1 && lengths[walkStep] <= target) walkStep++;

                double time = times[walkStep];
                point.position = InterpolationWithWeight(time);
                point.tangent = Derivative(time).ToVec3f();
                point.normal = new Vec3f(-point.tangent.Z, 0, point.tangent.X).Normalize();
                pointsList.Add(point);
            }
            point.position = InterpolationWithWeight(1f);
            point.tangent = Derivative(1f).ToVec3f();
            point.normal = new Vec3f(-point.tangent.Z, 0, point.tangent.X).Normalize();
            pointsList.Add(point);
            return pointsList;
        }

    }
}
