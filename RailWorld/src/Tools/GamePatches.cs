using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace RailWorld
{
    /// <summary>
    /// Латки на закриті методи гри. Оновлення гри може перейменувати метод чи його параметри, до яких латка
    /// прив'язана за ім'ям, і тоді harmony.Patch кидає виняток. Мод від цього не має падати: без латки втрачається
    /// лише те, що вона давала, і гравець дізнається про це з журналу.
    /// </summary>
    public static class GamePatches
    {
        /// <summary>Одна латка: метод гри і що ставиться перед ним і після нього.</summary>
        public readonly struct Patch
        {
            public readonly MethodBase Original;
            public readonly HarmonyMethod Prefix, Postfix;

            public Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
            {
                Original = original;
                Prefix = prefix;
                Postfix = postfix;
            }
        }

        /// <summary>
        /// Ставить латки групою: усі або жодної, бо частина групи без решти працювала б невлад. Якщо якогось методу
        /// гри немає або латка не стала, вже поставлені з групи знімаються, а в журнал пишеться lost: що без них не
        /// працюватиме. Повертає, чи група стала.
        /// </summary>
        public static bool Apply(Harmony harmony, ILogger logger, string lost, params Patch[] patches)
        {
            foreach (Patch patch in patches)
            {
                if (patch.Original != null) continue;
                logger.Error("[RailWorld] у грі змінилися методи, на які стають латки: " + lost);
                return false;
            }

            var applied = new List<MethodBase>();
            try
            {
                foreach (Patch patch in patches)
                {
                    harmony.Patch(patch.Original, patch.Prefix, patch.Postfix);
                    applied.Add(patch.Original);
                }
                return true;
            }
            catch (Exception e)
            {
                foreach (MethodBase original in applied) harmony.Unpatch(original, HarmonyPatchType.All, harmony.Id);
                logger.Error("[RailWorld] латка на метод гри не стала (" + e.Message + "): " + lost);
                return false;
            }
        }
    }
}
