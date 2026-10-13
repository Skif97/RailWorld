using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace RailWorld
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
    }
}
