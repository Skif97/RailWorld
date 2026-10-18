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
        public const string MinRadiusCode = "railMinRadius";
        public const string ClearanceCode = "railClearance";
        public const string MarkerReachCode = "railMarkerReach";

        /// <summary>Найменший радіус за замовчуванням, у ширинах колії: при колії 0.78 це 3.51 блока.</summary>
        public const double DefaultMinRadiusGauges = 4.5;

        /// <summary>
        /// Просвіт за замовчуванням, блоків. Колії на рівній землі стоять на висотах, кратних блоку, тож зазор між
        /// ними цілий; межа посередині між двома і трьома блоками не збігається з жодним із них, і похибка
        /// обчислень на косому перетині не перекидає відповідь. Тобто під мостом потрібно три блоки.
        /// </summary>
        public const double DefaultClearance = 2.5;

        /// <summary>Як далеко за замовчуванням точка розмітки притягується до колії, блоків.</summary>
        public const double DefaultMarkerReach = 2.5;

        /// <summary>Найменший радіус повороту в плані і перелому по висоті, у ширинах колії.</summary>
        public static double MinRadiusGauges { get; private set; } = DefaultMinRadiusGauges;

        /// <summary>Найменший радіус повороту в плані і перелому по висоті, блоків.</summary>
        public static double MinRadius => TrackGauge.StandardWidth * MinRadiusGauges;

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
        /// Бере правила з налаштувань світу. remember: світ, створений до появи цих налаштувань, отримує в них
        /// значення за замовчуванням, і далі тримає їх, хоч би як потім змінилися значення за замовчуванням у моді.
        /// Так робить сервер: налаштування світу зберігаються разом із ним.
        /// </summary>
        public static void Load(ITreeAttribute config, bool remember)
        {
            MinRadiusGauges = Read(config, MinRadiusCode, DefaultMinRadiusGauges, remember);
            Clearance = Read(config, ClearanceCode, DefaultClearance, remember);
            MarkerReach = Read(config, MarkerReachCode, DefaultMarkerReach, remember);
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
