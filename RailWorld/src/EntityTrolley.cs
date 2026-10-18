using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using System;
using Vintagestory.API.Common.Entities;
using RailWorld.src.RailWay;
using RailWorld.src.Items;
using Vintagestory.GameContent;

namespace RailWorld
{

    public class EntityTrolley : EntityAgent, ISeatInstSupplier, IRenderer
    {
        // Прискорення вільного падіння і опір коченню, блоків за секунду в квадраті
        public const double Gravity = 9.8;
        public const double RollingResistance = 0.5;

        // Опір повітря: гальмування = коефіцієнт * швидкість². Береться з атрибута airDragFactor сутності
        public const double DefaultAirDrag = 0.002;
        private double airDrag = DefaultAirDrag;

        // Швидкість, яку дає поштовх гравця, і стеля швидкості, блоків за секунду
        public const double PushSpeed = 2;
        public const double MaxSpeed = 30;
        // Вище цієї швидкості поштовх гравця вже нічого не додає
        public const double MaxPushSpeed = 10;

        // Пасажир, тримаючи «вперед» чи «назад», сам потроху розганяє вагонетку: до швидкості ходи навприсядки,
        // із прискоренням понад опір кочення, блоків за секунду в квадраті.
        // Швидкість ходи навприсядки з фізики гри: за тік 1/60 с до руху додається BaseMoveSpeed / 60 * SneakSpeedMultiplier,
        // потім він множиться на опір землі 0.7 і повітря 0.983^0.55. Усталене значення це 1.19 блока за секунду
        public const double DriveMaxSpeed = 1.2;
        public const double DriveAccel = 0.6;

        // Рейки з цього матеріалу розганяють вагонетку, тимчасова заміна штовхачів.
        // Секція з обома такими рейками при наїзді на неї додає стільки швидкості, скільки дав би
        // з'їзд із гірки такої висоти в блоках; з однією рейкою половину
        public const string BoostRailMaterial = "gold";
        public const double BoostHeight = 15;

        // На якій за рахунком секції поспіль без шпали вагонетка сходить з колії
        public const int MaxSectionsWithoutSleeper = 5;

        // Стан на колії. Живе на сервері, зберігається в Attributes сутності
        private bool onRail;
        private Vec3i railChunk = new Vec3i();
        private int railIndex;
        // Відстань від початку поточної секції вздовж неї
        private double railS;
        // Швидкість уздовж поточної секції: додатна в бік її кінця, від'ємна в бік початку
        private double railVelocity;
        private int sectionsWithoutSleeper;
        // Куди дивиться перед вагонетки відносно поточної секції: 1 у бік її кінця, -1 у бік початку.
        // Сусідні ділянки можуть бути прокладені назустріч, тому при переході це треба перераховувати
        private int railFacing = 1;

        // Мотор. Швидкості його передач у блоках за секунду, від першої до найвищої; порожньо означає,
        // що мотора немає. Беруться з атрибутів сутності
        private double[] motorGears = new double[0];
        // Наскільки швидко мотор розганяє і наскільки швидко гальмує, блоків за секунду в квадраті
        public const double MotorAccel = 4;
        public const double MotorBrake = 6;

        // Мотор вимкнено: вагонетка котиться сама, як звичайна. Так завжди, коли в ній ніхто не сидить
        private const int MotorOff = 0;
        // Мотор тримає свою швидкість і на підйомі, і на спуску
        private const int MotorDrive = 1;
        // Мотор гальмує до зупинки і тримає вагонетку на місці, навіть на ухилі
        private const int MotorHold = 2;

        private int motorMode = MotorOff;
        // Куди мотор везе відносно поточної секції: 1 у бік її кінця, -1 у бік початку
        private int motorDir = 1;
        // Увімкнена передача, рахуючи з одиниці. Має зміст, лише поки мотор везе
        private int motorGear;
        // Чи тримав пасажир «вперед» і «назад» минулого тіку: передачі перемикаються натисканням, а не утриманням
        private bool forwardWasDown;
        private bool backwardWasDown;

        // Клік, яким вагонетку поставили, не має ні штовхати її, ні садити в неї
        private const long InteractDelayAfterSpawnMs = 1000;
        private long spawnedAtMs;

        // На колії вагонетку веде наш код, тому тяжіння вимкнене. Поза колією це звичайна сутність
        public override bool ApplyGravity
        {
            get { return !WatchedAttributes.GetBool("onRail"); }
        }

        public override bool IsInteractable
        {
            get { return true; }
        }

        // Гра сама нахиляє сутності вбік на поворотах і вперед на сходинках, як коня під вершником.
        // Вона вмикає це, коли вважає сутність такою, що стоїть на землі, а з пасажиром вагонетка саме такою і стає.
        // Вагонетці це не потрібно: її нахил повністю задає колія
        public override bool CanSwivel
        {
            get { return false; }
        }

        public override bool CanStepPitch
        {
            get { return false; }
        }

        public override float MaterialDensity
        {
            get { return 30000f; }
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            Properties.KnockbackResistance = 0.95f;

            // Клієнт: вагонетка сама ставить себе на екрані раз на кадр, як це робить човен гри (EntityBoat)
            if (api is ICoreClientAPI clientApi) clientApi.Event.RegisterRenderer(this, EnumRenderStage.Before, "railworld-trolley");

            touchDistanceSq = (double)Math.Max(0.001f, SelectionBox.XSize);

            airDrag = properties.Attributes?["airDragFactor"].AsDouble(DefaultAirDrag) ?? DefaultAirDrag;
            motorGears = properties.Attributes?["motorGears"].AsArray<double>(null) ?? new double[0];

            spawnedAtMs = World.ElapsedMilliseconds;
            if (api.Side == EnumAppSide.Server) LoadRailState();
        }

        private void LoadRailState()
        {
            onRail = Attributes.GetBool("onRail");
            railChunk.Set(Attributes.GetInt("railChunkX"), Attributes.GetInt("railChunkY"), Attributes.GetInt("railChunkZ"));
            railIndex = Attributes.GetInt("railSectionIndex");
            railS = Attributes.GetDouble("railS");
            railVelocity = Attributes.GetDouble("railVelocity");
            sectionsWithoutSleeper = Attributes.GetInt("railNoSleeper");
            railFacing = Attributes.GetInt("railFacing", 1) < 0 ? -1 : 1;
            motorMode = Attributes.GetInt("motorMode", MotorOff);
            motorDir = Attributes.GetInt("motorDir", 1) < 0 ? -1 : 1;
            motorGear = Attributes.GetInt("motorGear");
            SetOnRail(onRail);
            SyncTrackPlace();
        }

        private void SaveRailState()
        {
            Attributes.SetBool("onRail", onRail);
            Attributes.SetInt("railChunkX", railChunk.X);
            Attributes.SetInt("railChunkY", railChunk.Y);
            Attributes.SetInt("railChunkZ", railChunk.Z);
            Attributes.SetInt("railSectionIndex", railIndex);
            Attributes.SetDouble("railS", railS);
            Attributes.SetDouble("railVelocity", railVelocity);
            Attributes.SetInt("railNoSleeper", sectionsWithoutSleeper);
            Attributes.SetInt("railFacing", railFacing);
            Attributes.SetInt("motorMode", motorMode);
            Attributes.SetInt("motorDir", motorDir);
            Attributes.SetInt("motorGear", motorGear);
        }

        // Нахил полотна під вагонеткою, радіани. У кути сутності він не вміщається (див. ModMath.TrackFrameToEntityAngles),
        // тому їде клієнту окремим числом, а той ставить його в рендер
        public const string CantAttribute = "railCant";
        // Нахил, з яким вагонетка намальована зараз: клієнт плавно веде його до присланого
        private float shownCant;

        /// <summary>Нахил полотна під вагонеткою: на клієнті той, що зараз на екрані, на сервері останній порахований.</summary>
        public float Cant => Api?.Side == EnumAppSide.Client ? shownCant : WatchedAttributes.GetFloat(CantAttribute);

        private void SetCant(float value)
        {
            if (Math.Abs(WatchedAttributes.GetFloat(CantAttribute) - value) < 0.0005f) return;
            WatchedAttributes.SetFloat(CantAttribute, value);
            WatchedAttributes.MarkPathDirty(CantAttribute);
        }

        // Клієнт: передає нахил полотна в рендер. xangle це додатковий поворот навколо осі X моделі після всіх
        // кутів сутності; сама гра ним гойдає сутності на воді
        // Місце в кадрі: після того як гра довела положення сутностей до присланих сервером (порядок 0)
        // і до того, як аніматор порахує пози моделі (порядок 0.4). Тоді і кути, і кадри анімацій, поставлені
        // тут, діють у цьому ж кадрі
        public double RenderOrder => 0.2;
        public int RenderRange => 999;

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (Api is ICoreClientAPI clientApi && clientApi.IsGamePaused) return;
            ClientFrame(dt);
        }

        public void Dispose()
        {
        }

        /// <summary>
        /// Клієнт, раз на кадр: ставить вагонетку на рейки на екрані.
        /// Положення приходять із сервера кілька разів на секунду, і гра веде сутність між двома останніми
        /// (EntityBehaviorInterpolatePosition) навпростець, а кути доводить окремо, нічого не знаючи про колію.
        /// На великій швидкості вагонетка за цей час проїжджає більше блока: на кривій вона зрізала б кут, а
        /// повернута була б не так, як лежить колія під нею. Тому між тими самими двома положеннями ведемо її
        /// колією і кути беремо з колії в тому місці, де вона зараз (PlaceOnTrack).
        /// Писати в Pos після гри можна: її поведінка тримає власний стан і назад Pos не читає, а рендер читає
        /// Pos уже після нас. Так само свій курс пише човен.
        /// </summary>
        private void ClientFrame(float dt)
        {
            if (WatchedAttributes.GetBool("onRail") && PlaceOnTrack(out float cant))
            {
                shownCant = cant;
            }
            else
            {
                // Не на колії або колії, якою вона їде, клієнт ще не знає: як прислав сервер
                float target = WatchedAttributes.GetFloat(CantAttribute);
                shownCant += (target - shownCant) * Math.Min(1f, dt * 10f);
            }

            // Нахил полотна це додатковий поворот навколо поздовжньої осі моделі: для таких поворотів
            // у рендера є поля xangle, yangle, zangle, ними ж гра качає човен
            if (Properties.Client.Renderer is EntityShapeRenderer renderer) renderer.xangle = shownCant;

            ShowMotion(dt);
        }

        // На скільки блоків у кожен бік від своєї секції клієнт бере колію: з запасом більше, ніж намальована
        // вагонетка відстає від присланої і ніж вона проїжджає між двома положеннями
        private const double ChainReach = 6;
        // Прислане положення, далі за це від колії, вже не на ній, блоків
        private const double OnTrackDistance = 0.5;

        private RailWaySystem railSystem;
        private System.Func<SectionLink, Section> findClientSection;
        private EntityBehaviorInterpolatePosition interpolation;
        private readonly TrackChain chain = new TrackChain();
        // Від якої секції і з якою колією клієнта ланцюжок збудовано: доки ні те, ні інше не змінилося, він
        // той самий, і щокадру не перебудовується
        private int chainChunkX, chainChunkY, chainChunkZ, chainSection, chainRevision = -1;
        private bool chainBuilt;

        // Ставить вагонетку на колію між двома положеннями, між якими її зараз веде гра: туди, куди та поставила б
        // її навпростець, тільки колією. Секцію, від якої шукати, каже сервер (SyncTrackPlace); положення й
        // номер секції приходять різними пакетами (положення UDP, атрибути TCP), тож на переході між секціями
        // одне з них може випередити інше, і положення шукаються на кілька секцій в обидва боки від неї.
        // false, якщо колії, якою вона їде, клієнт ще не знає
        private bool PlaceOnTrack(out float cant)
        {
            cant = 0;

            if (railSystem == null)
            {
                railSystem = Api.ModLoader.GetModSystem<RailWaySystem>();
                if (railSystem == null) return false;
                findClientSection = railSystem.FindClientSection;
            }
            if (interpolation == null) interpolation = GetBehavior<EntityBehaviorInterpolatePosition>();
            if (interpolation == null) return false;

            if (!UpdateChain()) return false;

            double halfHeight = SelectionBox.Y2 / 2;
            PositionSnapshot from = interpolation.pL, to = interpolation.pN;
            if (!chain.Locate(from.x, from.y, from.z, halfHeight, OnTrackDistance, out double atFrom)) return false;
            if (!chain.Locate(to.x, to.y, to.z, halfHeight, OnTrackDistance, out double atTo)) return false;

            // Яку частку шляху між ними гра вже пройшла: так само, як рахує вона сама. Поки нового положення
            // немає (wait), вона стоїть у останньому
            float done = interpolation.wait != 0 || to.interval <= 0 ? 1 : GameMath.Clamp(interpolation.dtAccum / to.interval, 0, 1);
            if (!chain.At(atFrom + (atTo - atFrom) * done, out Section here, out SectionChord chord, out double s, out bool reversed)) return false;

            // Перед дивиться в бік кінця своєї секції або початку; на секції, якою ланцюжок іде назустріч, навпаки
            int facing = WatchedAttributes.GetInt(TrackFacing, 1) < 0 ? -1 : 1;
            RailPose.PlaceSmooth(Pos, here, chord, s, halfHeight, reversed ? -facing : facing, out cant);
            return true;
        }

        // Ланцюжок колії від секції, яку назвав сервер. Будується заново, лише коли сервер назвав іншу секцію
        // або колія в клієнта змінилася (ClientRevision): прокладено, розібрано, переведено стрілку.
        // false, якщо цієї секції клієнт не знає
        private bool UpdateChain()
        {
            int chunkX = WatchedAttributes.GetInt(TrackChunkX), chunkY = WatchedAttributes.GetInt(TrackChunkY), chunkZ = WatchedAttributes.GetInt(TrackChunkZ);
            int index = WatchedAttributes.GetInt(TrackSection);
            int revision = railSystem.ClientRevision;
            if (chainBuilt && revision == chainRevision && index == chainSection && chunkX == chainChunkX && chunkY == chainChunkY && chunkZ == chainChunkZ) return true;

            chainChunkX = chunkX; chainChunkY = chunkY; chainChunkZ = chunkZ;
            chainSection = index;
            chainRevision = revision;

            DataInChunk data = DataInChunk.GetClient(new Vec3i(chunkX, chunkY, chunkZ));
            Section section = null;
            chainBuilt = data != null && data.RailWaySections.TryGetValue(index, out section);
            if (chainBuilt) chain.Build(section, ChainReach, findClientSection);
            return chainBuilt;
        }

        // ── Рух на екрані: колеса, гойдання кузова, поштовхи, скрегіт ──
        // Лише вигляд, на клієнті. Рухи це анімації моделі (shapes/entity/trolley.json), оголошені в сутності
        // (entities/trolley.json). Код їх лише вмикає і задає темп; кузов рухається відносно коліс,
        // як на підвісці, сама сутність при цьому стоїть на рейках.

        private const string SpinAnimation = "spin";
        private const string SwayAnimation = "sway";
        private const string BumpAnimation = "bump";

        // Радіус колеса, блоків: з нього кут, на який колеса повертаються за пройдений шлях
        private const double WheelRadius = 0.3;
        // Темп гойдання кузова на прямій відносно записаного в анімації (там один розмах за 4 секунди)
        // і в скільки разів він зростає в крутій кривій: до 1 + SwayCurveSpeedup
        private const float SwayBaseSpeed = 0.9f;
        private const float SwayCurveSpeedup = 1.8f;
        // Поштовхи йдуть через випадковий проміжок у цих межах, секунди руху
        private const float BumpEveryMin = 1.2f;
        private const float BumpEveryMax = 4.5f;
        // З якого радіуса і крутіше колеса скрегочуть об рейки, і на якій швидкості щонайменше
        private static double SquealRadius => TrackRules.MinRadius * 1.4;
        private const float SquealSpeed = 0.4f;

        private Vec3d shownAt;
        private float shownYaw;
        private float shownSpeed;
        private float shownCurve;
        // Скільки обертів зробили колеса
        private double wheelTurns;
        private float nextBumpIn = 2;
        // Гойдання має власний темп у кожної вагонетки, тому в нього власна копія опису анімації
        private AnimationMetaData swayMeta;
        private ILoadedSound squeal;


        private void ShowMotion(float dt)
        {
            Vec3d now = Pos.XYZ;
            if (shownAt == null || dt <= 0)
            {
                shownAt = now;
                shownYaw = Pos.Yaw;
                return;
            }

            // Шлях за кадр уздовж носа вагонетки: зі знаком, уперед чи назад
            Vec3f nose = Pos.GetViewVector();
            double rolled = (now.X - shownAt.X) * nose.X + (now.Z - shownAt.Z) * nose.Z;
            double moved = now.DistanceTo(shownAt);
            float turned = Math.Abs(GameMath.AngleRadDistance(shownYaw, Pos.Yaw));
            shownAt = now;
            shownYaw = Pos.Yaw;

            // Швидкість і кривизна шляху, згладжені: окремий кадр дає їх із шумом
            shownSpeed += ((float)(moved / dt) - shownSpeed) * Math.Min(1f, dt * 5f);
            float curve = moved > 0.002 ? (float)(turned / moved) : 0;
            shownCurve += (curve - shownCurve) * Math.Min(1f, dt * 4f);

            // Наскільки вагонетка їде, від 0 до 1, і наскільки крута крива під нею
            float moving = GameMath.Clamp(shownSpeed / 1.5f, 0, 1);
            float curved = GameMath.Clamp(shownCurve * (float)ModMath.CantFromRadius, 0, 1);

            // Аніматор з'являється не одразу: гра створює його, коли збере модель сутності. Доти анімацій
            // немає, і питати про них не можна. Так само це перевіряє човен гри
            if (AnimManager?.Animator != null)
            {
                SpinWheels(rolled);
                SwayBody(moving, curved);
                BumpBody(dt, moving);
            }
            Squeal(now);
        }

        /// <summary>
        /// Колеса: анімація одного оберту, яка сама не йде (швидкість нуль); кадр ставиться з пройденого
        /// шляху. Так само гра веде флюгер на човні (EntityBoat): при нульовій швидкості аніматор не рахує
        /// ні вагу анімації, ні її згладжування, тож обидві ставляться тут.
        /// </summary>
        private void SpinWheels(double rolled)
        {
            wheelTurns += rolled / (2 * Math.PI * WheelRadius);

            if (!AnimManager.IsAnimationActive(SpinAnimation)) AnimManager.StartAnimation(SpinAnimation);
            RunningAnimation spin = AnimManager.GetAnimationState(SpinAnimation);
            if (spin == null) return;

            // Частка оберту від 0 до 1; останній кадр анімації це той самий кут, що й перший
            double part = wheelTurns - Math.Floor(wheelTurns);
            spin.CurrentFrame = (float)(part * (spin.Animation.QuantityFrames - 1));
            spin.BlendedWeight = 1f;
            spin.EasingFactor = 1f;
        }

        // Кузов гойдається з боку на бік, поки вагонетка їде; у кривій частіше. Анімацію веде сама гра,
        // тут лише її темп
        private void SwayBody(float moving, float curved)
        {
            if (moving < 0.05f)
            {
                if (AnimManager.IsAnimationActive(SwayAnimation)) AnimManager.StopAnimation(SwayAnimation);
                return;
            }

            if (swayMeta == null)
            {
                if (!Properties.Client.AnimationsByMetaCode.TryGetValue(SwayAnimation, out AnimationMetaData declared)) return;
                swayMeta = declared.Clone();
            }
            swayMeta.AnimationSpeed = SwayBaseSpeed * (1 + SwayCurveSpeedup * curved);
            if (!AnimManager.IsAnimationActive(SwayAnimation)) AnimManager.StartAnimation(swayMeta);
        }

        // Поштовх: кузов коротко підскакує на підвісці. Анімація грає один раз і сама зупиняється
        private void BumpBody(float dt, float moving)
        {
            if (moving < 0.05f) return;

            nextBumpIn -= dt * moving;
            if (nextBumpIn > 0) return;

            nextBumpIn = BumpEveryMin + (float)Api.World.Rand.NextDouble() * (BumpEveryMax - BumpEveryMin);
            AnimManager.StartAnimation(BumpAnimation);
        }

        // Скрегіт коліс у дуже крутій кривій
        private void Squeal(Vec3d at)
        {
            bool tight = shownCurve > 1 / SquealRadius && shownSpeed > SquealSpeed;
            if (!tight)
            {
                if (squeal != null && squeal.IsPlaying) squeal.Stop();
                return;
            }

            if (squeal == null && Api is ICoreClientAPI clientApi)
            {
                squeal = clientApi.World.LoadSound(new SoundParams
                {
                    Location = new AssetLocation("game:sounds/effect/metalgrinding"),
                    ShouldLoop = true,
                    Position = at.ToVec3f(),
                    DisposeOnFinish = false,
                    Volume = 0.5f,
                    Range = 20
                });
            }
            if (squeal == null) return;

            squeal.SetPosition(at.ToVec3f());
            if (!squeal.IsPlaying) squeal.Start();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (Api is ICoreClientAPI clientApi) clientApi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            squeal?.Stop();
            squeal?.Dispose();
            squeal = null;
            base.OnEntityDespawn(despawn);
        }

        // Клієнту потрібно знати, чи вагонетка на колії: від цього залежать тяжіння і те, як він її малює
        private void SetOnRail(bool value)
        {
            onRail = value;
            if (WatchedAttributes.GetBool("onRail") == value) return;
            WatchedAttributes.SetBool("onRail", value);
            WatchedAttributes.MarkPathDirty("onRail");
        }

        // Секція, на якій вагонетка зараз, і куди дивиться її перед відносно цієї секції, для клієнта: від неї
        // він веде вагонетку колією (PlaceOnTrack). Міняються лише при переході на іншу секцію, тож і
        // відправляються гравцям лише тоді
        private const string TrackChunkX = "railChunkX";
        private const string TrackChunkY = "railChunkY";
        private const string TrackChunkZ = "railChunkZ";
        private const string TrackSection = "railSectionIndex";
        private const string TrackFacing = "railFacing";

        private void SyncTrackPlace()
        {
            SetWatched(TrackChunkX, railChunk.X);
            SetWatched(TrackChunkY, railChunk.Y);
            SetWatched(TrackChunkZ, railChunk.Z);
            SetWatched(TrackSection, railIndex);
            SetWatched(TrackFacing, railFacing);
        }

        private void SetWatched(string key, int value)
        {
            if (WatchedAttributes.HasAttribute(key) && WatchedAttributes.GetInt(key) == value) return;
            WatchedAttributes.SetInt(key, value);
            WatchedAttributes.MarkPathDirty(key);
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            // Клієнт ставить вагонетку на екрані сам, раз на кадр: див. ClientFrame
            if (Api.Side == EnumAppSide.Client) return;
            if (!onRail) return;

            TickOnRail(dt);
            SaveRailState();
        }

        private enum EnterResult { Entered, Blocked, Derailed }

        // Рух рахується аналітично по секціях: на секції прискорення стале, тому швидкість у її кінці
        // береться з v² = u² + 2as, а час проїзду з t = (v - u) / a. Результат не залежить від частоти тіків
        private void TickOnRail(float dt)
        {
            // Секцію беремо з даних чанка щотіку: її могли розібрати або видалити під вагонеткою
            Section section = GetSection(railChunk, railIndex, out bool chunkLoaded);
            if (section == null)
            {
                // Чанк вивантажений: чекаємо. Секції немає: колії під нами більше нема
                if (chunkLoaded) Derail(null);
                return;
            }
            if (!section.RailsInstalled)
            {
                Derail(section);
                return;
            }

            double timeLeft = dt;

            // Моторизовану вагонетку пасажир не розганяє сам: він лише вмикає мотор і гальмує
            bool hasMotor = motorGears.Length > 0;
            Vec3d drive = hasMotor ? null : GetDriveDirection();
            double motorTarget = hasMotor ? UpdateMotor(section) : 0;

            // Запобіжник: за тік не буває стільки переходів між секціями і зупинок
            for (int guard = 0; guard < 256 && timeLeft > 0; guard++)
            {
                double length = RailPose.Length(section);
                if (length < 1e-9) break;

                Vec3d start = section.FullStartPosition;
                Vec3d end = section.FullEndPosition;

                // Складова тяжіння вздовж осі секції (від початку до кінця): вниз по ухилу додатна
                double slopeAccel = -Gravity * (end.Y - start.Y) / length;

                // Пасажир розганяє вагонетку. Тяга долає опір кочення і ще трохи, і зникає на граничній швидкості
                if (drive != null)
                {
                    int driveSign = (end.X - start.X) * drive.X + (end.Z - start.Z) * drive.Z >= 0 ? 1 : -1;
                    if (railVelocity * driveSign < DriveMaxSpeed) slopeAccel += driveSign * (RollingResistance + DriveAccel);
                }

                // Мотор увімкнено: швидкість задає він, а не ухил. Цільова швидкість уздовж осі секції зі знаком
                bool governed = hasMotor && motorMode != MotorOff;
                double motorVelocity = motorMode == MotorDrive ? motorDir * motorTarget : 0;

                // Напрямок руху. Якщо стоїмо, його задає ухил, але лише коли він сильніший за опір
                int dir;
                if (railVelocity != 0)
                {
                    dir = Math.Sign(railVelocity);
                }
                else if (governed)
                {
                    // Стоїмо під мотором: або рушаємо, куди він везе, або він тримає нас на місці
                    if (motorVelocity == 0) break;
                    dir = Math.Sign(motorVelocity);
                }
                else if (Math.Abs(slopeAccel) > RollingResistance)
                {
                    dir = Math.Sign(slopeAccel);
                }
                else
                {
                    break;
                }

                double speed = Math.Abs(railVelocity);
                // Прискорення в напрямку руху: ухил допомагає або заважає, опір кочення і повітря завжди проти.
                // Опір повітря залежить від швидкості, а формули вимагають сталого прискорення, тому на цей відрізок
                // беремо його за швидкістю на початку відрізка. Відрізки короткі, похибка мала
                double accel = dir * slopeAccel - RollingResistance - airDrag * speed * speed;
                double distance = dir > 0 ? length - railS : railS;

                if (governed)
                {
                    // Швидкість, якої хоче мотор, у напрямку теперішнього руху. Від'ємна, якщо везти треба в інший бік:
                    // тоді спершу гальмуємо до нуля, а далі рушаємо вже туди
                    double wanted = dir * motorVelocity;

                    if (Math.Abs(wanted - speed) < 1e-6)
                    {
                        // Уже їдемо, як треба: ухил і опір мотор компенсує повністю
                        accel = 0;
                    }
                    else
                    {
                        accel = wanted > speed ? MotorAccel : -MotorBrake;

                        // Прискорення стале, тому точно відомо, коли і де швидкість зрівняється з потрібною.
                        // Якщо це станеться в цій секції і в цьому тіку, доїжджаємо до того місця і далі тримаємо її
                        if (wanted >= 0)
                        {
                            double timeToTarget = (wanted - speed) / accel;
                            double distanceToTarget = (wanted * wanted - speed * speed) / (2 * accel);

                            if (timeToTarget <= timeLeft && distanceToTarget <= distance)
                            {
                                Advance(dir, speed, accel, timeToTarget, length);
                                railVelocity = dir * wanted;
                                timeLeft -= timeToTarget;
                                continue;
                            }
                        }
                    }
                }

                if (distance > 1e-9)
                {
                    // Чи доїде до кінця секції і за який час
                    bool reachesEnd;
                    double timeToEnd = 0;
                    double speedAtEnd = 0;

                    if (Math.Abs(accel) < 1e-9)
                    {
                        // Рівномірний рух, формули з діленням на прискорення тут не працюють
                        reachesEnd = speed > 0;
                        if (reachesEnd)
                        {
                            timeToEnd = distance / speed;
                            speedAtEnd = speed;
                        }
                    }
                    else
                    {
                        double squared = speed * speed + 2 * accel * distance;
                        reachesEnd = squared > 0;
                        if (reachesEnd)
                        {
                            speedAtEnd = Math.Sqrt(squared);
                            timeToEnd = (speedAtEnd - speed) / accel;
                        }
                    }

                    if (!reachesEnd)
                    {
                        // Зупиниться всередині секції. Сюди потрапляємо лише з від'ємним прискоренням
                        double timeToStop = accel < 0 ? -speed / accel : 0;
                        if (timeToStop > timeLeft)
                        {
                            Advance(dir, speed, accel, timeLeft, length);
                            timeLeft = 0;
                        }
                        else
                        {
                            Advance(dir, speed, accel, timeToStop, length);
                            railVelocity = 0;
                            // Залишок часу не викидаємо: на наступному проході ухил може покотити назад
                            timeLeft -= timeToStop;
                        }
                        continue;
                    }

                    if (timeToEnd > timeLeft)
                    {
                        Advance(dir, speed, accel, timeLeft, length);
                        timeLeft = 0;
                        continue;
                    }

                    // Доїхали до кінця секції
                    railS = dir > 0 ? length : 0;
                    railVelocity = dir * Math.Min(speedAtEnd, MaxSpeed);
                    timeLeft -= timeToEnd;
                }

                EnterResult result = EnterNext(ref section, dir < 0);
                if (result == EnterResult.Derailed) return;
                if (result == EnterResult.Blocked) break;
            }

            RailPose.Place(Pos, section, railS, SelectionBox.Y2 / 2, railFacing, out float cant);
            SetCant(cant);
            Pos.Motion.Set(0, 0, 0);
        }

        // Читає керування пасажира моторизованої вагонетки і повертає швидкість, яку мотор має тримати.
        // «Вперед» це газ: перше натискання рушає в той бік колії, куди пасажир дивиться, кожне наступне
        // вмикає вищу передачу. «Назад» це гальмо: кожне натискання скидає передачу, а з першої зупиняє
        // вагонетку і тримає її на місці, навіть на ухилі. Напрямок вибирається лише при рушанні: далі
        // погляд ні на що не впливає. Без пасажира мотор вимкнений, і вагонетка котиться сама
        private double UpdateMotor(Section section)
        {
            EntityBehaviorSeatable seatable = GetBehavior<EntityBehaviorSeatable>();
            IMountableSeat driver = null;
            if (seatable?.Seats != null)
            {
                foreach (IMountableSeat seat in seatable.Seats)
                {
                    if (seat?.Passenger != null && seat.Controls != null)
                    {
                        driver = seat;
                        break;
                    }
                }
            }

            if (driver == null)
            {
                motorMode = MotorOff;
                motorGear = 0;
                forwardWasDown = false;
                backwardWasDown = false;
                return 0;
            }

            bool forward = driver.Controls.Forward;
            bool backward = driver.Controls.Backward;
            bool gas = forward && !forwardWasDown;
            bool brake = backward && !backwardWasDown;
            forwardWasDown = forward;
            backwardWasDown = backward;

            if (gas && !brake)
            {
                if (motorMode == MotorDrive)
                {
                    motorGear = Math.Min(motorGear + 1, motorGears.Length);
                }
                else
                {
                    Vec3f view = driver.Passenger.Pos.GetViewVector();
                    Vec3d start = section.FullStartPosition;
                    Vec3d end = section.FullEndPosition;
                    motorDir = (end.X - start.X) * view.X + (end.Z - start.Z) * view.Z >= 0 ? 1 : -1;
                    motorMode = MotorDrive;
                    motorGear = 1;
                }
                ShowGear(driver.Passenger);
            }
            else if (brake && !gas)
            {
                if (motorMode == MotorDrive && motorGear > 1)
                {
                    motorGear--;
                }
                else
                {
                    motorMode = MotorHold;
                    motorGear = 0;
                }
                ShowGear(driver.Passenger);
            }

            return motorMode == MotorDrive ? motorGears[GameMath.Clamp(motorGear, 1, motorGears.Length) - 1] : 0;
        }

        // Коротке повідомлення пасажирові посеред екрана: яка передача і швидкість
        private void ShowGear(Entity passenger)
        {
            IServerPlayer player = (passenger as EntityPlayer)?.Player as IServerPlayer;
            if (player == null) return;

            string text = motorMode == MotorDrive
                ? "Передача " + motorGear + " з " + motorGears.Length + ": " + motorGears[motorGear - 1] + " блоків за секунду"
                : "Гальмо";
            player.SendIngameError("trolleygear", text);
        }

        // Куди пасажир хоче їхати: горизонтальний напрямок його погляду, якщо тримає «вперед»,
        // зворотний, якщо «назад». null, якщо ніхто не керує
        private Vec3d GetDriveDirection()
        {
            EntityBehaviorSeatable seatable = GetBehavior<EntityBehaviorSeatable>();
            if (seatable?.Seats == null) return null;

            foreach (IMountableSeat seat in seatable.Seats)
            {
                if (seat?.Passenger == null || seat.Controls == null) continue;

                bool forward = seat.Controls.Forward;
                bool backward = seat.Controls.Backward;
                if (forward == backward) continue;

                Vec3f view = seat.Passenger.Pos.GetViewVector();
                double length = Math.Sqrt(view.X * view.X + view.Z * view.Z);
                if (length < 1e-6) continue;

                double sign = forward ? 1 : -1;
                return new Vec3d(view.X / length * sign, 0, view.Z / length * sign);
            }
            return null;
        }

        // Проїжджає час t усередині секції зі сталим прискоренням: s = ut + at²/2, v = u + at
        private void Advance(int dir, double speed, double accel, double time, double length)
        {
            double travelled = speed * time + 0.5 * accel * time * time;
            railS = GameMath.Clamp(railS + dir * travelled, 0, length);
            railVelocity = dir * GameMath.Clamp(speed + accel * time, 0, MaxSpeed);
        }

        // Переїжджає з краю поточної секції в сусідню. Швидкість за модулем не змінюється
        private EnterResult EnterNext(ref Section section, bool leaveAtStart)
        {
            RailDataSession session = new RailDataSession(World);
            SectionStep next = SectionLinker.GetNext(session, section, leaveAtStart);
            bool linked = section.GetLinks(leaveAtStart).Count > 0;
            if (session.HasChanges) session.SaveAll();

            if (next == null)
            {
                if (linked)
                {
                    // Далі колія є, але її чанк не завантажений: стоїмо на краю
                    railVelocity = 0;
                    return EnterResult.Blocked;
                }

                // Колія скінчилася
                Derail(section);
                return EnterResult.Derailed;
            }

            if (!next.Section.RailsInstalled)
            {
                Derail(section);
                return EnterResult.Derailed;
            }

            // Стрілку саме переводять: гостряки між двома положеннями, проїхати не можна ні з якого боку.
            // Це або стрілка на кінці, з якого виїжджаємо, або та, на яку заїжджаємо з боку її гілок
            long now = World.ElapsedMilliseconds;
            if (section.IsSwitching(leaveAtStart, now) || next.Section.IsSwitching(next.EnterAtStart, now))
            {
                Derail(section);
                return EnterResult.Derailed;
            }

            sectionsWithoutSleeper = next.Section.SleeperInstalled ? 0 : sectionsWithoutSleeper + 1;
            if (sectionsWithoutSleeper >= MaxSectionsWithoutSleeper)
            {
                Derail(section);
                return EnterResult.Derailed;
            }

            double speed = Math.Abs(railVelocity);
            section = next.Section;

            // Вагонетка розчищає колію, якою їде
            TrackBed.ClearSnow(World, section, TrackBed.SnowAfterTrolley);

            // Розгінні рейки: один раз при наїзді на секцію, в напрямку руху.
            // Рахуємо як з'їзд із гірки: v² = u² + 2gh
            int boostRails = (section.GetMaterial(SectionPart.FirstRail) == BoostRailMaterial ? 1 : 0)
                + (section.GetMaterial(SectionPart.SecondRail) == BoostRailMaterial ? 1 : 0);
            if (boostRails > 0)
            {
                speed = Math.Min(Math.Sqrt(speed * speed + 2 * Gravity * BoostHeight * boostRails / 2.0), MaxSpeed);
            }
            railChunk.Set(section.ChunkAddres.X, section.ChunkAddres.Y, section.ChunkAddres.Z);
            railIndex = section.IndexInChunk;

            // Виїхали через кінець і заїхали через кінець (або початок і початок): осі секцій дивляться назустріч,
            // тож відносно нової осі перед вагонетки тепер з іншого боку
            if (leaveAtStart == next.EnterAtStart)
            {
                railFacing = -railFacing;
                // З тієї самої причини перевертається і бік, у який везе мотор
                motorDir = -motorDir;
            }

            // Якщо заїхали через кінець сусідньої секції, їдемо в бік її початку
            if (next.EnterAtStart)
            {
                railS = 0;
                railVelocity = speed;
            }
            else
            {
                railS = RailPose.Length(section);
                railVelocity = -speed;
            }
            SyncTrackPlace();
            return EnterResult.Entered;
        }

        // Сходить з колії: далі це звичайна сутність із тяжінням, яка летить із тією швидкістю, що мала
        private void Derail(Section section)
        {
            if (section != null)
            {
                double length = RailPose.Length(section);
                if (length > 1e-9)
                {
                    Vec3d start = section.FullStartPosition;
                    Vec3d end = section.FullEndPosition;
                    // Motion рахується в блоках за 1/60 секунди
                    double k = railVelocity / length / 60;
                    Pos.Motion.Set((end.X - start.X) * k, (end.Y - start.Y) * k, (end.Z - start.Z) * k);
                }
            }

            railVelocity = 0;
            sectionsWithoutSleeper = 0;
            Pos.Pitch = 0;
            Pos.Roll = 0;
            SetCant(0);
            SetOnRail(false);
            SaveRailState();
        }

        // Сидіння для стандартної поведінки seatable
        public IMountableSeat CreateSeat(IMountable mountable, string seatId, SeatConfig config)
        {
            return new TrolleySeat(mountable, seatId, config);
        }

        // Правий клік садить у вагонетку (це робить поведінка seatable), Shift + правий клік штовхає її по колії
        public override void OnInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
        {
            if (mode != EnumInteractMode.Interact)
            {
                base.OnInteract(byEntity, slot, hitPosition, mode);
                return;
            }

            // Клік, яким вагонетку щойно поставили, і клік із вагонеткою в руці нічого не роблять
            if (World.ElapsedMilliseconds - spawnedAtMs < InteractDelayAfterSpawnMs) return;
            if (slot?.Itemstack?.Item is ItemTrolley) return;

            if (byEntity.Controls.ShiftKey)
            {
                if (Api.Side == EnumAppSide.Server && onRail) Push(byEntity);
                return;
            }

            base.OnInteract(byEntity, slot, hitPosition, mode);
        }

        // Штовхає вздовж колії в той бік, куди дивиться гравець
        private void Push(EntityAgent byEntity)
        {
            Section section = GetSection(railChunk, railIndex, out _);
            if (section == null) return;

            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;
            Vec3f view = byEntity.Pos.GetViewVector();
            double along = (end.X - start.X) * view.X + (end.Z - start.Z) * view.Z;

            // Руками вагонетку не розігнати понад MaxPushSpeed. Проти руху штовхати можна завжди
            int pushDir = along >= 0 ? 1 : -1;
            double speedInPushDir = railVelocity * pushDir;
            if (speedInPushDir < MaxPushSpeed)
            {
                railVelocity = pushDir * Math.Min(speedInPushDir + PushSpeed, MaxPushSpeed);
            }
        }

        private Section GetSection(Vec3i chunk, int index, out bool chunkLoaded)
        {
            chunkLoaded = World.BlockAccessor.GetChunk(chunk.X, chunk.Y, chunk.Z) != null;
            if (!chunkLoaded) return null;

            DataInChunk data = DataInChunk.Get(World, chunk);
            if (data == null) return null;
            data.RailWaySections.TryGetValue(index, out Section section);
            return section;
        }
    }
}
