namespace DeskBox.Services;

internal static class FileRenderWindowGrowthPolicy
{
    private const int MinimumScrollGrowthItems = 16;
    private const int MaximumScrollGrowthItems = 64;

    /// <summary>
    /// Chooses one budget request from a settled layout snapshot. Filling a
    /// larger viewport takes precedence over scroll prefetch: its old extent
    /// cannot describe the items that the coverage request is about to add.
    /// </summary>
    internal static int ComputeRequestedMinimum(
        int renderedCount,
        int viewportMinimum,
        bool canGrow,
        bool isNearRenderedEnd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(renderedCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportMinimum);

        if (!canGrow || renderedCount < viewportMinimum || !isNearRenderedEnd)
        {
            // Preserve the viewport budget even before any items have loaded;
            // the view model owns its monotonic budget and source-count clamp.
            return viewportMinimum;
        }

        int growth = Math.Clamp(
            viewportMinimum, MinimumScrollGrowthItems, MaximumScrollGrowthItems);
        return (int)Math.Min(int.MaxValue, (long)renderedCount + growth);
    }
}
