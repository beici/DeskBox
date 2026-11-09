using System.Buffers;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DeskBox.Services;
using Windows.Graphics;

namespace DeskBox.Tests;

public sealed class WidgetCompactAnimationCoordinatorTests : IDisposable
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type MoveType = typeof(WidgetCompactAnimationCoordinator)
        .GetNestedType("PendingBoundsMove", BindingFlags.NonPublic)!;
    private static readonly MethodInfo FlushMethod = typeof(WidgetCompactAnimationCoordinator)
        .GetMethod("FlushPendingBoundsMoves", PrivateStatic)!;
    private static readonly IDictionary PendingMoves = (IDictionary)typeof(WidgetCompactAnimationCoordinator)
        .GetField("PendingBoundsMoves", PrivateStatic)!.GetValue(null)!;
    private static readonly IList MoveBuffer = (IList)typeof(WidgetCompactAnimationCoordinator)
        .GetField("PendingBoundsMovesBuffer", PrivateStatic)!.GetValue(null)!;

    public WidgetCompactAnimationCoordinatorTests() => Reset();

    public void Dispose() => Reset();

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(129, false)]
    [InlineData(129, true)]
    public void FailedPreparation_CompletesEveryMoveAndReleasesCallbacks(int moveCount, bool completionThrows)
    {
        // The overflow path must clear recycled results before a preparation
        // failure can report them to completion callbacks.
        bool[] dirtyResults = ArrayPool<bool>.Shared.Rent(moveCount);
        Array.Fill(dirtyResults, true);
        ArrayPool<bool>.Shared.Return(dirtyResults);

        BatchObservation observation = FlushFailedPreparation(moveCount, completionThrows);

        Assert.Equal([0], observation.Prepared);
        Assert.Equal(Enumerable.Range(0, moveCount), observation.Completed.Select(result => result.Index));
        Assert.All(observation.Completed, result => Assert.False(result.Succeeded));
        Assert.Equal(0, observation.Fallbacks);
        Assert.Empty(PendingMoves);
        Assert.Empty(MoveBuffer);

        // Check object lifetime as well as List.Count: the reusable storage
        // must release its delegates even if this is the last animation frame.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(observation.Owners, owner => Assert.False(owner.IsAlive));
    }

    [Fact]
    public void EmptyFlush_DoesNotRepeatCompletedCallbacks()
    {
        BatchObservation observation = FlushFailedPreparation(1, completionThrows: false);

        FlushMethod.Invoke(null, null);

        Assert.Single(observation.Completed);
        Assert.Empty(PendingMoves);
        Assert.Empty(MoveBuffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BatchObservation FlushFailedPreparation(int moveCount, bool completionThrows)
    {
        var observation = new BatchObservation();
        for (int index = 0; index < moveCount; index++)
        {
            var owner = new CallbackOwner(index, observation, completionThrows);
            observation.Owners.Add(new WeakReference(owner));
            IntPtr handle = new(index + 1);
            PendingMoves[handle] = Activator.CreateInstance(MoveType,
            [
                handle,
                new RectInt32(0, 0, 100, 100),
                0u,
                (Action)owner.Prepare,
                (Action<bool>)owner.Complete,
                (Action)owner.Fallback,
            ])!;
        }

        // Preparation fails before TryCommitBatch, so no real HWND or native
        // window operation is involved in these lifecycle tests.
        TargetInvocationException failure = Assert.Throws<TargetInvocationException>(
            () => FlushMethod.Invoke(null, null));
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal("Preparation failed.", failure.InnerException!.Message);
        return observation;
    }

    private static void Reset()
    {
        PendingMoves.Clear();
        MoveBuffer.Clear();
    }

    private sealed class BatchObservation
    {
        public List<int> Prepared { get; } = [];
        public List<(int Index, bool Succeeded)> Completed { get; } = [];
        public List<WeakReference> Owners { get; } = [];
        public int Fallbacks { get; set; }
    }

    private sealed class CallbackOwner(int index, BatchObservation observation, bool completionThrows)
    {
        public void Prepare()
        {
            observation.Prepared.Add(index);
            throw new InvalidOperationException("Preparation failed.");
        }

        public void Complete(bool succeeded)
        {
            observation.Completed.Add((index, succeeded));
            if (completionThrows && index == 0)
            {
                throw new InvalidOperationException("Completion failed.");
            }
        }

        public void Fallback() => observation.Fallbacks++;
    }
}
