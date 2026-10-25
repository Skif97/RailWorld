using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace RailWorld
{
    /// <summary>
    /// З чого зроблена тринога: порода дощок, метал цвяхів (гвинти, болти, наконечники) і метал пластини (площадка під
    /// прилад, хомути, вушка). Це атрибути стека, як у ванільних меблів з поведінкою TypedTexture: вона за ними
    /// підставляє текстури (materialTypes і textureMapping у json триноги) і пише матеріали в підказці предмета.
    /// </summary>
    public static class TripodMaterials
    {
        // Ті самі коди, що materialTypes у json триноги
        public const string Wood = "wood", Nails = "nails", Plate = "plate";

        /// <summary>
        /// Матеріали з інгредієнтів у сітці крафту: дошки plank-{wood}, цвяхи metalnailsandstrips-{metal},
        /// пластина metalplate-{metal}. Рецепт бере цвяхи й пластину будь-якого металу без імені змінної, щоб не
        /// множити рецепти на кожне поєднання металів, тож матеріали записує сам предмет, коли його скрафтили.
        /// </summary>
        public static void FromIngredients(ItemSlot[] slots, ITreeAttribute into)
        {
            foreach (ItemSlot slot in slots)
            {
                CollectibleObject collectible = slot.Itemstack?.Collectible;
                if (collectible?.Code == null) continue;
                switch (collectible.FirstCodePart())
                {
                    case "plank": Set(into, Wood, collectible.Variant["wood"]); break;
                    case "metalnailsandstrips": Set(into, Nails, collectible.Variant["metal"]); break;
                    case "metalplate": Set(into, Plate, collectible.Variant["metal"]); break;
                }
            }
        }

        private static void Set(ITreeAttribute into, string key, string value)
        {
            if (value != null) into.SetString(key, value);
        }

        /// <summary>Матеріали зі стека в окреме дерево, як їх зберігає поставлена тринога.</summary>
        public static TreeAttribute Copy(ITreeAttribute from)
        {
            var tree = new TreeAttribute();
            foreach (string key in new[] { Wood, Nails, Plate })
            {
                string value = from?.GetString(key);
                if (value != null) tree.SetString(key, value);
            }
            return tree;
        }
    }
}
