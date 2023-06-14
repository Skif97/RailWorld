using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System;
using Vintagestory.API.Common.Entities;
using System.IO;

namespace RailWorld
{

    public class EntityTrolley : EntityAgent, IRenderer
    {

        public double RenderOrder => 0;

        public int RenderRange => 999;


        public double Speed = 0.0;
        public double FreeFallSpeed = 80.0; //метров в секунду характерно для угловатой формы
        private double AccelerationOfGravity = 0.0; //берется из текущей железнодорожной секции
        private double OwnRollingResistance = 0.98; //узлы вагонетки не идеальны поэтому расходуется часть енергии
        private double RoadRollingResistance; // берется из пути
        private bool Brake = false;
        private int SkeepEveryRailSection = 0; //настройка для оптимизации, если будет съедать много ресурсов
                                               //можно пропускать просчёт каких то секций на больших скоростях, ихи при большом количестве секцций

        private RailSection CurrentRailSection = null;

        private bool OnRail = false;

        private bool applyGravity = true;

        private bool isInteractable = true;

        private RailSection nextRailSection;
        private RailSection currentRailSection;
        private RailSection previousRailSection;

        public override bool ApplyGravity
        {
            get { return applyGravity; }


        }



        public override bool IsInteractable
        {
            get { return isInteractable; }
        }

        public override float MaterialDensity
        {
            get { return 30000f; }
        }



        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {

        }

        public void Dispose()
        {

        }



        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {

            base.Initialize(properties, api, InChunkIndex3d);
            Properties.KnockbackResistance = 0.95f;
            hasRepulseBehavior = true;

            touchDistanceSq = (double)Math.Max(0.001f, SelectionBox.XSize);


        }

        public override void OnEntitySpawn()
        {
            base.OnEntitySpawn();

        }

        public override void OnCollided()
        {
            base.OnCollided();

        }

        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();
        }



        private void onPhysicsTickCallback(float dtFac)
        {




            ServerPos.X = ServerPos.X + (ForwardSpeed * dtFac);
            ServerPos.Y = ServerPos.Y + (ForwardSpeed * dtFac);
        }


        public override void OnInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
        {
            //Pos.Pitch = 0f;
            //Pos.Yaw = 0f;
            //Pos.Roll = (float)-Math.PI / 180 * 30;
            //Pos.Roll = 0f;
            //ServerPos.Pitch = 0f;
            //ServerPos.Yaw = 0f;
            //ServerPos.X = Pos.X + 1D;
            //ServerPos.Y = Pos.Y + 1D;
            //ServerPos.Roll = (float)-Math.PI / 180 * 30;
            //this.Attributes.SetBool("rotateWhenFalling", false);
            applyGravity = applyGravity ? false : true;
            //ForwardSpeed = 0.5f;
            base.OnInteract(byEntity, slot, hitPosition, mode);
        }

        public override void OnFallToGround(double motionY)
        {
            base.OnFallToGround(motionY);
        }
        public override void OnGameTick(float dt)
        {
            // сначала считаем итоговую скорость для текущей секции рельс, потом отталкиваясь от времени считаем пройденную дистанцию
            // если дистанция больше текущей секции, берем следующую секцию, если больше следующей тогда пересчитываем скорость для этой секции
            // и отнимаем дистанцию, и идем так до тех пор пока не найдем нужную окончательную секцию 
            // после чего ищем место на секции где окажемся
            // переназначаем текущую секцию для вагонетки, обновляем положение
            double resistance = 1.0;
            if (OnRail)
            {

            }
            else
            {
                if (this.World.BlockAccessor.GetBlock(Pos.AsBlockPos).Id == 0)
                {

                }
            }

            //ServerPos.X = Pos.X + 0.01D;
            base.OnGameTick(dt);
            onPhysicsTickCallback(dt);

            //this.ForwardSpeed = .
            //this.onPhysicsTickCallback;
            //this.
        }


        private double TotalAccelerationOnDistance(double distance, double acceleration, double movementResistance)
        {
            //a = 2as*(1 - μ)
            return 2 * acceleration * distance * (1 - movementResistance);
        }

        private double TotalSpeedOnDistance(double startspeed, double distance, double acceleration, double speedRetentionFactor)
        {
            //v = √(u^2 + aTotal)
            return Math.Sqrt(Math.Pow(startspeed, 2) + TotalAccelerationOnDistance(distance, acceleration, speedRetentionFactor));
        }

        private double TotalSpeedOnDistance(double startspeed, double totalAcceleration)
        {
            //v = √(u^2 + 2as*(1 - μ))
            return Math.Sqrt(Math.Pow(startspeed, 2) + totalAcceleration);
        }

        private double TotalTravelTime(double startSpeed, double endSpeed, double totalAcceleration)
        {
            //t = (v - u) / a
            return (endSpeed - startSpeed) / totalAcceleration;
        }

        private double TotalDistanceOnTimeInterval(double startSpeed, double timeInterval, double totalAcceleration)
        {
            //s = u * t_sec + 0.5a * t_sec^2
            return startSpeed * timeInterval + 0.5f * totalAcceleration * timeInterval * timeInterval;

        }


        private Vec3d GetEndPointOnSections(int directionIndex)
        {
            if (directionIndex == 0)
            { return currentRailSection.firstDirectionEndPoint; }
            return currentRailSection.secondDirectionEndPoint;
        }

        private Vec3d GetDirectionVector(int directionIndex)
        {
            if (directionIndex == 0)
            { return currentRailSection.firstDirectionVector; }
            return currentRailSection.secondDirectionVector;
        }

        private int GetDirection(Vec3d directionVector)
        {
            if (currentRailSection.firstDirectionVector.X * directionVector.X +
                currentRailSection.firstDirectionVector.Y * directionVector.Y +
                currentRailSection.firstDirectionVector.Z * directionVector.Z >= 0)
            { return 0; }
            return 1;
        }

        private RailSection GetNextReilSec(int directionIndex)
        {
            if (directionIndex == 0)
            { this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityRail>(currentRailSection.sectionBlockAfterFirstDirection.ToBlockPos()).GetRailSection(currentRailSection.sectionSlotAfterFirstDirection) }
            return 1;
        }

        private void SpeedUpdateTotal(double dt)
        {
            //сначала пересчитать скорость и дистанцию вдоль всей секции и если итоговое время прохода вписывается в рамки заканчиваем проход
            if (dt > 0)
            {
                EntityPos trolleyPosition = this.Pos;
                double startSpeed = this.Speed;
                while (true)
                {
                    if (currentRailSection != null)
                    {
                        double accelerationOnRail = 0.2f;
                        double resistance = 0.01f;

                        int dirIndex = GetDirection(this.Pos.GetViewVector().ToVec3d());

                        Vec3d endPoint = GetEndPointOnSections(dirIndex);
                        Vec3d dirvec = GetDirectionVector(dirIndex);
                        double distanceToEnd = trolleyPosition.DistanceTo(endPoint);
                        double totalAcceleration = TotalAccelerationOnDistance(distanceToEnd, accelerationOnRail, resistance);
                        double totalSpeed = TotalSpeedOnDistance(startSpeed, totalAcceleration);
                        double totalTime = TotalTravelTime(startSpeed, totalSpeed, totalAcceleration);
                        if (totalTime < dt)
                        {
                            trolleyPosition.SetPos(endPoint);
                            startSpeed = totalSpeed;
                            dt = dt - totalTime;

                        }
                        else
                        {
                            double distance = TotalDistanceOnTimeInterval(startSpeed, dt, totalAcceleration);
                            totalAcceleration = TotalAccelerationOnDistance(distance, accelerationOnRail, resistance);
                            totalSpeed = TotalSpeedOnDistance(startSpeed, totalAcceleration);
                            startSpeed = totalSpeed;
                            dirvec.Normalize();
                            dirvec.Mul(distance);
                            trolleyPosition.SetPos(trolleyPosition.XYZ.AddCopy(dirvec));
                            break;
                        }
                    }

                }

                this.Pos = trolleyPosition;
                this.Speed = startSpeed;


            }

        }




        public void CheckRail()
        {

        }





    }
}
