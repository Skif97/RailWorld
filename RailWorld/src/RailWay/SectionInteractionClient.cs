using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Клієнтська взаємодія мишею з секціями колії. Вбудовується в SystemMouseInWorldInteractions гри
    /// і повторює для секцій те, що HandleMouseInteractionsBlockSelected робить для блоків.
    /// </summary>
    public class SectionInteractionClient
    {
        private static SectionInteractionClient instance;
        private static bool patched;
        private static MethodInfo beginUseItem;
        private static MethodInfo beginUseItemTyped;
        private static MethodInfo startAttackAnimation;

        private const float BuildRepeatDelay = 0.25f;

        private ICoreClientAPI capi;
        private RailWaySystem system;

        /// <summary>Деталь, на яку зараз дивиться гравець, або null.</summary>
        public SectionSelection Current { get; private set; }

        /// <summary>Наскільки зламана поточна деталь, 0..1.</summary>
        public float BreakProgress => isBreaking && totalResistance > 0 ? 1 - remainingResistance / totalResistance : 0;

        private long lastActionMs;

        // Взаємодія правою кнопкою
        private SectionSelection usingSel;
        private long usingBeginMs;
        private float stepPacketAccum;

        // Ламання у виживанні
        private bool isBreaking;
        private SectionSelection breakSel;
        private float remainingResistance;
        private float totalResistance;
        private long lastBreakMs;
        private int breakCounter;

        // Натиск лівої кнопки належить тому, на що гравець дивився в момент натискання:
        // почав на секції — ламає лише секції, почав на блоці — лише блоки, доки не відпустить
        private bool leftHeld;
        private bool leftPressOnSection;

        public SectionInteractionClient(ICoreClientAPI capi, RailWaySystem system, Harmony harmony)
        {
            this.capi = capi;
            this.system = system;
            instance = this;

            if (patched) return;
            patched = true;

            Type type = typeof(SystemMouseInWorldInteractions);
            beginUseItem = AccessTools.Method(type, "TryBeginUseActiveSlotItem", new Type[] { typeof(BlockSelection), typeof(EntitySelection) });
            beginUseItemTyped = AccessTools.Method(type, "TryBeginUseActiveSlotItem", new Type[] { typeof(BlockSelection), typeof(EntitySelection), typeof(EnumHandInteract), typeof(EnumHandHandling).MakeByRefType() });
            startAttackAnimation = AccessTools.Method(type, "StartAttackAnimation");

            harmony.Patch(AccessTools.Method(type, "UpdateCurrentSelection"),
                postfix: new HarmonyMethod(typeof(SectionInteractionClient), nameof(UpdateCurrentSelectionPostfix)));
            harmony.Patch(AccessTools.Method(type, "HandleMouseInteractionsNoBlockSelected"),
                prefix: new HarmonyMethod(typeof(SectionInteractionClient), nameof(NoBlockSelectedPrefix)));
            harmony.Patch(AccessTools.Method(type, "HandleMouseInteractionsBlockSelected"),
                prefix: new HarmonyMethod(typeof(SectionInteractionClient), nameof(BlockSelectedPrefix)));
        }

        public void Dispose()
        {
            if (instance == this) instance = null;
            Current = null;
        }

        // Після того як гра знайшла блок і сутність під прицілом, шукаємо секцію тим самим променем
        public static void UpdateCurrentSelectionPostfix()
        {
            instance?.UpdateSelection();
        }

        // Блок не виділено. Якщо виділена секція, кліки обробляємо ми, а не гра
        public static bool NoBlockSelectedPrefix(SystemMouseInWorldInteractions __instance, float dt)
        {
            if (instance == null || instance.Current == null) return true;
            instance.HandleMouseInteractionsSectionSelected(__instance, dt);
            return false;
        }

        // Натиск почався на секції: блок, що опинився під прицілом, цим натиском не чіпаємо
        public static bool BlockSelectedPrefix()
        {
            return instance == null || !(instance.leftHeld && instance.leftPressOnSection);
        }

        private void UpdateSelection()
        {
            SectionSelection previous = Current;
            Current = PickSection();

            bool left = capi.Input.InWorldMouseButton.Left;
            if (left && !leftHeld) leftPressOnSection = Current != null;
            leftHeld = left;

            if (usingSel != null && !usingSel.SamePart(Current)) CancelUse(EnumItemUseCancelReason.MovedAway);
            if (isBreaking && !breakSel.SamePart(Current)) StopBreaking();

            if (Current == null)
            {
                if (previous != null) system.SendSectionAction(EnumSectionAction.Deselect, previous);
                return;
            }

            // Секція ближча за блок і сутність, тому для гри під прицілом тепер нічого немає:
            // контур блока не малюється і блок крізь колію не ставиться
            EntityPlayer entity = capi.World.Player.Entity;
            entity.BlockSelection = null;
            entity.EntitySelection = null;

            bool firstTick = !Current.SamePart(previous);
            if (firstTick) system.SendSectionAction(EnumSectionAction.Select, Current);
            system.Handler.OnBeingLookedAt(capi.World.Player, Current, firstTick);
        }

        private SectionSelection PickSection()
        {
            IClientPlayer player = capi.World.Player;
            EntityPlayer entity = player?.Entity;
            if (entity == null || !capi.Input.MouseGrabbed) return null;
            if (player.WorldData.CurrentGameMode == EnumGameMode.Spectator) return null;

            // Той самий промінь, що і в GameMain.RayTraceForSelection
            Vec3d origin = entity.Pos.XYZ.Add(entity.LocalEyePos);
            Ray ray = Ray.FromAngles(origin, entity.Pos.Pitch, entity.Pos.Yaw, player.WorldData.PickingRange);
            if (ray == null) return null;

            // Порожнє місце під деталь виділяється, лише коли предмет у руці можна туди поставити
            ItemStack held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            SectionSelection sel = SectionPicker.Pick(origin, ray.dir.Clone().Normalize(), player.WorldData.PickingRange, system.GetClientBoxes,
                box => PickFilter(box, held, system.Handler.IsSectionTool(held)));
            if (sel == null) return null;

            // Блок колії лежить під своїми ж деталями і місцями під них, тому він їх не затуляє
            BlockSelection blockSel = entity.BlockSelection;
            if (blockSel != null && !(capi.World.BlockAccessor.GetBlock(blockSel.Position) is BlockTrackBed))
            {
                Vec3d hit = new Vec3d(blockSel.Position.X, blockSel.Position.InternalY, blockSel.Position.Z).Add(blockSel.HitPosition);
                if (origin.DistanceTo(hit) < sel.Distance) return null;
            }

            EntitySelection entitySel = entity.EntitySelection;
            if (entitySel != null)
            {
                Vec3d hit = entitySel.Position.AddCopy(entitySel.HitPosition);
                if (origin.DistanceTo(hit) < sel.Distance) return null;
            }

            return sel;
        }

        // З предметом для прокладання колії виділяється лише секція цілком, без нього лише окремі деталі
        private bool PickFilter(SectionBox box, ItemStack held, bool sectionTool)
        {
            if (sectionTool) return box.Part == SectionPart.Whole;
            if (box.Part == SectionPart.Whole) return false;
            if (box.Installed) return true;
            // У заблоковану секцію ставити не можна, тому її порожні місця під прицілом не виділяються
            return !box.Section.Blocked && system.Handler.CanInstall(held, box.Part);
        }

        // Дзеркало SystemMouseInWorldInteractions.HandleMouseInteractionsBlockSelected
        private void HandleMouseInteractionsSectionSelected(SystemMouseInWorldInteractions miw, float dt)
        {
            IClientPlayer player = capi.World.Player;
            SectionSelection sel = Current;
            MouseButtonState mouse = capi.Input.InWorldMouseButton;
            ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
            EnumGameMode mode = player.WorldData.CurrentGameMode;

            if ((capi.InWorldEllapsedMilliseconds - lastActionMs) / 1000f >= BuildRepeatDelay)
            {
                if (mouse.Left || mouse.Right || mouse.Middle)
                {
                    lastActionMs = capi.InWorldEllapsedMilliseconds;
                }
                else
                {
                    lastActionMs = 0;
                    StopBreaking();
                }

                // Натиск, що почався на блоці, секцій не ламає, навіть коли блок зник і за ним відкрилася колія
                if (mouse.Left && leftPressOnSection)
                {
                    EnumHandHandling handled = EnumHandHandling.NotHandled;
                    TryBeginUseActiveSlotItem(miw, EnumHandInteract.HeldItemAttack, ref handled);
                    if (handled != EnumHandHandling.PreventDefaultAnimation && handled != EnumHandHandling.PreventDefault)
                    {
                        startAttackAnimation.Invoke(miw, null);
                    }

                    if (handled == EnumHandHandling.PreventDefaultAction || handled == EnumHandHandling.PreventDefault || !sel.Box.Installed)
                    {
                        // Порожнє місце ламати нічим
                        StopBreaking();
                    }
                    else if (mode == EnumGameMode.Creative)
                    {
                        BreakSection(sel);
                    }
                    else
                    {
                        InitBreakSurvival(sel);
                    }
                }

                if (mouse.Right)
                {
                    bool haveHeldItemstack = slot?.Itemstack != null;
                    bool shift = player.Entity.Controls.ShiftKey;
                    bool priority = system.Handler.PlacedPriorityInteract;

                    if (!shift && TryBeginUseSection(sel)) return;
                    if (haveHeldItemstack && (!shift || slot.Itemstack.Collectible.HeldPriorityInteract) && TryBeginUseActiveSlotItem(miw)) return;
                    if (shift && priority && TryBeginUseSection(sel)) return;
                    // Тут у блоків стоїть встановлення блока з руки. Крізь секцію блоки не ставимо
                    if (haveHeldItemstack && shift && !slot.Itemstack.Collectible.HeldPriorityInteract && TryBeginUseActiveSlotItem(miw)) return;
                    if (shift && !priority && TryBeginUseSection(sel)) return;
                }
            }

            if (usingSel != null) ContinueUse(dt);

            long now = capi.ElapsedMilliseconds;
            if (isBreaking && mouse.Left && mode == EnumGameMode.Survival && now - lastBreakMs >= 40)
            {
                ContinueBreakSurvival(sel, slot, now);
            }
        }

        private bool TryBeginUseActiveSlotItem(SystemMouseInWorldInteractions miw)
        {
            return (bool)beginUseItem.Invoke(miw, new object[] { null, null });
        }

        private bool TryBeginUseActiveSlotItem(SystemMouseInWorldInteractions miw, EnumHandInteract useType, ref EnumHandHandling handling)
        {
            object[] args = new object[] { null, null, useType, handling };
            bool result = (bool)beginUseItemTyped.Invoke(miw, args);
            handling = (EnumHandHandling)args[3];
            return result;
        }

        // Дзеркало TryBeginUseBlock
        private bool TryBeginUseSection(SectionSelection sel)
        {
            IClientPlayer player = capi.World.Player;
            if (!capi.World.Claims.TryAccess(player, sel.Position, EnumBlockAccessFlags.Use)) return false;
            if (!system.Handler.OnSectionInteractStart(capi.World, player, sel)) return false;

            usingSel = sel;
            usingBeginMs = capi.ElapsedMilliseconds;
            stepPacketAccum = 0;
            system.SendSectionAction(EnumSectionAction.InteractStart, sel);
            return true;
        }

        // Дзеркало гілки BlockInteract у HandleHandInteraction
        private void ContinueUse(float dt)
        {
            IClientPlayer player = capi.World.Player;
            float secondsPassed = (capi.ElapsedMilliseconds - usingBeginMs) / 1000f;

            if (!capi.Input.InWorldMouseButton.Right)
            {
                CancelUse(EnumItemUseCancelReason.ReleasedMouse);
                if (usingSel == null) return;
            }

            if (system.Handler.OnSectionInteractStep(secondsPassed, capi.World, player, usingSel))
            {
                stepPacketAccum += dt;
                if (stepPacketAccum > 0.15)
                {
                    system.SendSectionAction(EnumSectionAction.InteractStep, usingSel);
                    stepPacketAccum = 0;
                }
                return;
            }

            system.Handler.OnSectionInteractStop(secondsPassed, capi.World, player, usingSel);
            system.SendSectionAction(EnumSectionAction.InteractStop, usingSel);
            usingSel = null;
        }

        private void CancelUse(EnumItemUseCancelReason reason)
        {
            float secondsPassed = (capi.ElapsedMilliseconds - usingBeginMs) / 1000f;
            if (!system.Handler.OnSectionInteractCancel(secondsPassed, capi.World, capi.World.Player, usingSel, reason)) return;

            system.SendSectionAction(EnumSectionAction.InteractCancel, usingSel, reason);
            usingSel = null;
        }

        private void InitBreakSurvival(SectionSelection sel)
        {
            // Викликається кожні 0.25 с, поки затиснута кнопка. Уже розпочате ламання не скидаємо
            if (isBreaking && breakSel.SamePart(sel)) return;

            breakSel = sel;
            totalResistance = system.Handler.GetResistance(capi.World, sel);
            remainingResistance = totalResistance;
            breakCounter = 0;
            lastBreakMs = capi.ElapsedMilliseconds;
            isBreaking = true;
        }

        private void ContinueBreakSurvival(SectionSelection sel, ItemSlot slot, long now)
        {
            if (!breakSel.SamePart(sel)) InitBreakSurvival(sel);

            float dt = (now - lastBreakMs) / 1000f;
            remainingResistance = system.Handler.OnGettingBroken(capi.World.Player, sel, slot, remainingResistance, dt, breakCounter);
            breakCounter++;
            lastBreakMs = now;

            if (remainingResistance <= 0)
            {
                BreakSection(sel);
                StopBreaking();
            }
        }

        private void StopBreaking()
        {
            isBreaking = false;
            breakSel = null;
            breakCounter = 0;
        }

        private void BreakSection(SectionSelection sel)
        {
            if (!capi.World.Claims.TryAccess(capi.World.Player, sel.Position, EnumBlockAccessFlags.BuildOrBreak)) return;
            system.Handler.OnSectionBroken(capi.World, sel, capi.World.Player);
            system.SendSectionAction(EnumSectionAction.Break, sel);
        }
    }
}
