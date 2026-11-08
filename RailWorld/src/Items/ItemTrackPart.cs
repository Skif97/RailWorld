using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace RailWorld.src.Items
{
    /// <summary>
    /// Деталь колії предметом: рейка чи шпала. Один предмет на всі варіанти: тип і матеріал лежать у властивостях
    /// стека, а форма й текстури у властивостях предмета мають місця {type} і {material}, куди вони підставляються.
    /// Меш кожного варіанта збирається раз і лежить у кеші клієнта, поки гравець у світі.
    /// </summary>
    public abstract class ItemTrackPart : Item, IItemCustomMesh
    {
        /// <summary>Що це за деталь: rail чи sleeper. З нього ключі кешів і назва в перекладах (railworld:item-…).</summary>
        protected abstract string Kind { get; }

        private string[] types;
        private string[] materials;
        private Dictionary<string, CompositeTexture> textures;
        private CompositeShape cshape;

        /// <summary>Усі матеріали, з яких буває цей предмет.</summary>
        public string[] Materials => materials;

        // Меші варіантів за «тип-матеріал» і завантажені на відеокарту за типом і матеріалом
        private string MeshesKey => "railworld-" + Kind + "-meshes";
        private string MeshRefsKey => "railworld-" + Kind + "-meshrefs";

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            LoadTypes();
        }

        // Меші на відеокарті звільняються, коли гравець виходить зі світу, як у предметів гри (BlockBarrel.OnUnloaded)
        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            if (!(api is ICoreClientAPI capi)) return;
            if (capi.ObjectCache.TryGetValue(MeshRefsKey, out object refs))
            {
                foreach (Dictionary<string, MultiTextureMeshRef> byMaterial in ((Dictionary<string, Dictionary<string, MultiTextureMeshRef>>)refs).Values)
                {
                    foreach (MultiTextureMeshRef meshRef in byMaterial.Values) meshRef?.Dispose();
                }
                capi.ObjectCache.Remove(MeshRefsKey);
            }
            capi.ObjectCache.Remove(MeshesKey);
        }

        private void LoadTypes()
        {
            types = Attributes["types"].AsArray<string>(null, null) ?? new string[0];
            cshape = Attributes["shape"].AsObject<CompositeShape>(null);
            textures = Attributes["textures"].AsObject<Dictionary<string, CompositeTexture>>(null) ?? new Dictionary<string, CompositeTexture>();

            // Матеріали: перелічені у властивостях і, якщо вказано, ще всі варіанти властивості світу (породи дерева)
            RegistryObjectVariantGroup group = Attributes["materials"].AsObject<RegistryObjectVariantGroup>(null);
            materials = group?.States ?? new string[0];
            if (group?.LoadFromProperties != null)
            {
                IAsset asset = api.Assets.TryGet(group.LoadFromProperties.WithPathPrefixOnce("worldproperties/").WithPathAppendixOnce(".json"), true);
                StandardWorldProperty property = asset?.ToObject<StandardWorldProperty>(null);
                if (property?.Variants != null) materials = property.Variants.Select(variant => variant.Code.Path).ToArray().Append(materials);
                else api.Logger.Warning("[RailWorld] немає властивості світу {0}: у предмета {1} лише перелічені матеріали", group.LoadFromProperties, Code);
            }
            // Лише матеріали, для яких у грі є текстура: інакше варіант малювався б «невідомою» текстурою
            materials = TrackPartMaterials.WithTextures(api, materials, textures);

            // Усі варіанти тип + матеріал у творчому меню
            var stacks = new List<JsonItemStack>();
            foreach (string type in types)
            {
                foreach (string material in materials)
                {
                    var stack = new JsonItemStack
                    {
                        Code = Code,
                        Type = EnumItemClass.Item,
                        Attributes = new JsonObject(new JObject { ["type"] = type, ["material"] = material })
                    };
                    stack.Resolve(api.World, Code + " type", true);
                    stacks.Add(stack);
                }
            }
            CreativeInventoryStacks = new[]
            {
                new CreativeTabAndStackList { Stacks = stacks.ToArray(), Tabs = new[] { "general", "items" } }
            };
        }

        /// <summary>
        /// Меш варіанта type і material. З overrideTexturesource меш збирається з цими текстурами і в кеш не йде.
        /// </summary>
        public virtual MeshData GetOrCreateMesh(string type, string material, ITexPositionSource overrideTexturesource = null)
        {
            Dictionary<string, MeshData> meshes = ObjectCacheUtil.GetOrCreate(api, MeshesKey, () => new Dictionary<string, MeshData>());
            string key = type + "-" + material;
            if (overrideTexturesource == null && meshes.TryGetValue(key, out MeshData cached)) return cached;

            var capi = (ICoreClientAPI)api;
            CompositeShape variantShape = cshape.Clone();
            variantShape.Base.Path = variantShape.Base.Path.Replace("{type}", type).Replace("{material}", material);
            variantShape.Base.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/");
            Shape shape = capi.Assets.TryGet(variantShape.Base, true)?.ToObject<Shape>(null);
            if (shape == null) return new MeshData(4, 3, false, true, true, true);

            ITexPositionSource texSource = overrideTexturesource;
            if (texSource == null)
            {
                // Текстури з властивостей предмета з підставленими типом і матеріалом
                var shapeTextures = new ShapeTextureSource(capi, shape, variantShape.Base.ToString());
                foreach (KeyValuePair<string, CompositeTexture> texture in textures)
                {
                    CompositeTexture variantTexture = texture.Value.Clone();
                    variantTexture.Base.Path = variantTexture.Base.Path.Replace("{type}", type).Replace("{material}", material);
                    variantTexture.Bake(capi.Assets);
                    shapeTextures.textures[texture.Key] = variantTexture;
                }
                texSource = shapeTextures;
            }

            capi.Tesselator.TesselateShape(Kind + " item", shape, out MeshData mesh, texSource, null, 0, 0, 0, null, null);
            if (overrideTexturesource == null) meshes[key] = mesh;
            return mesh;
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            // Меш на тип і матеріал. Ключ двома рівнями, а не склеєним рядком: це питається щокадру на кожен
            // намальований предмет, а рядок щоразу був би новим
            var meshRefs = ObjectCacheUtil.GetOrCreate(capi, MeshRefsKey, () => new Dictionary<string, Dictionary<string, MultiTextureMeshRef>>());
            string type = itemstack.Attributes.GetString("type", "");
            string material = itemstack.Attributes.GetString("material", "");
            if (!meshRefs.TryGetValue(type, out Dictionary<string, MultiTextureMeshRef> byMaterial)) meshRefs[type] = byMaterial = new Dictionary<string, MultiTextureMeshRef>();
            if (!byMaterial.TryGetValue(material, out MultiTextureMeshRef meshRef))
            {
                meshRef = capi.Render.UploadMultiTextureMesh(GetOrCreateMesh(type, material));
                byMaterial[material] = meshRef;
            }
            renderinfo.ModelRef = meshRef;
        }

        public MeshData GetMeshData(ICoreClientAPI capi, ItemStack itemstack)
        {
            return GetOrCreateMesh(itemstack.Attributes.GetString("type", ""), itemstack.Attributes.GetString("material", ""));
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            // Назва складається з назви предмета і назви матеріалу з перекладів гри
            string material = itemStack.Attributes.GetString("material", null);
            if (material == null) return base.GetHeldItemName(itemStack);
            return Lang.Get("railworld:item-" + Kind, Lang.Get("material-" + material));
        }
    }

    /// <summary>Рейка предметом.</summary>
    public class ItemRail : ItemTrackPart
    {
        protected override string Kind => "rail";
    }

    /// <summary>Шпала предметом.</summary>
    public class ItemSleeper : ItemTrackPart
    {
        protected override string Kind => "sleeper";
    }
}
