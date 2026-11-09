namespace DeskBox.Tests;

/// <summary>
/// Pins the round-14 concurrency fixes: cancelling a pending restore now
/// signals an in-flight apply instead of queueing behind the gate that the
/// apply holds for its whole run, and a settings save can no longer commit the
/// empty default profile that the load holds before it deserializes the file.
/// </summary>
public sealed class RoundFourteenConcurrencyContractTests
{
    [Fact]
    public void RestoreCancel_AbortsAnApplyThatAlreadyOwnsTheGate()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/DeskBoxDataBackupService.cs"));

        Assert.Contains(
            "private volatile bool _restoreCancelRequested;",
            source,
            StringComparison.Ordinal);

        int cancelStart = source.IndexOf(
            "public async Task CancelPendingRestoreAsync(",
            StringComparison.Ordinal);
        Assert.True(cancelStart >= 0, "CancelPendingRestoreAsync was not found");
        int cancelEnd = source.IndexOf(
            "internal async Task<DeskBoxRestoreApplyResult> ApplyPendingRestoreAsync(",
            StringComparison.Ordinal);
        Assert.True(cancelEnd > cancelStart, "ApplyPendingRestoreAsync was not found after the cancel method");
        string cancelBody = source[cancelStart..cancelEnd];

        // The flag must be raised before the gate is touched: waiting on the
        // gate first is exactly what let the cancel arrive after the swap.
        int flagIndex = cancelBody.IndexOf("_restoreCancelRequested = true;", StringComparison.Ordinal);
        int waitIndex = cancelBody.IndexOf(
            "await _gate.WaitAsync(TimeSpan.Zero, cancellationToken)",
            StringComparison.Ordinal);
        Assert.True(flagIndex >= 0, "the cancel path does not raise the abort flag");
        Assert.True(waitIndex > flagIndex, "the abort flag must be raised before waiting on the gate");
        Assert.DoesNotContain(
            "await _gate.WaitAsync(cancellationToken);",
            cancelBody,
            StringComparison.Ordinal);

        // Three consumers: the queued-apply check, the full-restore swap
        // guard, and the per-domain boundary inside the scoped restore.
        Assert.True(
            CountOccurrences(source, "if (_restoreCancelRequested)") >= 3,
            "the apply path does not observe the abort flag at its boundaries");
        Assert.Equal(2, CountOccurrences(source, "\"The restore was cancelled by the user.\""));

        // The flag is consumed by the apply's own finally, otherwise a cancel
        // that raced the last boundary check would abort the next restore.
        int applyStart = source.IndexOf(
            "internal async Task<DeskBoxRestoreApplyResult> ApplyPendingRestoreAsync(",
            StringComparison.Ordinal);
        string applyBody = source[applyStart..];
        int resetIndex = applyBody.IndexOf("_restoreCancelRequested = false;", StringComparison.Ordinal);
        int releaseIndex = applyBody.IndexOf("_gate.Release();", StringComparison.Ordinal);
        Assert.True(resetIndex >= 0, "the apply path never clears the abort flag");
        Assert.True(releaseIndex > resetIndex, "the abort flag must be cleared before the gate is released");
    }

    [Fact]
    public void SettingsSave_RefusesToCommitBeforeTheLoadCompletes()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/SettingsService.cs"));

        Assert.Contains(
            "private volatile bool _loadCompleted = true;",
            source,
            StringComparison.Ordinal);

        int loadStart = source.IndexOf("public async Task LoadAsync()", StringComparison.Ordinal);
        Assert.True(loadStart >= 0, "LoadAsync was not found");
        int loadEnd = source.IndexOf("private static int? TryReadRawSchemaVersion(", StringComparison.Ordinal);
        Assert.True(loadEnd > loadStart, "the load method tail was not found");
        string loadBody = source[loadStart..loadEnd];
        Assert.Contains("_loadCompleted = false;", loadBody, StringComparison.Ordinal);
        Assert.Contains("_loadCompleted = true;", loadBody, StringComparison.Ordinal);
        Assert.True(
            loadBody.LastIndexOf("_loadCompleted = true;", StringComparison.Ordinal) >
            loadBody.IndexOf("_loadCompleted = false;", StringComparison.Ordinal),
            "the load must reopen the write path once it finishes");

        int saveStart = source.IndexOf("private async Task<bool> SaveToFileOnlyAsync()", StringComparison.Ordinal);
        Assert.True(saveStart >= 0, "SaveToFileOnlyAsync was not found");
        string saveBody = source[saveStart..];
        int guardIndex = saveBody.IndexOf("if (!_loadCompleted)", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0, "the save path does not check the load flag");
        Assert.Contains(
            "\"settings load has not completed; refusing to commit the default profile\"",
            saveBody,
            StringComparison.Ordinal);
        Assert.True(
            guardIndex < saveBody.IndexOf("if (_settingsProfileUnreadable)", StringComparison.Ordinal),
            "the load guard must run with the other write-path gates, before any commit");
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskBox.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relativePath} was not found at {path}");
        return File.ReadAllText(path);
    }

    private static string StripCommentLines(string source)
    {
        var kept = new List<string>();
        foreach (string line in source.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("///", StringComparison.Ordinal) ||
                trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = source.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
