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

using RailWorld.src.RailWay;

namespace RailWorld
{
    public class ModMath
    {
        /// <summary>
        /// Скільки вокселів у блоці вздовж кожної осі: як у моделей гри і чізлених блоків. Своєї сталої для цього
        /// гра не має, вона й сама пише 16 числом.
        /// </summary>
        public const int VoxelsPerSide = 16;

        /// <summary>
        /// Розмір вокселя, блоків. Розміри деталей колії записуються у вокселях моделі: 0.6 * Voxel це 0,6 вокселя,
        /// як у редакторі моделей.
        /// </summary>
        public const double Voxel = 1.0 / VoxelsPerSide;


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

        public static Vec3d DirectionVectorPositionCorrection(Vec3d baseblock, Vec3d directionVector)
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

        /// <summary>
        /// Кути, з якими стандартний рендер (EntityShapeRenderer) поставить модель по колії:
        /// вісь X моделі проти напрямку колії, Z проти нормалі, Y по верху полотна.
        /// Поворот розкладається як у літака: курс, потім ухил навколо поперечної осі вагонетки, потім нахил
        /// полотна навколо поздовжньої, тобто Ry(yaw + 90°) * Rz(tilt) * Rx(cant). Рендер збирає поворот як
        /// Rx(Pitch) * Ry(Yaw + 90°) * Rz(Roll) * Rx(xangle), тому yaw іде в Yaw, tilt у Roll, cant у xangle,
        /// а Pitch лишається нулем.
        /// Розкладати на самі Pitch, Yaw і Roll не можна: у такого порядку є мертва точка, коли колія йде
        /// вздовж осі Z. Біля неї Pitch і Roll між сусідніми секціями перестрибують на пів оберту, і клієнт,
        /// який веде кожен кут окремо, на мить показує вагонетку перекошеною. У цього розкладу мертва точка
        /// лише при вертикальній колії.
        /// </summary>
        public static void TrackFrameToEntityAngles(Vec3d along, Vec3d side, Vec3d up, out float yaw, out float tilt, out float cant)
        {
            TrackFrameToEntityAngles(along.X, along.Y, along.Z, up.X, up.Y, up.Z, out yaw, out tilt, out cant);
        }

        /// <summary>
        /// Те саме з рамки, заданої числами: уздовж (a) і вгору (u). Бік для кутів не потрібен, його задають ці два.
        /// </summary>
        public static void TrackFrameToEntityAngles(double ax, double ay, double az, double ux, double uy, double uz, out float yaw, out float tilt, out float cant)
        {
            // Перед моделі це її вісь +X, як заведено в грі: рендер повертає модель на курс плюс чверть оберту,
            // і саме тоді +X моделі збігається з тим, куди дивиться сутність (EntityPos.GetViewVector).
            // Підйом цієї осі над горизонтом це ухил, а її напрямок у плані це курс
            double turn = Math.Atan2(-az, ax);
            double slope = Math.Asin(GameMath.Clamp(ay, -1, 1));

            double cosTurn = Math.Cos(turn), sinTurn = Math.Sin(turn);
            double cosSlope = Math.Cos(slope), sinSlope = Math.Sin(slope);

            // Куди дивилися б верх і бік моделі після курсу й ухилу, без нахилу полотна.
            // Справжній верх повернутий від першого до другого на кут нахилу
            double levelUp = -cosTurn * sinSlope * ux + cosSlope * uy + sinTurn * sinSlope * uz;
            double levelSide = sinTurn * ux + cosTurn * uz;

            yaw = (float)(turn - Math.PI / 2);
            tilt = (float)slope;
            cant = (float)Math.Atan2(levelSide, levelUp);
        }

        // На скільки блоків від кінця маршруту, що примикає до нахиленої колії, нахил полотна сходить до нуля
        public const double CantRampLength = TrackGauge.StandardWidth * 8.0;

        /// <summary>
        /// Кінці кривої, що примикають до вже покладеної колії, нахиленої в цьому місці впоперек (гілка стрілки
        /// на схилі лягає в полотно основної колії): на кінці нахил такий, як у тієї колії, і плавно сходить до
        /// нуля на відрізку CantRampLength. Далі крива лежить рівно. Виражу в кривих немає: його прибрано
        /// 2026-10-24, бо в грі він давав лише вигляд (відцентрової сили немає), а геометрію стрілок і перетинів
        /// ускладнював.
        /// </summary>
        /// <param name="startCant">Нахил колії, від якої крива починається, радіани; NaN, якщо з нуля.</param>
        /// <param name="endCant">Те саме для кінця кривої.</param>
        public static void RampEndCant(List<PointOnBezierCurve> points, double startCant = double.NaN, double endCant = double.NaN)
        {
            if (double.IsNaN(startCant)) startCant = 0;
            if (double.IsNaN(endCant)) endCant = 0;

            int count = points.Count;
            // Майже завжди обидва кінці рівні, і робити нічого
            if (count < 3 || (startCant == 0 && endCant == 0)) return;

            double[] dist = new double[count];
            for (int i = 1; i < count; i++)
            {
                Vec3d a = points[i - 1].position, b = points[i].position;
                double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                dist[i] = dist[i - 1] + Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            double total = dist[count - 1];
            double ramp = Math.Min(CantRampLength, total / 2);
            if (ramp <= 0) return;

            for (int i = 0; i < count; i++)
            {
                // Ближчий кінець і його нахил. Далі за ramp від кінця нахилу немає, і нормаль лишається як є
                bool nearStart = dist[i] <= total - dist[i];
                double edge = nearStart ? startCant : endCant;
                double fromEdge = nearStart ? dist[i] : total - dist[i];
                if (edge == 0 || fromEdge >= ramp) continue;

                double k = fromEdge / ramp;
                k = k * k * (3 - 2 * k);
                double cant = edge - edge * k;

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
    }

    
}