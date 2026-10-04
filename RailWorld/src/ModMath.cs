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
using Vintagestory.API.Util;
using System.Numerics;
using System.Drawing.Drawing2D;
using Vintagestory.API.Config;

namespace RailWorld
{
    public class ModMath
    {

        public static Vec3i FindChunk(double X, double Y, double Z)
        {
            int size = GlobalConstants.ChunkSize;
            return new Vec3i((int)Math.Truncate(X / size), (int)Math.Truncate(Y / size), (int)Math.Truncate(Z / size));
        }

        public static Vec3i FindChunk(Vec3d pos)
        {
            int size = GlobalConstants.ChunkSize;
            return new Vec3i((int)Math.Truncate(pos.X / size), (int)Math.Truncate(pos.Y / size), (int)Math.Truncate(pos.Z / size));
        }




        public static CubicBezierCurve3d CotrolPointSercherForArc(Vec3d position, double yaw, double radius, double arcLenghtRad, bool left, double cur, int slope = 0)
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


            if (left)
            {
                radiusVec.RotateAroundY(-Math.PI / 2);
            }
            else
            {
                radiusVec.RotateAroundY(Math.PI / 2);
            }


            radiusVec.Mul(radius);

            Vec3d arcCenter = firstPoint.Clone();

            arcCenter.Sub(radiusVec.X, 0f, radiusVec.Z);

            if (left)
            {
                radiusVec.RotateAroundY(arcLenghtRad);
            }
            else
            {
                radiusVec.RotateAroundY(-arcLenghtRad);
            }

            fourthPoint = arcCenter.Clone();
            fourthPoint.Add(radiusVec);
            fourthPoint = CorrectionPositionToCenterBlockXZ(fourthPoint);

            fourthPointDirectVec = radiusVec.Clone();
            fourthPointDirectVec.Normalize();

            if (left)
            {
                fourthPointDirectVec.RotateAroundY(-Math.PI / 2);
            }
            else
            {
                fourthPointDirectVec.RotateAroundY(Math.PI / 2);
            }

            firstPoint = DirectionVectorPositionCorrection(firstPoint, firstPointDirectVec, true);

            fourthPoint = DirectionVectorPositionCorrection(fourthPoint, fourthPointDirectVec, true);

            secondPoint = firstPoint.Clone();
            secondPoint.Add(firstPointDirectVec.Mul((arcLenghtRad * radius) / 2));

            thirdPoint = fourthPoint.Clone();
            thirdPoint.Add(fourthPointDirectVec.Mul((arcLenghtRad * radius) / 2));

            // Позиція це блок над землею. Піднімаємо вісь колії на товщину шпали, щоб шпала лягла на землю
            position.Y += 0.125f;

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

        public static Vec3d YawToVec(double yaw)
        {
            // Вычисление компонент вектора
            double x = Math.Cos(yaw);
            double z = Math.Sin(yaw);
            double y = 0f;

            // Возвращаем вектор направления
            return new Vec3d(x, y, z);
        }


        public static double TangentToYaw(Vec3d tangent)
        {
            return Math.Atan2(tangent.X, tangent.Z);
        }

        public static double TangentToPitch(Vec3d tangent)
        {
            double length = Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z);
            return Math.Atan2(tangent.Y, length);
        }

        /// <summary>
        /// Кути сутності, з якими стандартний рендер (EntityShapeRenderer) поставить модель по колії:
        /// вісь X моделі проти напрямку колії, Z проти нормалі, Y по верху полотна.
        /// Рендер збирає поворот як Rx(Pitch) * Ry(Yaw + 90°) * Rz(Roll), тому кути розкладаємо саме під цей порядок.
        /// </summary>
        public static void TrackFrameToEntityAngles(Vec3d along, Vec3d side, Vec3d up, out float yaw, out float pitch, out float roll)
        {
            // Рядки матриці повороту зі стовпцями (-along, up, -side)
            double m00 = -along.X, m01 = up.X, m02 = -side.X;
            double m10 = -along.Y, m11 = up.Y;
            double m12 = -side.Y, m22 = -side.Z;

            double a, b, c;
            if (Math.Abs(m02) > 0.99999)
            {
                // Колія рівно вздовж осі Z без нахилів: Pitch і Roll зливаються в один кут
                a = 0;
                b = Math.Sign(m02) * Math.PI / 2;
                c = Math.Atan2(m10, m11);
            }
            else
            {
                // Гілку вибираємо так, щоб Pitch лишався в межах ±90°
                double s = m22 >= 0 ? 1 : -1;
                a = Math.Atan2(-m12 * s, m22 * s);
                b = Math.Atan2(m02, s * Math.Sqrt(m00 * m00 + m01 * m01));
                c = Math.Atan2(-m01 * s, m00 * s);
            }

            pitch = (float)a;
            yaw = (float)(b - Math.PI / 2);
            roll = (float)c;
        }

        // Нахил полотна в поворотах (підвищення зовнішньої рейки)
        public const double MaxCantRad = 6 * Math.PI / 180;  // найбільший кут нахилу
        public const double FullCantRadius = 30;             // при такому і меншому радіусі нахил максимальний
        public const double CantRampLength = 6;              // на скільки блоків від краю кривої нахил наростає з нуля

        /// <summary>
        /// Нахиляє нормалі точок кривої в бік повороту. Кут залежить від кривизни в точці
        /// і плавно сходить до нуля на краях кривої, щоб стикуватися з сусідніми ділянками.
        /// </summary>
        public static void ApplyCant(List<PointOnBezierCurve> points)
        {
            int count = points.Count;
            if (count < 3) return;

            double[] dist = new double[count];
            for (int i = 1; i < count; i++)
            {
                Vec3d d = points[i].position - points[i - 1].position;
                dist[i] = dist[i - 1] + d.Length();
            }
            double total = dist[count - 1];
            double ramp = Math.Min(CantRampLength, total / 2);
            if (ramp <= 0) return;

            for (int i = 0; i < count; i++)
            {
                int a = Math.Max(i - 1, 0);
                int b = Math.Min(i + 1, count - 1);
                Vec3f ta = points[a].tangent;
                Vec3f tb = points[b].tangent;
                double turn = Math.Atan2(tb.X * ta.Z - tb.Z * ta.X, ta.X * tb.X + ta.Z * tb.Z);
                // Сусідні точки можуть збігтися (остання точка нарізки з кінцем кривої), тоді вийде 0 / 0.
                // У такій точці кривизну вважаємо нульовою
                double span = dist[b] - dist[a];
                double curvature = span > 1e-9 ? turn / span : 0;
                if (double.IsNaN(curvature) || double.IsInfinity(curvature)) curvature = 0;

                double k = GameMath.Clamp(Math.Min(dist[i], total - dist[i]) / ramp, 0, 1);
                k = k * k * (3 - 2 * k);
                double cant = Math.Sign(curvature) * MaxCantRad * Math.Min(1, FullCantRadius * Math.Abs(curvature)) * k;

                PointOnBezierCurve p = points[i];
                Vec3f n = p.normal;
                Vec3f t = p.tangent;
                // Верх полотна: перпендикуляр до дотичної і горизонтальної нормалі
                Vec3f up = new Vec3f(n.Y * t.Z - n.Z * t.Y, n.Z * t.X - n.X * t.Z, n.X * t.Y - n.Y * t.X);
                float cos = (float)Math.Cos(cant);
                float sin = (float)Math.Sin(cant);
                p.normal = new Vec3f(n.X * cos + up.X * sin, n.Y * cos + up.Y * sin, n.Z * cos + up.Z * sin);
                points[i] = p;
            }
        }

        public static CubicBezierCurve3d CotrolPointSercherForStraight(Vec3d position, double yaw, double lenght, double cur, int slope = 0)
        {
            Vec3d firstPoint;
            Vec3d secondPoint;
            Vec3d thirdPoint;
            Vec3d fourthPoint;

            Vec3d firstPointDirectVec;
            Vec3d fourthPointDirectVec;


            firstPoint = CorrectionPositionToCenterBlockXZ(position);
            firstPointDirectVec = GetDirectionVectorXZ(yaw).Normalize();

            secondPoint = firstPoint.AddCopy(firstPointDirectVec.MulCopy(lenght / 2f * 0.8f));

            fourthPoint = CorrectionPositionToCenterBlockXZ(firstPoint.AddCopy(firstPointDirectVec.MulCopy(lenght)));

            fourthPointDirectVec = firstPointDirectVec.Clone().NegateVec();

            thirdPoint = fourthPoint.AddCopy(fourthPointDirectVec.MulCopy(lenght / 2f * 0.8f));

            firstPoint = DirectionVectorPositionCorrection(firstPoint, firstPointDirectVec, true);

            fourthPoint = DirectionVectorPositionCorrection(fourthPoint, fourthPointDirectVec, true);

            // Позиція це блок над землею. Піднімаємо вісь колії на товщину шпали, щоб шпала лягла на землю
            position.Y += 0.125f;

            return new CubicBezierCurve3d(firstPoint.X, position.Y, firstPoint.Z,
                                          secondPoint.X, position.Y, secondPoint.Z,
                                          thirdPoint.X, position.Y + slope, thirdPoint.Z,
                                          fourthPoint.X, position.Y + slope, fourthPoint.Z,
                                          1f, cur, cur, 1f);

        }


    }




    public static class Quaterniond_Extensions
    {
        public static float[] ToEulerAngles(this double[] quaternion)
        {
            // Извлекаем компоненты кватерниона
            double qw = quaternion[0];
            double qx = quaternion[1];
            double qy = quaternion[2];
            double qz = quaternion[3];

            double sqw = qw * qw;
            double sqx = qx * qx;
            double sqy = qy * qy;
            double sqz = qz * qz;

            double yaw;
            double pitch;
            double roll;

            // roll (x-axis rotation)
            double sinr_cosp = 2f * (qw * qx + qy * qz);
            double cosr_cosp = 1f - 2f * (sqx + sqy);
            roll = Math.Atan2(sinr_cosp, cosr_cosp);

            // pitch (y-axis rotation)
            double sinp = 2f * (qw * qy - qz * qx);
            if (Math.Abs(sinp) >= 1f)
            {
                pitch = Math.PI / 2f * Math.Sign(sinp); // use 90 degrees if out of range
            }
            else
            {
                pitch = Math.Asin(sinp);
            }

            // yaw (z-axis rotation)
            double siny_cosp = 2f * (qw * qz + qx * qy);
            double cosy_cosp = 1f - 2f * (sqy + sqz);
            yaw = Math.Atan2(siny_cosp, cosy_cosp);



            // Приводим углы в диапазон от 0 до 2π радиан
            yaw = NormalizeAngle(yaw);
            pitch = NormalizeAngle(pitch);
            roll = NormalizeAngle(roll);

            // Возвращаем углы Эйлера в векторе
            return new float[] { (float)yaw, (float)pitch, (float)roll };
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle < 0f)
            {
                angle += 2 * Math.PI;
            }
            while (angle >= 2 * Math.PI)
            {
                angle -= 2 * Math.PI;
            }
            return angle;
        }

        public static float[] MatrixToEulerAngles(this double[] matrix)
        {
            double yaw = Math.Atan2(-matrix[2], matrix[0]);
            double pitch = Math.Asin(matrix[1]);
            double roll = Math.Atan2(-matrix[9], matrix[10]);

            // Приводим углы к нужному диапазону
            //yaw = NormalizeAngle(yaw);
            // pitch = NormalizeAngle(pitch);
            // roll = NormalizeAngle(roll);

            return new float[] { (float)yaw, (float)pitch, (float)roll };
        }

    }

    public static class ITreeAttributeExtension
    {
        public static ITreeAttribute SetVec3d(this ITreeAttribute tree, string name, Vec3d vec)
        {
            if(vec != null) 
            {
                tree.SetDouble(name + ".X", vec.X);
                tree.SetDouble(name + ".Y", vec.Y);
                tree.SetDouble(name + ".Z", vec.Z);
            }
            return tree;
        }

        public static Vec3d GetVec3d(this ITreeAttribute tree, string name)
        {
            double X = tree.GetDouble(name + ".X", 0f);
            double Y = tree.GetDouble(name + ".Y", 0f);
            double Z = tree.GetDouble(name + ".Z", 0f);
            return new Vec3d(X, Y, Z);
        }


        public static ITreeAttribute SetDoubleArray16(this ITreeAttribute tree, string name, double[] array)
        {

            for(int i = 0; i < 16; i++) 
            {
                if (array.GetLength(0)<i) 
                {
                    return tree;
                }
                tree.SetDouble(name + i, array[i]);
            }

            return tree;
        }

        public static double[] GetDoubleArray16(this ITreeAttribute tree, string name)
        {
            double[] array = new double[16];
            for (int i = 0; i < 16 ; i++)
            {
                if (tree.TryGetDouble(name + i) != null) 
                {
                    array[i] = tree.GetDouble(name + i);
                }
                else 
                {
                    array[i] = 0;
                }
            }

            return array;
        }

    }


    
}