using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using RailWorld.src.Items;

namespace RailWorld
{
    internal sealed class GuiDialogRailMenu : GuiDialog
    {
        string railMode;
        int railLengRad;
        int railClimDes;
        string railDirection;
        string sleeperMaterial;
        string railMaterial;

        // Слоти лише для показу вибраної шпали і рейки поруч зі списками
        DummyInventory previewInventory;
        static IClientNetworkAPI _cnapi;
        static ICoreClientAPI _capi;
        public override string ToggleKeyCombinationCode => "openrailmenu";

        public GuiDialogRailMenu(ICoreClientAPI capi) : base(capi) 
        {
            _capi = capi;
            previewInventory = new DummyInventory(capi, 2);
           ItemStack mystack = capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (mystack != null && mystack.Attributes != null && mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
            {
                railMode = mystack.Attributes.GetString("railMode", "SingleBlock");
                railLengRad = mystack.Attributes.GetInt("railLengRad", 30);
                railClimDes = mystack.Attributes.GetInt("railClimDes", 0);
                railDirection = mystack.Attributes.GetString("railDirection", "Left");
                sleeperMaterial = mystack.Attributes.GetString("sleeperMaterial", "oak");
                railMaterial = mystack.Attributes.GetString("railMaterial", "iron");

            }
        }

       // private string TitleRailMenu => Lang.Get("Train World: Selecting the type of rails to be placed");


        private void ComposeDialog()
        {
            
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds leftColumn = ElementBounds.Fixed(0, 200, 680, 340);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            ElementBounds singleBlockButton = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 70, 160, 40);
            ElementBounds turnButton90 = ElementBounds.Fixed(EnumDialogArea.CenterFixed, -86, 70, 160, 40);
            ElementBounds turnButton45 = ElementBounds.Fixed(EnumDialogArea.CenterFixed, 86, 70, 160, 40);
            ElementBounds straightButton = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, 70, 160, 40);

            ElementBounds radiusLengthText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 140, 160, 40);
            ElementBounds climbDescentText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 210, 160, 40);

            ElementBounds radiusLengthSlider = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, 140, 460, 40);
            ElementBounds climbDescentSlider = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, 210, 460, 40);

            ElementBounds leftButton = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 280, 330, 40);
            ElementBounds rightButton = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, 280, 330, 40);

            ElementBounds sleeperText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 350, 160, 40);
            ElementBounds sleeperDropDown = ElementBounds.Fixed(EnumDialogArea.RightFixed, -80, 350, 400, 40);
            ElementBounds sleeperIcon = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, 346, 48, 48);

            ElementBounds railText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 420, 160, 40);
            ElementBounds railDropDown = ElementBounds.Fixed(EnumDialogArea.RightFixed, -80, 420, 400, 40);
            ElementBounds railIcon = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, 416, 48, 48);

            previewInventory[0].Itemstack = PreviewStack("sleeper", sleeperMaterial);
            previewInventory[1].Itemstack = PreviewStack("rail", railMaterial);

            string[] sleeperValues = BuildOptions((capi.World.GetItem(new AssetLocation("railworld", "sleeper")) as ItemSleeper)?.Materials, out string[] sleeperNames);
            string[] railValues = BuildOptions((capi.World.GetItem(new AssetLocation("railworld", "rail")) as ItemRail)?.Materials, out string[] railNames);




            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(leftColumn);
            SingleComposer = capi.Gui.CreateCompo("Train World: Selecting the type of rails to be placed", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddButton("Single block", OnClickSingleBlockButton, singleBlockButton, EnumButtonStyle.Normal, "SingleBlockButton")
            .AddButton("Turn 90 deg", OnClickTurnButton90, turnButton90, EnumButtonStyle.Normal,  "Turn90Button")
            .AddButton("Turn 45 deg", OnClickTurnButton45, turnButton45, EnumButtonStyle.Normal, "Turn45Button")
            .AddButton("Straight", OnClickStraightButton, straightButton, EnumButtonStyle.Normal, "StraightButton")

            .AddStaticText("Radius/Length", CairoFont.ButtonText(), radiusLengthText, "RadiusLengthText")
            .AddSlider(OnNewRadiusLengthSliderValue, radiusLengthSlider, "RadiusLengthSlider")

            .AddStaticText("Climb/Descent", CairoFont.ButtonText(), climbDescentText, "ClimbDescentText")
            .AddSlider(OnNewClimbDescentSliderValue, climbDescentSlider, "ClimbDescentSlider")
    
            .AddButton("Left", OnClickButtonLeft, leftButton, EnumButtonStyle.Normal, "LeftButton")
            .AddButton("Right", OnClickButtonRight, rightButton, EnumButtonStyle.Normal, "RightButton")

            .AddStaticText("Sleepers", CairoFont.ButtonText(), sleeperText, "SleeperText")
            .AddDropDown(sleeperValues, sleeperNames, Math.Max(0, Array.IndexOf(sleeperValues, sleeperMaterial)), OnSleeperSelected, sleeperDropDown, "SleeperDropDown")
            .AddPassiveItemSlot(sleeperIcon, previewInventory, previewInventory[0])

            .AddStaticText("Rails", CairoFont.ButtonText(), railText, "RailText")
            .AddDropDown(railValues, railNames, Math.Max(0, Array.IndexOf(railValues, railMaterial)), OnRailSelected, railDropDown, "RailDropDown")
            .AddPassiveItemSlot(railIcon, previewInventory, previewInventory[1]);
            SingleComposer.GetSlider("RadiusLengthSlider").SetValues(railLengRad, 4, 150, 1);
            SingleComposer.GetSlider("ClimbDescentSlider").SetValues(railClimDes, -20, 20, 1);
            SingleComposer.GetButton(railDirection + "Button").SetActive(true);
            SingleComposer.GetButton(railMode + "Button").SetActive(true);

            if(railMode== "SingleBlock") 
            {
                SingleComposer.GetButton("RightButton").Enabled = false;
                SingleComposer.GetButton("LeftButton").Enabled = false;
                SingleComposer.GetSlider("RadiusLengthSlider").Enabled = false;
                SingleComposer.GetSlider("ClimbDescentSlider").Enabled = false;
            }
            if (railMode == "Straight")
            {
                SingleComposer.GetButton("RightButton").Enabled = false;
                SingleComposer.GetButton("LeftButton").Enabled = false;
            }
            SingleComposer.Compose();

        }
       

        // Варіанти для списку: зверху «не будувати», далі всі матеріали предмета
        private static string[] BuildOptions(string[] materials, out string[] names)
        {
            var values = new List<string> { RailWorld.DontBuild };
            var nameList = new List<string> { "Не будувати" };

            if (materials != null)
            {
                foreach (string material in materials)
                {
                    values.Add(material);
                    string key = "material-" + material;
                    string name = Lang.Get(key);
                    nameList.Add(name == key ? material : name);
                }
            }

            names = nameList.ToArray();
            return values.ToArray();
        }

        // Предмет для слота поруч зі списком. Для «не будувати» слот порожній
        private ItemStack PreviewStack(string itemCode, string material)
        {
            Item item = capi.World.GetItem(new AssetLocation("railworld", itemCode));
            if (item == null || material == RailWorld.DontBuild) return null;

            ItemStack stack = new ItemStack(item);
            stack.Attributes.SetString("type", "normal");
            stack.Attributes.SetString("material", material);
            return stack;
        }

        private void OnSleeperSelected(string code, bool selected)
        {
            sleeperMaterial = code;
            previewInventory[0].Itemstack = PreviewStack("sleeper", code);
            UpdateRailMode();
        }

        private void OnRailSelected(string code, bool selected)
        {
            railMaterial = code;
            previewInventory[1].Itemstack = PreviewStack("rail", code);
            UpdateRailMode();
        }

        private bool OnNewRadiusLengthSliderValue(int i)
        {
            railLengRad = i;
            UpdateRailMode();
            return true;
        }

        private bool OnNewClimbDescentSliderValue(int i)
        {
            railClimDes = i;
            UpdateRailMode();
            return true;
        }

        private bool OnClickSingleBlockButton()
        {
            SingleComposer.GetButton("Turn90Button").SetActive(false);
            SingleComposer.GetButton("Turn45Button").SetActive(false);
            SingleComposer.GetButton("StraightButton").SetActive(false);
            SingleComposer.GetButton("SingleBlockButton").SetActive(true);

            SingleComposer.GetButton("RightButton").Enabled = false;
            SingleComposer.GetButton("LeftButton").Enabled = false;
            SingleComposer.GetSlider("RadiusLengthSlider").Enabled = false;
            SingleComposer.GetSlider("ClimbDescentSlider").Enabled = false;
            railMode = "SingleBlock";
            UpdateRailMode();
            return true;
        }

        private bool OnClickTurnButton90()
        {
            SingleComposer.GetButton("SingleBlockButton").SetActive(false);
            SingleComposer.GetButton("StraightButton").SetActive(false);
            SingleComposer.GetButton("Turn90Button").SetActive(true);
            SingleComposer.GetButton("Turn45Button").SetActive(false);

            SingleComposer.GetButton("RightButton").Enabled = true;
            SingleComposer.GetButton("LeftButton").Enabled = true;
            SingleComposer.GetSlider("RadiusLengthSlider").Enabled = true;
            SingleComposer.GetSlider("ClimbDescentSlider").Enabled = true;
            railMode = "Turn90";
            UpdateRailMode();
            return true;
        }

        private bool OnClickTurnButton45()
        {
            SingleComposer.GetButton("SingleBlockButton").SetActive(false);
            SingleComposer.GetButton("StraightButton").SetActive(false);
            SingleComposer.GetButton("Turn90Button").SetActive(false);
            SingleComposer.GetButton("Turn45Button").SetActive(true);

            SingleComposer.GetButton("RightButton").Enabled = true;
            SingleComposer.GetButton("LeftButton").Enabled = true;
            SingleComposer.GetSlider("RadiusLengthSlider").Enabled = true;
            SingleComposer.GetSlider("ClimbDescentSlider").Enabled = true;
            railMode = "Turn45";
            UpdateRailMode();
            return true;
        }

        private bool OnClickStraightButton()
        {
            SingleComposer.GetButton("SingleBlockButton").SetActive(false);
            SingleComposer.GetButton("Turn90Button").SetActive(false);
            SingleComposer.GetButton("Turn45Button").SetActive(false);
            SingleComposer.GetButton("StraightButton").SetActive(true);

            SingleComposer.GetButton("RightButton").Enabled = false;
            SingleComposer.GetButton("LeftButton").Enabled = false;
            SingleComposer.GetSlider("RadiusLengthSlider").Enabled = true;
            SingleComposer.GetSlider("ClimbDescentSlider").Enabled = true;
            railMode = "Straight";
            UpdateRailMode();
            return true;
        }

        private bool OnClickButtonLeft()
        {
            SingleComposer.GetButton("RightButton").SetActive(false);
            SingleComposer.GetButton("LeftButton").SetActive(true);
            railDirection = "Left";
            UpdateRailMode();
            return true;
        }

        private bool OnClickButtonRight()
        {
            SingleComposer.GetButton("LeftButton").SetActive(false);
            SingleComposer.GetButton("RightButton").SetActive(true);
            railDirection = "Right";
            UpdateRailMode();
            return true;
            
        }

        private void UpdateRailMode() 
        {
            
            ItemStack mystack = capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (mystack != null && mystack.Attributes != null && mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
            {
                RailMenuPacket packet = new RailMenuPacket();
                packet.railMode = railMode;
                packet.railLengRad = railLengRad;
                packet.railClimDes = railClimDes;
                packet.railDirection = railDirection;
                packet.sleeperMaterial = sleeperMaterial;
                packet.railMaterial = railMaterial;
                SendRailMenuPacket(capi.Network,packet);
            }
        }

        static void SendRailMenuPacket(IClientNetworkAPI cnapi, RailMenuPacket packet2) 
        {
            _cnapi = cnapi;
            cnapi.GetChannel("TWchannel").SendPacket<RailMenuPacket>(packet2);
        }

        public override bool TryOpen()
        {
            if (!base.TryOpen()) return false;
            ComposeDialog();
            //_timerId = capi.World.RegisterGameTickListener(_ => UpdateSomeValues(), 50);
            return true;
        }

        public override bool TryClose()
        {
           // capi.World.UnregisterGameTickListener(_timerId);
          //  InputText = "";
         //   NuggetsOutputText = "";
            return base.TryClose();
        }
    }
}
