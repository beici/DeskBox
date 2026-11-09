using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DeskBox.Services;
using Xunit;

namespace DeskBox.Tests;

/// <summary>
/// Pins the round-18 storage fixes that cannot be reached from a public entry
/// point: the full-restore rollback lifetime (R16-M2-03), the raw schema probe
/// (R18-Q2-03) and the todo store's byte serialization (R17-P3-03). The byte
/// overload of <see cref="ResilientJsonStore"/> is covered behaviorally because
/// the todo store is its first production caller (R18-Q3-02).
/// </summary>
public sealed class RoundEighteenStorageContractTests : IDisposable
{
    private readonly string _tempRoot = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N")))
        .FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void FullRestore_KeepsTheRollbackCopyOnlyWhileItHoldsTheOldData()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/DeskBoxDataBackupService.cs"));

        int declaration = source.IndexOf("bool swapCompleted = false;", StringComparison.Ordinal);
        Assert.True(declaration >= 0, "the swap-completed flag is missing");

        int finallyIndex = source.IndexOf("finally", declaration, StringComparison.Ordinal);
        Assert.True(finallyIndex > declaration, "the full-restore finally block is missing");

        int release = source.IndexOf("_gate.Release();", finallyIndex, StringComparison.Ordinal);
        Assert.True(release > finallyIndex, "the full-restore finally block is unterminated");

        Assert.Contains(
            "swapCompleted = true;",
            source[declaration..finallyIndex],
            StringComparison.Ordinal);

        string cleanup = source[finallyIndex..release];
        Assert.Contains("swapCompleted", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Directory.Exists(DataDirectory)",
            cleanup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RawSchemaProbe_ReadsABoundedWindowBeforeMaterializingTheDocument()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/SettingsService.cs"));

        int start = source.IndexOf(
            "private static int? TryReadRawSchemaVersion(string path)",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "the raw schema probe is missing");

        int end = source.IndexOf(
            "private static int? ReadSchemaVersionToken(byte[] utf8Json)",
            start,
            StringComparison.Ordinal);
        Assert.True(end > start, "the schema-token reader is missing");

        string probe = source[start..end];
        Assert.Contains("RawSchemaVersionProbeBytes", probe, StringComparison.Ordinal);
        Assert.Contains("FileOptions.SequentialScan", probe, StringComparison.Ordinal);

        int windowRead = probe.IndexOf("ReadSchemaVersionToken(probe)", StringComparison.Ordinal);
        int wholeFileRead = probe.IndexOf("File.ReadAllBytes(path)", StringComparison.Ordinal);
        Assert.True(windowRead >= 0, "the probe window must be parsed first");
        Assert.True(
            wholeFileRead > windowRead,
            "the whole-document fallback must only run after the bounded probe");
    }

    [Fact]
    public void RawSchemaProbe_FallsBackToTheWholeDocumentWhenTheWindowTruncates()
    {
        string path = Path.Combine(_tempRoot, "settings.json");
        string padding = new string('x', 80 * 1024);
        File.WriteAllText(path, $"{{\"padding\":\"{padding}\",\"schemaVersion\":12}}");

        MethodInfo? probe = typeof(SettingsService).GetMethod(
            "TryReadRawSchemaVersion",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(probe);

        object? version = probe!.Invoke(null, [path]);

        // The bounded window cuts the padding string in half, so the fast path
        // must report "not found here" rather than letting the truncation throw
        // and collapse the answer to null.
        Assert.Equal(12, Assert.IsType<int>(version));
    }

    [Fact]
    public void TodoStore_SerializesStraightToUtf8Bytes()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/TodoWidgetStore.cs"));

        int start = source.IndexOf("private async Task SaveUnsafeAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "SaveUnsafeAsync is missing");

        int end = source.IndexOf("private static TodoWidgetData Normalize(", start, StringComparison.Ordinal);
        Assert.True(end > start, "SaveUnsafeAsync is unterminated");

        string body = source[start..end];
        Assert.Contains("SerializeToUtf8Bytes", body, StringComparison.Ordinal);
        Assert.Contains("ResilientJsonStore.SaveAsync(_storePath, utf8Json)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Serialize(", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ByteOverload_RoundTripsTheExactPayload()
    {
        string storePath = Path.Combine(_tempRoot, "store.json");
        byte[] payload = Encoding.UTF8.GetBytes("{\"value\":\"\u66f4\u65b0 \u2603\"}");

        await ResilientJsonStore.SaveAsync(storePath, payload);

        Assert.Equal(payload, await File.ReadAllBytesAsync(storePath));
        Assert.Empty(Directory.EnumerateFiles(_tempRoot, "*.tmp"));

        byte[] second = Encoding.UTF8.GetBytes("{\"value\":\"\u7b2c\u4e8c\u6b21\"}");
        await ResilientJsonStore.SaveAsync(storePath, second);

        Assert.Equal(second, await File.ReadAllBytesAsync(storePath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(ResilientJsonStore.GetBackupPath(storePath)));
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
