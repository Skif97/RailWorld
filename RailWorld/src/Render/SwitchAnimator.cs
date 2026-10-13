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
    /// Переведення стрілок на екрані. Сервер перемикає стрілку миттєво, і клієнт дізнається про це з його
    /// даних. Рух гостряків і важеля це лише вигляд: клієнт помічає, що положення стрілки змінилося,
    /// і далі сам плавно переводить деталі зі старого положення в нове. Тут лише стан цього руху;
    /// самі деталі ставить TrackChunkRenderer.
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
        }

        private Dictionary<string, SwitchState> states = new Dictionary<string, SwitchState>();

        // Стрілку визначає кінець секції, з якого колія розходиться
        private static string SwitchKey(Vec3i chunk, int index, bool atStart)
        {
            return $"{chunk.X}_{chunk.Y}_{chunk.Z}_{index}_{(atStart ? "S" : "E")}";
        }

        // Запам'ятовує положення стрілки. Якщо воно не те, що бачили минулого разу, з цієї миті починається рух.
        // Стрілка, яку бачимо вперше, одразу стоїть на місці
        public void Track(Vec3i chunkCoord, Section section, bool atStart)
        {
            string key = SwitchKey(chunkCoord, section.IndexInChunk, atStart);
            int position = section.GetSwitchPosition(atStart);

            if (!states.TryGetValue(key, out SwitchState state))
            {
                states[key] = new SwitchState { Chunk = chunkCoord.Clone(), Index = section.IndexInChunk, AtStart = atStart, Position = position, From = position };
                return;
            }
            if (state.Position == position) return;

            // Рух починається з того місця, де стрілка зараз: якщо її перевели назад, не дочекавшись кінця,
            // вона розвертається з півдороги, а не перестрибує в крайнє положення
            state.From = SwitchValue(state);
            state.Position = position;
            state.ChangedAtMs = capi.ElapsedMilliseconds;
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
            if (!states.TryGetValue(SwitchKey(chunk, index, atStart), out SwitchState state)) return 1;
            double value = SwitchValue(state);
            return state.Position == 1 ? value : 1 - value;
        }

        /// <summary>
        /// Раз на кадр: викликає redraw для кожної стрілки, що рухається (чанк, номер секції, кінець).
        /// Останній раз уже в кінцевому положенні.
        /// </summary>
        public void Advance(Action<Vec3i, int, bool> redraw)
        {
            foreach (SwitchState state in states.Values)
            {
                if (!state.Moving) continue;
                if (capi.ElapsedMilliseconds - state.ChangedAtMs >= DurationMs) state.Moving = false;

                redraw(state.Chunk, state.Index, state.AtStart);
            }
        }

        /// <summary>Забуває стрілки чанка, колія в якому зникла.</summary>
        public void Forget(Vec3i chunkCoord)
        {
            foreach (string key in states.Where(pair => pair.Value.Chunk.Equals(chunkCoord)).Select(pair => pair.Key).ToList()) states.Remove(key);
        }
    }
}
