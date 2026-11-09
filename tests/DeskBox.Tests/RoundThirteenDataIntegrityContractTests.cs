namespace DeskBox.Tests;

/// <summary>
/// Pins the round-13 data-integrity fixes: virtual file drops materialize
/// through a scratch name so a failed or killed write cannot truncate the real
/// target, image-source failure pins expire so re-picking the same unchanged
/// image retries, title-icon assets sweep stale extensions only after the new
/// file is in place, and a hole in the settings migration chain is logged
/// instead of stalling silently.
/// </summary>
public sealed class RoundThirteenDataIntegrityContractTests
{
    [Fact]
    public void VirtualDrops_MaterializeThroughAScratchFile()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Helpers/NativeDropTarget.cs"));

        Assert.Contains(
            "string temporaryPath = destinationPath + \".\" + Guid.NewGuid().ToString(\"N\") + \".deskbox-partial\";",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "string temporaryPath = destinationPath + \".deskbox-partial\";",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "File.Move(temporaryPath, destinationPath, overwrite: true);",
            source,
            StringComparison.Ordinal);

        // Both payload shapes must write the scratch name, never the target.
        Assert.Contains("SaveComStream(actualMedium, temporaryPath, ref remainingBudgetBytes);", source, StringComparison.Ordinal);
        Assert.Contains("SaveGlobalMemory(actualMedium, temporaryPath, ref remainingBudgetBytes);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveComStream(actualMedium, destinationPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveGlobalMemory(actualMedium, destinationPath", source, StringComparison.Ordinal);

        // A failed copy must clean the scratch file, not the user's target: the
        // old code deleted destinationPath, which destroyed a pre-existing file
        // whose replacement had not been written yet.
        Assert.DoesNotContain("File.Delete(destinationPath)", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "try { File.Delete(temporaryPath); } catch { }"));

        // The stream writer now truncates its scratch name: leaving CreateNew
        // meant a scratch file abandoned by a killed process made every later
        // drop of the same name fail with "already exists".
        int streamWriter = source.IndexOf("private static void SaveComStream(", StringComparison.Ordinal);
        Assert.True(streamWriter >= 0, "SaveComStream was not found");
        string streamWriterBody = source[streamWriter..];
        Assert.Contains("FileMode.Create,", streamWriterBody, StringComparison.Ordinal);
        Assert.DoesNotContain("FileMode.CreateNew", streamWriterBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageCacheFailurePins_ExpireSoARepickRetries()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/WidgetSharedImageSourceCache.cs"));

        Assert.Contains(
            "private static readonly TimeSpan FailurePinLifetime = TimeSpan.FromSeconds(5);",
            source,
            StringComparison.Ordinal);

        // The pin is only honoured inside the burst window, and the stamp now
        // carries when it was recorded so the window can be evaluated.
        Assert.Contains(
            "DateTime.UtcNow - failed.RecordedUtc < FailurePinLifetime",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "out (long Length, DateTime LastWriteUtc, DateTime RecordedUtc) failed)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "s_failedStamps[filePath] = (entry.Length, entry.LastWriteUtc, DateTime.UtcNow);",
            source,
            StringComparison.Ordinal);

        // The test seam must clear both tables, otherwise a failure pin leaks
        // from one test into the next.
        int reset = source.IndexOf("internal static void ResetForTests()", StringComparison.Ordinal);
        Assert.True(reset >= 0, "ResetForTests was not found");
        string resetBody = source[reset..];
        Assert.Contains("s_entries.Clear();", resetBody, StringComparison.Ordinal);
        Assert.Contains("s_failedStamps.Clear();", resetBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TitleIconAssets_SweepStaleExtensionsOnlyAfterTheNewFileIsInPlace()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/WidgetTitleIconAssetStore.cs"));

        int swap = source.IndexOf("File.Move(tempPath, targetPath, overwrite: true);", StringComparison.Ordinal);
        int sweep = source.IndexOf("foreach (string stale in Directory.EnumerateFiles(directory, fileStem + \".*\"))", StringComparison.Ordinal);
        int copied = source.IndexOf("return WidgetTitleIconAssetResult.Copied;", StringComparison.Ordinal);

        Assert.True(swap >= 0, "the atomic swap was not found");
        Assert.True(sweep >= 0, "the stale-extension sweep was not found");
        Assert.True(copied >= 0, "the success return was not found");
        Assert.True(
            swap < sweep,
            "the stale sweep must run after the new file is swapped in, otherwise a failed copy deletes the asset the settings still point at");
        Assert.True(
            sweep < copied,
            "the sweep must still run before reporting success");
    }

    [Fact]
    public void MigrationChain_LogsAGapInsteadOfStallingSilently()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/SettingsMigrationService.cs"));

        Assert.Contains(
            "if (!anyApplied && version < CurrentSchemaVersion)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "No migration starts at schema version",
            source,
            StringComparison.Ordinal);

        // The diagnostic must sit after the loop and before the checkpoint is
        // recorded, so it reports the version the chain actually stopped at.
        int gapGuard = source.IndexOf("if (!anyApplied && version < CurrentSchemaVersion)", StringComparison.Ordinal);
        int checkpoint = source.IndexOf("working.SchemaVersion = version;", StringComparison.Ordinal);
        Assert.True(gapGuard >= 0, "the gap diagnostic was not found");
        Assert.True(checkpoint > gapGuard, "the checkpoint must be recorded after the diagnostic");
    }

    private static int CountOccurrences(string source, string needle)
    {
        int count = 0;
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = source.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
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
