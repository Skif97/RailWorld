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

        private Vec3d dirvec= new Vec3d();
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
            //dt = dt / 1000f;
            if (dt > 0)
            {
                Vec3d trolleyPosition = this.ServerPos.XYZ;
                double startSpeed = this.Speed;
                if(Speed == 0f) 
                {
                    dirvec = this.ServerPos.GetViewVector().ToVec3d();
                }
                int dirIndex;
                Vec3d endPoint;
                double distance;
                double totalSpeed = 0f;
                double totalTime; 
                double accelerationOnRail = 0.40f;
               // double resistance = 0.001f;

                while (true)
                {
                    if (currentRailSection != null)
                    {
                        dirIndex = GetDirection(dirvec);
                        dirvec = GetDirectionVector(dirIndex);
                        endPoint = GetEndPointOnSections(dirIndex);
                        distance = trolleyPosition.DistanceTo(endPoint);
                        totalSpeed = TotalSpeedOnDistance(startSpeed, distance, accelerationOnRail);
                        totalTime = TotalTravelTime(startSpeed, totalSpeed, accelerationOnRail);
                        if (totalTime > dt)
                        {
                            distance = TotalDistanceOnTimeInterval(startSpeed, dt, accelerationOnRail);
                            totalSpeed = TotalSpeedOnDistance(startSpeed, distance, accelerationOnRail);
                            startSpeed = totalSpeed;

                            dirvec.Normalize();
                            dirvec.Mul(distance);
                            trolleyPosition.Add(dirvec);
                            break;
                        }

                        startSpeed = totalSpeed;

                        trolleyPosition = endPoint;
                        var currentRailSection2 = GetNextReilSec(dirIndex);
                       // var currentRailSection3 = currentRailSection;
                        currentRailSection = currentRailSection2;
                        dt = dt - totalTime;
                    }
                    else
                    {
                        startSpeed = 0f;
                        break;
                    }
                }
                if(Api.Side == EnumAppSide.Server) 
                {
                    if (currentRailSection != null)
                    {
                        this.ServerPos.Yaw = currentRailSection.centerYaw;
                        
                        this.ServerPos.Pitch = currentRailSection.centerPitch;

                        this.ServerPos.Roll = currentRailSection.centerRoll;
                    }
                    this.ServerPos.SetPos(trolleyPosition);

                    this.Speed = totalSpeed;
                }



            }
        }

        //public bool UpdateSpeedPosOnRailSec ()
        //{
        //}


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
            return Math.Sqrt(startspeed * startspeed + 2f * acceleration * distance);

        }

        private double TotalTravelTime(double startSpeed, double endSpeed, double totalAcceleration)
        {
            //t = (v - u) / a
            return (endSpeed - startSpeed) / totalAcceleration;
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


    }
}
