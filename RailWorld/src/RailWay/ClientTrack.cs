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
        // Бокси виділення деталей чанка одним списком і місце в ньому боксів кожної секції за її номером
        private class ChunkBoxes
        {
            public readonly List<SectionBox> All = new List<SectionBox>();
            public readonly Dictionary<int, (int First, int Count)> OfSection = new Dictionary<int, (int First, int Count)>();
        }

        private Dictionary<Vec3i, ChunkBoxes> boxes = new Dictionary<Vec3i, ChunkBoxes>();

        // Найдальше, куди бокси секції сягають від її середини, серед усіх секцій, які клієнт бачив. Пошук під
        // прицілом бере секції в межах цього від променя. Рахується з самих боксів: довжини шпали й секції в
        // кожної свої, і важіль стоїть далі за край шпали
        private double boxReach;
        private readonly SectionRayPicker picker = new SectionRayPicker(chunk => DataInChunk.GetClient(chunk)?.Grid);
        private readonly SectionBoxes boxesOf;

        public ClientTrack()
        {
            boxesOf = BoxesOf;
        }

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

        public List<SectionBox> GetBoxes(Vec3i chunkCoord)
        {
            boxes.TryGetValue(chunkCoord, out ChunkBoxes found);
            return found?.All;
        }

        /// <summary>Деталь під прицілом: найближча на промені не далі за range. dir має бути нормалізований.</summary>
        public SectionSelection Pick(Vec3d origin, Vec3d dir, double range, System.Func<SectionBox, bool> filter)
        {
            return picker.Pick(origin, dir, range, boxReach, boxesOf, filter);
        }

        private bool BoxesOf(Section section, out List<SectionBox> all, out int first, out int count)
        {
            all = null;
            first = count = 0;
            if (!boxes.TryGetValue(section.ChunkAddres, out ChunkBoxes chunk) || !chunk.OfSection.TryGetValue(section.IndexInChunk, out var place)) return false;
            all = chunk.All;
            (first, count) = place;
            return true;
        }

        /// <summary>Приймає нові дані колії чанка: запам'ятовує їх і збирає бокси виділення.</summary>
        public void Set(Vec3i chunkCoord, DataInChunk data, SectionHandler handler)
        {
            var found = new ChunkBoxes();
            foreach (var kvp in data.RailWaySections)
            {
                int first = found.All.Count;
                handler.GetSelectionBoxes(found.All, chunkCoord, kvp.Key, kvp.Value);
                found.OfSection[kvp.Key] = (first, found.All.Count - first);
                for (int i = first; i < found.All.Count; i++) boxReach = Math.Max(boxReach, Reach(found.All[i], kvp.Value));
            }
            boxes[chunkCoord] = found;
            DataInChunk.SetClient(chunkCoord, data);
        }

        /// <summary>Як далеко від середини секції сягає її бокс: до середини бокса і ще його пів діагоналі.</summary>
        public static double Reach(SectionBox box, Section section)
        {
            FastVec3d middle = section.Center;
            double dx = box.Center.X - middle.X, dy = box.Center.Y - middle.Y, dz = box.Center.Z - middle.Z;
            double up = Math.Max(Math.Abs(box.MinUp), Math.Abs(box.MaxUp));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) + Math.Sqrt(box.HalfSide * box.HalfSide + box.HalfAlong * box.HalfAlong + up * up);
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
            boxReach = 0;
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

        /// <summary>
        /// Запис про перетин, у вузлі якого лежить секція. false, якщо секція не у вузлі або чанк із записом
        /// до клієнта ще не дійшов.
        /// </summary>
        public bool FindCrossing(Section section, out RailCrossing record, out CrossingRef place)
        {
            record = null;
            place = section.GetCrossingRef();
            if (place == null) return false;
            DataInChunk data = DataInChunk.GetClient(place.Chunk);
            return data != null && data.Crossings.TryGetValue(place.Key, out record) && record.TrackOf(place) != null;
        }
    }
}
