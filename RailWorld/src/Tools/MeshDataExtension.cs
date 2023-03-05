using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    public static class MeshDataExtension
    {
        public static MeshData RotateAroundAxis(this MeshData meshData, Vec3d axis, double rad)
        {
            double[] tmpMat = Mat4d.Create();
            double[] quat = Quaterniond.Create();

            quat = Quaterniond.SetAxisAngle(quat, axis.ToDoubleArray(), rad);

            Mat4d.FromQuat(tmpMat, quat);

            meshData.MatrixTransform(tmpMat);

            return meshData; 
        }
        public static MeshData RotateD(this MeshData meshData, double radX, double radY, double radZ)
        {
            double[] array = Mat4d.Create();
            Mat4d.RotateX(array, array, radX);
            Mat4d.RotateY(array, array, radY);
            Mat4d.RotateZ(array, array, radZ);
            return meshData.MatrixTransform(array);
        }
    }
}
