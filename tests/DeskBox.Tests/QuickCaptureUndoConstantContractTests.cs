using System.Reflection;
using System.Text.RegularExpressions;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// FQC-14: pins the coupling between the QuickCapture delete-undo retention
/// window and the delete feedback toast duration. The delete toasts carry an
/// Undo action, so <see cref="WidgetFeedbackPolicy"/> gives them the long
/// action duration (5s); the service-side protection window
/// (<c>QuickCaptureService.UndoRetentionWindow</c>, 10s) must stay strictly
/// longer — while the toast is up, the user can still press Undo, so the
/// deleted item's image/attachment retention must outlive the toast. Both
/// durations are read from production code (no value is duplicated here), so
/// a one-sided adjustment breaks this test instead of silently letting the
/// Undo affordance outlive the protection.
/// </summary>
public sealed class QuickCaptureUndoConstantContractTests
{
    [Fact]
    public void UndoProtectionWindow_IsStrictlyLongerThanUndoToastDuration()
    {
        TimeSpan protection = ReadUndoRetentionWindow();
        TimeSpan toast = WidgetFeedbackPolicy.GetDisplayDuration(
            CreateUndoToastRequest());

        Assert.True(protection > TimeSpan.Zero, "the protection window must be positive");
        Assert.True(toast > TimeSpan.Zero, "the undo toast duration must be positive");
        Assert.True(
            protection > toast,
            $"QuickCaptureService undo protection window ({protection}) must stay " +
            $"strictly longer than the delete toast duration ({toast}): the toast " +
            "offers Undo for its whole display, so the image/attachment retention " +
            "must outlive it.");
    }

    [Fact]
    public void DeleteToastPaths_KeepUndoActionShapeThatDrivesTheLongDuration()
    {
        // The protection contract above is only meaningful while the delete
        // toasts remain ACTION toasts (the policy's long-duration branch). The
        // quick-capture surface cannot be instantiated headlessly, so the
        // wiring is pinned with source contracts, matching the existing
        // contract-test pattern (QuickCaptureDataIntegrityContractTests).
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs"));

        // Both delete exits (single delete and batch delete) must raise the
        // toast with an Undo action text plus a restore action lambda. A new
        // delete exit must keep the same shape; update this pin together with
        // the retention contract above.
        Assert.Equal(
            2,
            Regex.Matches(
                source,
                "T\\(\"Common\\.Undo\"\\),\\s*async \\(\\) =>",
                RegexOptions.None).Count);
    }

    private static TimeSpan ReadUndoRetentionWindow()
    {
        FieldInfo field = typeof(QuickCaptureService).GetField(
            "UndoRetentionWindow",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "QuickCaptureService.UndoRetentionWindow was not found; this test " +
                "pins its value against the undo toast duration (FQC-14).");
        return Assert.IsType<TimeSpan>(field.GetValue(null));
    }

    private static WidgetFeedbackRequest CreateUndoToastRequest()
    {
        // Same shape as the delete paths in QuickCaptureSurfaceContent: a
        // message, Success severity, a dedup key, and — the load-bearing part —
        // a non-whitespace action text plus a non-null action, which selects
        // WidgetFeedbackPolicy's action-toast duration branch.
        return new WidgetFeedbackRequest(
            "QuickCapture.Deleted",
            WidgetFeedbackSeverity.Success,
            "quick-delete",
            "Undo",
            () => Task.CompletedTask);
    }
}
