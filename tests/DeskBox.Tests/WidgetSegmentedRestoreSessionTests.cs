using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class WidgetSegmentedRestoreSessionTests
{
    [Fact]
    public void HiddenHost_DoesNotStartRenderingWait()
    {
        var session = new WidgetSegmentedRestoreSession();

        Assert.False(session.TryStart(240));
        Assert.False(session.IsWaiting);
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Deferred, session.ObserveFrame(240));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(95.9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void UnsafeLayoutSlot_WaitsForAnEventWithoutStartingRendering(double width)
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();

        Assert.False(session.TryStart(width));
        Assert.False(session.IsWaiting);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(240)]
    [InlineData(1920)]
    public void SafeLayout_StillRequiresThreeRenderedStableFrames(double width)
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();

        Assert.True(session.TryStart(width));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(width));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(width));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Ready, session.ObserveFrame(width));
        Assert.False(session.IsWaiting);
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Deferred, session.ObserveFrame(width));
    }

    [Fact]
    public void HideDuringWait_CancelsAndRevealStartsACompleteNewAttempt()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));
        session.ObserveFrame(240);
        session.ObserveFrame(240);

        session.SetHostVisibility(false);

        Assert.False(session.IsWaiting);
        Assert.False(session.TryStart(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Deferred, session.ObserveFrame(240));

        session.SetHostVisibility(true);
        Assert.True(session.TryStart(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Ready, session.ObserveFrame(240));
    }

    [Fact]
    public void DuplicateVisibilityAndQueueNotifications_DoNotRestartStableProgress()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));
        session.ObserveFrame(240);
        session.ObserveFrame(240);

        session.SetHostVisibility(true);

        Assert.False(session.TryStart(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Ready, session.ObserveFrame(240));
    }

    [Fact]
    public void WidthChange_RequiresThreeFramesAtTheNewSlot()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));
        session.ObserveFrame(240);
        session.ObserveFrame(240);
        session.NotifyLayoutChanged();

        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(300));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(300));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Ready, session.ObserveFrame(300));
    }

    [Fact]
    public void FractionalLayoutNoise_PreservesTheExistingHalfPixelTolerance()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));

        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240.25));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Ready, session.ObserveFrame(240.5));
    }

    [Fact]
    public void AccumulatedFractionalDrift_DoesNotMasqueradeAsAStableSlot()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));

        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240.4));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240.8));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240.8));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Ready, session.ObserveFrame(240.8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void SlotBecomesUnsafe_CancelsImmediatelyWithoutAnAutomaticRetry(double width)
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));
        session.ObserveFrame(240);
        session.NotifyLayoutChanged();

        Assert.Equal(WidgetSegmentedRestoreFrameResult.Deferred, session.ObserveFrame(width));
        Assert.False(session.IsWaiting);
        Assert.False(session.TryStart(width));

        session.NotifyLayoutChanged();
        Assert.True(session.TryStart(240));
    }

    [Fact]
    public void UnstableFrames_ExhaustTheirAttemptInsteadOfPollingForever()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));

        for (int index = 0; index < WidgetSegmentedRestoreSession.MaximumFramesPerAttempt - 1; index++)
        {
            Assert.Equal(
                WidgetSegmentedRestoreFrameResult.Waiting,
                session.ObserveFrame(240 + index));
        }

        Assert.Equal(WidgetSegmentedRestoreFrameResult.Deferred, session.ObserveFrame(300));
        Assert.False(session.IsWaiting);
    }

    [Fact]
    public void FinalSizeEventAtBudgetBoundary_IsConsumedByOneMoreAttempt()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));
        for (int index = 0; index < WidgetSegmentedRestoreSession.MaximumFramesPerAttempt - 1; index++)
        {
            session.ObserveFrame(240 + index);
        }

        session.NotifyLayoutChanged();
        Assert.Equal(WidgetSegmentedRestoreFrameResult.RetryAfterLayoutChange, session.ObserveFrame(300));
        Assert.False(session.IsWaiting);

        Assert.True(session.TryStart(300));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(300));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(300));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Ready, session.ObserveFrame(300));
    }

    [Fact]
    public void ConsumedSizeEvent_DoesNotCreateAnEndlessAutomaticRetry()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));
        session.NotifyLayoutChanged();
        Assert.Equal(WidgetSegmentedRestoreFrameResult.RetryAfterLayoutChange, ExhaustAttempt(session));

        Assert.True(session.TryStart(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Deferred, ExhaustAttempt(session));
        Assert.False(session.IsWaiting);
    }

    [Fact]
    public void HideBeforeDeferredRetry_PreventsTheQueuedAttemptFromStarting()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));
        session.NotifyLayoutChanged();
        Assert.Equal(WidgetSegmentedRestoreFrameResult.RetryAfterLayoutChange, ExhaustAttempt(session));

        session.SetHostVisibility(false);

        Assert.False(session.TryStart(240));
        Assert.False(session.IsWaiting);
    }

    [Fact]
    public void CancellationForUnloadOrTransition_DiscardsStableFrames()
    {
        WidgetSegmentedRestoreSession session = CreateVisibleSession();
        Assert.True(session.TryStart(240));
        session.ObserveFrame(240);
        session.ObserveFrame(240);

        session.Cancel();

        Assert.Equal(WidgetSegmentedRestoreFrameResult.Deferred, session.ObserveFrame(240));
        Assert.True(session.TryStart(240));
        Assert.Equal(WidgetSegmentedRestoreFrameResult.Waiting, session.ObserveFrame(240));
    }

    private static WidgetSegmentedRestoreSession CreateVisibleSession()
    {
        var session = new WidgetSegmentedRestoreSession();
        session.SetHostVisibility(true);
        return session;
    }

    private static WidgetSegmentedRestoreFrameResult ExhaustAttempt(WidgetSegmentedRestoreSession session)
    {
        WidgetSegmentedRestoreFrameResult result = WidgetSegmentedRestoreFrameResult.Waiting;
        for (int index = 0; index < WidgetSegmentedRestoreSession.MaximumFramesPerAttempt; index++)
        {
            result = session.ObserveFrame(240 + index);
        }
        return result;
    }
}
