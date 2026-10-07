using Vintagestory.API.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;
using Vintagestory.API.Common.Entities;
using RailWorld.src.RailWay;
using RailWorld.src.Items;
using Vintagestory.GameContent;

namespace RailWorld
{

    public class EntityTrolley : EntityAgent, ISeatInstSupplier
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
        private void ShowCant(float dt)
        {
            float target = WatchedAttributes.GetFloat(CantAttribute);
            shownCant += (target - shownCant) * Math.Min(1f, dt * 10f);
            if (Properties.Client.Renderer is EntityShapeRenderer renderer) renderer.xangle = shownCant;
        }

        // Клієнту стан потрібен лише для тяжіння, тому синхронізуємо один прапорець і лише при зміні
        private void SetOnRail(bool value)
        {
            onRail = value;
            if (WatchedAttributes.GetBool("onRail") == value) return;
            WatchedAttributes.SetBool("onRail", value);
            WatchedAttributes.MarkPathDirty("onRail");
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (Api.Side == EnumAppSide.Client)
            {
                ShowCant(dt);
                return;
            }
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
                double length = SectionLength(section);
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

            ApplyRailPose(Pos, section, railS, SelectionBox.Y2 / 2, railFacing, out float cant);
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
                railS = SectionLength(section);
                railVelocity = -speed;
            }
            return EnterResult.Entered;
        }

        // Сходить з колії: далі це звичайна сутність із тяжінням, яка летить із тією швидкістю, що мала
        private void Derail(Section section)
        {
            if (section != null)
            {
                double length = SectionLength(section);
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

        private static double SectionLength(Section section)
        {
            return section.FullStartPosition.DistanceTo(section.FullEndPosition);
        }

        /// <summary>
        /// Ставить позицію на колію: на відстані s від початку секції, колесами на головці рейки,
        /// з поворотом, ухилом і нахилом полотна. halfHeight це половина висоти боксу сутності,
        /// facing каже, в який бік секції дивиться перед: 1 у бік кінця, -1 у бік початку.
        /// </summary>
        public static void ApplyRailPose(EntityPos pos, Section section, double s, double halfHeight, int facing, out float cant)
        {
            cant = 0;
            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;
            Vec3d along = new Vec3d(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            if (along.Length() < 1e-9) return;
            along.Normalize();

            // Рамка колії: уздовж, убік (з нахилом полотна) і вгору
            Vec3d side = new Vec3d(section.CenterNormal.X, section.CenterNormal.Y, section.CenterNormal.Z);
            side.Sub(along.Clone().Mul(side.Dot(along))).Normalize();
            Vec3d up = side.Cross(along);

            // Розворот переду це поворот на 180° навколо верху: уздовж і вбік міняють знак, верх лишається
            Vec3d forward = facing < 0 ? along.Clone().Mul(-1) : along;
            Vec3d right = facing < 0 ? side.Clone().Mul(-1) : side;

            // Ухил іде в Roll: рендер застосовує його одразу після курсу, тобто навколо поперечної осі вагонетки.
            // Нахил полотна повертається окремо, Pitch лишається нулем
            ModMath.TrackFrameToEntityAngles(forward, right, up, out float yaw, out float tilt, out cant);
            pos.Yaw = yaw;
            pos.Pitch = 0;
            pos.Roll = tilt;

            // Рендер обертає модель навколо середини її висоти, тому зсуваємо позицію так,
            // щоб після нахилу низ моделі лишився на осі колії
            double lift = SectionBox.RailHeight + halfHeight;
            pos.X = start.X + along.X * s + up.X * lift;
            pos.Y = start.Y + along.Y * s + up.Y * lift - halfHeight;
            pos.Z = start.Z + along.Z * s + up.Z * lift;
        }
    }
}
