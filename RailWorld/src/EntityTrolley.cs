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

namespace TrainWorld
{

	public class EntityTrolley : EntityAgent, IRenderer
	{

		public double RenderOrder => 0;

		public int RenderRange => 999;

		public double ForwardSpeed = 0.0;

		private bool applyGravity = false;

		public override bool ApplyGravity
		{
			get { return applyGravity; }

		}




		public override bool IsInteractable
		{
			get { return true; }
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
			Pos.Roll = 0f;
			ServerPos.Pitch = 0f;
			ServerPos.Yaw = 0f;
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
			//ServerPos.X = Pos.X + 0.01D;
			base.OnGameTick(dt);
			onPhysicsTickCallback(dt);

			//this.ForwardSpeed = .
			//this.onPhysicsTickCallback;
			//this.
		}


		


	}
}
