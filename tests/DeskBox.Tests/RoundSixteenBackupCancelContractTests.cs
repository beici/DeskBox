using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DeskBox.Tests;

/// <summary>
/// Pins the round-16 fix for the scoped-restore cancel. The apply loop aborts by
/// throwing when the flag is set, and the flag-only OCE used to fall through to
/// the retry handler: the attempt counter grew, the marker survived, and the
/// next launch applied the very restore the user had cancelled. The dedicated
/// handler must drop the marker and the staging payload instead.
/// </summary>
public sealed class RoundSixteenBackupCancelContractTests
{
    [Fact]
    public void ScopedCancel_DiscardsThePendingRestoreInsteadOfRetryingIt()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/DeskBoxDataBackupService.cs"));

        int handler = source.IndexOf(
            "catch (OperationCanceledException) when (_restoreCancelRequested)",
            StringComparison.Ordinal);
        Assert.True(handler >= 0, "the flag-only cancel handler is missing");

        int retryHandler = source.IndexOf(
            "[DataBackup] Scoped restore failed (will retry on next launch",
            StringComparison.Ordinal);
        Assert.True(retryHandler > handler, "the cancel handler must run before the retry handler");

        string cancelBlock = source[handler..retryHandler];
        Assert.Contains("TryDeleteFile(PendingRestoreMarkerPath);", cancelBlock, StringComparison.Ordinal);
        Assert.Contains("TryDeleteDirectory(stagingRoot);", cancelBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAttemptCount = attempts", cancelBlock, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above the test output.");
    }

    private static string StripCommentLines(string source) =>
        string.Join(
            '\n',
            source.Split('\n').Where(line =>
            {
                string trimmed = line.TrimStart();
                return !trimmed.StartsWith("//", StringComparison.Ordinal)
                    && !trimmed.StartsWith("///", StringComparison.Ordinal);
            }));
}
