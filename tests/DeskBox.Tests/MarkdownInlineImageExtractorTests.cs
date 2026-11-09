using System.Diagnostics;
using DeskBox.Services;

namespace DeskBox.Tests;

public class MarkdownInlineImageExtractorTests
{
    [Fact]
    public async Task ExtractAsync_HostileWhitespacePayload_CompletesQuickly()
    {
        // 100k base64-ish characters with interleaved whitespace and no
        // terminator: the pre-atomic-group regex backtracked quadratically
        // over runs like this. It is linear now; the ceiling stays generous
        // for CI jitter.
        string hostile =
            "![a](data:image/png;base64," +
            new string('A', 100_000).Replace("AAAA", "AA A", StringComparison.Ordinal) +
            " trailing text with no closing paren";
        var stopwatch = Stopwatch.StartNew();
        MarkdownInlineImageExtractionResult result =
            await MarkdownInlineImageExtractor.ExtractAsync(
                hostile,
                managedDirectory: Path.Combine(Path.GetTempPath(), "DeskBoxTests", "md-inline-unused"));
        stopwatch.Stop();

        Assert.Equal(hostile, result.Body);
        Assert.Empty(result.Attachments);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"inline-image scan took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task ExtractAsync_PayloadWithInternalWhitespace_IsDecoded()
    {
        byte[] image = new byte[64];
        Random.Shared.NextBytes(image);
        string base64 = Convert.ToBase64String(image);
        string withBreaks = string.Join(
            " \r\n",
            base64.Chunk(16).Select(chunk => new string(chunk)));
        string markdown =
            $"before ![diagram](data:image/png;base64,{withBreaks}) after";
        string managedDirectory = Path.Combine(
            Path.GetTempPath(),
            "DeskBoxTests",
            "md-inline",
            Guid.NewGuid().ToString("N"));

        try
        {
            MarkdownInlineImageExtractionResult result =
                await MarkdownInlineImageExtractor.ExtractAsync(markdown, managedDirectory);
            Assert.Single(result.Attachments);
            Assert.DoesNotContain("base64", result.Body, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(managedDirectory))
            {
                Directory.Delete(managedDirectory, recursive: true);
            }
        }
    }
}
