using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class WidgetCompactTransitionProgressPolicyTests
{
    [Fact]
    public void AdaptedSubmissionCadence_AdvancesAtFullSpeedWithoutSpendingStallBudget()
    {
        const double displayBudgetMs = 1000d / 120;
        const double submissionWorkMs = 24;
        var pacing = new WidgetAnimationFramePacingPolicy();
        pacing.Reset(0, displayBudgetMs);
        for (int frame = 0; frame < 8; frame++)
        {
            pacing.RecordSubmission(frame * 40, submissionWorkMs);
        }
        Assert.True(pacing.TargetIntervalMilliseconds > displayBudgetMs * 2);

        double maximumStallMs = WidgetCompactTransitionProgressPolicy.ResolveMaximumStallMs(240);
        double stalledMs = 0;
        double progressMs = 0;
        double elapsedMs = 0;
        for (int frame = 0; frame < 20; frame++)
        {
            double intervalMs = pacing.TargetIntervalMilliseconds;
            var step = WidgetCompactTransitionProgressPolicy.ResolveStep(
                intervalMs, intervalMs, maximumStallMs - stalledMs);
            elapsedMs += intervalMs;
            progressMs += step.ProgressMilliseconds;
            stalledMs += step.AbsorbedStallMilliseconds;
            pacing.RecordSubmission(400 + elapsedMs, submissionWorkMs);
        }

        Assert.Equal(elapsedMs, progressMs, 6);
        Assert.Equal(0, stalledMs);
    }

    [Fact]
    public void SlowerDisplayChange_DoesNotTurnNormalFramesIntoStalls()
    {
        var pacing = new WidgetAnimationFramePacingPolicy();
        pacing.Reset(0, 1000d / 240);
        pacing.RecordSubmission(0, 0.5);
        Assert.True(pacing.ShouldSubmit(20, 1000d / 30));

        var step = WidgetCompactTransitionProgressPolicy.ResolveStep(
            1000d / 30, pacing.TargetIntervalMilliseconds, 180);

        Assert.Equal(1000d / 30, step.ProgressMilliseconds, 6);
        Assert.Equal(0, step.AbsorbedStallMilliseconds);
    }

    [Fact]
    public void RecoveredFastCadence_AgainAbsorbsRealStallsAtTheCurrentRate()
    {
        const double displayBudgetMs = 1000d / 120;
        var pacing = new WidgetAnimationFramePacingPolicy();
        pacing.Reset(0, displayBudgetMs);
        for (int frame = 0; frame < 8; frame++)
        {
            pacing.RecordSubmission(frame * 40, 24);
        }
        for (int frame = 8; frame < 64; frame++)
        {
            pacing.RecordSubmission(frame * 40, 0.5);
        }
        Assert.Equal(displayBudgetMs, pacing.TargetIntervalMilliseconds, 6);

        var step = WidgetCompactTransitionProgressPolicy.ResolveStep(
            100, pacing.TargetIntervalMilliseconds, 180);

        Assert.Equal(1000d / 60, step.ProgressMilliseconds, 6);
        Assert.Equal(100 - 1000d / 60, step.AbsorbedStallMilliseconds, 6);
    }

    [Fact]
    public void FinalRecoveryStep_OnlySpendsTheRemainingBudget()
    {
        var step = WidgetCompactTransitionProgressPolicy.ResolveStep(100, 1000d / 120, 5);

        Assert.Equal(95, step.ProgressMilliseconds);
        Assert.Equal(5, step.AbsorbedStallMilliseconds);
    }

    [Fact]
    public void RepeatedLongStalls_NeverStretchTheMorphBeyondItsRecoveryBudget()
    {
        double maximumStallMs = WidgetCompactTransitionProgressPolicy.ResolveMaximumStallMs(240);
        double stalledMs = 0;
        double progressMs = 0;
        foreach (double intervalMs in new[] { 100d, 100d, 100d, 100d })
        {
            var step = WidgetCompactTransitionProgressPolicy.ResolveStep(
                intervalMs, 1000d / 120, maximumStallMs - stalledMs);
            progressMs += step.ProgressMilliseconds;
            stalledMs += step.AbsorbedStallMilliseconds;
            Assert.InRange(stalledMs, 0, maximumStallMs);
        }

        Assert.Equal(maximumStallMs, stalledMs, 6);
        Assert.Equal(400 - maximumStallMs, progressMs, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidOrBackwardClockDelta_NeitherAdvancesNorConsumesRecovery(double rawStepMs)
    {
        var step = WidgetCompactTransitionProgressPolicy.ResolveStep(rawStepMs, 1000d / 120, 180);

        Assert.Equal(0, step.ProgressMilliseconds);
        Assert.Equal(0, step.AbsorbedStallMilliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidSubmissionInterval_UsesTheDefaultFrameBudget(double submissionIntervalMs)
    {
        var step = WidgetCompactTransitionProgressPolicy.ResolveStep(100, submissionIntervalMs, 180);

        Assert.Equal(1000d / 30, step.ProgressMilliseconds, 6);
        Assert.Equal(100 - 1000d / 30, step.AbsorbedStallMilliseconds, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidOrExhaustedRecoveryBudget_DoesNotExtendTheTransition(double remainingStallBudgetMs)
    {
        var step = WidgetCompactTransitionProgressPolicy.ResolveStep(100, 1000d / 120, remainingStallBudgetMs);

        Assert.Equal(100, step.ProgressMilliseconds);
        Assert.Equal(0, step.AbsorbedStallMilliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidDuration_KeepsTheDefaultBoundedRecoveryBudget(double durationMs)
    {
        Assert.Equal(180, WidgetCompactTransitionProgressPolicy.ResolveMaximumStallMs(durationMs));
    }

    [Fact]
    public void LargeFiniteInputs_DoNotProduceInfiniteProgressOrStepLimits()
    {
        double maximumStepMs = WidgetCompactTransitionProgressPolicy.ResolveMaximumStepMs(
            double.MaxValue, int.MaxValue);
        var step = WidgetCompactTransitionProgressPolicy.ResolveStep(
            double.MaxValue, double.MaxValue, 180);

        Assert.Equal(double.MaxValue, maximumStepMs);
        Assert.Equal(double.MaxValue, step.ProgressMilliseconds);
        Assert.Equal(0, step.AbsorbedStallMilliseconds);
    }
}
