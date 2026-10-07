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
            if (stack == null || part == SectionPart.Whole) return false;
            if (part == SectionPart.Ballast) return GetGravelRock(stack) != null;
            if (part == SectionPart.Sleeper) return stack.Item is ItemSleeper;
            return stack.Item is ItemRail;
        }

        /// <summary>
        /// Порода гравію, якщо в стаку звичайний блок гравію з гри, інакше null. Таким блоком роблять підсипку.
        /// </summary>
        public static string GetGravelRock(ItemStack stack)
        {
            Block block = stack?.Block;
            if (block?.Code == null || block.Code.Domain != "game") return null;

            string rock = block.Variant?["rock"];
            return rock != null && block.Code.Path == "gravel-" + rock ? rock : null;
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
            if (sel.Part == SectionPart.Switch)
            {
                if (world.Side == EnumAppSide.Server) ToggleSwitch(world, sel);
                return true;
            }

            if (sel.Section.IsInstalled(sel.Part)) return false;

            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (!CanInstall(slot?.Itemstack, sel.Part)) return false;

            if (world.Side == EnumAppSide.Server) InstallPart(world, byPlayer, sel, slot);
            return true;
        }

        /// <summary>
        /// Переводить стрілку секції на іншу гілку. Викликається на сервері. Якщо стрілки на обох кінцях секції,
        /// переводиться та, до важеля якої гравець ближче цілився.
        /// </summary>
        protected virtual void ToggleSwitch(IWorldAccessor world, SectionSelection sel)
        {
            DataInChunk data = DataInChunk.Get(world, sel.Chunk);
            if (data == null || !data.RailWaySections.TryGetValue(sel.Index, out Section section)) return;

            bool atStart;
            if (section.HasSwitch(true) && section.HasSwitch(false))
            {
                Vec3d hit = sel.HitPosition ?? section.GetGlobalPos();
                atStart = hit.SquareDistanceTo(SectionPicker.LeverPosition(section, true)) < hit.SquareDistanceTo(SectionPicker.LeverPosition(section, false));
            }
            else if (section.HasSwitch(true)) atStart = true;
            else if (section.HasSwitch(false)) atStart = false;
            else return;

            section.ToggleSwitch(atStart, world.ElapsedMilliseconds);
            DataInChunk.Save(world, sel.Chunk, data);
            RailWorld.SendChunkDataToClients(sel.Chunk, data);

            Vec3d lever = SectionPicker.LeverPosition(section, atStart);
            world.PlaySoundAt(new AssetLocation("game:sounds/toggleswitch"), lever.X, lever.Y, lever.Z, null, true, 16);
        }

        /// <summary>
        /// Встановлює деталь із предмета в руці. Викликається на сервері: записує тип і матеріал предмета
        /// в секцію, забирає один предмет і розсилає зміни.
        /// </summary>
        protected virtual void InstallPart(IWorldAccessor world, IPlayer byPlayer, SectionSelection sel, ItemSlot slot)
        {
            DataInChunk data = DataInChunk.Get(world, sel.Chunk);
            if (data == null || !data.RailWaySections.TryGetValue(sel.Index, out Section section)) return;

            // Чужий блок під секцією могли вже прибрати, тому перевіряємо щоразу заново
            if (!TrackBed.CanAttach(world, section))
            {
                if (!section.Blocked)
                {
                    section.Blocked = true;
                    DataInChunk.Save(world, sel.Chunk, data);
                    RailWorld.SendChunkDataToClients(sel.Chunk, data);
                }
                (byPlayer as Vintagestory.API.Server.IServerPlayer)?.SendIngameError("railblocked", "Під колією стоять блоки, спершу приберіть їх");
                return;
            }
            section.Blocked = false;

            ItemStack stack = slot.Itemstack;
            bool ballast = sel.Part == SectionPart.Ballast;
            string type = stack.Attributes.GetString("type", "normal");
            string material = ballast
                ? GetGravelRock(stack)
                : stack.Attributes.GetString("material", sel.Part == SectionPart.Sleeper ? "oak" : "iron");

            // Скільки гравію вже лежить під секцією: сусідні секції могли засипати спільні блоки
            int voxelsBefore = ballast ? TrackBed.CountVoxels(world, section) : 0;

            if (!section.InstallPart(sel.Part, type, material)) return;

            DataInChunk.Save(world, sel.Chunk, data);
            RailWorld.SendChunkDataToClients(sel.Chunk, data);

            // З першою деталлю секція займає свої клітинки, з наступними оновлює їхню форму
            TrackBed.Sync(world, section);

            // Рахуємо досипане до того, як позначати сусідів: за їхній гравій гравець не платить
            int voxelsAdded = ballast ? TrackBed.CountVoxels(world, section) - voxelsBefore : 0;
            if (ballast) MarkCoveredNeighbours(world, section, material);

            if (byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative) return;

            if (ballast)
            {
                ChargeGravel(byPlayer, slot, voxelsAdded);
            }
            else
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }
        }

        // Скільки секцій у кожен бік переглядати після засипання однієї
        protected const int NeighbourScanLimit = 8;

        /// <summary>
        /// Після засипання секції дивиться на сусідів уздовж колії. Блоки засипаються цілком, тому сусідній секції
        /// часто вже нічого досипати: тоді вона одразу позначається як така, що має підсипку, і гравцеві
        /// не треба клацати по ній окремо. Іде в обидва боки, доки сусіди покриті повністю.
        /// </summary>
        protected virtual void MarkCoveredNeighbours(IWorldAccessor world, Section section, string material)
        {
            RailDataSession session = new RailDataSession(world);
            var marked = new List<Section>();

            for (int end = 0; end < 2; end++)
            {
                Section current = section;
                bool atStart = end == 0;

                for (int step = 0; step < NeighbourScanLimit; step++)
                {
                    SectionStep next = SectionLinker.GetNext(session, current, atStart);
                    if (next == null) break;

                    Section neighbour = next.Section;
                    if (neighbour.BallastInstalled || neighbour.Blocked) break;
                    if (!TrackBed.IsBallastCovered(world, neighbour)) break;

                    neighbour.InstallPart(SectionPart.Ballast, "normal", material);
                    session.MarkDirty(neighbour.ChunkAddres);
                    marked.Add(neighbour);

                    // Далі виходимо з сусіда через його протилежний кінець
                    current = neighbour;
                    atStart = !next.EnterAtStart;
                }
            }

            if (session.HasChanges) session.SaveAll();

            // Позначені секції тепер тримають свої блоки нарівні з тією, що їх засипала
            foreach (Section neighbour in marked) TrackBed.Sync(world, neighbour);
        }

        /// <summary>
        /// Списує гравій за досипаний об'єм. Об'єм рахується у вокселях, а блок гравію це ціла їх пачка,
        /// тому залишок від розпочатого блока зберігається за гравцем і витрачається наступними секціями.
        /// </summary>
        protected virtual void ChargeGravel(IPlayer byPlayer, ItemSlot slot, int voxels)
        {
            if (voxels <= 0) return;

            const string key = "railworldGravelVoxels";
            int bank = byPlayer.Entity.WatchedAttributes.GetInt(key) - voxels;

            while (bank < 0 && slot.StackSize > 0)
            {
                slot.TakeOut(1);
                bank += TrackBed.VoxelsPerBlock;
            }
            if (bank < 0) bank = 0;

            byPlayer.Entity.WatchedAttributes.SetInt(key, bank);
            slot.MarkDirty();
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
            // Важіль стрілки не знімається: він зникає сам разом із відгалуженням
            if (sel.Part == SectionPart.Switch) return float.MaxValue;
            // Секція цілком знімається інструментом одразу
            if (sel.Part == SectionPart.Whole) return 0f;
            return sel.Part == SectionPart.Sleeper || sel.Part == SectionPart.Ballast ? 1.5f : 3f;
        }

        /// <summary>
        /// Викликається на клієнті, поки гравець ламає деталь у виживанні. Повертає залишок опору.
        /// </summary>
        public virtual float OnGettingBroken(IPlayer player, SectionSelection sel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
        {
            if (sel.Part == SectionPart.Switch) return remainingResistance;
            return remainingResistance - dt;
        }

        /// <summary>
        /// Предмети за зняту деталь: той самий тип і матеріал, з якими її встановили.
        /// Для секції цілком це предмети всіх її встановлених деталей.
        /// </summary>
        public virtual ItemStack[] GetDrops(IWorldAccessor world, SectionSelection sel, IPlayer byPlayer)
        {
            var drops = new List<ItemStack>();
            if (sel.Part == SectionPart.Switch) return drops.ToArray();

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
            // Гравій предметом не повертається: він лишається у світі шаром, коли блок колії звільняється
            if (part == SectionPart.Ballast) return null;

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
            // У креативі ламається все одразу, але важіль стрілки зламати не можна
            if (sel.Part == SectionPart.Switch) return;

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

            // Секції більше немає або в ній не лишилося деталей: звільняємо клітинки. Інакше оновлюємо їхню форму
            if (whole) TrackBed.Detach(world, section);
            else TrackBed.Sync(world, section);

            if (drops == null || byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative) return;
            foreach (ItemStack stack in drops)
                world.SpawnItemEntity(stack, sel.HitPosition);
        }
    }
}
