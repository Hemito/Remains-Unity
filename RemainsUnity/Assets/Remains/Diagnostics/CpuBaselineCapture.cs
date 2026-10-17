using System;
using System.Collections.Generic;

namespace Remains.Diagnostics
{
    // Opt-in, main-thread capture. Section durations and allocations are inclusive: nested
    // sections overlap and must not be added together. The byte counter includes all managed
    // threads during the measured interval, not allocation ownership. Frame closes the current sample after all Section calls; missing sections count
    // as zero for that frame. Summarize excludes an unfinished frame from frame statistics.
    public static class CpuBaselineCapture
    {
        private sealed class Samples
        {
            public readonly List<double> calls = new List<double>();
            public readonly List<double> frames = new List<double>();
            public long allocatedBytes;
            public double pendingMs;
        }

        private static readonly Dictionary<string, Samples> sections = new Dictionary<string, Samples>(StringComparer.Ordinal);
        private static readonly List<double> totalFrames = new List<double>();
        private static readonly List<double> workFrames = new List<double>();
        private static readonly List<double> waitFrames = new List<double>();
        private static readonly List<double> allocationFrames = new List<double>();

        public static bool Enabled { get; set; }
        public static int FrameCount => totalFrames.Count;
        public static int FrameLimit { get; set; }

        public static void Reset()
        {
            sections.Clear();
            totalFrames.Clear();
            workFrames.Clear();
            waitFrames.Clear();
            allocationFrames.Clear();
        }

        public static void Section(string name, double milliseconds, long allocatedBytes)
        {
            if (!Enabled) return;
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Имя участка замера не задано.", nameof(name));
            Validate(milliseconds, nameof(milliseconds));
            if (allocatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(allocatedBytes));
            if (!sections.TryGetValue(name, out var sample))
            {
                sample = new Samples();
                for (var i = 0; i < totalFrames.Count; i++) sample.frames.Add(0);
                sections.Add(name, sample);
            }
            sample.calls.Add(milliseconds);
            sample.pendingMs += milliseconds;
            sample.allocatedBytes += allocatedBytes;
        }

        public static void Frame(double totalMs, double waitMs, long allocatedBytes)
        {
            if (!Enabled) return;
            Validate(totalMs, nameof(totalMs));
            Validate(waitMs, nameof(waitMs));
            if (waitMs > totalMs) throw new ArgumentOutOfRangeException(nameof(waitMs), "Ожидание превышает длительность кадра.");
            if (allocatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(allocatedBytes));
            totalFrames.Add(totalMs);
            workFrames.Add(totalMs - waitMs);
            waitFrames.Add(waitMs);
            allocationFrames.Add(allocatedBytes);
            foreach (var sample in sections.Values)
            {
                sample.frames.Add(sample.pendingMs);
                sample.pendingMs = 0;
            }
            if (FrameLimit > 0 && totalFrames.Count >= FrameLimit) Enabled = false;
        }

        public static CpuBaselineSummary Summarize(double budgetMs = 1000d / 60)
        {
            Validate(budgetMs, nameof(budgetMs));
            if (budgetMs == 0) throw new ArgumentOutOfRangeException(nameof(budgetMs));
            var names = new List<string>(sections.Keys);
            names.Sort(StringComparer.Ordinal);
            var metrics = new CpuBaselineMetric[names.Count];
            for (var i = 0; i < names.Count; i++)
            {
                var sample = sections[names[i]];
                metrics[i] = new CpuBaselineMetric
                {
                    name = names[i],
                    perCall = Stats(sample.calls),
                    perFrame = Stats(sample.frames),
                    calls = sample.calls.Count,
                    allocatedBytes = sample.allocatedBytes
                };
            }
            return new CpuBaselineSummary
            {
                budgetMs = budgetMs,
                frameCount = totalFrames.Count,
                total = Stats(totalFrames),
                work = Stats(workFrames),
                wait = Stats(waitFrames),
                allocationsPerFrame = Stats(allocationFrames),
                framesOverBudget = CountOver(totalFrames, budgetMs),
                workFramesOverBudget = CountOver(workFrames, budgetMs),
                metrics = metrics
            };
        }

        private static void Validate(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }

        private static int CountOver(List<double> values, double budget)
        {
            var result = 0;
            foreach (var value in values) if (value > budget) result++;
            return result;
        }

        private static CpuBaselineStats Stats(List<double> values)
        {
            var result = new CpuBaselineStats { count = values.Count };
            if (values.Count == 0) return result;
            var sorted = values.ToArray();
            Array.Sort(sorted);
            foreach (var value in sorted) result.total += value;
            result.mean = result.total / sorted.Length;
            result.max = sorted[sorted.Length - 1];
            // Nearest-rank quantiles; no interpolation that could hide a short spike.
            result.p50 = Quantile(sorted, 0.50);
            result.p95 = Quantile(sorted, 0.95);
            result.p99 = Quantile(sorted, 0.99);
            return result;
        }

        private static double Quantile(double[] sorted, double percentile)
        {
            return sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];
        }
    }

    [Serializable]
    public sealed class CpuBaselineSummary
    {
        public double budgetMs;
        public int frameCount;
        public CpuBaselineStats total;
        public CpuBaselineStats work;
        public CpuBaselineStats wait;
        public CpuBaselineStats allocationsPerFrame;
        public int framesOverBudget;
        public int workFramesOverBudget;
        public CpuBaselineMetric[] metrics;
    }

    [Serializable]
    public sealed class CpuBaselineMetric
    {
        public string name;
        public long calls;
        public long allocatedBytes;
        public CpuBaselineStats perCall;
        public CpuBaselineStats perFrame;
    }

    [Serializable]
    public sealed class CpuBaselineStats
    {
        public int count;
        public double p50;
        public double p95;
        public double p99;
        public double max;
        public double mean;
        public double total;
    }
}
