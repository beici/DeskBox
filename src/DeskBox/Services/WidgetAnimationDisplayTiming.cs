using System.Diagnostics;
using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Platform;

namespace DeskBox.Services;

/// <summary>UI-thread display cache. Expensive topology queries run only during active work.</summary>
internal static class WidgetAnimationDisplayTiming
{
    // FTHR-04: the static mutable state below is touched from multiple widget
    // UI threads (frame-budget queries) plus the diagnostic Clear() path, so
    // it is guarded by this private gate. Hold times are microsecond-scale:
    // dictionary operations plus the bounded GetMonitorInfoEx/EnumDisplaySettings
    // fallback; the stall-prone QueryDisplayConfig sweep already runs in
    // Task.Run inside RefreshIfNeeded.
    private static readonly object s_gate = new();
    private static long s_lastRefresh;
    private static Task<Dictionary<string, Win32Helper.DisplayTiming>>? s_refreshTask;
    private static Dictionary<string, Win32Helper.DisplayTiming> s_timings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<IntPtr, Win32Helper.DisplayTiming> Monitors = [];

    internal static double GetFrameBudgetMilliseconds(IntPtr windowHandle)
        => GetMonitorFrameBudgetMilliseconds(Win32Helper.GetAnimationMonitor(windowHandle));

    internal static double GetFrameBudgetMillisecondsForPoint(int x, int y)
        => GetMonitorFrameBudgetMilliseconds(Win32Helper.GetAnimationMonitorForPoint(x, y));

    private static double GetMonitorFrameBudgetMilliseconds(IntPtr monitor)
    {
        lock (s_gate)
        {
            RefreshIfNeeded();
            if (!Monitors.TryGetValue(monitor, out var timing))
            {
                timing = Win32Helper.GetAnimationDisplayTiming(monitor, s_timings);
                Monitors[monitor] = timing;
            }
            // During an animation the shared boost lease requests the DRR high mode.
            // This is a budget, not evidence that the compositor actually presents at that rate.
            double rate = timing.IsDynamic ? Math.Max(timing.RefreshRateHz, timing.PhysicalRefreshRateHz) : timing.RefreshRateHz;
            return WidgetDisplayRefreshRatePolicy.ResolveFrameTickInterval(rate).TotalMilliseconds;
        }
    }

    internal static void Clear()
    {
        lock (s_gate)
        {
            s_lastRefresh = 0;
            s_timings.Clear();
            Monitors.Clear();
        }
    }

    /// <summary>Caller must hold <see cref="s_gate"/>.</summary>
    private static void RefreshIfNeeded()
    {
        if (s_refreshTask is { IsCompleted: true } completed)
        {
            if (completed.IsCompletedSuccessfully)
            {
                s_timings = completed.Result;
                Monitors.Clear();
            }
            else
            {
                _ = completed.Exception; // Observe failures; EnumDisplaySettings remains available.
            }
            s_refreshTask = null;
        }
        long now = Stopwatch.GetTimestamp();
        if (s_refreshTask is not null ||
            (s_lastRefresh != 0 && Stopwatch.GetElapsedTime(s_lastRefresh, now).TotalMilliseconds < 250)) return;
        // Display-driver queries can stall. Never run them inside a UI frame.
        // At most one query is in flight; no timer or continuation keeps polling at idle.
        s_refreshTask = Task.Run(Win32Helper.QueryActiveDisplayTimings);
        s_lastRefresh = now;
    }
}
