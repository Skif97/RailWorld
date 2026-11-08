using Vintagestory.API.Datastructures;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Правила прокладання, які можна змінити для світу: найменший радіус, просвіт між коліями одна над одною,
    /// як далеко точка розмітки притягується до колії. Вони лежать у налаштуваннях світу, як і ігрові
    /// (worldconfig.json мода описує їх для екрана «Налаштувати світ»), тож живуть разом зі світом, і клієнт
    /// отримує від сервера ті самі числа, що й будує сервер. Змінюються командою гри /worldconfig і діють
    /// після перезапуску світу. Числа, з яких рахуються записи стрілок і перетинів, сюди не входять: їх зміна
    /// вимагала б перерахувати записи.
    /// У worldconfig.json мусить бути список playStyles, хоч і порожній: гра перебирає його без перевірки на null
    /// (WorldAPI.CurrentPlayStyle), і без нього сервер падає, щойно до нього під'єднується гравець.
    /// </summary>
    public static class TrackRules
    {
        public const string MinRadiusNarrowCode = "railMinRadiusNarrow";
        public const string MinRadiusBroadCode = "railMinRadiusBroad";
        public const string SlopeChangeNarrowCode = "railSlopeChangeNarrow";
        public const string SlopeChangeBroadCode = "railSlopeChangeBroad";
        public const string MaxSlopeNarrowCode = "railMaxSlopeNarrow";
        public const string MaxSlopeBroadCode = "railMaxSlopeBroad";
        public const string ClearanceCode = "railClearance";
        public const string MarkerReachCode = "railMarkerReach";
        public const string TrolleysUnattendedCode = "railTrolleysUnattended";

        /// <summary>
        /// Найменший радіус за замовчуванням, блоків. Вузька колія для вагонеток: 4.5 її ширини. Широка під справжні
        /// вагони колії 1520: на такій кривій кузов вагона завдовжки 24 блоки виходить за свою смугу на прямій
        /// трохи більше ніж на пів блока.
        /// </summary>
        public const double DefaultMinRadiusNarrow = 3.51, DefaultMinRadiusBroad = 50;

        /// <summary>
        /// Найбільша зміна ухилу за замовчуванням, відсоткових пунктів на блок шляху. Вузька: стільки, скільки дає її
        /// найменший радіус (100 / 3.51). Широка: 1, тоді на переломі профілю кузови сусідніх вагонів не сходяться
        /// (лише середини дахів у ямці торкаються на сантиметр).
        /// </summary>
        public const double DefaultSlopeChangeNarrow = 28.5, DefaultSlopeChangeBroad = 1;

        /// <summary>
        /// Найбільший ухил за замовчуванням, відсотків. Вузька: 175, це 60°, тобто межа лише та сама, що й розрив рейок
        /// (RouteRules.BreakSlopeDeg). Широка: 10. Крутіше за справжні магістралі, але звичайна колія таке ще бачила
        /// (гірські трамваї, легкі моторвагонні потяги).
        /// </summary>
        public const double DefaultMaxSlopeNarrow = 175, DefaultMaxSlopeBroad = 10;

        /// <summary>
        /// Просвіт за замовчуванням, блоків. Колії на рівній землі стоять на висотах, кратних блоку, тож зазор між
        /// ними цілий; межа посередині між двома і трьома блоками не збігається з жодним із них, і похибка
        /// обчислень на косому перетині не перекидає відповідь. Тобто під мостом потрібно три блоки.
        /// </summary>
        public const double DefaultClearance = 2.5;

        /// <summary>Як далеко за замовчуванням точка розмітки притягується до колії, блоків.</summary>
        public const double DefaultMarkerReach = 2.5;

        /// <summary>Найменший радіус повороту в плані і перелому по висоті на вузькій і на широкій колії, блоків.</summary>
        public static double MinRadiusNarrow { get; private set; } = DefaultMinRadiusNarrow;
        public static double MinRadiusBroad { get; private set; } = DefaultMinRadiusBroad;

        /// <summary>Найбільша зміна ухилу на вузькій і на широкій колії, відсоткових пунктів на блок.</summary>
        public static double SlopeChangeNarrow { get; private set; } = DefaultSlopeChangeNarrow;
        public static double SlopeChangeBroad { get; private set; } = DefaultSlopeChangeBroad;

        /// <summary>Найбільший ухил на вузькій і на широкій колії, відсотків.</summary>
        public static double MaxSlopeNarrow { get; private set; } = DefaultMaxSlopeNarrow;
        public static double MaxSlopeBroad { get; private set; } = DefaultMaxSlopeBroad;

        /// <summary>
        /// Найменший просвіт між двома коліями, що проходять одна над одною, блоків. Якщо колія, яку маршрут
        /// перетинає в плані, вища чи нижча за нього менше ніж на стільки, це перетин в одному рівні, і маршрут
        /// вирівнюється по ній. Якщо більше, маршрут проходить над нею або під нею, і вони одна одної не
        /// стосуються. Той самий просвіт між двома витками самого маршруту.
        /// </summary>
        public static double Clearance { get; private set; } = DefaultClearance;

        /// <summary>
        /// Як далеко від осі покладеної колії точка розмітки, поставлена на блок, стає на саму колію, блоків: і в
        /// плані, і по висоті вгору чи вниз. Маршрут, що починається чи закінчується поруч із колією, до неї й
        /// приєднується: на іншій висоті він однаково або перетнув би її, або до неї примкнув. Колію впритул до
        /// іншої так не покладеш, і це навмисно.
        /// </summary>
        public static double MarkerReach { get; private set; } = DefaultMarkerReach;

        /// <summary>
        /// Чи вагонетки їздять далеко від гравців: у русі самі тримають завантаженими чанки під собою і попереду.
        /// Інакше вагонетка чекає на межі завантаженого світу, поки туди не прийде гравець.
        /// </summary>
        public static bool TrolleysUnattended { get; private set; }

        /// <summary>
        /// Бере правила з налаштувань світу. remember: світ, створений до появи цих налаштувань, отримує в них
        /// значення за замовчуванням, і далі тримає їх, хоч би як потім змінилися значення за замовчуванням у моді.
        /// Так робить сервер: налаштування світу зберігаються разом із ним.
        /// </summary>
        public static void Load(ITreeAttribute config, bool remember)
        {
            MinRadiusNarrow = Read(config, MinRadiusNarrowCode, DefaultMinRadiusNarrow, remember);
            MinRadiusBroad = Read(config, MinRadiusBroadCode, DefaultMinRadiusBroad, remember);
            SlopeChangeNarrow = Read(config, SlopeChangeNarrowCode, DefaultSlopeChangeNarrow, remember);
            SlopeChangeBroad = Read(config, SlopeChangeBroadCode, DefaultSlopeChangeBroad, remember);
            MaxSlopeNarrow = Read(config, MaxSlopeNarrowCode, DefaultMaxSlopeNarrow, remember);
            MaxSlopeBroad = Read(config, MaxSlopeBroadCode, DefaultMaxSlopeBroad, remember);
            Clearance = Read(config, ClearanceCode, DefaultClearance, remember);
            MarkerReach = Read(config, MarkerReachCode, DefaultMarkerReach, remember);
            TrolleysUnattended = ReadBool(config, TrolleysUnattendedCode, false, remember);
        }

        private static bool ReadBool(ITreeAttribute config, string code, bool fallback, bool remember)
        {
            if (config == null) return fallback;
            if (!config.HasAttribute(code))
            {
                if (remember) config.SetBool(code, fallback);
                return fallback;
            }
            return config.GetBool(code, fallback);
        }

        private static double Read(ITreeAttribute config, string code, double fallback, bool remember)
        {
            if (config == null) return fallback;
            if (!config.HasAttribute(code))
            {
                if (remember) config.SetDouble(code, fallback);
                return fallback;
            }
            return config.GetDecimal(code, fallback);
        }
    }
}
