using System.IO.Compression;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class ArchiveZipEntrySafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "deskbox-zip-entry-safety-" + Guid.NewGuid().ToString("N"));

    public ArchiveZipEntrySafetyTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("note.txt:payload")]
    [InlineData("nested/note.txt:payload")]
    [InlineData("NUL.txt")]
    [InlineData("CON")]
    [InlineData("aux.log")]
    [InlineData("PRN.txt")]
    [InlineData("COM1.txt")]
    [InlineData("lpt9.log")]
    [InlineData("COM\u00b9.txt")]
    [InlineData("LPT\u00b2.txt")]
    [InlineData("COM\u00b3.txt")]
    [InlineData("folder/NUL/file.txt")]
    [InlineData("folder./file.txt")]
    [InlineData("folder /file.txt")]
    [InlineData("file.txt.")]
    [InlineData("file.txt ")]
    [InlineData("../escaped.txt")]
    [InlineData("..\\escaped.txt")]
    public async Task Extract_RejectsUnsafeWindowsEntryBeforeWritingIt(string entryName)
    {
        string archivePath = CreateArchive(entryName);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ArchiveZipService.ExtractAllAsync(archivePath));

        string destination = Path.Combine(_root, "entries");
        if (Directory.Exists(destination))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(
                destination, "*", SearchOption.AllDirectories));
        }
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Theory]
    [InlineData("./nested/./valid.txt")]
    [InlineData("nested\\valid.txt")]
    [InlineData("COM0.txt")]
    [InlineData("COM10.txt")]
    [InlineData("NUL-safe.txt")]
    [InlineData(".hidden.txt")]
    public async Task Extract_PreservesLegalEntryNames(string entryName)
    {
        string archivePath = CreateArchive(entryName);

        string destination = await ArchiveZipService.ExtractAllAsync(archivePath);

        string extractedPath = Path.GetFullPath(Path.Combine(destination,
            entryName.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal("payload", await File.ReadAllTextAsync(extractedPath));
    }

    private string CreateArchive(string entryName)
    {
        string archivePath = Path.Combine(_root, "entries.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
        writer.Write("payload");
        return archivePath;
    }
}
