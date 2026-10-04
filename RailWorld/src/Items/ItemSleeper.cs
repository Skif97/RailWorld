using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace RailWorld.src.Items
{
    public class ItemSleeper : Item, IItemCustomMesh
    {
        private string[] types;

        private string[] materials;

        /// <summary>Усі матеріали, з яких буває цей предмет.</summary>
        public string[] Materials => materials;

        private Dictionary<string, CompositeTexture> textures;

        private CompositeShape cshape;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            LoadTypes();
        }

        public void LoadTypes()
        {
            types = Attributes["types"].AsArray<string>(null, null);
            cshape = Attributes["shape"].AsObject<CompositeShape>(null);
            textures = Attributes["textures"].AsObject<Dictionary<string, CompositeTexture>>(null);
            RegistryObjectVariantGroup grp = Attributes["materials"].AsObject<RegistryObjectVariantGroup>(null);
            materials = grp.States; //на прямую заполняет поле из жсон файла раздел стейт
            if (grp.LoadFromProperties != null) // проверка не пусто ли это поле
            {
                IAsset asset = api.Assets.TryGet(grp.LoadFromProperties.WithPathPrefixOnce("worldproperties/").WithPathAppendixOnce(".json"), true);
                StandardWorldProperty prop = asset != null ? asset.ToObject<StandardWorldProperty>(null) : null;
                materials = (from p in prop.Variants select p.Code.Path).ToArray().Append(materials);
                //заполнение списка материалов из файла параметров мира, (список дерева)
            }

            List<JsonItemStack> stacks = new List<JsonItemStack>();
            foreach (string type in types)
            {
                foreach (string material in materials)
                {
                    //перебор и заполнение всех вариантов материал + тип, создание айтем стаков для каждого
                    JsonItemStack jstack = new JsonItemStack
                    {
                        Code = Code,
                        Type = EnumItemClass.Item,
                        Attributes = new JsonObject(JToken.Parse(string.Concat(new string[]
                        {
                            "{ \"type\": \"",
                            type,
                            "\", \"material\": \"",
                            material,
                            "\" }"
                        })))
                    };
                    JsonItemStack jsonItemStack = jstack;
                    IWorldAccessor world = api.World;
                    AssetLocation code = Code;
                    jsonItemStack.Resolve(world, (code != null ? code.ToString() : null) + " type", true);
                    stacks.Add(jstack);
                }
            }
            //добавляет все варианты в креативное меню
            CreativeInventoryStacks = new CreativeTabAndStackList[]
            {
                new CreativeTabAndStackList
                {
                    Stacks = stacks.ToArray(),
                    Tabs = new string[]
                    {
                        "general",
                        "items"
                    }
                }
            };
        }
        public static string GetCode(string type, string material)
        {
            return "sleeper-" + type + "-" + material;
        }

        public virtual MeshData GetOrCreateMesh(string type, string material, ITexPositionSource overrideTexturesource = null)
        {
            Dictionary<string, MeshData> cMeshes = ObjectCacheUtil.GetOrCreate(api, "SleeperMeshes", () => new Dictionary<string, MeshData>());
            ICoreClientAPI capi = api as ICoreClientAPI;
            string key = type + "-" + material;
            MeshData mesh;
            if (overrideTexturesource != null || !cMeshes.TryGetValue(key, out mesh))
            {
                mesh = new MeshData(4, 3, false, true, true, true);
                CompositeShape rcshape = cshape.Clone();
                rcshape.Base.Path = rcshape.Base.Path.Replace("{type}", type).Replace("{material}", material); //подставляет материал и тип в адрес из АТРИБУТОВ из файла
                rcshape.Base.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/");
                IAsset asset = capi.Assets.TryGet(rcshape.Base, true); // пытаемся получить форму
                Shape shape = asset != null ? asset.ToObject<Shape>(null) : null;
                ITexPositionSource texSource = overrideTexturesource; //теперь собираем текстуру
                if (texSource == null)
                {
                    ShapeTextureSource stexSource = new ShapeTextureSource(capi, shape, rcshape.Base.ToString());
                    texSource = stexSource;
                    foreach (KeyValuePair<string, CompositeTexture> val in textures) //перебирвем все строки текстур из АТРИБУТОВ (в которых есть подстановочная часть)
                    {
                        CompositeTexture ctex = val.Value.Clone(); //копируем бащовую строку
                        ctex.Base.Path = ctex.Base.Path.Replace("{type}", type).Replace("{material}", material); //подменяем там подстановочные области
                        ctex.Bake(capi.Assets); //(если б  были оверлеи запекаем)
                        stexSource.textures[val.Key] = ctex; //в шейп дописываем текстуры
                    }
                }
                if (shape == null)
                {
                    return mesh;
                }


                capi.Tesselator.TesselateShape("Sleeper item", shape, out mesh, texSource, null, 0, 0, 0, null, null); //теселируем меш с подставленными текстурами
                if (overrideTexturesource == null)
                {
                    cMeshes[key] = mesh;  //добавляем новый теселированный меш в словарь всех вариантов шпалы
                }
            }
            return mesh;
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            Dictionary<string, MultiTextureMeshRef> meshRefs = ObjectCacheUtil.GetOrCreate(capi, "SleeperMeshesInventory", () => new Dictionary<string, MultiTextureMeshRef>());
            //Проверка наличия ссылки на меш в кеше
            string type = itemstack.Attributes.GetString("type", "");
            string material = itemstack.Attributes.GetString("material", "");
            string key = type + "-" + material;
            MultiTextureMeshRef meshref;
            if (!meshRefs.TryGetValue(key, out meshref))
            {
                MeshData mesh = GetOrCreateMesh(type, material, null); //берем из сохраненных или генерирование нового меша
                meshref = capi.Render.UploadMultiTextureMesh(mesh); //загрузка на видеокарту
                meshRefs[key] = meshref; //сохранение ссылки в кеш
            }
            renderinfo.ModelRef = meshref;
        }

        public MeshData GetMeshData(ICoreClientAPI capi, ItemStack itemstack)
        {
            string type = itemstack.Attributes.GetString("type", "");
            string material = itemstack.Attributes.GetString("material", "");
            return GetOrCreateMesh(type, material, null);
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            return Lang.Get("item-sleeper-" + itemStack.Attributes.GetString("material", null), Array.Empty<object>());
        }

    }
}
