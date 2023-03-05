using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using System;
using Vintagestory.API.Common.Entities;
using ProtoBuf;

namespace RailWorld
{
	

	[ProtoContract]
	public struct RailMenuPacket
	{
		[ProtoMember(1)]
		public string railMode;
		[ProtoMember(2)]
		public int railLengRad;
		[ProtoMember(3)]
		public int railClimDes;
		[ProtoMember(4)]
		public string railDirection;

	}

	public class RailWorld : ModSystem
	{
		public const int sectiontPerBlock = 3;
		static ICoreClientAPI _capi;
		static ICoreServerAPI _sapi;
		static IServerNetworkAPI _snapi;
		static IClientNetworkAPI _cnapi;
		GuiDialog _dialog;

		
		public override bool ShouldLoad(EnumAppSide forSide)
		{
			return true;
		}

		public override void StartClientSide(ICoreClientAPI api)
		{
			base.StartClientSide(api);
		   _capi = api;
			
			api.Input.RegisterHotKey(
			  "openrailmenu",
			  Lang.Get("Train World: Open rail menu"),
			  GlKeys.F,
			  HotkeyType.GUIOrOtherControls);

			api.Input.SetHotKeyHandler("openrailmenu", ToggleGuiDialogRailMenu);

			RegisterClientChannel(api.Network);
		}

		

		public override void StartServerSide(ICoreServerAPI api)
		{
			_sapi = api;
			base.StartServerSide(api);
			RegisterServerChannel(api.Network);
		}

		static void RegisterClientChannel(IClientNetworkAPI cnapi)
		{
			_cnapi = cnapi;
			cnapi.RegisterChannel("TWchannel")
				.RegisterMessageType<RailMenuPacket>();
		}

		static void RegisterServerChannel(IServerNetworkAPI snapi)
		{
			_snapi = snapi;
			 snapi.RegisterChannel("TWchannel")
			.RegisterMessageType<RailMenuPacket>()
			.SetMessageHandler(delegate (IServerPlayer fromPlayer, RailMenuPacket networkMessage)
			{
				ItemStack mystack = _sapi.World.PlayerByUid(fromPlayer.PlayerUID).InventoryManager.ActiveHotbarSlot.Itemstack;
				if (mystack != null && mystack.Attributes != null && mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
				{
					mystack.Attributes.SetString("railMode", networkMessage.railMode);
					mystack.Attributes.SetInt("railLengRad", networkMessage.railLengRad);
					mystack.Attributes.SetInt("railClimDes", networkMessage.railClimDes);
					mystack.Attributes.SetString("railDirection", networkMessage.railDirection);
					_sapi.World.PlayerByUid(fromPlayer.PlayerUID).InventoryManager.ActiveHotbarSlot.MarkDirty();
				}
			});
		}

		private bool ToggleGuiDialogRailMenu(KeyCombination keyCombination)
		{
			ItemStack mystack = _capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
			if (mystack != null && mystack.Attributes != null && mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
			{
				if (_dialog is null) _dialog = new GuiDialogRailMenu(_capi);
				if (!_dialog.IsOpened()) return _dialog.TryOpen();
				if (!_dialog.TryClose()) return true;
				_dialog.Dispose();
				_dialog = null;
				
			}
			return true;
		}

		public override void Start(ICoreAPI api)
        {
            base.Start(api);
			api.RegisterBlockClass("BlockRail", typeof(BlockRail));
			api.RegisterBlockEntityClass("BlockEntityRail", typeof(BlockEntityRail));


			api.RegisterItemClass("ItemTrolley", typeof(ItemTrolley));

			api.RegisterEntity("EntityTrolley", typeof(EntityTrolley));



		}



		


	}

}
