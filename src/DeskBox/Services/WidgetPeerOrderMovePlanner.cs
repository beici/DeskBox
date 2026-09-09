// Copyright (c) DeskBox. All rights reserved.

namespace DeskBox.Services;

/// <summary>
/// Plans the smallest set of Z-order moves that turns a live window chain into
/// a target order. Re-issuing a move for a window that is already in place is
/// not free: DWM re-samples an acrylic-backed widget's backdrop whenever its
/// Z-order position changes, which users see as an edge flash, and a large
/// batch stalls the compositor long enough to drop frames from a capsule morph.
/// </summary>
internal static class WidgetPeerOrderMovePlanner
{
    internal readonly record struct PeerOrderMove(IntPtr Handle, IntPtr InsertAfter);

    /// <summary>
    /// Returns the moves to apply in order, an empty list when the live chain
    /// already matches the target, or <c>null</c> when the live chain does not
    /// contain exactly the target windows so the caller must fall back to a
    /// full reorder.
    /// </summary>
    internal static IReadOnlyList<PeerOrderMove>? Plan(
        IReadOnlyList<IntPtr> targetHighestToLowest,
        IReadOnlyList<IntPtr> liveHighestToLowest,
        IntPtr boundary)
    {
        int count = targetHighestToLowest.Count;
        if (count == 0 || liveHighestToLowest.Count != count)
        {
            return null;
        }

        var targetIndex = new Dictionary<IntPtr, int>(count);
        for (int index = 0; index < count; index++)
        {
            targetIndex[targetHighestToLowest[index]] = index;
        }

        if (targetIndex.Count != count)
        {
            return null;
        }

        int[] sequence = new int[count];
        var liveIndexOf = new Dictionary<IntPtr, int>(count);
        for (int index = 0; index < count; index++)
        {
            IntPtr handle = liveHighestToLowest[index];
            if (!targetIndex.TryGetValue(handle, out int target))
            {
                return null;
            }

            sequence[index] = target;
            liveIndexOf[handle] = index;
        }

        // Longest strictly increasing subsequence of the live positions: those
        // windows are already in the correct relative order and stay put.
        int[] lengths = new int[count];
        int[] previous = new int[count];
        int bestEnd = 0;
        for (int index = 0; index < count; index++)
        {
            lengths[index] = 1;
            previous[index] = -1;
            for (int prior = 0; prior < index; prior++)
            {
                if (sequence[prior] < sequence[index] &&
                    lengths[prior] + 1 > lengths[index])
                {
                    lengths[index] = lengths[prior] + 1;
                    previous[index] = prior;
                }
            }

            if (lengths[index] > lengths[bestEnd])
            {
                bestEnd = index;
            }
        }

        var keptLiveIndex = new HashSet<int>();
        for (int index = bestEnd; index >= 0; index = previous[index])
        {
            keptLiveIndex.Add(index);
        }

        // Emit the movers highest-to-lowest. SetWindowPos can only insert a
        // window *after* another one, so a mover's anchor - its target
        // predecessor - has to be settled first, and a predecessor always has
        // the lower target index. Walking bottom-to-top instead anchored each
        // mover to a predecessor that had not moved yet, so the pass reported
        // success while leaving the chain out of order; the next pass then
        // moved the survivors and flashed their backdrops all over again.
        var moves = new List<PeerOrderMove>();
        for (int index = 0; index < count; index++)
        {
            IntPtr handle = targetHighestToLowest[index];
            if (keptLiveIndex.Contains(liveIndexOf[handle]))
            {
                continue;
            }

            moves.Add(new PeerOrderMove(
                handle,
                index == 0 ? boundary : targetHighestToLowest[index - 1]));
        }

        return moves;
    }
}
