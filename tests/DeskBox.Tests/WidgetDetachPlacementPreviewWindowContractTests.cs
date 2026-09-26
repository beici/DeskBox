namespace DeskBox.Tests;

/// <summary>
/// DEF-071: the group-detach placement preview must never issue windowing
/// Win32 calls (SetWindowPos, ShowWindow, SetLayeredWindowAttributes) while
/// holding <c>_gate</c>. Those calls synchronously pump messages on the
/// HWND's owning UI thread; when a UI-thread caller (Hide, MarkCommitted,
/// FadeOutAndHideAsync, Dispose) held <c>_gate</c> while the 16 ms tracking
/// poll on the pool thread was mid-call, both threads waited on each other
/// and only a process kill recovered. The fix moves every Win32 call outside
/// the lock (lock-held state snapshot, unlock, apply), and this source-text
/// contract — in the repository's contract-test style — pins that invariant
/// so the pairing cannot quietly return.
/// </summary>
public sealed class WidgetDetachPlacementPreviewWindowContractTests
{
    private const string SourceRelativePath =
        "src/DeskBox/Views/WidgetDetachPlacementPreviewWindow.cs";

    private static readonly string[] s_windowingCalls =
    {
        "SetWindowPos",
        "ShowWindow",
        "SetLayeredWindowAttributes"
    };

    [Fact]
    public void WindowingWin32Calls_NeverAppearInsideGateLockBlocks()
    {
        string source = File.ReadAllText(TestPaths.SourceFile(SourceRelativePath));

        List<(int Start, int End)> lockBlocks = GetGateLockBlocks(source);
        Assert.NotEmpty(lockBlocks);

        foreach ((int start, int end) in lockBlocks)
        {
            string block = source[start..end];
            foreach (string call in s_windowingCalls)
            {
                Assert.DoesNotContain(call, block, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void WindowingWin32Calls_StillExistAndAreAppliedOutsideTheLock()
    {
        // Positive controls: the contract guards a relocation of the Win32
        // calls outside the lock, not their silent deletion, and the plan /
        // apply split must remain in place (mutators queue PendingNativeWork
        // and invoke it after releasing _gate).
        string source = File.ReadAllText(TestPaths.SourceFile(SourceRelativePath));

        Assert.Contains("lock (_gate)", source, StringComparison.Ordinal);
        Assert.Contains("Win32Helper.SetWindowPos(", source, StringComparison.Ordinal);
        Assert.Contains("Win32Helper.ShowWindow(", source, StringComparison.Ordinal);
        Assert.Contains(
            "Win32Helper.SetLayeredWindowAttributes(",
            source,
            StringComparison.Ordinal);
        Assert.Contains("PendingNativeWork", source, StringComparison.Ordinal);
        Assert.Contains("work.Invoke(_hWnd)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the brace-matched body of every <c>lock (_gate)</c> block in
    /// the source. The production file must keep braces out of its comments
    /// and string literals inside those blocks for the naive matcher to stay
    /// exact; TestPathsContractTests-style conventions otherwise apply.
    /// </summary>
    private static List<(int Start, int End)> GetGateLockBlocks(string source)
    {
        var blocks = new List<(int Start, int End)>();
        int searchFrom = 0;
        while (true)
        {
            int lockIndex = source.IndexOf(
                "lock (_gate)",
                searchFrom,
                StringComparison.Ordinal);
            if (lockIndex < 0)
            {
                return blocks;
            }

            searchFrom = lockIndex + 1;

            int open = source.IndexOf('{', lockIndex);
            if (open < 0)
            {
                return blocks;
            }

            int depth = 0;
            int i = open;
            for (; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }

            blocks.Add((open, Math.Min(i + 1, source.Length)));
        }
    }
}
