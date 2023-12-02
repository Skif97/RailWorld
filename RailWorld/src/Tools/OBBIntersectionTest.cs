using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RailWorld
{
    public class OBBIntersectionTest
    {

        public IWorldIntersectionSupplier blockSelectionTester;


        public Vec3d hitPosition = new Vec3d();


        public Ray ray = new Ray();

        private double[] rayTransMat;

        public BlockPos pos = new BlockPos();


        public void LoadRayAndPos(Line3D line3d)
        {
            this.ray.origin.Set(line3d.Start[0], line3d.Start[1], line3d.Start[2]);
            this.ray.dir.Set(line3d.End[0] - line3d.Start[0], line3d.End[1] - line3d.Start[1], line3d.End[2] - line3d.Start[2]);
            this.pos.Set((int)line3d.Start[0], (int)line3d.Start[1], (int)line3d.Start[2]);
        }

        public void LoadRayAndPos(Ray ray)
        {
            this.ray = ray;
            this.pos.Set(ray.origin);
        }

        public bool RayIntersectsWithMesh(MeshData origMesh)
        {
            if (origMesh == null)
            {
                return false;
            }
            if (origMesh.IndicesCount < 3)
            {
                return false;
            }
            List<Vec3d> verticesList = this.TransformMashAndGetVertices(origMesh.Clone());

            bool flag = false;
            //bool allVerticesOnRightSide = verticesList[0].X >= 0 ? true : false;
            //for (int i = 1; i < verticesList.Count; i++)
            //{
            //    if (verticesList[i].X >= 0 != allVerticesOnRightSide) { flag = true; }
            //}
            //if (!flag) { return false; }
            bool allVerticesOnUpSide = verticesList[0].Y >= 0 ? true : false;
            flag = false;
            for (int i = 1; i < verticesList.Count; i++)
            {
                if (verticesList[i].Y >= 0 != allVerticesOnUpSide) { flag = true; }
            }
            if (!flag) { return false; }

            return true;

        }

        public MeshData TransformMesh (MeshData origMesh)
        {
            this.RayToMat();
            return origMesh.Clone().MatrixTransform(this.rayTransMat);

        }

        private void RayToMat () 
        {
            Vec3d pos = this.ray.origin;
            Vec3d posend = this.ray.dir;
            Vec3d forward  = this.ray.dir.Clone().Normalize(); //Z
            Vec3d right = forward.Clone().Cross(new Vec3d(0d, 1d, 0d)); //X
            Vec3d up = forward.Clone().Cross(right); //Y


            //double[] mat = new double[16];
           // Mat4d.Identity(mat);

            //Mat4d.Translate(mat, mat, pos.X, pos.Y, pos.Z);

            Vec3d Zaxis = new Vec3d(0d, 0d, 1d);

            Zaxis.Normalize();

            double  cos = Zaxis.Clone().Dot(forward.Clone());

            Vec3d R = Zaxis.Clone().Cross(forward.Clone());
            R.Normalize();
            double angle = Math.Acos(cos);
            double sin = Math.Sin(cos);

            double[] mat = new double[16] {
                cos + (1 - cos) * R.X * R.X,          (1 - cos) * R.X * R.Y - sin * R.Z,    (1 - cos) * R.X * R.Z + sin * R.Y,  pos.X,
                (1 - cos) * R.Y * R.X + sin * R.Z,    cos + (1 - cos) * R.Y * R.Y,          (1 - cos) * R.Z * R.Y - sin * R.X,  pos.Y,
                (1 - cos) * R.Z * R.X - sin * R.Y,    (1 - cos) * R.Z * R.Y + sin * R.X,    cos + (1 - cos) * R.Z * R.Z,        pos.Z,
                0,                                    0,                                    0,                                  1
            };
            Mat4d.Identity(mat);
            //Mat4d.Translate(mat, mat, -0.5d, 0d, -2d);
           // Mat4d.Translate(mat, mat, 2d, -2.0d, -1.0d);
            this.rayTransMat = mat;
        }

        private List<Vec3d> TransformMashAndGetVertices(MeshData origMesh) 
        {
            List<Vec3d> verticesList = new List<Vec3d>();
            MeshData mesh = origMesh.Clone();
            this.RayToMat();
            mesh.MatrixTransform(this.rayTransMat);

            for (int i = 0; i < mesh.xyz.Length; i += 3)
            {
                verticesList.Add(new Vec3d(mesh.xyz[i], mesh.xyz[i + 1], mesh.xyz[i + 2]));
            }
            return verticesList;
        }
    }
}
