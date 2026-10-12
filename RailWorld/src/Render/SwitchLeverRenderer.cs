using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Важелі стрілок. У важеля рухаються ручка, вантаж на ній, щогла з покажчиком і тяги, кожне довкола своєї
    /// осі, тож це анімована модель, і малюється вона засобами гри: її аніматор рахує, де кожна деталь на
    /// потрібному кадрі, а її рендер анімованих моделей малює. Так гра малює двері й прес для фруктів.
    /// Анімація сама не програється: на який кадр її поставити, каже той, хто веде стрілку, і важіль
    /// рухається разом з гостряками.
    /// Модель shapes/part/switchlever.json збирає tools/make_lever.py зі сцени в shapes/reference.
    /// </summary>
    public class SwitchLeverRenderer : IDisposable
    {
        // Переведення з першого положення в друге: перший кадр це перша гілка, останній друга
        private const string AnimationCode = "throw";
        private const string ShapeName = "railworld:shapes/part/switchlever.json";

        private class Lever
        {
            public Vec3i Chunk;
            public Vec3d Position;
            public AnimatorBase Animator;
            public AnimatableRenderer Renderer;
            public Dictionary<string, AnimationMetaData> Active;
            // Чи важіль поставили знову за цю перебудову чанка
            public bool Kept;
        }

        private ICoreClientAPI capi;
        private Shape shape;
        private MeshData mesh;
        private bool broken;
        private Dictionary<string, Lever> levers = new Dictionary<string, Lever>();

        public SwitchLeverRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        // Готує модель так само, як AnimationUtil.InitializeShapeAndAnimator у грі: деталям, що рухаються,
        // роздаються номери, і меш несе їх у вершинах. false, якщо моделі або анімації в ній немає
        private bool Load()
        {
            if (mesh != null) return true;
            if (broken) return false;

            Shape loaded = capi.Assets.TryGet(new AssetLocation(ShapeName))?.ToObject<Shape>();
            if (loaded?.Animations == null || !loaded.Animations.Any(animation => animation.Code == AnimationCode))
            {
                capi.Logger.Error("[RailWorld] немає моделі " + ShapeName + " або анімації " + AnimationCode + " в ній: важелі стрілок не малюватимуться");
                broken = true;
                return false;
            }

            Dictionary<string, ShapeElement> elements = loaded.CollectAndResolveReferences(capi.Logger, ShapeName);
            loaded.CacheInvTransforms();
            loaded.ResolveAndFindJoints(capi.Logger, ShapeName, elements, Array.Empty<string>());

            var meta = new TesselationMetaData
            {
                TexSource = new ShapeTextureSource(capi, loaded, ShapeName),
                WithJointIds = true,
                WithDamageEffect = true,
                TypeForLogging = ShapeName,
                Rotation = new Vec3f()
            };
            capi.Tesselator.TesselateShape(meta, loaded, out mesh);
            shape = loaded;
            return true;
        }

        /// <summary>
        /// Початок і кінець перебудови чанка. Між ними кожен його важіль ставиться знову; той, що лишився
        /// на місці, зберігає готовий меш, а той, якого не поставили, зникає.
        /// </summary>
        public void BeginChunk(Vec3i chunkCoord)
        {
            foreach (Lever lever in levers.Values)
            {
                if (lever.Chunk.Equals(chunkCoord)) lever.Kept = false;
            }
        }

        public void EndChunk(Vec3i chunkCoord)
        {
            Remove(pair => pair.Value.Chunk.Equals(chunkCoord) && !pair.Value.Kept);
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            Remove(pair => pair.Value.Chunk.Equals(chunkCoord));
        }

        private void Remove(System.Func<KeyValuePair<string, Lever>, bool> which)
        {
            foreach (string key in levers.Where(which).Select(pair => pair.Key).ToList())
            {
                levers[key].Renderer.Dispose();
                levers.Remove(key);
            }
        }

        /// <summary>
        /// Ставить важіль або переводить уже поставлений. yaw і pitch це поворот моделі довкола вертикалі
        /// і нахил уздовж колії, радіани. thrown каже, де стрілка: 0 це перша гілка, 1 друга, між ними рух.
        /// </summary>
        public void Set(Vec3i chunkCoord, string key, Vec3d position, float yaw, float pitch, double thrown)
        {
            if (!Load()) return;

            if (levers.TryGetValue(key, out Lever lever) && !Same(lever.Position, position))
            {
                // Стрілку перебудували, і важіль став в інше місце: рендер гри місця не міняє
                lever.Renderer.Dispose();
                levers.Remove(key);
                lever = null;
            }
            if (lever == null)
            {
                lever = new Lever { Chunk = chunkCoord.Clone(), Position = position.Clone() };
                lever.Animator = AnimationUtil.GetAnimator(capi, ShapeName, shape);
                lever.Active = new Dictionary<string, AnimationMetaData>
                {
                    [AnimationCode] = new AnimationMetaData { Code = AnimationCode, Animation = AnimationCode }.Init()
                };
                lever.Renderer = new AnimatableRenderer(capi, lever.Position, null, lever.Animator, lever.Active, mesh);
                levers[key] = lever;
            }
            lever.Kept = true;

            // Точка (8, 0, 8) моделі, де стоїть важіль, це середина блока
            float[] transform = Mat4f.Create();
            Mat4f.RotateY(transform, transform, yaw);
            Mat4f.RotateX(transform, transform, pitch);
            Mat4f.Translate(transform, transform, -0.5f, 0f, -0.5f);
            lever.Renderer.CustomTransform = transform;

            Pose(lever, thrown);
        }

        // Ставить анімацію на кадр. Аніматор гри сам кадрів не гортає, якщо часу не минуло, тож час йому
        // даємо нульовий, а кадр і повну вагу анімації ставимо самі
        private static void Pose(Lever lever, double thrown)
        {
            RunningAnimation state = lever.Animator.GetAnimationState(AnimationCode);
            // Перший виклик лише вмикає анімацію і скидає її на початок
            if (!state.Active) lever.Animator.OnFrame(lever.Active, 0);

            state.CurrentFrame = (float)(GameMath.Clamp(thrown, 0, 1) * (state.Animation.QuantityFrames - 1));
            state.EasingFactor = 1;
            lever.Animator.OnFrame(lever.Active, 0);
        }

        private static bool Same(Vec3d a, Vec3d b)
        {
            return a.SquareDistanceTo(b) < 1e-8;
        }

        public void Dispose()
        {
            Remove(pair => true);
        }
    }
}
