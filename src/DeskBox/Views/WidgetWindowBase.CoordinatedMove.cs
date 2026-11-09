using DeskBox.Helpers;
using DeskBox.Services;
using Windows.Graphics;

namespace DeskBox.Views;

public abstract partial class WidgetWindowBase
{
    public bool CanParticipateInCoordinatedMove =>
        !IsPositionLocked &&
        !IsClosing &&
        !IsHideAnimationRunning;

    public RectInt32 CoordinatedMoveBounds => GetActualWindowBounds();

    public void BeginCoordinatedMoveParticipation(bool isSource)
    {
        if (!isSource)
        {
            BeginWidgetBoundsInteraction();
            DisplayChangeWatcher?.SuppressRestore();
        }
    }

    public void PrepareCoordinatedMoveBounds(RectInt32 bounds)
    {
        if (IsClosing)
        {
            return;
        }

        IsApplyingBounds = true;
        if (IsCompactBoundsStateActive)
        {
            _stableCompactBounds = bounds;
            _compactArrangementSizeOverride = new SizeInt32(bounds.Width, bounds.Height);
        }
    }

    public void CompleteCoordinatedMoveBoundsPreview()
    {
        IsApplyingBounds = false;
    }

    public void ApplyCoordinatedMoveBoundsFallback(RectInt32 bounds)
    {
        if (IsClosing)
        {
            return;
        }

        ApplyWindowBounds(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            persist: false,
            updateConfig: false);
    }

    public void CompleteCoordinatedMoveParticipation(bool hasMoved, bool isSource)
    {
        try
        {
            if (hasMoved && !IsClosing)
            {
                RectInt32 finalBounds = GetActualWindowBounds();
                finalBounds = CompleteExpandedWidgetDrag(
                    finalBounds,
                    coordinateCapsuleBar: false);
                CommitUserPlacementFromWindow(WidgetPlacementSource.CoordinatedMove);
            }
        }
        finally
        {
            EndWidgetBoundsInteraction();
            DisplayChangeWatcher?.ResumeRestore();
            if (isSource)
            {
                OnDragEnd(hasMoved);
            }
            // The source window's Ctrl-drag early-exit paths return before
            // reaching their own RestoreBackdropAfterInteraction call, and
            // QueueBackdropRefresh alone short-circuits when the backdrop
            // signature did not change (Backdrop.cs documents this), which
            // would leave a Win10 legacy-acrylic window dimmed until the
            // next manual drag. RestoreBackdropAfterInteraction is
            // idempotent (guarded by _isInteractionBackdropDowngraded), so
            // running it for every participant covers both early exits
            // without disturbing windows that already restored.
            RestoreBackdropAfterInteraction();
            QueueBackdropRefresh();
        }
    }
}
