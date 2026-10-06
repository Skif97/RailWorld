using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Перетворює нарізану криву на секції у світі. Спільне для всіх способів укладання:
    /// старих режимів із меню і маршруту по точках. Працює тільки на сервері.
    /// </summary>
    public static class TrackBuilder
    {
        /// <summary>
        /// Будує секції з точок: кожні три поспіль точки з кроком два це одна секція.
        /// Матеріали і заміну блоків бере з налаштувань предмета, яким кладуть колію.
        /// </summary>
        public static void Build(IWorldAccessor world, IPlayer byPlayer, ItemStack tool, List<PointOnBezierCurve> pointList)
        {
            string sleeperMaterial = tool.Attributes.GetString("sleeperMaterial", "oak");
            string railMaterial = tool.Attributes.GetString("railMaterial", "iron");
            string ballastMaterial = tool.Attributes.GetString("ballastMaterial", RailWorld.DontBuild);
            bool replaceBlocks = tool.Attributes.GetBool("replaceBlocks");

            RailDataSession session = new RailDataSession(world);
            List<Section> stretch = new List<Section>();
            int existing = 0, blocked = 0, unloaded = 0;

            for (int i = 0; i < pointList.Count - 2; i += 2)
            {
                var section = new Section(
                    pointList[i], pointList[i + 1], pointList[i + 2], TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength);

                // Чанк може бути не завантажений, тоді секцію записати нікуди
                DataInChunk data = session.Get(section.ChunkAddres, create: true);
                if (data == null)
                {
                    unloaded++;
                    continue;
                }

                // Якщо тут уже лежить така сама секція, другу поверх неї не кладемо
                if (SectionLinker.HasSameSection(data, section))
                {
                    existing++;
                    continue;
                }

                // У меню ввімкнено заміну: блоки на шляху колії ламаються, а не блокують секцію
                if (replaceBlocks) TrackBed.ClearObstructions(world, section, byPlayer);

                if (TrackBed.CanAttach(world, section))
                {
                    // Деталь, для якої в меню вибрано «не будувати», лишається порожнім місцем
                    if (sleeperMaterial != RailWorld.DontBuild) section.InstallationSleeper(sleeperMaterial);
                    if (railMaterial != RailWorld.DontBuild) section.InstallationRails(railMaterial);
                    if (ballastMaterial != RailWorld.DontBuild) section.InstallPart(SectionPart.Ballast, "normal", ballastMaterial);
                }
                else
                {
                    // Під секцією стоїть чужий блок: трасу лишаємо, але без деталей, доки його не приберуть
                    section.Blocked = true;
                    blocked++;
                }

                data.Add(section);
                session.MarkDirty(section.ChunkAddres);
                stretch.Add(section);
            }

            SectionLinker.LinkStretch(session, stretch);
            session.SaveAll();

            // Секції з деталями займають клітинки під собою. Після збереження, бо блоки колії шукають свої секції в даних чанка
            foreach (Section section in stretch)
                TrackBed.Attach(world, section);

            // За мить надсилаємо блоки ще раз: при довгій ділянці не всі оновлення доходять у правильному порядку
            world.RegisterCallback(dt => TrackBed.Resend(world, stretch), 500);

            // Підсумок гравцеві: що сервер побудував і з чого. Без нього не видно, чому на місці колії
            // лишилося старе: секція, яка вже лежить на цьому місці, не перекладається
            string report = string.Format("Колія: нових секцій {0}. Шпали: {1}, рейки: {2}, підсипка: {3}.",
                stretch.Count, Describe(sleeperMaterial), Describe(railMaterial), Describe(ballastMaterial));
            if (existing > 0) report += " Уже лежало і не чіпалося: " + existing + ".";
            if (blocked > 0) report += " Заблоковано блоками: " + blocked + ".";
            if (unloaded > 0) report += " У незавантажених чанках: " + unloaded + ".";

            (byPlayer as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, report, EnumChatType.Notification);
        }

        private static string Describe(string material)
        {
            return material == RailWorld.DontBuild ? "немає" : material;
        }
    }
}
