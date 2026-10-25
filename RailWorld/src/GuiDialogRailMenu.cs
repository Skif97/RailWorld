using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
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
        public override string ToggleKeyCombinationCode => "openrailmenu";

        public GuiDialogRailMenu(ICoreClientAPI capi) : base(capi) 
        {
            previewInventory = new DummyInventory(capi, 3);
            ItemStack mystack = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (RailWorld.IsTrackTool(mystack))
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



        private void ComposeDialog()
        {
            
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            // Рядки меню йдуть один за одним з однаковим кроком
            double Row(int n) => MenuTop + n * MenuRowStep;
            // Підпис стоїть посередині рядка по висоті, як повзунок чи список поруч: гра малює текст від верху його
            // рамки, тож зсуваємо на половину різниці між висотою рядка і висотою тексту (висоту шрифту гра ділить на
            // масштаб інтерфейсу так само, GuiDialogBlockEntityFirepit)
            double labelDrop = (RowHeight - CairoFont.WhiteSmallishText().GetFontExtents().Height / RuntimeEnv.GUIScale) / 2;
            // Тло підганяється під цей прямокутник і додає до нього свій відступ з кожного боку. Елементи меню
            // розташовані від вікна: ліві від його лівого краю, праві від правого. Щоб і ті, і ті закінчувалися на
            // одній лінії, вікно має бути рівно на ширину рядків і по 20 з боків, тож відступ тла віднімаємо
            double padding = GuiStyle.ElementToDialogPadding;
            ElementBounds menuArea = ElementBounds.Fixed(0, 0, MenuWidth + 40 - 2 * padding, Row(MenuRows) - 2 * padding);

            ElementBounds radiusLengthText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, Row(2) + labelDrop, 200, RowHeight);
            ElementBounds climbDescentText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, Row(3) + labelDrop, 200, RowHeight);

            ElementBounds radiusLengthSlider = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, Row(2), 420, 40);
            ElementBounds climbDescentSlider = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, Row(3), 420, 40);

            ElementBounds leftButton = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, Row(4), 315, 40);
            ElementBounds rightButton = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, Row(4), 315, 40);

            ElementBounds sleeperText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, Row(5) + labelDrop, 160, RowHeight);
            ElementBounds sleeperDropDown = ElementBounds.Fixed(EnumDialogArea.RightFixed, -80, Row(5), 400, 40);
            ElementBounds sleeperIcon = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, Row(5) - 4, 48, 48);

            ElementBounds railText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, Row(6) + labelDrop, 160, RowHeight);
            ElementBounds railDropDown = ElementBounds.Fixed(EnumDialogArea.RightFixed, -80, Row(6), 400, 40);
            ElementBounds railIcon = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, Row(6) - 4, 48, 48);

            ElementBounds ballastText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, Row(7) + labelDrop, 160, RowHeight);
            ElementBounds ballastDropDown = ElementBounds.Fixed(EnumDialogArea.RightFixed, -80, Row(7), 400, 40);
            ElementBounds ballastIcon = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, Row(7) - 4, 48, 48);

            ElementBounds replaceText = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20, Row(8) + labelDrop, 400, RowHeight);
            ElementBounds replaceSwitch = ElementBounds.Fixed(EnumDialogArea.RightFixed, -20, Row(8), 40, 40);

            string[] ballastValues = BuildBallastOptions(out string[] ballastNames);
            previewInventory[2].Itemstack = BallastPreviewStack(ballastMaterial);

            previewInventory[0].Itemstack = PreviewStack("sleeper", sleeperMaterial);
            previewInventory[1].Itemstack = PreviewStack("rail", railMaterial);

            string[] sleeperValues = BuildOptions((capi.World.GetItem(new AssetLocation("railworld", "sleeper")) as ItemSleeper)?.Materials, out string[] sleeperNames);
            string[] railValues = BuildOptions((capi.World.GetItem(new AssetLocation("railworld", "rail")) as ItemRail)?.Materials, out string[] railNames);




            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(menuArea);
            GuiComposer composer = capi.Gui.CreateCompo(Lang.Get("railworld:menu-title"), dialogBounds)
                .AddShadedDialogBG(bgBounds);
            // Кнопки режимів двома рядками: перші ModesInFirstRow у першому, решта в другому. У рядку вони
            // однакові, з рівними проміжками, і разом займають ту саму ширину, що й рядки під ними (від 20 до 660)
            for (int i = 0; i < Modes.Length; i++)
            {
                string mode = Modes[i].Mode;
                bool first = i < ModesInFirstRow;
                int inRow = first ? ModesInFirstRow : Modes.Length - ModesInFirstRow;
                int place = first ? i : i - ModesInFirstRow;
                double modeWidth = (MenuWidth - (inRow - 1) * ModeGap) / inRow;
                ElementBounds bounds = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 20 + place * (modeWidth + ModeGap), Row(first ? 0 : 1), modeWidth, 40);
                composer.AddButton(Lang.Get(Modes[i].Label), () => SelectMode(mode), bounds, ButtonFont(), EnumButtonStyle.Normal, mode + "Button");
            }

            SingleComposer = composer
            .AddStaticText(Lang.Get("railworld:menu-radius-length"), CairoFont.WhiteSmallishText(), radiusLengthText, "RadiusLengthText")
            .AddSlider(OnNewRadiusLengthSliderValue, radiusLengthSlider, "RadiusLengthSlider")

            .AddStaticText(Lang.Get("railworld:menu-climb-descent"), CairoFont.WhiteSmallishText(), climbDescentText, "ClimbDescentText")
            .AddSlider(OnNewClimbDescentSliderValue, climbDescentSlider, "ClimbDescentSlider")
    
            .AddButton(Lang.Get("railworld:menu-left"), () => SelectDirection("Left"), leftButton, ButtonFont(), EnumButtonStyle.Normal, "LeftButton")
            .AddButton(Lang.Get("railworld:menu-right"), () => SelectDirection("Right"), rightButton, ButtonFont(), EnumButtonStyle.Normal, "RightButton")

            .AddStaticText(Lang.Get("railworld:menu-sleepers"), CairoFont.WhiteSmallishText(), sleeperText, "SleeperText")
            .AddDropDown(sleeperValues, sleeperNames, Math.Max(0, Array.IndexOf(sleeperValues, sleeperMaterial)), OnSleeperSelected, sleeperDropDown, "SleeperDropDown")
            .AddPassiveItemSlot(sleeperIcon, previewInventory, previewInventory[0])

            .AddStaticText(Lang.Get("railworld:menu-rails"), CairoFont.WhiteSmallishText(), railText, "RailText")
            .AddDropDown(railValues, railNames, Math.Max(0, Array.IndexOf(railValues, railMaterial)), OnRailSelected, railDropDown, "RailDropDown")
            .AddPassiveItemSlot(railIcon, previewInventory, previewInventory[1])

            .AddStaticText(Lang.Get("railworld:menu-ballast"), CairoFont.WhiteSmallishText(), ballastText, "BallastText")
            .AddDropDown(ballastValues, ballastNames, Math.Max(0, Array.IndexOf(ballastValues, ballastMaterial)), OnBallastSelected, ballastDropDown, "BallastDropDown")
            .AddPassiveItemSlot(ballastIcon, previewInventory, previewInventory[2])

            .AddStaticText(Lang.Get("railworld:menu-replace"), CairoFont.WhiteSmallishText(), replaceText, "ReplaceText")
            .AddSwitch(OnReplaceToggled, replaceSwitch, "ReplaceSwitch");
            SingleComposer.GetSwitch("ReplaceSwitch").SetValue(replaceBlocks);
            SingleComposer.GetSlider("RadiusLengthSlider").SetValues(railLengRad, 4, 150, 1);
            SingleComposer.GetSlider("ClimbDescentSlider").SetValues(railClimDes, -20, 20, 1);
            // У властивостях інструмента може лежати назва, якої меню не знає: тоді жодна кнопка не натиснута
            SingleComposer.GetButton(railDirection + "Button")?.SetActive(true);
            ShowMode(railMode);
            SingleComposer.Compose();

        }
       

        // Варіанти для списку: зверху «не будувати», далі всі матеріали предмета
        private static string[] BuildOptions(string[] materials, out string[] names)
        {
            var values = new List<string> { RailWorld.DontBuild };
            var nameList = new List<string> { Lang.Get("railworld:menu-dontbuild") };

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
            // Блоки гри за світ не міняються, тож перебираємо їх раз, а не на кожне відкриття меню
            string[][] options = ObjectCacheUtil.GetOrCreate(capi, "railworld-ballast-options", () => FindBallastOptions());
            names = options[1];
            return options[0];
        }

        // Варіанти підсипки і їхні назви: [0] значення, [1] назви
        private string[][] FindBallastOptions()
        {
            var values = new List<string> { RailWorld.DontBuild };
            var nameList = new List<string> { Lang.Get("railworld:menu-dontbuild") };

            foreach (Block block in capi.World.Blocks)
            {
                if (block?.Code == null) continue;
                ItemStack stack = new ItemStack(block);
                string rock = SectionHandler.GetGravelRock(stack);
                if (rock == null) continue;

                values.Add(rock);
                nameList.Add(block.GetHeldItemName(stack));
            }

            return new[] { values.ToArray(), nameList.ToArray() };
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

        // Шрифт кнопок: ігровий шрифт малих кнопок (колір і жирність кнопок гри) розміром із підписи меню
        private static CairoFont ButtonFont()
        {
            return CairoFont.SmallButtonText().WithFontSize((float)GuiStyle.SmallishFontSize);
        }

        // Режими меню: ключ підпису кнопки в lang, чи потрібен у режимі бік повороту (ліво чи право) і чи потрібні
        // повзунки довжини й підйому.
        // Кнопка режиму називається його кодом із «Button» в кінці
        private static readonly (string Mode, string Label, bool Side, bool Sliders)[] Modes =
        {
            (RoutePlanner.ModeCode, "railworld:menu-mode-points", false, false),
            ("SingleBlock", "railworld:menu-mode-singleblock", false, false),
            ("Straight", "railworld:menu-mode-straight", false, true),
            ("Turn90", "railworld:menu-mode-turn90", true, true),
            ("Turn45", "railworld:menu-mode-turn45", true, true)
        };

        // Скільки кнопок режимів у першому рядку: решта, повороти, у другому
        private const int ModesInFirstRow = 3;

        // Розмітка меню: верх першого рядка, крок між рядками, ширина рядка і проміжок між кнопками режимів
        private const double MenuTop = 70, MenuRowStep = 70, MenuWidth = 640, ModeGap = 10;
        // Висота кнопки, повзунка чи списку в рядку
        private const double RowHeight = 40;
        // Скільки рядків у меню: два рядки режимів, повзунки, бік, три списки і перемикач
        private const int MenuRows = 9;

        // Найбільша висота розгорнутого списку, як у гри (GuiElementListMenu.MaxHeight)
        private const int DropDownMaxHeight = 350;
        private static readonly string[] DropDowns = { "SleeperDropDown", "RailDropDown", "BallastDropDown" };

        // Список розгортається вниз від клацання. Гра обмежує його висоту місцем під ним, але рахує це місце
        // разом зі зсувом вікна (GuiElementListMenu.ComposeDynamicElements), тож у вікні не вгорі екрана він
        // вилазить за низ. Перед кожним клацанням кажемо кожному списку, скільки місця під ним насправді; коли
        // список вищий, гра сама додає йому прокрутку
        public override void OnMouseDown(MouseEvent args)
        {
            if (SingleComposer != null)
            {
                foreach (string key in DropDowns)
                {
                    GuiElementDropDown dropDown = SingleComposer.GetDropDown(key);
                    if (dropDown?.listMenu == null) continue;
                    double below = (capi.Render.FrameHeight - dropDown.Bounds.renderY - dropDown.Bounds.OuterHeight) / RuntimeEnv.GUIScale - 10;
                    dropDown.listMenu.MaxHeight = (int)Math.Max(60, Math.Min(below, DropDownMaxHeight));
                }
            }
            base.OnMouseDown(args);
        }

        // Показує в меню режим mode: його кнопка натиснута, решта ні, а бік і повзунки доступні, лише якщо вони
        // в цьому режимі потрібні. Режиму, якого меню не знає (у властивостях інструмента може лежати будь-що),
        // не натиснуто, і доступне все
        private void ShowMode(string mode)
        {
            bool side = true, sliders = true;
            foreach (var known in Modes)
            {
                bool chosen = known.Mode == mode;
                SingleComposer.GetButton(known.Mode + "Button").SetActive(chosen);
                if (!chosen) continue;
                side = known.Side;
                sliders = known.Sliders;
            }
            SingleComposer.GetButton("RightButton").Enabled = side;
            SingleComposer.GetButton("LeftButton").Enabled = side;
            SingleComposer.GetSlider("RadiusLengthSlider").Enabled = sliders;
            SingleComposer.GetSlider("ClimbDescentSlider").Enabled = sliders;
        }

        private bool SelectMode(string mode)
        {
            railMode = mode;
            ShowMode(mode);
            UpdateRailMode();
            return true;
        }

        private bool SelectDirection(string direction)
        {
            SingleComposer.GetButton("LeftButton").SetActive(direction == "Left");
            SingleComposer.GetButton("RightButton").SetActive(direction == "Right");
            railDirection = direction;
            UpdateRailMode();
            return true;
        }

        private void UpdateRailMode() 
        {
            
            ItemStack mystack = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (RailWorld.IsTrackTool(mystack))
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
            cnapi.GetChannel("TWchannel").SendPacket<RailMenuPacket>(packet2);
        }

        public override bool TryOpen()
        {
            if (!base.TryOpen()) return false;
            ComposeDialog();
            return true;
        }

    }
}
