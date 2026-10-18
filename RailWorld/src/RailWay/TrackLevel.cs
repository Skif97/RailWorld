using System;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Чи дві колії на одному рівні: тоді вони можуть перетинатися, сходитися в стрілку чи лежати одна на одній.
    /// Інакше одна проходить над іншою, мостом чи тунелем, і правила одна одну не бачать.
    /// Прокладання лишає лише два випадки: колії в одній площині або щонайменше за RouteCurve.CrossingClearance
    /// одна над одною (проміжне відкидає правило «надто близько над чи під»). Межа посередині між ними відрізняє
    /// їх із запасом. Висоти обох колій беруться в одній точці плану: середини секцій можуть стояти за кілька
    /// блоків одна від одної, і на схилі різниця їхніх висот це не різниця рівнів.
    /// </summary>
    public static class TrackLevel
    {
        /// <summary>Різниця висот, з якої колії вже на різних рівнях.</summary>
        public const double Limit = RouteCurve.CrossingClearance / 2;

        /// <summary>Чи дві висоти, взяті в одній точці плану, на одному рівні.</summary>
        public static bool Same(double y, double otherY)
        {
            return Math.Abs(y - otherY) < Limit;
        }

        /// <summary>Чи точка p на одному рівні з колією section у тому ж місці плану.</summary>
        public static bool Same(Section section, Vec3d p)
        {
            return Same(At(section, p.X, p.Z), p.Y);
        }

        /// <summary>
        /// Чи дві секції на одному рівні. Висоти міряються посередині між їхніми серединами в плані: там, де
        /// колії зустрічаються, якщо вони зустрічаються.
        /// </summary>
        public static bool Same(Section a, Section b)
        {
            Vec3d ma = a.GetGlobalPos(), mb = b.GetGlobalPos();
            double x = (ma.X + mb.X) / 2, z = (ma.Z + mb.Z) / 2;
            return Same(At(a, x, z), At(b, x, z));
        }

        /// <summary>
        /// Висота полотна секції в точці плану: площина секції, продовжена до цієї точки. Для секції, що стоїть
        /// майже сторч, площини немає, і береться висота її середини.
        /// </summary>
        public static double At(Section section, double x, double z)
        {
            return RouteCurve.CrossPlane(section, new Vec3d(x, 0, z), 0, 0, out double y, out _) ? y : section.GetGlobalPos().Y;
        }
    }
}
