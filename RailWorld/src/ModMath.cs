using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace TrainWorld
{
    public class ModMath
    {


        public static CubicBezierCurve3d CotrolPointSercherForArc(Vec3d position, double yaw, double radius, double arcLenghtRad, bool right, double cur, int slope = 0)
        {
            Vec3d firstPoint;
            Vec3d secondPoint;
            Vec3d thirdPoint;
            Vec3d fourthPoint;

            Vec3d firstPointDirectVec;
            Vec3d fourthPointDirectVec;


            firstPoint = CorrectionPositionToCenterBlockXZ(position);
            firstPointDirectVec = GetDirectionVectorXZ(yaw);

            Vec3d radiusVec = firstPointDirectVec.Clone();


            if (right)
            {
                radiusVec.RotateAroundY(Math.PI / 2);
            }
            else
            {
                radiusVec.RotateAroundY(-Math.PI / 2);
            }


            radiusVec.Mul(radius);

            Vec3d arcCenter = firstPoint.Clone();

            arcCenter.Sub(radiusVec.X, 0f, radiusVec.Z);

            if (right)
            {
                radiusVec.RotateAroundY(-arcLenghtRad);
            }
            else
            {
                radiusVec.RotateAroundY(arcLenghtRad);
            }

            fourthPoint = arcCenter.Clone();
            fourthPoint.Add(radiusVec);
            fourthPoint = CorrectionPositionToCenterBlockXZ(fourthPoint);

            fourthPointDirectVec = radiusVec.Clone();
            fourthPointDirectVec.Normalize();

            if (right)
            {
                fourthPointDirectVec.RotateAroundY(Math.PI / 2);
            }
            else
            {
                fourthPointDirectVec.RotateAroundY(-Math.PI / 2);
            }

            firstPoint = DirectionVectorPositionCorrection(firstPoint, firstPointDirectVec, true);

            fourthPoint = DirectionVectorPositionCorrection(fourthPoint, fourthPointDirectVec, true);

            secondPoint = firstPoint.Clone();
            secondPoint.Add(firstPointDirectVec.Mul((arcLenghtRad * radius) / 2));

            thirdPoint = fourthPoint.Clone();
            thirdPoint.Add(fourthPointDirectVec.Mul((arcLenghtRad * radius) / 2));

            position.Y += 0.171875f;

            return new CubicBezierCurve3d(firstPoint.X, position.Y, firstPoint.Z,
                                          secondPoint.X, position.Y, secondPoint.Z,
                                          thirdPoint.X, position.Y + slope, thirdPoint.Z,
                                          fourthPoint.X, position.Y + slope, fourthPoint.Z,
                                          1f, cur, cur, 1f);

        }

        public static CubicBezierCurve3d CubicBezierCotrolPointSercher4P(Vec3d position, double yaw, double cur, double radius, int slope = 0, int type = 0)
        {


            if (type == 0)
            {
                return CotrolPointSercherForArc(position, yaw, radius, Math.PI / 2, false, cur, slope);
            }
            else if (type == 1)
            {
                return CotrolPointSercherForArc(position, yaw, radius, Math.PI / 2, true, cur, slope);
            }
            else if (type == 2)
            {
                return CotrolPointSercherForArc(position, yaw, radius, Math.PI / 4, false, cur, slope);
            }
            else //(type == 3)
            {
                return CotrolPointSercherForArc(position, yaw, radius, Math.PI / 4, true, cur, slope);
            }




        }

        public static Vec3d DirectionVectorPositionCorrection(Vec3d baseblock, Vec3d directionVector, bool ItIsStart)
        {
            directionVector.Normalize();
            double yawAngle = Math.Acos(directionVector.X / Math.Sqrt((directionVector.X * directionVector.X) + (directionVector.Z * directionVector.Z)));
            if (directionVector.Z > 0)
            {
                yawAngle = Math.PI + (Math.PI - yawAngle);
            }

            return YawPositionCorrection(baseblock, yawAngle);
        }

        public static Vec3d GetDirectionVectorXZ(double yaw)
        {
            double sector = Math.Floor(yaw / (Math.PI / 2));
            double sectorRad = sector * Math.PI / 2;
            double subSectorRad = yaw - sectorRad;
            double subSector = Math.Floor(subSectorRad / (Math.PI / 8));

            double rad = 0;
            if (subSector == 0)
            {
                rad = (Math.PI / 2) * sector;
            }
            else if (subSector == 1 || subSector == 2)
            {
                rad = ((Math.PI / 2) * sector) + ((Math.PI / 8) * 2);
            }
            else if (subSector == 3)
            {
                rad = ((Math.PI / 2) * sector) + (Math.PI / 2);
            }
            Vec3d resultVec = new Vec3d(1f, 0f, 0f).RotateAroundY(rad);
            return resultVec;
        }

        public static Vec3d YawPositionCorrection(Vec3d baseblock, double yawAngle)
        {
            double xCorrection = 0f;
            double zCorrection = 0f;
            int sector = (int)Math.Floor(yawAngle / (Math.PI / 8));
            if (sector == 0 || sector == 15 || sector > 15)
            {
                xCorrection = -0.5f;
            }
            else if (sector == 1 || sector == 2)
            {
                xCorrection = -0.5f;
                zCorrection = 0.5f;
            }
            else if (sector == 3 || sector == 4)
            {
                zCorrection = 0.5f;
            }
            else if (sector == 5 || sector == 6)
            {
                xCorrection = 0.5f;
                zCorrection = 0.5f;
            }
            else if (sector == 7 || sector == 8)
            {
                xCorrection = 0.5f;
            }
            else if (sector == 9 || sector == 10)
            {
                xCorrection = 0.5f;
                zCorrection = -0.5f;
            }
            else if (sector == 11 || sector == 12)
            {
                zCorrection = -0.5f;
            }
            else if (sector == 13 || sector == 14)
            {
                xCorrection = -0.5f;
                zCorrection = -0.5f;
            }


            Vec3d final = new Vec3d(baseblock.X + xCorrection, baseblock.Y, baseblock.Z + zCorrection);
            return final;
        }

        public static Vec3d CorrectionPositionToCenterBlockXZ(Vec3d baseblock)
        {


            return new Vec3d(baseblock.XInt + 0.5f, baseblock.Y, baseblock.ZInt + 0.5f);
        }


        public static List<RailSection> GenerateRailSections(List<PointOnBezierCurve> pointsOnCurve, double trackWidth)
        {


            List<RailSection> railSections = new List<RailSection>();

            for (int i = 0; i < pointsOnCurve.Count - 2; i+=2)
            {
                //if (pointsOnCurve[i].position.X == pointsOnCurve[i + 1].position.X && pointsOnCurve[i].position.Z == pointsOnCurve[i + 1].position.Z)
                //{
                //}
                RailSection raillSection = new RailSection(pointsOnCurve[i], pointsOnCurve[i + 1], pointsOnCurve[i+2], trackWidth);

                railSections.Add(raillSection);
            }
            

            return railSections;
        }

        public static double TangentToYaw(Vec3d tangent)
        {
            Vec2d atan = new Vec2d(tangent.X, tangent.Z).Normalize();

            return Math.PI - Math.Atan2(atan.Y, atan.X);
        }

        public static double TangentToPitch(Vec3d tangent)
        {

            return Math.Asin(tangent.Y / tangent.Length());
        }

        public static void ShowTheOutline(EntityAgent byEntity, CubicBezierCurve3d position, double th, int HighlightSlotId)
        {
            byEntity.World.HighlightBlocks(byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID), HighlightSlotId, new List<BlockPos>(), new List<int>(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
            List<BlockPos> blocks = new List<BlockPos>();
            List<int> colors = new List<int>();
            BresenHam.PlotCubicBezierWidth(position.x0, position.z0, position.x1, position.z1, position.x2, position.z2, position.x3, position.z3, th,
            delegate (double x, double z, double aa)
            {
                if (aa >= 0.55)
                {
                    blocks.Add(new BlockPos((int)x, (int)position.y0, (int)z));
                    colors.Add(ColorUtil.ColorFromRgba(215, 94, 94, 120));
                }
                else
                {
                    blocks.Add(new BlockPos((int)x, (int)position.y0, (int)z));
                    colors.Add(ColorUtil.ColorFromRgba(215, 215, 94, 120));
                }
            });
            byEntity.World.HighlightBlocks(byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID), HighlightSlotId, blocks, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        }


    }

    public static class ITreeAttributeExtension
    {
        public static ITreeAttribute SetVec3d(this ITreeAttribute tree, string name, Vec3d vec)
        {
            tree.SetDouble(name + ".X", vec.X);
            tree.SetDouble(name + ".Y", vec.Y);
            tree.SetDouble(name + ".Z", vec.Z);
            return tree;
        }

        public static Vec3d GetVec3d(this ITreeAttribute tree, string name)
        {
            double X = tree.GetDouble(name + ".X", 0f);
            double Y = tree.GetDouble(name + ".Y", 0f);
            double Z = tree.GetDouble(name + ".Z", 0f);
            return new Vec3d(X, Y, Z);
        }
    }
    
}