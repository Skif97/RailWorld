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
        public Vec3d tangent;
        public Vec3d normal;

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

        public CubicBezierCurve3d(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3)
        {
            x0 = p0.X;
            y0 = p0.Y;
            z0 = p0.Z;
            w0 = 1f;

            x1 = p1.X;
            y1 = p1.Y;
            z1 = p1.Z;
            w1 = 1f;

            x2 = p2.X;
            y2 = p2.Y;
            z2 = p2.Z;
            w2 = 1f;

            x3 = p3.X;
            y3 = p3.Y;
            z3 = p3.Z;
            w3 = 1f;
        }

        public CubicBezierCurve3d(Vec4d p0, Vec4d p1, Vec4d p2, Vec4d p3)
        {
            x0 = p0.X;
            y0 = p0.Y;
            z0 = p0.Z;
            w0 = p0.W;

            x1 = p1.X;
            y1 = p1.Y;
            z1 = p1.Z;
            w1 = p1.W;

            x2 = p2.X;
            y2 = p2.Y;
            z2 = p2.Z;
            w2 = p2.W;

            x3 = p3.X;
            y3 = p3.Y;
            z3 = p3.Z;
            w3 = p3.W;
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
            double t2 = t * t;
            double mt = 1 - t;
            double mt2 = mt * mt;

            double c0 =  mt2;
            double c1 = 2  * mt * t;
            double c2 =  t2;
            double basis = c0 + c1 + c2;
            return (c0 * 3f * (p1 - p0) + c1 * 3f * (p2 - p1) + c2 * 3f * (p3 - p2)) / basis;
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


        public double LengthFull(double timeStep = 0.00001f )
        {
            return LengthToEnd(timeStep, 0f);
        }

        public double LengthToEnd(double timeStep = 0.00001f, double starttime = 0f)
        {
            double xPrevious = x0;
            double yPrevious = y0;
            double zPrevious = z0;

            double xNext = x0;
            double yNext = y0;
            double zNext = z0;
            double leng = 0;


            for (double time = starttime; time >= 0 && time <= 1; time += timeStep)
            {
                xNext = InterpolationX_WithWeight(time);
                yNext = InterpolationY_WithWeight(time);
                zNext = InterpolationZ_WithWeight(time);

                leng += LenghtLine3D(xNext, yNext, zNext,xPrevious, yPrevious, zPrevious);
                xPrevious = xNext;
                yPrevious = yNext;
                zPrevious = zNext;
            }
            leng += LenghtLine3D(xNext, yNext, zNext, x3, y3, z3); 
            return leng;
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
            int approximateQuantityPiece = (int)Math.Floor(LengthFull(minTimeStep) / approximatelyPieceLength);
            int quntityPoints = 0;
            //if (approximateQuantityPiece % 2 == 0) { quntityPoints = approximateQuantityPiece - 1; }
           // else { quntityPoints = approximateQuantityPiece; }
            quntityPoints = ((int)Math.Floor(approximateQuantityPiece / 2f) * 2)+1;
            double pieceLength = LengthFull(minTimeStep) / (quntityPoints - 1f);
            double timeNext = 0f;
            double totalLenght = 0;
            int step = 0;

            point.position = InterpolationWithWeight(0f);
            point.tangent = Derivative(0f);
            point.normal = new Vec3d(-point.tangent.Z, 0, point.tangent.X).Normalize();
            pointsList.Add(point);
            step++;
            Vec3d prevPos = point.position;

            while (timeNext < 1 && step < quntityPoints) 
            {
                timeNext += minTimeStep;
                point.position = InterpolationWithWeight(timeNext);
                totalLenght += LenghtLine3D(prevPos.X, prevPos.Y, prevPos.Z, point.position.X, point.position.Y, point.position.Z);
                prevPos = point.position;
                if (totalLenght > pieceLength * step) 
                {
                    point.tangent = Derivative(timeNext);
                    point.normal = new Vec3d(-point.tangent.Z, 0, point.tangent.X).Normalize();
                    pointsList.Add(point);
                    step++;
                }
            }
            point.position = InterpolationWithWeight(1f);
            point.tangent = Derivative(1f);
            point.normal = new Vec3d(-point.tangent.Z, 0, point.tangent.X).Normalize();
            pointsList.Add(point);
            step++;
            return pointsList;
        }

    }
}
