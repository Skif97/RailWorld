using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace RailWorld
{
    /// <summary>
    /// Фізика вагонетки. Поза колією це ванільна EntityBehaviorControlledPhysics. На колії сервер спершу веде
    /// вагонетку колією (EntityTrolley.PhysicsTickOnRail), а далі все робить ванільна фізика: так рух по колії, пасажир
    /// на сидінні і пакет із положенням для клієнтів беруться з одного кроку фізики, як у човна гри, що теж рухає себе
    /// лише фізикою. Крок фізики гра може робити в робочих потоках (PhysicsManager.DoWork, коли сутностей багато),
    /// тому в ньому вагонетка дані колії лише читає, а те, що змінює спільне (кеш даних чанків, сніг на полотні),
    /// робить в AfterPhysicsTick, який гра завжди викликає в основному потоці після всієї фізики.
    /// </summary>
    public class EntityBehaviorTrolleyPhysics : EntityBehaviorControlledPhysics
    {
        public EntityBehaviorTrolleyPhysics(Entity entity) : base(entity)
        {
        }

        public override void OnPhysicsTick(float dt)
        {
            if (entity.Api.Side == EnumAppSide.Server && entity.State == EnumEntityState.Active && entity is EntityTrolley trolley)
            {
                trolley.PhysicsTickOnRail(dt);
            }
            base.OnPhysicsTick(dt);
        }

        public override void AfterPhysicsTick(float dt)
        {
            if (entity is EntityTrolley trolley) trolley.AfterPhysicsTickOnRail();
            base.AfterPhysicsTick(dt);
        }

        public override string PropertyName()
        {
            return "trolleyphysics";
        }
    }
}
