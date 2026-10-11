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
    /// Що клієнт знає про колію: бокси виділення деталей і пошук по даних, які прислав сервер.
    /// Самі дані лежать у DataInChunk; тут те, що з них виводиться для малювання і взаємодії.
    /// </summary>
    public class ClientTrack
    {
        // Бокси виділення деталей по чанках
        private Dictionary<Vec3i, List<SectionBox>> boxes = new Dictionary<Vec3i, List<SectionBox>>();

        /// <summary>Чанки, про колію в яких знає клієнт.</summary>
        public IEnumerable<Vec3i> ChunkCoords => boxes.Keys;

        /// <summary>
        /// Секція, на яку вказує посилання, з того, що клієнт зараз знає про колію. null, якщо її чанку немає.
        /// </summary>
        public Section FindSection(SectionLink link)
        {
            DataInChunk data = link == null ? null : DataInChunk.GetClient(link.Chunk);
            if (data == null) return null;
            data.RailWaySections.TryGetValue(link.Index, out Section section);
            return section;
        }

        /// <summary>Секції колії, що зберігаються в чанку, якими їх знає клієнт; null, якщо колії там немає.</summary>
        public IEnumerable<Section> SectionsIn(Vec3i chunkCoord)
        {
            return DataInChunk.GetClient(chunkCoord)?.RailWaySections.Values;
        }

        public List<SectionBox> GetBoxes(Vec3i chunkCoord)
        {
            boxes.TryGetValue(chunkCoord, out List<SectionBox> found);
            return found;
        }

        /// <summary>Приймає нові дані колії чанка: запам'ятовує їх і збирає бокси виділення.</summary>
        public void Set(Vec3i chunkCoord, DataInChunk data, SectionHandler handler)
        {
            var found = new List<SectionBox>();
            foreach (var kvp in data.RailWaySections)
                handler.GetSelectionBoxes(found, chunkCoord, kvp.Key, kvp.Value);
            boxes[chunkCoord] = found;
            DataInChunk.SetClient(chunkCoord, data);
        }

        /// <summary>Забуває колію чанка. Повертає true, якщо вона там була.</summary>
        public bool Remove(Vec3i chunkCoord)
        {
            boxes.Remove(chunkCoord);
            return DataInChunk.RemoveClient(chunkCoord);
        }

        public void Clear()
        {
            boxes.Clear();
        }

        /// <summary>
        /// Запис про стрілку, на гілці якої секція лежить з боку кінця atStart, і місце секції в ній.
        /// Запис рахує і зберігає сервер; клієнт його лише читає. false, якщо секція з цього боку не на
        /// гілці стрілки або чанк зі стрілкою до клієнта ще не дійшов.
        /// </summary>
        public bool FindSwitch(Section section, bool atStart, out RailSwitch record, out SwitchRef place)
        {
            record = null;
            place = section.GetSwitchRef(atStart);
            if (place == null) return false;
            DataInChunk data = DataInChunk.GetClient(place.Chunk);
            return data != null && data.Switches.TryGetValue(place.Key, out record) && record.BranchOf(place) != null;
        }

        /// <summary>Секція другої гілки тієї самої стрілки під тим самим номером, або null, якщо клієнт її не знає.</summary>
        public Section Opposite(RailSwitch record, SwitchRef place)
        {
            SectionLink link = record.Opposite(place);
            return link == null ? null : FindSection(link);
        }
    }
}
