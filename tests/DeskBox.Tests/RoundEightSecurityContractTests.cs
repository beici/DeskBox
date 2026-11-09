using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Pins the round-8 security fixes: the restore rebase normalizes archive paths
/// before trusting them, the Everything native gate cannot be held forever, the
/// thumbnail proxy bounds its output drains, widget ids cannot address a parent
/// directory, and a failed GDI blit no longer produces a black "successful"
/// screenshot.
/// </summary>
public sealed class RoundEightSecurityContractTests
{
    [Fact]
    public void RestoreRebase_NormalizesTheArchivePathBeforeTrustingIt()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/RestoreMachineLocalPathRebase.cs"));

        Assert.Contains("normalizedSource = Path.GetFullPath(sourceProfile)", source, StringComparison.Ordinal);
        Assert.Contains("candidate = Path.GetFullPath(trimmed);", source, StringComparison.Ordinal);
        Assert.Contains("if (!IsStrictlyUnderRoot(candidate, normalizedSource))", source, StringComparison.Ordinal);
        Assert.Contains("if (!IsUnderRootOrEqual(rebased, normalizedTarget))", source, StringComparison.Ordinal);

        // The old shape trusted the raw archive string and only normalized the
        // result, which let "..\" segments escape the target profile.
        Assert.DoesNotContain("Path.Combine(targetProfile, suffix)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsStrictlyUnderRoot(trimmed, sourceProfile))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeQueryGate_DoesNotWaitForever()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/EverythingSearchService.cs"));

        Assert.Contains(
            "if (!await _nativeGate.WaitAsync(NativeQueryTimeout, cancellationToken).ConfigureAwait(false))",
            source,
            StringComparison.Ordinal);
        Assert.Contains("DiagnosticCode = \"gate-timeout\"", source, StringComparison.Ordinal);

        // Only the query path is pinned: the connection refresh probe keeps its
        // own short-lived acquisition without the query timeout.
        int queryStart = source.IndexOf("string providerQuery = BuildProviderQuery(", StringComparison.Ordinal);
        int queryEnd = source.IndexOf("inFlightQuery = Task.Run(", StringComparison.Ordinal);
        Assert.True(queryStart >= 0 && queryEnd > queryStart, "query gate region was not found");
        string queryRegion = source[queryStart..queryEnd];
        Assert.DoesNotContain("_nativeGate.WaitAsync(cancellationToken)", queryRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void ThumbnailOutputDrain_IsBounded()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Helpers/ShellThumbnailProxy.cs"));

        Assert.Contains("await Task.WhenAll(outputTask, errorTask).WaitAsync(ExtractionTimeout);", source, StringComparison.Ordinal);
        Assert.Contains("catch (TimeoutException)", source, StringComparison.Ordinal);
        Assert.Contains("Output drain timed out", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WidgetId_RejectsDirectoryReferences()
    {
        string dot = TodoWidgetStore.SanitizeWidgetId("..");
        string dots = TodoWidgetStore.SanitizeWidgetId("...");
        string spaces = TodoWidgetStore.SanitizeWidgetId("   ");

        Assert.NotEqual("..", dot);
        Assert.NotEqual("...", dots);
        Assert.NotEqual(string.Empty, spaces);
        Assert.Equal(32, dot.Length);
        Assert.Equal(32, dots.Length);
        Assert.Equal(32, spaces.Length);
    }

    [Fact]
    public void WidgetId_KeepsOrdinaryIdsAndTrimsTrailingDots()
    {
        Assert.Equal("widget-1", TodoWidgetStore.SanitizeWidgetId("widget-1"));
        Assert.Equal("todo", TodoWidgetStore.SanitizeWidgetId("todo."));
        Assert.Equal("a b", TodoWidgetStore.SanitizeWidgetId(" a b "));
    }

    [Fact]
    public void ScreenCapture_FailsClosedWhenTheBlitFails()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/ScreenCaptureService.cs"));

        Assert.Contains("if (memoryDc == IntPtr.Zero)", source, StringComparison.Ordinal);
        Assert.Contains("if (!Win32Helper.BitBlt(", source, StringComparison.Ordinal);
        Assert.Contains("Win32Helper.DeleteObject(dib);", source, StringComparison.Ordinal);
    }

    private static string StripCommentLines(string source)
    {
        var builder = new System.Text.StringBuilder(source.Length);
        foreach (string line in source.Split('\n'))
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "src", "DeskBox")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("DeskBox repository root was not found.");
        }

        return File.ReadAllText(Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
