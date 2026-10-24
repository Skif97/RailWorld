using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Важелі стрілок. У важеля рухаються ручка, вантаж на ній, щогла з покажчиком і тяги, кожне довкола своєї
    /// осі. Як двері в грі: поки важіль стоїть, він звичайна деталь у пачці чанка (одна з двох поз, зібраних із
    /// першого й останнього кадру анімації), зі своїм світлом і тінню, і нічого не рахує. Лише на час
    /// переведення вмикається анімована модель, і веде її гра своїми засобами: AnimationUtil програє анімацію за
    /// часом. В останнє положення важіль переходить, програючи анімацію, назад у перше, відмотуючи її; якщо його
    /// перевели назад, не дочекавшись кінця, він розвертається з того кадру, де був. Коли рух скінчився,
    /// анімована модель звільняється, і на її місце стає нерухома деталь нової пози.
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

        // Ключі мешів нерухомого важеля в рендері деталей: у першому і в останньому положенні
        private const string RestKey = "switchlever-rest";
        private const string ThrownKey = "switchlever-thrown";

        private class Lever
        {
            public Vec3i Chunk;
            public Vec3d Position;
            // Як важіль стоїть у площині колії: для нерухомої деталі і для анімованої моделі
            public Vec3f Rotation;
            public float[] Placement;
            // Анімована модель, лише поки важіль рухається; коли рух почався
            public AnimationUtil Util;
            public long MovedAtMs;
            // Анімацію поставлено на останній кадр, щоб відмотати її назад: зупинити її можна, лише коли вона там
            public bool RewindPending;
            // Чи важіль стоїть або йде в останнє положення
            public bool Thrown;
            // Чи важіль поставили знову за цю перебудову чанка
            public bool Kept;
        }

        private ICoreClientAPI capi;
        private RailWayRendererSystem parts;
        private long settleListenerId;
        private Shape shape;
        private MeshData mesh;
        // Нерухомий важіль у першому і в останньому положенні
        private MeshData restMesh, thrownMesh;
        private int lastFrame;
        private bool broken;
        private readonly ChunkKeyed<SectionEndKey, Lever> levers = new ChunkKeyed<SectionEndKey, Lever>(key => key.Section);
        // Ті з них, що зараз рухаються анімованою моделлю (Util): лише їм щось потрібно кожні 15 мс
        private readonly List<Lever> animated = new List<Lever>();

        public SwitchLeverRenderer(ICoreClientAPI capi, RailWayRendererSystem parts)
        {
            this.capi = capi;
            this.parts = parts;
            // Рух закінчується за той самий час, що й у гостряків: тоді анімована модель поступається деталі
            settleListenerId = capi.Event.RegisterGameTickListener(dt => Settle(), 15);
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

            AnimationKeyFrame last = animation.KeyFrames.FirstOrDefault(frame => frame.Frame == lastFrame);
            if (last == null)
            {
                capi.Logger.Error("[RailWorld] в анімації " + AnimationCode + " моделі " + ShapeName + " немає ключового кадру " + lastFrame + ": важелі стрілок не малюватимуться");
                broken = true;
                mesh = null;
                return false;
            }
            restMesh = Posed(loaded, null);
            thrownMesh = Posed(loaded, last);
            return true;
        }

        // Нерухома модель у позі ключового кадру frame; null це вихідна поза, перший кадр. Анімація додає кути кадру
        // до власного повороту деталі довкола тієї самої точки і зсуває її на зсув кадру у вокселях
        // (ShapeElement.GetLocalTransformMatrix, Animation): те саме робимо з копією моделі, і збирає її гра
        private MeshData Posed(Shape source, AnimationKeyFrame frame)
        {
            Shape posed = source.Clone();
            if (frame != null)
            {
                foreach (KeyValuePair<string, AnimationKeyFrameElement> pair in frame.Elements)
                {
                    ShapeElement element = posed.GetElementByName(pair.Key);
                    if (element == null) continue;
                    AnimationKeyFrameElement key = pair.Value;
                    element.RotationX += key.RotationX ?? 0;
                    element.RotationY += key.RotationY ?? 0;
                    element.RotationZ += key.RotationZ ?? 0;
                    double dx = key.OffsetX ?? 0, dy = key.OffsetY ?? 0, dz = key.OffsetZ ?? 0;
                    element.From = new[] { element.From[0] + dx, element.From[1] + dy, element.From[2] + dz };
                    element.To = new[] { element.To[0] + dx, element.To[1] + dy, element.To[2] + dz };
                }
            }
            capi.Tesselator.TesselateShape("RailWorld switchlever", posed, out MeshData result, new ShapeTextureSource(capi, posed, ShapeName));
            return result;
        }

        /// <summary>
        /// Початок і кінець перебудови чанка. Між ними кожен його важіль ставиться знову; той, що лишився
        /// на місці, зберігає свій рендер і продовжує рух, а той, якого не поставили, зникає.
        /// </summary>
        public void BeginChunk(Vec3i chunkCoord)
        {
            Dictionary<SectionEndKey, Lever> inChunk = levers.In(chunkCoord);
            if (inChunk == null) return;
            foreach (Lever lever in inChunk.Values) lever.Kept = false;
        }

        public void EndChunk(Vec3i chunkCoord)
        {
            Dictionary<SectionEndKey, Lever> inChunk = levers.In(chunkCoord);
            if (inChunk == null) return;

            List<SectionEndKey> gone = null;
            foreach (KeyValuePair<SectionEndKey, Lever> pair in inChunk)
            {
                if (!pair.Value.Kept) (gone ?? (gone = new List<SectionEndKey>())).Add(pair.Key);
            }
            if (gone == null) return;
            foreach (SectionEndKey key in gone)
            {
                Drop(inChunk[key]);
                levers.Remove(key);
            }
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            Dictionary<SectionEndKey, Lever> removed = levers.TakeChunk(chunkCoord);
            if (removed != null) foreach (Lever lever in removed.Values) Drop(lever);
        }

        /// <summary>
        /// Ставить важіль або переводить уже поставлений. toTrack це напрямок від важеля до колії, along напрямок
        /// самої колії; обидва з її ухилом, важіль стає в їхню площину. thrown каже, в якому положенні йому бути:
        /// false в першому, true в останньому (LeverPose). Важіль, якого ще не було, одразу стоїть у своєму
        /// положенні; той, що вже стояв в іншому, переходить у нове рухом.
        /// </summary>
        public void Set(Vec3i chunkCoord, SectionEndKey key, Vec3d position, Vec3d toTrack, Vec3d along, bool thrown)
        {
            if (!Load()) return;

            if (levers.TryGetValue(key, out Lever lever) && !Same(lever.Position, position))
            {
                // Стрілку перебудували, і важіль став в інше місце
                Drop(lever);
                levers.Remove(key);
                lever = null;
            }
            if (lever == null)
            {
                lever = new Lever { Chunk = chunkCoord.Clone(), Position = position.Clone(), Thrown = thrown };
                levers.Set(key, lever);
            }
            lever.Kept = true;
            lever.Rotation = RailWayRendererSystem.InPlane(toTrack, along);
            lever.Placement = Placement(toTrack, along);

            if (lever.Thrown != thrown)
            {
                lever.Thrown = thrown;
                Move(lever);
            }

            if (lever.Util == null)
            {
                Rest(lever);
                return;
            }
            lever.Util.renderer.CustomTransform = lever.Placement;
            // Поки анімація ще стає на останній кадр, видно нерухому деталь останнього положення
            if (lever.RewindPending) parts.AddMeshPart(lever.Chunk, ThrownKey, thrownMesh, lever.Position, lever.Rotation);
        }

        // Нерухомий важіль: деталь у його позі. Перебудова чанка ставить її щоразу знову
        private void Rest(Lever lever)
        {
            parts.AddMeshPart(lever.Chunk, lever.Thrown ? ThrownKey : RestKey, lever.Thrown ? thrownMesh : restMesh, lever.Position, lever.Rotation);
        }

        // Важіль починає рух у положення lever.Thrown: нерухома деталь поступається анімованій моделі
        private void Move(Lever lever)
        {
            lever.MovedAtMs = capi.ElapsedMilliseconds;

            if (lever.Util == null)
            {
                lever.Util = new AnimationUtil(capi, lever.Position);
                animated.Add(lever);
                lever.Util.InitializeAnimator(ShapeName, mesh, shape, null);
                lever.Util.renderer.CustomTransform = lever.Placement;
                // З першого положення анімація йде вперед. З останнього вона спершу стає на останній кадр, а
                // відмотується, коли там стане: кадр гра ставить на наступному кадрі, і раніша зупинка просто
                // зняла б анімацію. Доти модель не малюється, а стоїть нерухома деталь: інакше на кадр було б
                // видно першу позу
                Play(lever, lever.Thrown ? 0 : lastFrame);
                lever.RewindPending = !lever.Thrown;
                if (lever.RewindPending)
                {
                    lever.Util.renderer.ShouldRender = false;
                    return;
                }
                lever.Util.renderer.ShouldRender = true;
                Unrest(lever);
                return;
            }

            // Перевели назад, не дочекавшись кінця: розвертається з того кадру, де був
            lever.RewindPending = false;
            lever.Util.renderer.ShouldRender = true;
            Unrest(lever);
            if (!lever.Thrown)
            {
                lever.Util.StopAnimation(AnimationCode);
                return;
            }
            RunningAnimation state = lever.Util.animator.GetAnimationState(AnimationCode);
            Play(lever, state != null && state.Running ? state.CurrentFrame : 0);
        }

        // Важелі, що доїхали: анімована модель звільняється, на її місце стає нерухома деталь
        private void Settle()
        {
            long now = capi.ElapsedMilliseconds;
            // З кінця: той, що доїхав, тут же виходить зі списку
            for (int i = animated.Count - 1; i >= 0; i--)
            {
                Lever lever = animated[i];
                if (lever.RewindPending)
                {
                    RunningAnimation state = lever.Util.animator.GetAnimationState(AnimationCode);
                    if (state == null || !state.Running) continue;
                    lever.Util.StopAnimation(AnimationCode);
                    lever.Util.renderer.ShouldRender = true;
                    Unrest(lever);
                    lever.RewindPending = false;
                    lever.MovedAtMs = now;
                    continue;
                }
                if (now - lever.MovedAtMs < SwitchAnimator.DurationMs) continue;
                lever.Util.Dispose();
                lever.Util = null;
                animated.RemoveAt(i);
                Rest(lever);
            }
        }

        // Прибирає нерухому деталь важеля: її місце займає анімована модель
        private void Unrest(Lever lever)
        {
            parts.RemoveMeshPart(lever.Chunk, RestKey, lever.Position);
            parts.RemoveMeshPart(lever.Chunk, ThrownKey, lever.Position);
        }

        // Важіль зникає: звільняє анімовану модель і прибирає нерухому деталь
        private void Drop(Lever lever)
        {
            if (lever.Util != null) animated.Remove(lever);
            lever.Util?.Dispose();
            lever.Util = null;
            Unrest(lever);
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
            capi.Event.UnregisterGameTickListener(settleListenerId);
            foreach (Dictionary<SectionEndKey, Lever> inChunk in levers.All)
            {
                foreach (Lever lever in inChunk.Values) Drop(lever);
            }
            levers.Clear();
        }
    }
}
