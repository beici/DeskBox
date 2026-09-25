using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class PerformanceLoggerTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("enabled", true)]
    public void IsEnabledSetting_ParsesOptInValues(string? value, bool expected)
    {
        Assert.Equal(expected, PerformanceLogger.IsEnabledSetting(value));
    }

    [Theory]
    [InlineData(0, 0, 0.0)]
    [InlineData(3, 1, 75.0)]
    [InlineData(1, 3, 25.0)]
    [InlineData(-1, 2, 0.0)]
    public void CalculateHitRatePercent_UsesNonNegativeLookupCounts(
        long hits,
        long misses,
        double expected)
    {
        Assert.Equal(
            expected,
            PerformanceLogger.CalculateHitRatePercent(hits, misses),
            precision: 3);
    }

    [Theory]
    [InlineData(0, 0, 0.0)]
    [InlineData(200_000, 2, 10.0)]
    [InlineData(-1, 2, 0.0)]
    public void CalculateAverageDurationMilliseconds_UsesCompletedLoadCount(
        long totalDurationTicks,
        long sampleCount,
        double expected)
    {
        Assert.Equal(
            expected,
            PerformanceLogger.CalculateAverageDurationMilliseconds(
                totalDurationTicks,
                sampleCount),
            precision: 3);
    }

    [Fact]
    public void ThumbnailEstimatedBytes_ConcurrentWideWritesAndReads_DoNotTear()
    {
        // FTHR-06: the public diagnostic counters are written from worker
        // threads and read from diagnostics; a torn 64-bit read would observe
        // a value that is neither of the two written patterns.
        const long patternA = long.MaxValue - 12345;
        const long patternB = long.MinValue + 67890;
        const int iterations = 50_000;
        long original = PerformanceLogger.ThumbnailEstimatedBytes;
        var torn = new System.Collections.Concurrent.ConcurrentBag<long>();
        try
        {
            // Seed an accepted pattern before the tasks start: a reader that
            // begins before the first write would otherwise legitimately
            // observe the pre-write initial value, which is not a torn read.
            PerformanceLogger.ThumbnailEstimatedBytes = patternA;
            Task reader = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    long observed = PerformanceLogger.ThumbnailEstimatedBytes;
                    if (observed != patternA && observed != patternB)
                    {
                        torn.Add(observed);
                    }
                }
            });
            Task writerA = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PerformanceLogger.ThumbnailEstimatedBytes = patternA;
                }
            });
            Task writerB = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PerformanceLogger.ThumbnailEstimatedBytes = patternB;
                }
            });

            Task.WaitAll(reader, writerA, writerB);

            Assert.Empty(torn);

            // After the writers drain, a single-threaded round trip is exact.
            PerformanceLogger.ThumbnailEstimatedBytes = patternA;
            Assert.Equal(patternA, PerformanceLogger.ThumbnailEstimatedBytes);
        }
        finally
        {
            PerformanceLogger.ThumbnailEstimatedBytes = original;
        }
    }

    [Fact]
    public void PublicDiagnosticCounters_ConcurrentIntWrites_AreObservedAtomically()
    {
        // FTHR-06: int counters ride the same Interlocked discipline; every
        // read during the concurrent phase must be one of the written values.
        const int iterations = 50_000;
        int originalCacheCount = PerformanceLogger.ThumbnailCacheCount;
        int originalTimerCount = PerformanceLogger.ActiveMusicTimerCount;
        var unexpected = new System.Collections.Concurrent.ConcurrentBag<int>();
        try
        {
            Task reader = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    int observed = PerformanceLogger.ThumbnailCacheCount;
                    // The pre-write initial value is a legitimate observation
                    // (readers may start before the first write lands); only a
                    // value outside {initial, iterations, iterations*3} is a
                    // torn read.
                    if (observed != originalCacheCount &&
                        observed != iterations &&
                        observed != iterations * 3)
                    {
                        unexpected.Add(observed);
                    }
                }
            });
            Task writer1 = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PerformanceLogger.ThumbnailCacheCount = iterations;
                }
            });
            Task writer2 = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PerformanceLogger.ThumbnailCacheCount = iterations * 3;
                }
            });
            Task timerWriter = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PerformanceLogger.ActiveMusicTimerCount = 1;
                }
            });

            Task.WaitAll(reader, writer1, writer2, timerWriter);

            Assert.Empty(unexpected);
        }
        finally
        {
            PerformanceLogger.ThumbnailCacheCount = originalCacheCount;
            PerformanceLogger.ActiveMusicTimerCount = originalTimerCount;
        }
    }
}
