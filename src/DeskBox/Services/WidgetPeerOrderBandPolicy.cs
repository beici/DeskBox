namespace DeskBox.Services;

internal readonly record struct WidgetPeerOrderWindow(
    IntPtr Handle,
    bool IsVisible,
    bool IsTopMost);

/// <summary>
/// Repairs the order inside each existing visible window band. A foreign
/// window (including an auxiliary DeskBox window) or a topmost boundary
/// separates bands: repairing capsule shadows must not raise peers over it.
/// </summary>
internal static class WidgetPeerOrderBandPolicy
{
    public static IReadOnlyList<IReadOnlyList<IntPtr>>? Plan(
        IReadOnlyList<IntPtr> requestedHighestToLowest,
        IReadOnlyList<WidgetPeerOrderWindow> observedHighestToLowest)
    {
        var ranks = new Dictionary<IntPtr, int>();
        for (int index = 0; index < requestedHighestToLowest.Count; index++)
        {
            IntPtr handle = requestedHighestToLowest[index];
            if (handle == IntPtr.Zero || !ranks.TryAdd(handle, index))
            {
                return null;
            }
        }

        var bands = new List<IReadOnlyList<IntPtr>>();
        var seen = new HashSet<IntPtr>();
        List<IntPtr>? band = null;
        bool? topMost = null;
        foreach (WidgetPeerOrderWindow window in observedHighestToLowest)
        {
            if (!window.IsVisible)
            {
                continue;
            }

            if (!ranks.ContainsKey(window.Handle))
            {
                band = null;
                topMost = null;
                continue;
            }

            if (!seen.Add(window.Handle))
            {
                return null;
            }

            if (band is null || topMost != window.IsTopMost)
            {
                band = [];
                bands.Add(band);
                topMost = window.IsTopMost;
            }

            band.Add(window.Handle);
        }

        // A disappearing HWND or an incomplete native walk must not turn an
        // unknown boundary into permission to move the remaining windows.
        if (seen.Count != ranks.Count)
        {
            return null;
        }

        return bands.Select(band => (IReadOnlyList<IntPtr>)band
            .OrderBy(handle => ranks[handle]).ToArray()).ToArray();
    }
}
