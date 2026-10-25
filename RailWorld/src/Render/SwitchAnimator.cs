using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Переведення стрілок на екрані. Сервер перемикає стрілку миттєво, і клієнт дізнається про це з його
    /// даних. Рух гостряків це лише вигляд: клієнт помічає, що положення стрілки змінилося, і далі сам
    /// плавно переводить їх зі старого положення в нове. Тут лише стан цього руху; самі деталі ставить
    /// TrackChunkRenderer. Важіль стрілки веде гра своїм аніматором (SwitchLeverRenderer), йому звідси
    /// потрібна лише тривалість руху.
    /// </summary>
    public class SwitchAnimator
    {
        private ICoreClientAPI capi;

        public SwitchAnimator(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>Скільки триває рух на екрані. Трохи довше за час, коли вагонетка на стрілці сходить із колії.</summary>
        public const int DurationMs = Section.SwitchTimeMs + 200;

        // Стрілка, яку клієнт уже бачив: у якому вона положенні і коли воно востаннє змінилося (час клієнта)
        private class SwitchState
        {
            public Vec3i Chunk;
            public int Index;
            public bool AtStart;
            public int Position;
            // Де стрілка була в мить останньої зміни: 0 це перша гілка, 1 друга, між ними проміжні положення
            public double From;
            public long ChangedAtMs = long.MinValue / 2;
            public bool Moving;
            // Чи перебудова чанка бачила цю стрілку (BeginChunk, EndChunk)
            public bool Kept;
        }

        // Стрілку визначає кінець секції, з якого колія розходиться
        private readonly ChunkKeyed<SectionEndKey, SwitchState> states = new ChunkKeyed<SectionEndKey, SwitchState>(key => key.Section);
        // Ті з них, що зараз рухаються: лише їх Advance і обходить
        private readonly List<SwitchState> moving = new List<SwitchState>();

        // Запам'ятовує положення стрілки. Якщо воно не те, що бачили минулого разу, з цієї миті починається рух.
        // Стрілка, яку бачимо вперше, одразу стоїть на місці
        public void Track(Vec3i chunkCoord, Section section, bool atStart)
        {
            var key = new SectionEndKey(chunkCoord, section.IndexInChunk, atStart);
            int position = section.GetSwitchPosition(atStart);

            if (!states.TryGetValue(key, out SwitchState state))
            {
                states.Set(key, new SwitchState { Chunk = chunkCoord.Clone(), Index = section.IndexInChunk, AtStart = atStart, Position = position, From = position, Kept = true });
                return;
            }
            state.Kept = true;
            if (state.Position == position) return;

            // Рух починається з того місця, де стрілка зараз: якщо її перевели назад, не дочекавшись кінця,
            // вона розвертається з півдороги, а не перестрибує в крайнє положення
            state.From = SwitchValue(state);
            state.Position = position;
            state.ChangedAtMs = capi.ElapsedMilliseconds;
            if (!state.Moving) moving.Add(state);
            state.Moving = true;
        }

        // Де стрілка зараз: 0 це перша гілка, 1 друга. Рух плавно розганяється і плавно зупиняється;
        // неповний шлях займає відповідно менше часу
        private double SwitchValue(SwitchState state)
        {
            double distance = Math.Abs(state.Position - state.From);
            if (distance < 1e-6) return state.Position;

            double passed = GameMath.Clamp((capi.ElapsedMilliseconds - state.ChangedAtMs) / (DurationMs * distance), 0, 1);
            return state.From + (state.Position - state.From) * passed * passed * (3 - 2 * passed);
        }

        /// <summary>Наскільки стрілка вже стала в теперішнє положення, від 0 до 1. Стрілка, якої ще не бачили, стоїть на місці.</summary>
        public double Progress(Vec3i chunk, int index, bool atStart)
        {
            if (!states.TryGetValue(new SectionEndKey(chunk, index, atStart), out SwitchState state)) return 1;
            double value = SwitchValue(state);
            return state.Position == 1 ? value : 1 - value;
        }

        /// <summary>
        /// Раз на кадр: викликає redraw для кожної стрілки, що рухається (чанк, номер секції, кінець).
        /// Останній раз уже в кінцевому положенні.
        /// </summary>
        public void Advance(Action<Vec3i, int, bool> redraw)
        {
            // З кінця: та, що доїхала, після останнього кроку виходить зі списку
            for (int i = moving.Count - 1; i >= 0; i--)
            {
                SwitchState state = moving[i];
                if (capi.ElapsedMilliseconds - state.ChangedAtMs >= DurationMs) state.Moving = false;

                redraw(state.Chunk, state.Index, state.AtStart);
                if (!state.Moving) moving.RemoveAt(i);
            }
        }

        /// <summary>
        /// Початок і кінець перебудови чанка, як у гостряків і важелів: між ними перебудова кличе Track для кожної
        /// стрілки, яку бачить, а стани тих, яких уже немає, наприкінці забуваються. Інакше стрілка, яку прибрали
        /// й поставили на тому самому кінці секції знову, пам'ятала б положення старої і з'являлася б рухом.
        /// </summary>
        public void BeginChunk(Vec3i chunkCoord)
        {
            Dictionary<SectionEndKey, SwitchState> inChunk = states.In(chunkCoord);
            if (inChunk == null) return;
            foreach (SwitchState state in inChunk.Values) state.Kept = false;
        }

        public void EndChunk(Vec3i chunkCoord)
        {
            Dictionary<SectionEndKey, SwitchState> inChunk = states.In(chunkCoord);
            if (inChunk == null) return;

            List<SectionEndKey> gone = null;
            foreach (KeyValuePair<SectionEndKey, SwitchState> pair in inChunk)
            {
                if (!pair.Value.Kept) (gone ?? (gone = new List<SectionEndKey>())).Add(pair.Key);
            }
            if (gone == null) return;
            foreach (SectionEndKey key in gone)
            {
                SwitchState state = inChunk[key];
                if (state.Moving) moving.Remove(state);
                states.Remove(key);
            }
        }

        /// <summary>Забуває стрілки чанка, колія в якому зникла.</summary>
        public void Forget(Vec3i chunkCoord)
        {
            states.TakeChunk(chunkCoord);
            moving.RemoveAll(state => state.Chunk.Equals(chunkCoord));
        }
    }
}
