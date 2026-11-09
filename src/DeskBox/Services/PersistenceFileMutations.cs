namespace DeskBox.Core.Persistence;

/// <summary>
/// Mechanism wrappers for the file mutations that the persistence layer needs.
/// </summary>
/// <remarks>
/// The module boundary contract (<c>ModuleBoundaryContractTests</c>) allows
/// <c>File</c>/<c>Directory</c> mutations only inside the domains that own them:
/// <c>DeskBox.FileSafety</c>, <c>DeskBox.Core.Persistence</c> (local persistence
/// machinery) and <c>DeskBox.Platform</c>. Stores that have to move a file or a
/// directory out of the way route through these wrappers instead of touching
/// <c>File</c>/<c>Directory</c> directly, so the ratchet keeps tracking one
/// place instead of every service.
/// </remarks>
internal static class PersistenceFileMutations
{
    /// <summary>Moves a directory, keeping <see cref="Directory.Move"/>'s exception semantics.</summary>
    public static void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName) =>
        Directory.Move(sourceDirectoryName, destinationDirectoryName);

    /// <summary>Moves a file, keeping <see cref="File.Move(string, string)"/>'s exception semantics.</summary>
    public static void MoveFile(string sourceFileName, string destinationFileName) =>
        File.Move(sourceFileName, destinationFileName);
}
