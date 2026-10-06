using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Isolated scripted input. Measures CPU across Update and LateUpdate,
    // excluding the frame limiter; it never reads or writes the player's library.
    [DefaultExecutionOrder(-10000)]
    public sealed class RuntimePerformanceProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private double began = -1;
        private long frameStarted;
        private bool finished;
        private int stage, requestedThrows, initialItemCount;
        private float deadline;
        private Vector2 origin;
        private readonly List<Sample>[] samples = Enumerable.Range(0, 4).Select(_ => new List<Sample>(2400)).ToArray();
        private readonly List<Recovery> recovery = new(128);
        private readonly string[] stages = { "idle", "movement", "cursor", "throw" };
        private readonly double[] ends = { 11, 17, 23, 31 };
        public void Initialize(PrototypeRuntime value)
        {
            if (string.IsNullOrEmpty(value.Options.SaveDirectory) || value.Options.Role != "host")
                throw new InvalidOperationException("Аудит кадров требует хоста и отдельного каталога данных.");
            runtime = value; deadline = Time.unscaledTime + 90;
            runtime.CommandSource = Command;
            runtime.Session.RecoveryMeasured += (name, seconds, characters) =>
                recovery.Add(new Recovery { stage = name, ms = seconds * 1000, characters = characters });
            gameObject.AddComponent<RuntimePerformanceFrameEnd>().Probe = this;
        }
        private void Update()
        {
            frameStarted = Stopwatch.GetTimestamp();
            if (finished) return;
            var actor = runtime.Session.LocalActor;
            if (actor == null) { if (Time.unscaledTime > deadline) Finish(false, "Не подключился локальный персонаж."); return; }
            if (began < 0)
            {
                began = Time.unscaledTimeAsDouble; origin = actor.Position;
                var item=actor.inventory.FirstOrDefault(i=>i.definition=="p10" && i.count>=8);
                if(item==null) {Finish(false,"Для аудита нужны восемь существующих 10-мм патронов в одной стопке. Тест не добавляет вещи в живой инвентарь.");return;}
                initialItemCount=actor.inventory.Where(i=>i.definition=="p10").Sum(i=>i.count);
                if (!HeldItemSelection.Select(actor, item.id, out string reason)) { Finish(false, reason); return; }
                runtime.Session.PublishActor(actor);
            }
            double elapsed = Time.unscaledTimeAsDouble - began;
            stage = Array.FindIndex(ends, end => elapsed < end);
            if (stage < 0) Finish(requestedThrows >= 7, "CPU измерен на обычном движении, наведении и сетевых бросках. Native GC-счётчик недоступен.");
        }
        private PlayerCommand Command()
        {
            if (began < 0 || finished || runtime.Session.LocalActor == null) return default;
            double elapsed = Time.unscaledTimeAsDouble - began;
            var actor = runtime.Session.LocalActor;
            var aim = origin + new Vector2(2, 1);
            var command = new PlayerCommand { hasAim = true, aim = aim };
            if (stage == 1) command.movement = new Vector2((elapsed - 11) % 2 < 1 ? 1 : -1, 0);
            if (stage == 2) command.aim = origin + new Vector2((float)Math.Sin(elapsed * 7) * 4, (float)Math.Cos(elapsed * 5) * 2 + 1);
            if (stage == 3 && requestedThrows < (int)(elapsed - 23) + 1)
            { command.aim = actor.Position + new Vector2(3, 1.5f); command.fire = true; requestedThrows++; }
            return command;
        }
        internal void CompleteFrame()
        {
            if (finished || began < 0 || stage < 0 || Time.unscaledTimeAsDouble - began < 5) return;
            samples[stage].Add(new Sample { ms = (Stopwatch.GetTimestamp() - frameStarted) * 1000d / Stopwatch.Frequency });
        }
        private void Finish(bool success, string detail)
        {
            finished = true;
            int completedThrows=began<0?0:initialItemCount-(runtime.Session.LocalActor?.inventory.Where(i=>i.definition=="p10").Sum(i=>i.count)??initialItemCount);
            success&=completedThrows==requestedThrows;
            var report = new Report { success = success, detail = detail, requestedThrows = requestedThrows,
                completedThrows=completedThrows,
                character = runtime.Session.LocalActor?.character, recovery = recovery.ToArray(),
                phases = samples.Select((values, index) => Summarize(stages[index], values)).ToArray() };
            File.WriteAllText(runtime.Options.Report, JsonUtility.ToJson(report, true));
            Application.Quit(success ? 0 : 1);
        }
        private static Phase Summarize(string name, List<Sample> values)
        {
            var times = values.Select(s => s.ms).OrderBy(ms => ms).ToArray();
            return new Phase { name = name, frames = values.Count, meanMs = times.Length == 0 ? 0 : times.Average(),
                p95Ms = Percentile(times, .95), p99Ms = Percentile(times, .99), maxMs = times.Length == 0 ? 0 : times[^1],
                over16Ms = values.Count(s => s.ms > 16.667), over33Ms = values.Count(s => s.ms > 33.333) };
        }
        private static double Percentile(double[] values, double quantile) => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * quantile) - 1)];
        [Serializable] private struct Sample { public double ms; }
        [Serializable] private sealed class Phase { public string name; public int frames, over16Ms, over33Ms; public double meanMs, p95Ms, p99Ms, maxMs; }
        [Serializable] private sealed class Recovery { public string stage; public double ms; public int characters; }
        [Serializable] private sealed class Report { public bool success; public string detail, character; public int requestedThrows,completedThrows; public Phase[] phases; public Recovery[] recovery; }
    }
    [DefaultExecutionOrder(32000)]
    public sealed class RuntimePerformanceFrameEnd : MonoBehaviour
    {
        internal RuntimePerformanceProbe Probe;
        private void LateUpdate() => Probe.CompleteFrame();
    }
}
