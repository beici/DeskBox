using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class FileServiceNetworkVolumeGuardTests
{
    [Fact]
    public void IsNetworkVolumePath_IdentifiesUncShares()
    {
        Assert.True(FileService.IsNetworkVolumePath(
            @"\\server\share\folder\file.txt"));
    }

    [Fact]
    public void IsNetworkVolumePath_IdentifiesMappedNetworkDrives()
    {
        // Drive letters whose drive type is Network must be detected through
        // DriveInfo — a mapped Z: is exactly the leak the audit found, since
        // it can be NTFS and pass the migration layer's file-system check.
        string? networkRoot = Directory.GetLogicalDrives()
            .FirstOrDefault(drive =>
            {
                try
                {
                    return new DriveInfo(drive).DriveType == DriveType.Network;
                }
                catch
                {
                    return false;
                }
            });

        if (networkRoot is null)
        {
            // No mapped network drive on this host: the UNC case above and
            // the local negative case below still pin the classification.
            return;
        }

        Assert.True(FileService.IsNetworkVolumePath(
            Path.Combine(networkRoot, "folder", "file.txt")));
    }

    [Fact]
    public void IsNetworkVolumePath_LocalVolumesAreNotNetwork()
    {
        string localPath = Path.Combine(Path.GetTempPath(), "file.txt");

        Assert.False(FileService.IsNetworkVolumePath(localPath));
    }

    [Fact]
    public void CanRecycleHeadless_RefusesAVolumeWhoseBinQueryFails()
    {
        string localPath = Path.Combine(Path.GetTempPath(), "file.txt");

        // A fixed local volume is not enough on its own: when the bin cannot
        // take the item, FOF_ALLOWUNDO silently deletes permanently instead.
        Assert.False(FileService.CanRecycleHeadless(localPath, _ => false));
    }

    [Fact]
    public void CanRecycleHeadless_RefusesUncPathsEvenWhenTheProbeSucceeds()
    {
        Assert.False(FileService.CanRecycleHeadless(
            @"\\server\share\folder\file.txt",
            _ => true));
    }

    [Fact]
    public void CanRecycleHeadless_AllowsALocalFixedVolumeWithARecycleBin()
    {
        string localPath = Path.Combine(Path.GetTempPath(), "file.txt");
        string? root = Path.GetPathRoot(Path.GetFullPath(localPath));
        if (root is null || new DriveInfo(root).DriveType != DriveType.Fixed)
        {
            return;
        }

        Assert.True(FileService.CanRecycleHeadless(localPath, _ => true));
    }

    [Fact]
    public async Task DeleteEntryAsync_RefusesHeadlessRecycleWhenTheVolumeCannotRecycle()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "deskbox-recycle-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "payload.txt");
        await File.WriteAllTextAsync(file, "payload");

        try
        {
            // The probe is the only variable here: a real fixed-volume file is
            // requested for a silent (zero-handle) recycle, so a regression that
            // drops the guard would move it into the bin and report success.
            var service = new FileService { HeadlessRecycleProbe = _ => false };

            bool deleted = await service.DeleteEntryAsync(file);

            Assert.False(deleted);
            Assert.True(File.Exists(file), "the guard must leave the file in place");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
