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
            // Перед моделі це її вісь +X, як заведено в грі: рендер повертає модель на курс плюс чверть оберту,
            // і саме тоді +X моделі збігається з тим, куди дивиться сутність (EntityPos.GetViewVector).
            // Підйом цієї осі над горизонтом це ухил, а її напрямок у плані це курс
            double turn = Math.Atan2(-along.Z, along.X);
            double slope = Math.Asin(GameMath.Clamp(along.Y, -1, 1));

            double cosTurn = Math.Cos(turn), sinTurn = Math.Sin(turn);
            double cosSlope = Math.Cos(slope), sinSlope = Math.Sin(slope);

            // Куди дивилися б верх і бік моделі після курсу й ухилу, без нахилу полотна.
            // Справжній верх повернутий від першого до другого на кут нахилу
            double levelUp = -cosTurn * sinSlope * up.X + cosSlope * up.Y + sinTurn * sinSlope * up.Z;
            double levelSide = sinTurn * up.X + cosTurn * up.Z;

            yaw = (float)(turn - Math.PI / 2);
            tilt = (float)slope;
            cant = (float)Math.Atan2(levelSide, levelUp);
        }

        // Нахил полотна в поворотах (підвищення зовнішньої рейки)
        /// <summary>
        /// Чи нахиляти полотно в поворотах. Якщо вимкнути, нова колія лежить рівно і в кривих; там, де вона
        /// примикає до вже покладеної нахиленої колії, нахил плавно сходить до нуля.
        /// </summary>
        public static readonly bool CantInCurves = true;

        /// <summary>
        /// Нахил є лише в крутих поворотах. На радіусі CantFromRadius і більшому його немає зовсім, на
        /// CantTightRadius і меншому він найбільший, між ними плавно наростає. Обидва в ширинах колії:
        /// при колії 0.78 це 10.5 і 5.3 блока, тобто три і півтора найменших радіуси маршруту.
        /// Пологі криві, а з ними й перетини через них, лежать рівно.
        /// </summary>
        public const double CantFromRadius = TrackGauge.StandardWidth * 13.5;
        public const double CantTightRadius = TrackGauge.StandardWidth * 6.75;

        public const double MaxCantRad = 6 * Math.PI / 180;  // найбільший кут нахилу
        // На скільки блоків від краю кривої нахил наростає з нуля і на якому відрізку він усереднюється
        public const double CantRampLength = TrackGauge.StandardWidth * 8.0;

        /// <summary>
        /// Нахиляє нормалі точок кривої в бік повороту. Кут залежить від кривизни, усередненої по відрізку
        /// колії довжиною CantRampLength навколо точки: там, де сходяться дуги різної кривизни, нахил
        /// міняється поступово, а не стрибком між двома секціями. На краях кривої він плавно сходить до нуля,
        /// щоб стикуватися з сусідніми ділянками.
        /// </summary>
        /// <param name="startCant">Нахил, з якого крива має початися, радіани; NaN, якщо з нуля.
        /// Потрібен, коли крива починається від уже покладеної колії, а та в цьому місці нахилена.</param>
        /// <param name="endCant">Те саме для кінця кривої.</param>
        public static void ApplyCant(List<PointOnBezierCurve> points, double startCant = double.NaN, double endCant = double.NaN)
        {
            if (double.IsNaN(startCant)) startCant = 0;
            if (double.IsNaN(endCant)) endCant = 0;

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

            // Нахил, якого просить кривизна в самій точці
            double[] raw = new double[count];
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

                // Частка найбільшого нахилу: нуль на пологій кривій, одиниця на крутій, між ними плавно
                double tight = GameMath.Clamp((Math.Abs(curvature) - 1 / CantFromRadius) / (1 / CantTightRadius - 1 / CantFromRadius), 0, 1);
                tight = tight * tight * (3 - 2 * tight);
                raw[i] = CantInCurves ? Math.Sign(curvature) * MaxCantRad * tight : 0;
            }

            // Середнє по відрізку навколо точки. Вікно їде разом із точкою, тому межі лише зсуваються вперед
            double half = CantRampLength / 2;
            int from = 0, to = 0;
            double sum = 0;

            for (int i = 0; i < count; i++)
            {
                while (to < count && dist[to] <= dist[i] + half) sum += raw[to++];
                while (dist[from] < dist[i] - half) sum -= raw[from++];

                // На краю кривої нахил такий, як у колії, до якої вона примикає, і плавно переходить у свій
                bool nearStart = dist[i] <= total - dist[i];
                double edge = nearStart ? startCant : endCant;
                double fromEdge = nearStart ? dist[i] : total - dist[i];

                double k = GameMath.Clamp(fromEdge / ramp, 0, 1);
                k = k * k * (3 - 2 * k);
                double cant = edge + (sum / (to - from) - edge) * k;

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