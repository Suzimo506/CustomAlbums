using System.Diagnostics;
using MelonLoader;
using UnityEngine;

namespace CustomAlbums.Utilities
{
    internal static class PerfTrace
    {
        private const double SlowThresholdMs = 1.0;
        private const double FrameThresholdMs = 18.0;
        private const float ReportIntervalSeconds = 5f;
        private static readonly Dictionary<string, PerfStat> Stats = new();
        private static bool _enabled;
        private static float _nextReportTime;
        private static int _frame;
        private static double _frameTotalMs;
        private static int _frameSpikeLogs;

        public static bool Enabled => _enabled;

        public static void SetGameMain(bool enabled)
        {
            if (_enabled == enabled) return;

            _enabled = enabled;
            Stats.Clear();
            _frameTotalMs = 0d;
            _frameSpikeLogs = 0;
            _frame = Time.frameCount;
            _nextReportTime = Time.unscaledTime + ReportIntervalSeconds;
            MelonLogger.Warning(enabled
                ? "[CustomAlbums.Perf] enabled in GameMain"
                : "[CustomAlbums.Perf] disabled");
        }

        public static void BeginFrame()
        {
            if (!_enabled) return;

            var frame = Time.frameCount;
            if (frame == _frame) return;

            if (_frameTotalMs >= FrameThresholdMs && _frameSpikeLogs < 3)
            {
                _frameSpikeLogs++;
                MelonLogger.Warning($"[CustomAlbums.PerfSpike] frame={_frame} measured={_frameTotalMs:0.00}ms");
            }

            _frame = frame;
            _frameTotalMs = 0d;
        }

        public static void UpdateReport()
        {
            if (!_enabled || Time.unscaledTime < _nextReportTime) return;

            _nextReportTime = Time.unscaledTime + ReportIntervalSeconds;
            _frameSpikeLogs = 0;
            var slow = Stats
                .Where(pair => pair.Value.Count > 0)
                .OrderByDescending(pair => pair.Value.MaxMs)
                .Take(8)
                .Select(pair => $"{pair.Key}: max={pair.Value.MaxMs:0.00}ms avg={pair.Value.TotalMs / pair.Value.Count:0.00}ms count={pair.Value.Count}");
            var text = string.Join(" | ", slow);
            if (!string.IsNullOrEmpty(text))
            {
                MelonLogger.Warning("[CustomAlbums.PerfSummary] " + text);
            }

            Stats.Clear();
        }

        public static Scope Measure(string name)
        {
            return new Scope(_enabled ? name : null);
        }

        public static void Record(string name, double elapsedMs)
        {
            if (!_enabled) return;

            _frameTotalMs += elapsedMs;
            if (!Stats.TryGetValue(name, out var stat))
            {
                stat = new PerfStat();
                Stats[name] = stat;
            }

            stat.Count++;
            stat.TotalMs += elapsedMs;
            if (elapsedMs > stat.MaxMs) stat.MaxMs = elapsedMs;

            if (elapsedMs >= SlowThresholdMs && stat.SlowLogs < 3)
            {
                stat.SlowLogs++;
                MelonLogger.Warning($"[CustomAlbums.PerfSlow] {name} {elapsedMs:0.00}ms frame={Time.frameCount}");
            }
        }

        public readonly struct Scope : IDisposable
        {
            private readonly string _name;
            private readonly long _start;

            public Scope(string name)
            {
                _name = name;
                _start = name == null ? 0L : Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_name == null) return;
                var elapsedMs = (Stopwatch.GetTimestamp() - _start) * 1000d / Stopwatch.Frequency;
                Record(_name, elapsedMs);
            }
        }

        private sealed class PerfStat
        {
            public int Count;
            public double TotalMs;
            public double MaxMs;
            public int SlowLogs;
        }
    }
}
