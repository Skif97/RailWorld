using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RailWorld.src.Items
{
    /// <summary>
    /// Матеріали деталей колії (рейок, шпал). Список береться з властивостей світу гри (метали, породи дерева) і
    /// стану з атрибутів предмета, але гра не обіцяє, що в кожного матеріалу є текстура: серед металів є мельхіор
    /// без текстури плити. Матеріал без текстури малювався б «невідомою» текстурою і в творчому меню, і в меню
    /// колії, і на колії, тож лишаються лише ті, для яких текстура справді є.
    /// </summary>
    internal static class TrackPartMaterials
    {
        /// <summary>
        /// Матеріали, для яких є кожна текстура предмета з підставленим {material}. textures це текстури з атрибутів
        /// предмета, як їх бере його меш.
        /// </summary>
        public static string[] WithTextures(ICoreAPI api, IEnumerable<string> materials, Dictionary<string, CompositeTexture> textures)
        {
            return materials.Where(material => HasTextures(api, material, textures)).ToArray();
        }

        private static bool HasTextures(ICoreAPI api, string material, Dictionary<string, CompositeTexture> textures)
        {
            if (textures == null) return true;
            foreach (CompositeTexture texture in textures.Values)
            {
                if (texture?.Base == null) continue;
                // Так само, як шлях текстури розгортає гра: домен, тека textures і розширення png
                var location = new AssetLocation(texture.Base.ToString().Replace("{material}", material))
                    .WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png");
                if (!api.Assets.Exists(location)) return false;
            }
            return true;
        }
    }
}
