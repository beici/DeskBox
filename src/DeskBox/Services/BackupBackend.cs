using DeskBox.Contracts;

namespace DeskBox.Services;

internal sealed class BackupBackend(
    SettingsService settings,
    DeskBoxDataBackupService local,
    CloudBackupService cloud) : IBackupBackend
{
    public bool LocalScheduleEnabled => local.AutomaticBackupOptions.IsEnabled;
    public bool CloudScheduleEnabled => cloud.Options.IsConfigured;

    public bool RefreshOptions()
    {
        // Record value equality covers every effective field. LastSuccessUtc
        // is excluded on the cloud side: the service updates it in memory on
        // every successful upload, and a failed settings persist would then
        // make every refresh look "changed" forever.
        AutomaticBackupOptions previousLocal = local.AutomaticBackupOptions;
        CloudBackupOptions previousCloud = cloud.Options;
        local.UpdateAutomaticBackupOptions(DataBackupSettingsPolicy.GetOptions(settings.Settings));
        cloud.UpdateOptions(CloudBackupSettingsPolicy.GetOptions(settings.Settings));
        bool localChanged = previousLocal != local.AutomaticBackupOptions;
        bool cloudChanged =
            previousCloud with { LastSuccessUtc = DateTimeOffset.MinValue } !=
            cloud.Options with { LastSuccessUtc = DateTimeOffset.MinValue };
        return localChanged || cloudChanged;
    }

    public async Task PrepareManualBackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await settings.FlushPendingSaveAsync(notifySubscribers: false))
            throw new IOException("Settings could not be saved before creating a backup.");
        cancellationToken.ThrowIfCancellationRequested();
        RefreshOptions();
    }

    // The local snapshot chain (recursive enumeration, staging copies, JSON
    // validation, zip + SHA256) and the cloud upload chain contain no
    // ConfigureAwait(false); started from the UI thread they pinned every
    // continuation to the dispatcher and froze it for seconds on large data
    // directories. Offloading in this production adapter keeps BackupRuntime's
    // scheduling semantics intact (the lane is still entered synchronously) and
    // leaves test backends untouched.
    public Task<LocalBackupResult> CreateSnapshotAsync(bool force, CancellationToken cancellationToken) =>
        Task.Run(() => local.CreateAutomaticSnapshotResultAsync(force, cancellationToken), cancellationToken);

    public Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken) =>
        Task.Run(() => local.ExportBackupAsync(destinationDirectory, cancellationToken), cancellationToken);

    public Task<CloudBackupRunResult> UploadAsync(bool scheduled, CancellationToken cancellationToken) =>
        Task.Run(
            () => scheduled
                ? cloud.RunScheduledIfDueAsync(cancellationToken)
                : cloud.RunBackupNowAsync(cancellationToken),
            cancellationToken);
}
