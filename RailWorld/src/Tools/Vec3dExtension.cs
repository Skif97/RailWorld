using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace TrainWorld
{
    public static class Vec3dExtension
    {
        public static Vec3d RotateAroundY(this Vec3d vec, double rad)
        {

            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double x = vec.X;
            double z = vec.Z;
            vec.X = (x * cos) + (z * sin);
            vec.Z = (z * cos) - (x * sin);

            return vec;
        }

        //public static Vec3d Rotate(this Vec3d vec, double rad)
        //{
        //    double[] array = Mat3d.Create();
        //    Mat3d.Rotate(array, array, rad);
        //    return vec.MatrixTransform(array);
        //}

        public static Vec3d Floor(this Vec3d vec)
        {
            vec.X = Math.Floor(vec.X);
            vec.Y = Math.Floor(vec.Y);
            vec.Z = Math.Floor(vec.Z);
            return vec;
        }

        public static Vec3d FloorCopy(this Vec3d vec)
        {
            return new Vec3d(Math.Floor(vec.X), Math.Floor(vec.Y), Math.Floor(vec.Z));
        }

        public static Vec3d Average(this Vec3d vec, Vec3d vec2)
        {
            vec.X = (vec.X + vec2.X) / 2;
            vec.Y = (vec.Y + vec2.Y) / 2;
            vec.Z = (vec.Z + vec2.Z) / 2;
            return vec;
        }

        public static Vec3d AverageCopy(this Vec3d vec, Vec3d vec2)
        {
            return new Vec3d((vec.X + vec2.X) / 2, (vec.Y + vec2.Y) / 2, (vec.Z + vec2.Z) / 2);
        }

        public static Vec3d AddXZCopy(this Vec3d vec, Vec3d vec2)
        {
            return new Vec3d(vec.X + vec2.X, vec.Y, vec.Z + vec2.Z);
        }

        public static Vec3d NegateVec(this Vec3d vec)
        {
            vec.X = -vec.X;
            vec.Y = -vec.Y;
            vec.Z = -vec.Z;
            return vec;
        }

        public static Vec3d MulCopy(this Vec3d vec, double val)
        {
            return new Vec3d(vec.X * val, vec.Y * val, vec.Z * val);
        }

        public static BlockPos ToBlockPos(this Vec3d vec)
        {
            return new BlockPos(vec.ToVec3i());
        }

        public static Vec3i ToVec3i(this Vec3d vec)
        {
            return new Vec3i(vec.XInt, vec.YInt, vec.ZInt);
        }

        public static Vec3d MatrixTransform(this Vec3d vec, double[] m)
        {
            double x = vec.X;
            double y = vec.Y;
            double z = vec.Z;
            vec.X = m[0] * x + m[4] * y + m[7] * z;
            vec.Y = m[1] * x + m[5] * y + m[8] * z;
            vec.Z = m[2] * x + m[6] * y + m[9] * z;

            return vec;
        }

        public static Vec3d RotateAroundAxis(this Vec3d vec, Vec3d axis, double rad)
        {
            double[] tmpMat = Mat4d.Create();
            double[] quat = Quaterniond.Create();

            quat = Quaterniond.SetAxisAngle(quat, axis.ToDoubleArray(), rad);

            Mat4d.FromQuat(tmpMat, quat);
            vec.MatrixTransform(tmpMat);
            return vec;

        }

    }
}
