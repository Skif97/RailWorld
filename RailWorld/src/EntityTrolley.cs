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
        private bool Brake = false;

        public RailSection CurrentRailSection = null;

        private bool OnRail = false;

        private bool applyGravity = true;

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
            currentRailSection = GetReilSec(this.Attributes.GetVec3d("currentSectionBlock"), this.Attributes.GetInt("currentSectionSlot"));
            Speed = 1f;
        }

        public override void OnCollided()
        {
            base.OnCollided();

        }

        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();
        }



        private void onPhysicsTickCallback(double dt)
        {
            if (dt > 0)
            {
                EntityPos trolleyPosition = this.Pos;
                double startSpeed = this.Speed;
                if (currentRailSection != null)
                {
                    while (true)
                    {
                        double accelerationOnRail = 0.01f;
                        double resistance = 0.5f;

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
                            currentRailSection = GetNextReilSec(dirIndex);

                        }
                        else
                        {
                            double distance = TotalDistanceOnTimeInterval(startSpeed, dt, totalAcceleration);
                            totalAcceleration = TotalAccelerationOnDistance(distance, accelerationOnRail, resistance);
                            totalSpeed = TotalSpeedOnDistance(startSpeed, totalAcceleration);
                            startSpeed = totalSpeed;
                            dirvec.Normalize();
                            dirvec. Mul(distance);
                            trolleyPosition.SetPos(trolleyPosition.XYZ.AddCopy(dirvec));
                            break;
                        }
                        
                    }
                }
                
                if(Api.Side == EnumAppSide.Client) 
                {
                    this.Pos = trolleyPosition;
                }
                else 
                {
                    this.ServerPos = trolleyPosition;
                }
                this.Speed = startSpeed;


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

            base.OnGameTick(dt);
            onPhysicsTickCallback(dt);

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
                return currentRailSection.FDVector; 
            }
            else 
            {
                return currentRailSection.SDVector;
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
                BERail = this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityRail>(currentRailSection.FDNextSectionBlock.ToBlockPos());
                if (BERail != null) 
                {
                    return BERail.GetRailSection(currentRailSection.FDNextSectionSlot);
                }
               
            }
            else 
            {
                BERail = this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityRail>(currentRailSection.SDNextSectionBlock.ToBlockPos());
                if (BERail != null)
                {
                    return BERail.GetRailSection(currentRailSection.SDNextSectionSlot);
                }
            }
            return null;
        }

        private RailSection GetReilSec(Vec3d pos, int slot)
        {
          
            if(pos != null && slot != null) 
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


    }
}
