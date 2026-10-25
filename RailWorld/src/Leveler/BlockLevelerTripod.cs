using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace RailWorld
{
    /// <summary>
    /// Тринога і тринога з приладом на ній (нівелір, теодоліт). Малювання з матеріалів крафту, кут постановки і два
    /// блоки заввишки дають ванільні поведінки TypedTexture, RotateablePlaceable і Multiblock (див. json); тут лише
    /// своє: матеріали з крафту, збирання й розбирання в світі, падіння з матеріалами і прилад як інструмент колії.
    /// </summary>
    public class BlockLevelerTripod : Block
    {
        // Тринога без приладу: на неї ставлять прилад, і нею стає тринога, з якої прилад зняли
        private static readonly AssetLocation BareTripodCode = new AssetLocation("railworld", "tripod");

        /// <summary>Прилад, що стоїть на цій тринозі (атрибут instrument у json); null у триноги без приладу.</summary>
        public AssetLocation Instrument { get; private set; }

        /// <summary>Тринога з приладом, яким прокладають колію (теодоліт). Задає атрибут trackTool у json.</summary>
        public bool IsTrackTool { get; private set; }

        // Яким блоком стає тринога, коли на неї ставлять прилад: кожна тринога з приладом сама каже, який прилад на ній
        private Dictionary<AssetLocation, BlockLevelerTripod> mountedByInstrument;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            string instrument = Attributes?["instrument"].AsString();
            Instrument = instrument == null ? null : new AssetLocation(instrument);
            IsTrackTool = Attributes?["trackTool"].AsBool() == true;
        }

        // Тринога з тим приладом, що в руці гравця; null, якщо в руці не прилад
        private BlockLevelerTripod MountedFor(IPlayer player)
        {
            if (mountedByInstrument == null)
            {
                mountedByInstrument = new Dictionary<AssetLocation, BlockLevelerTripod>();
                foreach (Block block in api.World.Blocks)
                {
                    if (block is BlockLevelerTripod mounted && mounted.Instrument != null) mountedByInstrument[mounted.Instrument] = mounted;
                }
            }
            AssetLocation held = player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible.Code;
            return held != null && mountedByInstrument.TryGetValue(held, out BlockLevelerTripod found) ? found : null;
        }

        // ── Малювання ────────────────────────────────────────────────────────

        /// <summary>
        /// Меш форми shape з матеріалами стека: текстури за матеріалами дає ванільне джерело TypedTextureSource за
        /// textureMapping з json, як шафа малює себе в світі. Цим малюються і поставлений блок, і предмет у меню.
        /// </summary>
        public MeshData GenMesh(ICoreClientAPI capi, ITesselatorAPI tesselator, AssetLocation shapeLocation, ItemStack stack)
        {
            CollectibleBehaviorTypedTexture typed = GetCollectibleBehavior<CollectibleBehaviorTypedTexture>(true);
            Shape shape = capi.TesselatorManager.GetCachedShape(shapeLocation);
            if (typed == null || shape == null) return null;

            var source = new TypedTextureSource(capi, capi.BlockTextureAtlas, Code.Domain, typed.TextureMapping, typed.GetMaterials(stack));
            tesselator.TesselateShape("leveler tripod", shape, out MeshData mesh, source);
            return mesh;
        }

        private string GuiMeshesKey => "railworldGuiMeshes-" + Code;

        // У меню тринога розкладена, як стоїть у світі. У руках і на землі складена: це малює TypedTexture формою для
        // рук з json
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            CollectibleBehaviorTypedTexture typed = GetCollectibleBehavior<CollectibleBehaviorTypedTexture>(true);
            if (target != EnumItemRenderTarget.Gui || typed == null)
            {
                base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
                return;
            }

            var meshes = ObjectCacheUtil.GetOrCreate(capi, GuiMeshesKey, () => new Dictionary<string, MultiTextureMeshRef>());
            string key = string.Join("|", typed.GetMaterials(itemstack).Values);
            if (!meshes.TryGetValue(key, out MultiTextureMeshRef meshRef))
            {
                MeshData mesh = GenMesh(capi, capi.Tesselator, Shape.Base, itemstack);
                if (mesh == null)
                {
                    base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
                    return;
                }
                meshRef = meshes[key] = capi.Render.UploadMultiTextureMesh(mesh);
            }
            renderinfo.ModelRef = meshRef;
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            if (api is ICoreClientAPI capi && capi.ObjectCache.TryGetValue(GuiMeshesKey, out object cached))
            {
                foreach (MultiTextureMeshRef meshRef in ((Dictionary<string, MultiTextureMeshRef>)cached).Values) meshRef.Dispose();
                capi.ObjectCache.Remove(GuiMeshesKey);
            }
        }

        // ── Інструмент колії ─────────────────────────────────────────────────

        // Правий клік інструментом колії в руці колію не кладе: він ставить точку розмітки або готову форму з меню, а
        // кладе колію сервер, коли гравець підтвердить перегляд. Сам інструмент у світ ставиться з Shift
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!IsTrackTool || byEntity.Controls.ShiftKey)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            handling = EnumHandHandling.PreventDefault;
            // Лише перший кадр натискання: утримання кнопки нових точок не ставить
            if (api.Side == EnumAppSide.Client && firstEvent) RoutePlanner.Instance?.OnRightClick();
        }

        // Інструментом колії в руці блоки не ламаються. Для секції blockSel дорівнює null, тому видалення секцій цим самим
        // предметом працює далі
        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            if (!IsTrackTool)
            {
                base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
                return;
            }
            // Поки триває розмітка маршруту, лівий клік це крок назад, а не видалення секції
            if (api.Side == EnumAppSide.Client && RoutePlanner.Instance?.OnLeftClick() == true)
            {
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }
            if (blockSel != null)
            {
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
        }

        // ── Кліки по поставленій тринозі ─────────────────────────────────────

        /// <summary>Блок, яким керується тринога: сама тринога, навіть якщо клікнули по її верхній клітинці.</summary>
        public static BlockPos ControlPos(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return blockAccessor.GetBlock(pos) is IMultiblockOffset upper ? upper.GetControlBlockPos(pos) : pos;
        }

        /// <summary>
        /// Що робить клік по поставленій тринозі. Правий і лівий клік повертають на крок у той чи інший бік. З Alt лівий
        /// клік ламає, тобто забирає триногу цілою разом з приладом, а правий знімає прилад у руку. З Shift і з
        /// приладом у руці по тринозі без приладу клік не наш: Shift лишено для меню приладу, а прилад ставиться на
        /// триногу звичайною взаємодією з блоком. null означає, що клік робить те, що зробила б гра.
        /// </summary>
        public LevelerAction? ClickAction(IPlayer player, bool left, bool alt)
        {
            if (player.Entity.Controls.ShiftKey) return null;
            if (alt)
            {
                if (left || Instrument == null) return null;
                return LevelerAction.TakeInstrument;
            }
            if (!left && Instrument == null && MountedFor(player) != null) return null;
            return left ? LevelerAction.RotateLeft : LevelerAction.RotateRight;
        }

        /// <summary>
        /// Сервер виконує клік, який надіслав клієнт: повертає триногу або знімає прилад у порожню руку.
        /// </summary>
        public void DoAction(IWorldAccessor world, IPlayer player, BlockPos pos, LevelerAction action)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityLevelerTripod be) return;
            switch (action)
            {
                case LevelerAction.RotateLeft:
                case LevelerAction.RotateRight:
                    be.GetBehavior<BEBehaviorLevelerRotation>()?.Rotate(action == LevelerAction.RotateRight ? 1 : -1);
                    break;
                case LevelerAction.TakeInstrument:
                    ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
                    if (Instrument == null || slot == null || !slot.Empty) return;
                    slot.Itemstack = new ItemStack(world.GetItem(Instrument));
                    slot.MarkDirty();
                    Exchange(world, pos, world.GetBlock(BareTripodCode));
                    break;
            }
        }

        // ── Збирання ─────────────────────────────────────────────────────────

        // З приладом у руці правий клік по тринозі ставить на неї прилад. Блок лише підміняється, тож матеріали і кут
        // лишаються в блок-сутності. Знімає прилад клік з Alt (ClickAction)
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockLevelerTripod mounted = Instrument == null ? MountedFor(byPlayer) : null;
            if (mounted != null)
            {
                if (world.Side == EnumAppSide.Server)
                {
                    ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
                    if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) slot.TakeOut(1);
                    slot.MarkDirty();
                    Exchange(world, blockSel.Position, mounted);
                }
                return true;
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        private static void Exchange(IWorldAccessor world, BlockPos pos, Block block)
        {
            world.BlockAccessor.ExchangeBlock(block.Id, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
        }

        // ── Предмет з матеріалами ────────────────────────────────────────────

        // Скрафчена тринога пише в себе, з чого її зробили; з гри це видно і в попередньому показі в сітці
        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
            TripodMaterials.FromIngredients(allInputslots, outputSlot.Itemstack.Attributes);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            return (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityLevelerTripod)?.ToStack() ?? base.OnPickBlock(world, pos);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return new[] { OnPickBlock(world, pos) };
        }

        public override BlockDropItemStack[] GetDropsForHandbook(ItemStack handbookStack, IPlayer forPlayer)
        {
            return new[] { new BlockDropItemStack(handbookStack) };
        }

        // Матеріалів тисячі поєднань, а сторінка в довіднику одна: стек без атрибутів. Рецепт теж дає стек без
        // атрибутів (матеріали з'являються вже при крафті), тож довідник знаходить для цієї сторінки всі рецепти й
        // гортає їх в одній сітці. Творче меню показує свій список з json
        public override List<ItemStack> GetHandBookStacks(ICoreClientAPI capi)
        {
            return base.GetHandBookStacks(capi) == null ? null : new List<ItemStack> { new ItemStack(this) };
        }
    }
}
