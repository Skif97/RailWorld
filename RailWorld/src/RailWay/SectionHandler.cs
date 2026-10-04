using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using RailWorld.src.Items;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Поведінка секцій колії при взаємодії гравця. Те саме, що клас Block для блоків:
    /// методи викликаються і на клієнті, і на сервері, і призначені для перевизначення.
    /// Активний екземпляр лежить у RailWaySystem.Handler.
    /// </summary>
    public class SectionHandler
    {
        protected ICoreAPI api;

        /// <summary>
        /// Якщо true, із затиснутим Shift секція отримує правий клік раніше за предмет у руці.
        /// </summary>
        public virtual bool PlacedPriorityInteract => false;

        public virtual void OnLoaded(ICoreAPI api)
        {
            this.api = api;
        }

        /// <summary>
        /// Бокси виділення секції. Кожен бокс несе деталь, яку він представляє, і чи вона встановлена.
        /// </summary>
        public virtual void GetSelectionBoxes(List<SectionBox> boxes, Vec3i chunkCoord, int index, Section section)
        {
            SectionPicker.AddSectionBoxes(boxes, chunkCoord, index, section);
        }

        /// <summary>
        /// Чи можна предметом із руки встановити цю деталь. Поки це так, порожні місця під деталь
        /// підсвічуються і виділяються.
        /// </summary>
        public virtual bool CanInstall(ItemStack stack, SectionPart part)
        {
            if (stack?.Item == null || part == SectionPart.Whole) return false;
            if (part == SectionPart.Sleeper) return stack.Item is ItemSleeper;
            return stack.Item is ItemRail;
        }

        /// <summary>
        /// Чи предмет у руці показує всі порожні місця під деталі в завантажених чанках, а не лише поблизу.
        /// Стандартно це блок, яким прокладають колію.
        /// </summary>
        public virtual bool ShowsAllEmptyParts(ItemStack stack)
        {
            return stack?.Block is BlockRail;
        }

        /// <summary>
        /// Чи предмет у руці працює із секцією цілком: виділяє її одним великим боксом,
        /// а лівий клік видаляє секцію повністю. Стандартно це блок, яким прокладають колію.
        /// </summary>
        public virtual bool IsSectionTool(ItemStack stack)
        {
            return stack?.Block is BlockRail;
        }

        /// <summary>
        /// Колір каркаса виділення. null означає стандартний.
        /// </summary>
        public virtual Vec4f GetSelectionColor(ICoreClientAPI capi, SectionSelection sel)
        {
            return null;
        }

        /// <summary>
        /// Викликається на клієнті щокадру, поки гравець дивиться на деталь.
        /// </summary>
        public virtual void OnBeingLookedAt(IPlayer byPlayer, SectionSelection sel, bool firstTick)
        {
        }

        /// <summary>
        /// Правий клік по деталі. Повернути true, щоб почати взаємодію (далі підуть Step і Stop або Cancel).
        /// Стандартно: якщо деталі немає, а в руці підхожий предмет, встановлює її.
        /// </summary>
        public virtual bool OnSectionInteractStart(IWorldAccessor world, IPlayer byPlayer, SectionSelection sel)
        {
            if (sel.Section.IsInstalled(sel.Part)) return false;

            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (!CanInstall(slot?.Itemstack, sel.Part)) return false;

            if (world.Side == EnumAppSide.Server) InstallPart(world, byPlayer, sel, slot);
            return true;
        }

        /// <summary>
        /// Встановлює деталь із предмета в руці. Викликається на сервері: записує тип і матеріал предмета
        /// в секцію, забирає один предмет і розсилає зміни.
        /// </summary>
        protected virtual void InstallPart(IWorldAccessor world, IPlayer byPlayer, SectionSelection sel, ItemSlot slot)
        {
            DataInChunk data = DataInChunk.Get(world, sel.Chunk);
            if (data == null || !data.RailWaySections.TryGetValue(sel.Index, out Section section)) return;

            ItemStack stack = slot.Itemstack;
            string type = stack.Attributes.GetString("type", "normal");
            string material = stack.Attributes.GetString("material", sel.Part == SectionPart.Sleeper ? "oak" : "iron");
            if (!section.InstallPart(sel.Part, type, material)) return;

            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }

            DataInChunk.Save(world, sel.Chunk, data);
            RailWorld.SendChunkDataToClients(sel.Chunk, data);
        }

        /// <summary>
        /// Поки затиснута права кнопка. Повернути false, щоб завершити взаємодію.
        /// </summary>
        public virtual bool OnSectionInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, SectionSelection sel)
        {
            return false;
        }

        public virtual void OnSectionInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, SectionSelection sel)
        {
        }

        /// <summary>
        /// Взаємодію перервано (відпустили кнопку або відвели погляд). Повернути false, щоб не дати її перервати.
        /// </summary>
        public virtual bool OnSectionInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, SectionSelection sel, EnumItemUseCancelReason cancelReason)
        {
            return true;
        }

        /// <summary>
        /// Скільки секунд треба ламати деталь голими руками.
        /// </summary>
        public virtual float GetResistance(IWorldAccessor world, SectionSelection sel)
        {
            // Секція цілком знімається інструментом одразу
            if (sel.Part == SectionPart.Whole) return 0f;
            return sel.Part == SectionPart.Sleeper ? 1.5f : 3f;
        }

        /// <summary>
        /// Викликається на клієнті, поки гравець ламає деталь у виживанні. Повертає залишок опору.
        /// </summary>
        public virtual float OnGettingBroken(IPlayer player, SectionSelection sel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
        {
            return remainingResistance - dt;
        }

        /// <summary>
        /// Предмети за зняту деталь: той самий тип і матеріал, з якими її встановили.
        /// Для секції цілком це предмети всіх її встановлених деталей.
        /// </summary>
        public virtual ItemStack[] GetDrops(IWorldAccessor world, SectionSelection sel, IPlayer byPlayer)
        {
            var drops = new List<ItemStack>();

            if (sel.Part == SectionPart.Whole)
            {
                foreach (SectionPart part in new SectionPart[] { SectionPart.Sleeper, SectionPart.FirstRail, SectionPart.SecondRail })
                {
                    if (!sel.Section.IsInstalled(part)) continue;
                    ItemStack partStack = CreatePartStack(world, sel.Section, part);
                    if (partStack != null) drops.Add(partStack);
                }
            }
            else
            {
                ItemStack stack = CreatePartStack(world, sel.Section, sel.Part);
                if (stack != null) drops.Add(stack);
            }

            return drops.ToArray();
        }

        protected virtual ItemStack CreatePartStack(IWorldAccessor world, Section section, SectionPart part)
        {
            bool sleeper = part == SectionPart.Sleeper;
            Item item = world.GetItem(new AssetLocation("railworld", sleeper ? "sleeper" : "rail"));
            if (item == null) return null;

            ItemStack stack = new ItemStack(item);
            stack.Attributes.SetString("type", section.GetPartType(part) ?? "normal");
            stack.Attributes.SetString("material", section.GetMaterial(part) ?? (sleeper ? "oak" : "iron"));
            return stack;
        }

        /// <summary>
        /// Деталь зламано. Викликається на сервері: знімає деталь, розсилає зміни і кидає предмети.
        /// Секція без деталей лишається, щоб їх можна було поставити назад;
        /// повністю її прибирає лише злам секції цілком предметом для прокладання колії.
        /// </summary>
        public virtual void OnSectionBroken(IWorldAccessor world, SectionSelection sel, IPlayer byPlayer)
        {
            if (world.Side != EnumAppSide.Server) return;

            RailDataSession session = new RailDataSession(world);
            DataInChunk data = session.Get(sel.Chunk);
            if (data == null || !data.RailWaySections.TryGetValue(sel.Index, out Section section)) return;

            bool whole = sel.Part == SectionPart.Whole;
            if (whole)
            {
                if (!IsSectionTool(byPlayer?.InventoryManager.ActiveHotbarSlot?.Itemstack)) return;
            }
            else if (!section.IsInstalled(sel.Part)) return;

            sel.Section = section;
            ItemStack[] drops = GetDrops(world, sel, byPlayer);

            if (whole)
            {
                // Сусіди більше не мають на неї посилатися
                SectionLinker.Unlink(session, section);
                data.RailWaySections.Remove(sel.Index);
            }
            else section.RemovePart(sel.Part);

            session.MarkDirty(sel.Chunk);
            session.SaveAll();

            if (drops == null || byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative) return;
            foreach (ItemStack stack in drops)
                world.SpawnItemEntity(stack, sel.HitPosition);
        }
    }
}
