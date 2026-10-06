using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using RailWorld.src.Items;
using RailWorld.src.RailWay;

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
        string ballastMaterial;
        bool replaceBlocks;

        // Слоти лише для показу вибраної шпали і рейки поруч зі списками
        DummyInventory previewInventory;
        static IClientNetworkAPI _cnapi;
        static ICoreClientAPI _capi;
        public override string ToggleKeyCombinationCode => "openrailmenu";

        public GuiDialogRailMenu(ICoreClientAPI capi) : base(capi) 
        {
            _capi = capi;
            previewInventory = new DummyInventory(capi, 3);
           ItemStack mystack = capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (mystack != null && mystack.Attributes != null && mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
            {
                railMode = mystack.Attributes.GetString("railMode", "SingleBlock");
                railLengRad = mystack.Attributes.GetInt("railLengRad", 30);
                railClimDes = mystack.Attributes.GetInt("railClimDes", 0);
                railDirection = mystack.Attributes.GetString("railDirection", "Left");
                sleeperMaterial = mystack.Attributes.GetString("sleeperMaterial", "oak");
                railMaterial = mystack.Attributes.GetString("railMaterial", "iron");
                ballastMaterial = mystack.Attributes.GetString("ballastMaterial", RailWorld.DontBuild);
                replaceBlocks = mystack.Attributes.GetBool("replaceBlocks");

            }
        }

       // private string TitleRailMenu => Lang.Get("Train World: Selecting the type of rails to be placed");


        private void ComposeDialog()
        {
            
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds leftColumn = ElementBounds.Fixed(0, 200, 680, 480);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            ElementBounds pointsButton = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 20, 640, 40);
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

            ElementBounds ballastText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 490, 160, 40);
            ElementBounds ballastDropDown = ElementBounds.Fixed(EnumDialogArea.RightFixed, -80, 490, 400, 40);
            ElementBounds ballastIcon = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, 486, 48, 48);

            ElementBounds replaceText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, 560, 400, 40);
            ElementBounds replaceSwitch = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, 560, 40, 40);

            string[] ballastValues = BuildBallastOptions(out string[] ballastNames);
            previewInventory[2].Itemstack = BallastPreviewStack(ballastMaterial);

            previewInventory[0].Itemstack = PreviewStack("sleeper", sleeperMaterial);
            previewInventory[1].Itemstack = PreviewStack("rail", railMaterial);

            string[] sleeperValues = BuildOptions((capi.World.GetItem(new AssetLocation("railworld", "sleeper")) as ItemSleeper)?.Materials, out string[] sleeperNames);
            string[] railValues = BuildOptions((capi.World.GetItem(new AssetLocation("railworld", "rail")) as ItemRail)?.Materials, out string[] railNames);




            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(leftColumn);
            SingleComposer = capi.Gui.CreateCompo("Train World: Selecting the type of rails to be placed", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddButton("By points", OnClickPointsButton, pointsButton, EnumButtonStyle.Normal, "PointsButton")
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
            .AddPassiveItemSlot(railIcon, previewInventory, previewInventory[1])

            .AddStaticText("Ballast", CairoFont.ButtonText(), ballastText, "BallastText")
            .AddDropDown(ballastValues, ballastNames, Math.Max(0, Array.IndexOf(ballastValues, ballastMaterial)), OnBallastSelected, ballastDropDown, "BallastDropDown")
            .AddPassiveItemSlot(ballastIcon, previewInventory, previewInventory[2])

            .AddStaticText("Replace blocks in the way", CairoFont.ButtonText(), replaceText, "ReplaceText")
            .AddSwitch(OnReplaceToggled, replaceSwitch, "ReplaceSwitch");
            SingleComposer.GetSwitch("ReplaceSwitch").SetValue(replaceBlocks);
            SingleComposer.GetSlider("RadiusLengthSlider").SetValues(railLengRad, 4, 150, 1);
            SingleComposer.GetSlider("ClimbDescentSlider").SetValues(railClimDes, -20, 20, 1);
            SingleComposer.GetButton(railDirection + "Button").SetActive(true);
            SingleComposer.GetButton(railMode + "Button").SetActive(true);

            if(railMode== "SingleBlock" || railMode == RoutePlanner.ModeCode) 
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

        // Варіанти підсипки: зверху «не будувати», далі всі породи, з яких у грі буває гравій
        private string[] BuildBallastOptions(out string[] names)
        {
            var values = new List<string> { RailWorld.DontBuild };
            var nameList = new List<string> { "Не будувати" };

            foreach (Block block in capi.World.Blocks)
            {
                if (block?.Code == null) continue;
                ItemStack stack = new ItemStack(block);
                string rock = SectionHandler.GetGravelRock(stack);
                if (rock == null) continue;

                values.Add(rock);
                nameList.Add(block.GetHeldItemName(stack));
            }

            names = nameList.ToArray();
            return values.ToArray();
        }

        private ItemStack BallastPreviewStack(string rock)
        {
            if (rock == RailWorld.DontBuild) return null;
            Block block = capi.World.GetBlock(new AssetLocation("game", "gravel-" + rock));
            return block == null ? null : new ItemStack(block);
        }

        private void OnReplaceToggled(bool on)
        {
            replaceBlocks = on;
            UpdateRailMode();
        }

        private void OnBallastSelected(string code, bool selected)
        {
            ballastMaterial = code;
            previewInventory[2].Itemstack = BallastPreviewStack(code);
            UpdateRailMode();
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

        // Маршрут по точках: радіус, підйом і сторона тут не потрібні, форму задають самі точки
        private bool OnClickPointsButton()
        {
            SingleComposer.GetButton("SingleBlockButton").SetActive(false);
            SingleComposer.GetButton("Turn90Button").SetActive(false);
            SingleComposer.GetButton("Turn45Button").SetActive(false);
            SingleComposer.GetButton("StraightButton").SetActive(false);
            SingleComposer.GetButton("PointsButton").SetActive(true);

            SingleComposer.GetButton("RightButton").Enabled = false;
            SingleComposer.GetButton("LeftButton").Enabled = false;
            SingleComposer.GetSlider("RadiusLengthSlider").Enabled = false;
            SingleComposer.GetSlider("ClimbDescentSlider").Enabled = false;
            railMode = RoutePlanner.ModeCode;
            UpdateRailMode();
            return true;
        }

        private bool OnClickSingleBlockButton()
        {
            SingleComposer.GetButton("PointsButton").SetActive(false);
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
            SingleComposer.GetButton("PointsButton").SetActive(false);
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
            SingleComposer.GetButton("PointsButton").SetActive(false);
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
            SingleComposer.GetButton("PointsButton").SetActive(false);
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
                packet.ballastMaterial = ballastMaterial;
                packet.replaceBlocks = replaceBlocks;
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
