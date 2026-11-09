using System.Text;

namespace DeskBox.Services;

public sealed partial class FileService
{
    private const string CaseOnlyRenameTemporaryPrefix =
        ".deskbox-case-rename-";

    internal static bool IsCaseOnlyPathChange(
        string sourcePath,
        string destinationPath) =>
        !string.Equals(sourcePath, destinationPath, StringComparison.Ordinal) &&
        string.Equals(
            sourcePath,
            destinationPath,
            StringComparison.OrdinalIgnoreCase);

    private static async Task MoveCaseOnlyEntryAsync(
        string sourcePath,
        string destinationPath)
    {
        bool sourceIsFile = File.Exists(sourcePath);
        bool sourceIsDirectory = Directory.Exists(sourcePath);
        if (!sourceIsFile && !sourceIsDirectory)
        {
            throw new FileNotFoundException(
                "The file or folder to rename no longer exists.",
                sourcePath);
        }

        ThrowIfExactDestinationEntryExists(
            sourcePath,
            destinationPath);

        string temporaryPath = CreateCaseOnlyRenameTemporaryPath(
            sourcePath);
        string? journalPath = TryWriteCaseOnlyRenameJournal(
            temporaryPath,
            sourcePath,
            destinationPath);
        await MoveEntryAtomicallyAsync(sourcePath, temporaryPath);

        try
        {
            await MoveEntryAtomicallyAsync(temporaryPath, destinationPath);
            DeleteCaseOnlyRenameJournal(journalPath);
        }
        catch (Exception renameException)
        {
            try
            {
                bool rolledBack = false;
                if ((File.Exists(temporaryPath) ||
                     Directory.Exists(temporaryPath)) &&
                    !File.Exists(sourcePath) &&
                    !Directory.Exists(sourcePath))
                {
                    await MoveEntryAtomicallyAsync(temporaryPath, sourcePath);
                    rolledBack = true;
                }
                // Deleting the journal while the entry still sits under the
                // temporary name strands it permanently — startup recovery is
                // the only owner of that state (its dest-and-source case
                // fails closed), so the journal must survive to reach it.
                if (rolledBack)
                {
                    DeleteCaseOnlyRenameJournal(journalPath);
                }
            }
            catch (Exception rollbackException)
            {
                throw new IOException(
                    $"The case-only rename failed and DeskBox could not restore " +
                    $"'{sourcePath}' from its temporary name.",
                    new AggregateException(
                        renameException,
                        rollbackException));
            }

            throw;
        }
    }

    private static async Task MoveEntryAtomicallyAsync(
        string sourcePath,
        string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            await Task.Run(() => File.Move(sourcePath, destinationPath));
            return;
        }

        if (Directory.Exists(sourcePath))
        {
            await Task.Run(() => Directory.Move(sourcePath, destinationPath));
            return;
        }

        throw new FileNotFoundException(
            "The file or folder to rename no longer exists.",
            sourcePath);
    }

    private static void ThrowIfExactDestinationEntryExists(
        string sourcePath,
        string destinationPath)
    {
        string? parentDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(parentDirectory) ||
            !Directory.Exists(parentDirectory))
        {
            return;
        }

        string destinationName = Path.GetFileName(destinationPath);
        foreach (string entryPath in
                 Directory.EnumerateFileSystemEntries(parentDirectory))
        {
            if (string.Equals(entryPath, sourcePath, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(
                    Path.GetFileName(entryPath),
                    destinationName,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    $"The destination '{destinationPath}' already exists.");
            }
        }
    }

    private static string CreateCaseOnlyRenameTemporaryPath(
        string sourcePath)
    {
        string? parentDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new IOException(
                $"Unable to determine the parent folder for '{sourcePath}'.");
        }

        for (int attempt = 0; attempt < 32; attempt++)
        {
            string candidate = Path.Combine(
                parentDirectory,
                $"{CaseOnlyRenameTemporaryPrefix}{Guid.NewGuid():N}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException(
            $"Unable to allocate a temporary name beside '{sourcePath}'.");
    }

    // R8 B11: a crash between the two moves of a case-only rename strands
    // the user's file under the temporary name. A small journal beside the
    // app data lets the next startup finish (or roll back) the rename.
    // Never sweep by prefix — the entry under the temporary name IS the
    // user's real file.
    private static string CaseOnlyRenameJournalDirectory => Path.Combine(
        DeskBoxDataPathService.Current.DataDirectory,
        "case-rename-journal");

    private static string? TryWriteCaseOnlyRenameJournal(
        string temporaryPath,
        string sourcePath,
        string destinationPath)
    {
        try
        {
            string directory = CaseOnlyRenameJournalDirectory;
            Directory.CreateDirectory(directory);
            string journalPath = Path.Combine(
                directory,
                $"{Guid.NewGuid():N}.journal");
            // WriteAllText leaves the bytes in the OS cache: on power loss a
            // journal that already "returned" can surface empty or torn, and
            // recovery would then discard it as invalid. The flush commits
            // the journal to disk before the first rename starts.
            byte[] payload = Encoding.UTF8.GetBytes(
                $"{temporaryPath}\n{sourcePath}\n{destinationPath}\n");
            using var stream = new FileStream(
                journalPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            stream.Write(payload, 0, payload.Length);
            stream.Flush(flushToDisk: true);
            return journalPath;
        }
        catch (Exception ex)
        {
            App.Log(
                "[FileService] Could not write the case-only rename journal: " +
                ex.Message);
            return null;
        }
    }

    private static void DeleteCaseOnlyRenameJournal(string? journalPath)
    {
        if (journalPath is null)
        {
            return;
        }

        try
        {
            File.Delete(journalPath);
        }
        catch (Exception ex)
        {
            App.Log(
                "[FileService] Could not delete the case-only rename journal: " +
                ex.Message);
        }
    }

    internal static void RecoverPendingCaseOnlyRenames(DateTime? processStartTimeUtc = null)
    {
        string directory = CaseOnlyRenameJournalDirectory;
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string journalPath in Directory.GetFiles(
                     directory,
                     "*.journal"))
        {
            try
            {
                // A journal younger than this process may belong to a rename
                // in flight right now; racing its second move would report a
                // failure for a rename that actually succeeds. Young
                // journals are left for a later launch.
                if (processStartTimeUtc.HasValue &&
                    File.GetLastWriteTimeUtc(journalPath) > processStartTimeUtc.Value)
                {
                    App.Log(
                        "[FileService] Skipping a case-only rename journal " +
                        $"that may still be in flight: '{journalPath}'");
                    continue;
                }

                string[] lines = File.ReadAllLines(journalPath);
                if (lines.Length != 3 ||
                    lines.Any(string.IsNullOrWhiteSpace))
                {
                    File.Delete(journalPath);
                    continue;
                }

                string temporaryPath = lines[0];
                string sourcePath = lines[1];
                string destinationPath = lines[2];
                bool temporaryExists = File.Exists(temporaryPath) ||
                    Directory.Exists(temporaryPath);
                if (!temporaryExists)
                {
                    // The rename completed (or the user already resolved the
                    // stranding by hand); the journal is stale.
                    File.Delete(journalPath);
                    continue;
                }

                bool destinationExists = File.Exists(destinationPath) ||
                    Directory.Exists(destinationPath);
                bool sourceExists = File.Exists(sourcePath) ||
                    Directory.Exists(sourcePath);
                if (!destinationExists)
                {
                    MoveEntryAtomicallySync(temporaryPath, destinationPath);
                }
                else if (!sourceExists)
                {
                    MoveEntryAtomicallySync(temporaryPath, sourcePath);
                }
                else
                {
                    App.Log(
                        "[FileService] A stranded case-only rename could not " +
                        $"be recovered automatically; leaving '{temporaryPath}' " +
                        "in place for the user.");
                    continue;
                }

                File.Delete(journalPath);
            }
            catch (Exception ex)
            {
                App.Log(
                    "[FileService] Case-only rename recovery failed for " +
                    $"'{journalPath}': {ex.Message}");
            }
        }
    }

    private static void MoveEntryAtomicallySync(
        string sourcePath,
        string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        Directory.Move(sourcePath, destinationPath);
    }
}
