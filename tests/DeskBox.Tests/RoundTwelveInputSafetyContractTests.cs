namespace DeskBox.Tests;

/// <summary>
/// Pins the round-12 input-safety fixes: file names lose their bidi overrides
/// and keep device names distinct before the extension, settings writes re-read
/// the on-disk schema stamp instead of trusting the load-time copy, a backup
/// answered by an unreadable primary keeps the writer closed, an out-of-memory
/// failure no longer quarantines a healthy store, Escape during IME composition
/// no longer cancels the surrounding edit, and clipboard images are refused on
/// their pixel count instead of their encoded size.
/// </summary>
public sealed class RoundTwelveInputSafetyContractTests
{
    [Theory]
    [InlineData("CON.txt", "CON_.txt")]
    [InlineData("con.TXT", "con_.TXT")]
    [InlineData("LPT9.log", "LPT9_.log")]
    [InlineData("NUL", "NUL_")]
    public void SanitizeFileSystemName_KeepsReservedDeviceNamesDistinct(string input, string expected)
    {
        Assert.Equal(expected, DeskBox.Services.FileService.SanitizeFileSystemName(input));
    }

    [Fact]
    public void SanitizeFileSystemName_RemovesBidiOverridesAndFoldsCanonicalSpellings()
    {
        string disguised = DeskBox.Services.FileService.SanitizeFileSystemName("invoice\u202Etxt.exe");
        Assert.DoesNotContain('\u202E', disguised);

        // A decomposed spelling and its precomposed twin must not be able to
        // coexist as two visually identical entries.
        string decomposed = DeskBox.Services.FileService.SanitizeFileSystemName("cafe\u0301");
        Assert.Equal("caf\u00E9", decomposed);
    }

    [Fact]
    public void FileNames_FoldFormatCharactersAndKeepDeviceNamesDistinctBeforeTheExtension()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/FileService.cs"));

        Assert.Contains(
            "sanitized = sanitized.Normalize(NormalizationForm.FormC);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format ? '-' : ch);",
            source,
            StringComparison.Ordinal);

        int reservedCheck = source.IndexOf("if (IsReservedWindowsDeviceName(sanitized))", StringComparison.Ordinal);
        int extensionStart = source.IndexOf("int extensionStart = sanitized.IndexOf('.');", StringComparison.Ordinal);
        int insertAtStem = source.IndexOf(": sanitized.Insert(extensionStart, \"_\");", StringComparison.Ordinal);
        Assert.True(reservedCheck >= 0, "reserved device-name check was not found");
        Assert.True(extensionStart > reservedCheck, "the extension split must live inside the reserved-name branch");
        Assert.True(insertAtStem > extensionStart, "the suffix must be inserted into the stem, not appended");

        // The pre-fix shape appended the suffix to the whole name, which left
        // "CON.txt" still resolving to the CON device.
        Assert.DoesNotContain("sanitized = sanitized + \"_\";", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSave_RereadsTheDiskSchemaStampInsteadOfTrustingTheLoadTimeCopy()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/SettingsService.cs"));

        Assert.Contains(
            "await ReadOnDiskSchemaVersionAsync(_settingsPath));",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "int loadedSchemaVersion = Math.Max(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "int loadedSchemaVersion = Volatile.Read(ref _loadedDiskSchemaVersion);",
            source,
            StringComparison.Ordinal);
        // The settings save hot path must not reach into the sync store; the
        // stamp reader stays local to this service.
        Assert.DoesNotContain("SyncStoreSchemaGate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsLoad_TreatsAnUnreadablePrimaryAsUnwritableEvenWhenABackupAnswered()
    {
        string settingsSource = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/SettingsService.cs"));
        string storeSource = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/ResilientJsonStore.cs"));

        // The backup's stamp describes the backup, so the load result has to
        // carry the "primary was unreadable" fact up to the save gate.
        Assert.Contains(
            "bool PrimaryUnreadable = false);",
            storeSource,
            StringComparison.Ordinal);
        Assert.Contains("PrimaryUnreadable: true);", storeSource, StringComparison.Ordinal);
        Assert.Contains(
            "loadResult.PrimaryUnreadable ||",
            settingsSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptStoreQuarantine_DoesNotTreatOutOfMemoryAsCorruption()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/ResilientJsonStore.cs"));

        Assert.Equal(2, CountOccurrences(source, "when (ex is not OutOfMemoryException)"));
    }

    [Fact]
    public void EscapeHandlers_IgnoreImeCompositionBeforeCancellingTheEdit()
    {
        string fileSurface = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs"));
        string todo = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs"));
        string annotation = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/ScreenshotAnnotationLayer.xaml.cs"));

        AssertEscapeGuardPrecedesCancel(
            fileSurface,
            "if (TextCompositionTracker.IsComposing(ItemRenameTextBox))",
            "CancelItemRename();");
        AssertEscapeGuardPrecedesCancel(
            todo,
            "if (TextCompositionTracker.IsComposing(DetailTitleTextBox))",
            "await CloseDetailAsync();");
        AssertEscapeGuardPrecedesCancel(
            annotation,
            "if (TextCompositionTracker.IsComposing(sender as TextBox))",
            "CommitTextEditor(commit: false);");
    }

    [Fact]
    public void TodoDetailEscape_MarksTheKeyHandledBeforeAwaitingTheClose()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs"));

        int escapeBranch = source.IndexOf("VirtualKey.Escape", StringComparison.Ordinal);
        int handled = source.IndexOf("e.Handled = true;", escapeBranch, StringComparison.Ordinal);
        int awaitedClose = source.IndexOf("await CloseDetailAsync();", escapeBranch, StringComparison.Ordinal);
        Assert.True(handled >= 0 && awaitedClose > handled,
            "Escape must be marked handled before the close awaits, or it keeps bubbling");
    }

    [Fact]
    public void ClipboardImages_AreRefusedOnTheirPixelCount()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/IQuickCaptureClipboardReader.cs"));

        Assert.Contains("private const long MaxDecodedPixels = 40_000_000;", source, StringComparison.Ordinal);
        Assert.Contains(
            "long pixels = (long)decoder.PixelWidth * decoder.PixelHeight;",
            source,
            StringComparison.Ordinal);
        // Both decode paths — the clipboard stream and the StorageFile fallback
        // — must check the budget before GetSoftwareBitmapAsync materializes a
        // frame, not after the encoder produced the bytes.
        Assert.Equal(2, CountOccurrences(source, "if (!IsWithinDecodeBudget(decoder))"));

        int decodeStart = source.IndexOf(
            "private static async Task<byte[]?> TryDecodeStreamAsPngAsync",
            StringComparison.Ordinal);
        int decodeEnd = source.IndexOf(
            "private static async Task<byte[]?> TryReadStorageFileAsPngAsync",
            StringComparison.Ordinal);
        Assert.True(decodeStart >= 0 && decodeEnd > decodeStart, "clipboard decode methods were not found");
        string decodeRegion = source[decodeStart..decodeEnd];
        int budgetCheck = decodeRegion.IndexOf("if (!IsWithinDecodeBudget(decoder))", StringComparison.Ordinal);
        int frameMaterialization = decodeRegion.IndexOf("await decoder.GetSoftwareBitmapAsync()", StringComparison.Ordinal);
        Assert.True(budgetCheck >= 0 && frameMaterialization > budgetCheck,
            "the pixel budget must run before the decoder materializes the frame");
    }

    private static void AssertEscapeGuardPrecedesCancel(
        string source,
        string guard,
        string cancel)
    {
        int escapeBranch = source.IndexOf("VirtualKey.Escape", StringComparison.Ordinal);
        Assert.True(escapeBranch >= 0, "Escape branch was not found");

        int guardIndex = source.IndexOf(guard, escapeBranch, StringComparison.Ordinal);
        int cancelIndex = source.IndexOf(cancel, escapeBranch, StringComparison.Ordinal);
        Assert.True(guardIndex > escapeBranch, $"composition guard '{guard}' is missing from the Escape branch");
        Assert.True(cancelIndex > guardIndex, $"'{cancel}' must run after the composition guard");
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
