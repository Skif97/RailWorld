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

    public class EntityTrolley :  EntityAgent
    {

        public double RenderOrder => 0;

        public int RenderRange => 999;

        private Vec3d DirVec = new Vec3d();
        public double Speed = 0f;
        public double FreeFallSpeed = 80.0f; //метров в секунду характерно для угловатой формы
        private bool Brake = false;

        private bool OnRail = false;

        private bool applyGravity = false;

        private bool isInteractable = true;

        private RailSection currentRailSection;


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

        //public IMountable[] MountPoints => throw new NotImplementedException();

        //public void OnRenderFrame(float dt, EnumRenderStage stage)
        //{ 
        
        //}
            

        //public void Dispose()
        //{

        //}



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
            if(Api.Side == EnumAppSide.Server) 
            {
                currentRailSection = GetReilSec(this.Attributes.GetVec3d("currentSectionBlock"), this.Attributes.GetInt("currentSectionSlot"));
            }
        }

        public override void OnCollided()
        {
            base.OnCollided();

        }

        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();
        }



        private void onServerPhysicsTickCallback(double dt)
        {
            while (dt > 0f)
            {
                if (currentRailSection != null)
                {
                    int dirIndex;
                    double accelerationOnRail;
                    Vec3d endPoint;
                    double distance;
                    double totalSpeed;
                    double totalTime;
                    double timeToZeroSpeed;


                    if (Speed == 0f) //ищем правильное направление движения при нулевой сторости, если скорость не нулевая, то вектор остаётся прежним
                    {
                        if (currentRailSection.FDAcceleration > 0f)
                        {
                            DirVec = currentRailSection.FDVector.Clone();
                        }
                        else if (currentRailSection.SDAcceleration > 0f)
                        {
                            DirVec = currentRailSection.SDVector.Clone();
                        }
                        //else
                        //{
                        //    DirVec = ModMath.YawToVec(this.ServerPos.Yaw);
                        //    return;
                        //}
                    }

                    DirVec.Normalize();

                    dirIndex = GetDirection(DirVec); // ищем нужное ускорение из жд секции, нужно спрятать под капот

                    if (dirIndex == 1)
                    {
                        accelerationOnRail = currentRailSection.FDAcceleration;
                    }
                    else
                    {
                        accelerationOnRail = currentRailSection.SDAcceleration;
                    }

                    accelerationOnRail -= currentRailSection.SDResistance; //корректируем ускорение


                    endPoint = GetEndPointOnSections(dirIndex);
                    distance = ServerPos.DistanceTo(endPoint);
                    totalSpeed = TotalSpeedOnDistance(Speed, distance, accelerationOnRail);

                    if (totalSpeed == 0f) 
                    {
                        return;
                    }

                    if (totalSpeed < 0f) //если скорость в конце участка отрицательная
                    {
                        timeToZeroSpeed = TotalTravelTimeToZeroSpeed(Speed, accelerationOnRail); //ищем время для прохождения участка
                        if (timeToZeroSpeed > dt) // если нехватает времени чтобы достичь точку с нулевой скоростью, ищем точку до неё
                        {
                            distance = TotalDistanceOnTimeInterval(Speed, dt, accelerationOnRail);
                            Speed = TotalSpeedOnDistance(Speed, distance, accelerationOnRail);
                            DirVec = GetDirectionVector(dirIndex).Normalize();
                            DirVec.Mul(distance);
                            ServerPos.Add(DirVec.X, DirVec.Y, DirVec.Z);
                            dt = 0f;
                        }
                        else // если времени больше  то останавливаемся в нулевой точке 
                        {
                            distance = TotalDistanceOnTimeInterval(Speed, timeToZeroSpeed, accelerationOnRail);
                            Speed = 0f;
                            DirVec = GetDirectionVector(dirIndex).Normalize();
                            DirVec.Mul(distance);
                            ServerPos.Add(DirVec.X, DirVec.Y, DirVec.Z);
                            dt -= timeToZeroSpeed;
                            dt--;
                        }
                    }
                    else // если скорость положительная
                    {
                        totalTime = TotalTravelTime(Speed, totalSpeed, accelerationOnRail); //ищем время для прохождения участка
                        if (totalTime > dt) // если нехватает времени чтобы достичь конечную точку, ищем точку до неё
                        {
                            distance = TotalDistanceOnTimeInterval(Speed, dt, accelerationOnRail);
                            Speed = TotalSpeedOnDistance(Speed, distance, accelerationOnRail);
                            DirVec = GetDirectionVector(dirIndex).Normalize();
                            DirVec.Mul(distance);
                            ServerPos.Add(DirVec.X, DirVec.Y, DirVec.Z);
                            dt = 0f;
                        }
                        else // если время хватает обновляет данные, становимся в эту точку
                        {
                            Speed = TotalSpeedOnDistance(Speed, distance, accelerationOnRail);
                            ServerPos.SetPos(endPoint);
                            dt -= totalTime;
                            currentRailSection = GetNextReilSec(dirIndex);
                        }
                    }


                }
                else
                {
                    if (Speed != 0f)
                    {
                    
                        dt= 0f;
                        //тут описать движение по инерции вне рельс
                    }
                    dt = 0f;
                }
            }
            if (currentRailSection!=null) 
            {
                ServerPos.Yaw = currentRailSection.centerYaw;
                ServerPos.Pitch = currentRailSection.centerPitch;
                ServerPos.Roll = currentRailSection.centerRoll;
            }

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
           // applyGravity = applyGravity ? false : true;

            base.OnInteract(byEntity, slot, hitPosition, mode);
        }

        public override void OnFallToGround(double motionY)
        {
            base.OnFallToGround(motionY);
        }

        public override void OnGameTick(float dt)
        {           

            base.OnGameTick(dt);
            if(Api.Side == EnumAppSide.Server) 
            {
                onServerPhysicsTickCallback(dt);
            }
        }

        private double TotalSpeedOnDistance(double startspeed, double distance, double acceleration) 
        {
            //v = √(u^2 + 2as)
            double squareTotalSpeed = startspeed * startspeed + 2f * acceleration * distance;
            if (squareTotalSpeed < 0) 
            {
                return -1f;
            }
            else 
            {
                return Math.Sqrt(squareTotalSpeed);
            }
            

        }

        private double TotalTravelTime(double startSpeed, double endSpeed, double totalAcceleration)
        {
            //t = (v - u) / a
            return (endSpeed - startSpeed) / totalAcceleration;
        }

        private double TotalTravelTimeToZeroSpeed(double startSpeed, double totalAcceleration)
        {
            //t = -u / a
            return -startSpeed / totalAcceleration;
        }

        private double TotalDistanceOnTimeInterval(double startSpeed, double timeInterval, double acceleration)
        {
            //s = u * t_sec + 0.5a * t_sec^2
            return startSpeed * timeInterval + 0.5f * acceleration * timeInterval * timeInterval;

        }





        

        

        private Vec3d GetEndPointOnSections(int directionIndex)
        {
            if (directionIndex == 1)
            { 
                return currentRailSection.FDEnd ; 
            }
            else 
            {
                return currentRailSection.SDEnd;
            }
        }

        private Vec3d GetDirectionVector(int directionIndex)
        {
            if (directionIndex == 1)
            { 
                return currentRailSection.FDVector.Clone(); 
            }
            else 
            {
                return currentRailSection.SDVector.Clone();
            }
        }

        private int GetDirection(Vec3d directionVector)
        {

            if (currentRailSection.FDVector.X * directionVector.X +
                currentRailSection.FDVector.Y * directionVector.Y +
                currentRailSection.FDVector.Z * directionVector.Z >= 0)
            {
                return 1;
            }
            else
            {
                return 2;
            }
        }

        private RailSection GetNextReilSec(int directionIndex)
        {
            BlockEntityRail BERail;
            if (directionIndex == 1)
            {
                BERail = (BlockEntityRail)this.Api.World.BlockAccessor.GetBlockEntity(currentRailSection.FDNextSectionBlock.ToBlockPos());
                if (BERail != null) 
                {
                    return BERail.GetRailSection(currentRailSection.FDNextSectionSlot);
                }
               
            }
            else 
            {
                BERail = (BlockEntityRail)this.Api.World.BlockAccessor.GetBlockEntity(currentRailSection.SDNextSectionBlock.ToBlockPos());
                if (BERail != null)
                {
                    return BERail.GetRailSection(currentRailSection.SDNextSectionSlot);
                }
            }
            return null;
        }

        private RailSection GetReilSec(Vec3d pos, int slot)
        {
          
            if(pos != null ) 
            {
                BlockEntityRail BERail = this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityRail>(pos.ToBlockPos());
                if(BERail != null) 
                {
                    RailSection rs = BERail.GetRailSection(slot);
                    if(rs != null) 
                    {
                        return rs;
                    }
                }
            }
            return null;
                   
        }

        //public bool IsMountedBy(Entity entity)
        //{
        //    throw new NotImplementedException();
        //}

        //public Vec3f GetMountOffset(Entity entity)
        //{
        //    throw new NotImplementedException();
        //}
    }
}
