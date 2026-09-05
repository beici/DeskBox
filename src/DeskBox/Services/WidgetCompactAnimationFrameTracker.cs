// Copyright (c) DeskBox. All rights reserved.

using System.Diagnostics;
using DeskBox.Models;

namespace DeskBox.Services;

public readonly record struct WidgetCompactAnimationFrameSummary(
    int RefreshRateHz,
    int FrameCount,
    int EstimatedDroppedFrames,
    double MaximumFrameIntervalMilliseconds,
    double ElapsedMilliseconds,
    double FirstFrameMilliseconds)
{
    public double FrameBudgetMilliseconds => 1000d / Math.Max(1, RefreshRateHz);
}

/// <summary>
/// Small allocation-free tracker for diagnosing capsule animation cadence.
/// Timestamps are Stopwatch ticks so the policy can be covered by unit tests.
/// </summary>
public sealed class WidgetCompactAnimationFrameTracker
{
    private readonly long _startedTimestamp;
    private long _lastFrameTimestamp;
    private double _maximumFrameIntervalMilliseconds;
    private double _firstFrameMilliseconds = -1;
    private int _frameCount;
    private int _estimatedDroppedFrames;

    public WidgetCompactAnimationFrameTracker(long startedTimestamp, int refreshRateHz)
    {
        _startedTimestamp = startedTimestamp;
        _lastFrameTimestamp = startedTimestamp;
        RefreshRateHz = WidgetDisplayRefreshRatePolicy.Normalize((uint)Math.Max(0, refreshRateHz));
    }

    public int RefreshRateHz { get; }

    public void RecordFrame(long timestamp)
    {
        if (timestamp <= _lastFrameTimestamp)
        {
            return;
        }

        double intervalMs = Stopwatch.GetElapsedTime(_lastFrameTimestamp, timestamp).TotalMilliseconds;
        _lastFrameTimestamp = timestamp;
        _frameCount++;
        _maximumFrameIntervalMilliseconds = Math.Max(_maximumFrameIntervalMilliseconds, intervalMs);
        if (_firstFrameMilliseconds < 0)
        {
            // Latency from "the Composition fades started" to the first native
            // geometry frame. A large value means the transition was stalled
            // before it could animate at all, which reads as a jump rather
            // than as dropped frames spread over the whole morph.
            _firstFrameMilliseconds = Stopwatch
                .GetElapsedTime(_startedTimestamp, timestamp)
                .TotalMilliseconds;
        }

        double frameBudgetMs = 1000d / RefreshRateHz;
        if (intervalMs > frameBudgetMs * 1.5)
        {
            _estimatedDroppedFrames += Math.Max(1, (int)Math.Round(intervalMs / frameBudgetMs) - 1);
        }
    }

    public WidgetCompactAnimationFrameSummary Complete(long timestamp)
    {
        long completedTimestamp = Math.Max(timestamp, _startedTimestamp);
        return new WidgetCompactAnimationFrameSummary(
            RefreshRateHz,
            _frameCount,
            _estimatedDroppedFrames,
            _maximumFrameIntervalMilliseconds,
            Stopwatch.GetElapsedTime(_startedTimestamp, completedTimestamp).TotalMilliseconds,
            Math.Max(0, _firstFrameMilliseconds));
    }
}
