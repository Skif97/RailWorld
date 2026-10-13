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
    /// осі, тож це анімована модель, і веде її гра своїми засобами, так само, як двері: AnimationUtil програє
    /// анімацію за часом і малює модель. Важелю кажуть лише, в якому з двох положень йому бути. В останнє він
    /// переходить, програючи анімацію, назад у перше, відмотуючи її; якщо його перевели назад, не дочекавшись
    /// кінця, він розвертається з того кадру, де був.
    /// Гостряки веде SwitchAnimator за власним годинником, тож під час руху важіль і гостряки можуть трохи
    /// розходитися; у крайніх положеннях вони збігаються завжди.
    /// Модель shapes/part/switchlever.json збирає tools/make_lever.py зі сцени в shapes/reference.
    /// </summary>
    public class SwitchLeverRenderer : IDisposable
    {
        // Переведення з першого положення в останнє. У моделі вона тримається на останньому кадрі, а коли її
        // зупиняють, відмотується назад (onAnimationEnd Hold, onActivityStopped Rewind)
        private const string AnimationCode = "throw";
        private const string ShapeName = "railworld:shapes/part/switchlever.json";

        // Поза анімації береться одразу на повну вагу і не згасає, поки анімація відмотується: інакше важіль
        // на початку руху відставав би, а назад повертався б не тим шляхом, яким ішов
        private const float EaseIn = 1000f;
        private const float EaseOut = 0f;

        private class Lever
        {
            public Vec3i Chunk;
            public Vec3d Position;
            public AnimationUtil Util;
            // Чи важіль стоїть або йде в останнє положення
            public bool Thrown;
            // Чи важіль поставили знову за цю перебудову чанка
            public bool Kept;
        }

        private ICoreClientAPI capi;
        private Shape shape;
        private MeshData mesh;
        private int lastFrame;
        private bool broken;
        private Dictionary<string, Lever> levers = new Dictionary<string, Lever>();

        public SwitchLeverRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        // Готує модель так само, як AnimationUtil.InitializeShapeAndAnimator у грі: деталям, що рухаються,
        // роздаються номери, і меш несе їх у вершинах. Меш один на всі важелі. false, якщо моделі або анімації
        // в ній немає
        private bool Load()
        {
            if (mesh != null) return true;
            if (broken) return false;

            Shape loaded = capi.Assets.TryGet(new AssetLocation(ShapeName))?.ToObject<Shape>();
            Animation animation = loaded?.Animations?.FirstOrDefault(found => found.Code == AnimationCode);
            if (animation == null)
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
            lastFrame = animation.QuantityFrames - 1;
            return true;
        }

        /// <summary>
        /// Початок і кінець перебудови чанка. Між ними кожен його важіль ставиться знову; той, що лишився
        /// на місці, зберігає свій рендер і продовжує рух, а той, якого не поставили, зникає.
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
                levers[key].Util.Dispose();
                levers.Remove(key);
            }
        }

        /// <summary>
        /// Ставить важіль або переводить уже поставлений. toTrack це напрямок від важеля до колії, along напрямок
        /// самої колії; обидва з її ухилом, важіль стає в їхню площину. thrown каже, в якому положенні йому бути:
        /// false в першому, true в останньому (LeverPose). Важіль, якого ще не було, одразу стоїть у своєму
        /// положенні; той, що вже стояв в іншому, переходить у нове рухом.
        /// </summary>
        public void Set(Vec3i chunkCoord, string key, Vec3d position, Vec3d toTrack, Vec3d along, bool thrown)
        {
            if (!Load()) return;

            if (levers.TryGetValue(key, out Lever lever) && !Same(lever.Position, position))
            {
                // Стрілку перебудували, і важіль став в інше місце: рендер гри місця не міняє
                lever.Util.Dispose();
                levers.Remove(key);
                lever = null;
            }
            if (lever == null)
            {
                lever = new Lever { Chunk = chunkCoord.Clone(), Position = position.Clone() };
                lever.Util = new AnimationUtil(capi, lever.Position);
                lever.Util.InitializeAnimator(ShapeName, mesh, shape, null);
                // Рендер гри малює модель, лише поки програється анімація: важіль видно завжди
                lever.Util.renderer.ShouldRender = true;
                levers[key] = lever;

                if (thrown) Play(lever, lastFrame);
                lever.Thrown = thrown;
            }
            lever.Kept = true;
            lever.Util.renderer.CustomTransform = Placement(toTrack, along);

            if (lever.Thrown == thrown) return;
            lever.Thrown = thrown;

            if (!thrown)
            {
                lever.Util.StopAnimation(AnimationCode);
                return;
            }
            // Якщо анімація ще відмотується, рух уперед починається з кадру, до якого вона дійшла
            RunningAnimation state = lever.Util.animator.GetAnimationState(AnimationCode);
            Play(lever, state != null && state.Running ? state.CurrentFrame : 0);
        }

        // Програє переведення від кадру from. Триває воно стільки ж, скільки рух гостряків
        private void Play(Lever lever, float from)
        {
            lever.Util.StartAnimation(new AnimationMetaData
            {
                Animation = AnimationCode,
                Code = AnimationCode,
                // Аніматор гри веде 30 кадрів за секунду
                AnimationSpeed = lastFrame / 30f * 1000f / SwitchAnimator.DurationMs,
                EaseInSpeed = EaseIn,
                EaseOutSpeed = EaseOut,
                StartFrameOnce = from
            }.Init());
        }

        // Матриця, що ставить модель у площину колії. У моделі колія лежить з боку +X від важеля, а вздовж
        // колії йде вісь Z; у котрий її бік, моделі байдуже, тож беремо той, за якого верх моделі дивиться вгору
        private static float[] Placement(Vec3d toTrack, Vec3d along)
        {
            Vec3d x = toTrack.Clone().Normalize();
            double lean = along.X * x.X + along.Y * x.Y + along.Z * x.Z;
            Vec3d z = new Vec3d(along.X - x.X * lean, along.Y - x.Y * lean, along.Z - x.Z * lean).Normalize();
            Vec3d y = z.Cross(x);
            if (y.Y < 0)
            {
                z = new Vec3d(-z.X, -z.Y, -z.Z);
                y = new Vec3d(-y.X, -y.Y, -y.Z);
            }

            float[] transform =
            {
                (float)x.X, (float)x.Y, (float)x.Z, 0,
                (float)y.X, (float)y.Y, (float)y.Z, 0,
                (float)z.X, (float)z.Y, (float)z.Z, 0,
                0, 0, 0, 1
            };
            // Точка (8, 0, 8) моделі, де стоїть важіль, це середина блока
            Mat4f.Translate(transform, transform, -0.5f, 0f, -0.5f);
            return transform;
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
