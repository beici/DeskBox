using DeskBox.Models;

using DeskBox.Contracts;
using System.Globalization;

namespace DeskBox.Services;

/// <summary>
/// Orchestrates cloud backups (roadmap §10): builds a domain-scoped archive
/// via <see cref="DeskBoxDataBackupService.ExportScopedBackupAsync"/>,
/// resolves the provider secret from <see cref="ICredentialStore"/>, moves
/// the zip through <see cref="ICloudBackupTransport"/>, then applies remote
/// retention. Scheduled runs ride the existing 1-minute backup timer; the
/// service itself is transport-agnostic so the official cloud later plugs
/// in as another <see cref="ICloudBackupTransport"/> implementation.
/// </summary>
internal sealed class CloudBackupService
{
    internal const string SnapshotFilePrefix = "DeskBox-CloudBackup-";
    internal const string SnapshotFileExtension = ".zip";

    // R20-V5-03: snapshot names written while the device identity is missing
    // carry this placeholder. It is shared by every install in that state, so
    // ownership checks must not treat it as ours.
    private const string LegacyDeviceSuffixPlaceholder = "nodevice";

    private readonly DeskBoxDataBackupService _backupService;
    private readonly SettingsService _settingsService;
    private readonly ICredentialStore _credentialStore;
    private readonly Func<CloudBackupOptions, string?, ICloudBackupTransport> _transportFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Destination edits invalidate LastSuccessUtc on each debounced save,
    // so scheduled attempts need their own spacing floor to keep typing
    // the remote path from amplifying into an upload-per-keystroke loop.
    private static readonly TimeSpan MinimumScheduledAttemptSpacing =
        TimeSpan.FromMinutes(10);

    // Backoff between listing-verification attempts (see VerifyUploadAsync):
    // eventually-consistent DAV backends can lag seconds behind an accepted
    // PUT. Instance-level rather than static purely so tests can shrink the
    // ~7-second production budget.
    internal int[] VerificationRetryDelayMs { get; set; } = [800, 2000, 4000];

    private CloudBackupOptions _options = CloudBackupSettingsPolicy.GetOptions(new AppSettings());
    private bool _optionsInitialized;
    private DateTimeOffset _lastScheduledAttemptUtc = DateTimeOffset.MinValue;
    private string? _loggedMissingCredentialKey;
    // Same dedup intent as the credential key above: UI-thread-only
    // producers, so a plain field is sufficient — worst case is one
    // extra log line, never wrong state.
    private bool _loggedPendingRestore;
    private string? _loggedScheduledFailureKey;

    /// <summary>
    /// Raised once per finished upload attempt — manual or scheduled,
    /// success or failure. The open settings page uses it to refresh its
    /// status row and snapshot list; the app uses it to toast the first
    /// failure of a scheduled-failure streak.
    /// </summary>
    internal event Action<CloudBackupRunCompletedInfo>? BackupRunCompleted;

    internal CloudBackupService(
        DeskBoxDataBackupService backupService,
        SettingsService settingsService,
        ICredentialStore credentialStore,
        Func<CloudBackupOptions, string?, ICloudBackupTransport>? transportFactory = null)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _transportFactory = transportFactory ?? CreateTransport;
    }

    internal CloudBackupOptions Options => _options;

    internal void UpdateOptions(CloudBackupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A success recorded against a different destination must not
        // suppress the first backup to the NEW destination: switching
        // provider/endpoint/folder/account/scope invalidates LastSuccess.
        // The comparison runs regardless of IsConfigured — a configured →
        // unconfigured → configured detour must not smuggle the old
        // success through either. Only a real destination switch counts:
        // the first options push after startup keeps the persisted value,
        // and an already-empty timestamp needs no invalidation write.
        bool destinationChanged =
            _optionsInitialized && DestinationIdentityChanged(_options, options);
        if (destinationChanged &&
            options.LastSuccessUtc != DateTimeOffset.MinValue)
        {
            options = options with { LastSuccessUtc = DateTimeOffset.MinValue };
            _settingsService.Settings.CloudBackup.CloudBackupLastSuccessUtcTicks = 0;
            _settingsService.SaveDebounced();
            App.Log("[CloudBackup] Backup destination changed; last-success invalidated.");
        }

        // Failure and unverified ticks are destination-scoped too: a run
        // recorded against the old endpoint must not make the new one look
        // broken or unconfirmed before its first run.
        if (destinationChanged &&
            (_settingsService.Settings.CloudBackup.CloudBackupLastFailureUtcTicks != 0 ||
             _settingsService.Settings.CloudBackup.CloudBackupLastUnverifiedUtcTicks != 0))
        {
            _settingsService.Settings.CloudBackup.CloudBackupLastFailureUtcTicks = 0;
            _settingsService.Settings.CloudBackup.CloudBackupLastUnverifiedUtcTicks = 0;
            _settingsService.SaveDebounced();
        }

        _options = options;
        _optionsInitialized = true;
    }

    /// <summary>Fields defining which backup destination a success belongs to.</summary>
    private static bool DestinationIdentityChanged(CloudBackupOptions previous, CloudBackupOptions next) =>
        !string.Equals(previous.Provider, next.Provider, StringComparison.Ordinal) ||
        !string.Equals(previous.ServerUrl, next.ServerUrl, StringComparison.Ordinal) ||
        !string.Equals(previous.RemotePath, next.RemotePath, StringComparison.Ordinal) ||
        !string.Equals(previous.Username, next.Username, StringComparison.Ordinal) ||
        previous.Scope != next.Scope;

    /// <summary>
    /// Timer-tick entry point: uploads only when configured and the interval
    /// has elapsed. Never throws — a transient network failure must not take
    /// down the timer path.
    /// </summary>
    internal async Task<CloudBackupRunResult> RunScheduledIfDueAsync(CancellationToken cancellationToken = default)
    {
        // Skip rather than queue: a manual run or a still-uploading
        // scheduled run must not pile up a second upload behind it —
        // especially one holding options the user already changed.
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return CloudBackupRunResult.InProgress;
        }

        // Read before the gate: WaitAsync(0) never suspends, and the catch
        // path needs the attempted options to decide whether the failure
        // stamp still applies to the current destination.
        CloudBackupOptions options = _options;
        try
        {
            if (!options.IsConfigured)
            {
                return CloudBackupRunResult.NotConfigured;
            }

            // A pending restore replaces the staged domains on the next
            // restart — uploading the about-to-be-replaced state would push
            // a stale snapshot and could race the staged restore files.
            if (File.Exists(_backupService.PendingRestoreMarkerPath))
            {
                // The marker lives from Prepare* until restart/cancel —
                // logging every tick and every settings save spams the log.
                if (!_loggedPendingRestore)
                {
                    _loggedPendingRestore = true;
                    App.Log("[CloudBackup] Scheduled upload skipped: a restore is pending.");
                }

                return CloudBackupRunResult.PendingRestore;
            }

            _loggedPendingRestore = false;

            // Configured but never saved (or lost) a credential would send
            // every scheduled run into an anonymous 401 — skip quietly. This
            // path also runs on every settings save, so the skip is logged
            // once per endpoint rather than once per keystroke.
            string credentialKey = CloudBackupSettingsPolicy.CredentialKey(options);
            if (await _credentialStore.GetSecretAsync(credentialKey, cancellationToken) is null)
            {
                if (!string.Equals(_loggedMissingCredentialKey, credentialKey, StringComparison.Ordinal))
                {
                    _loggedMissingCredentialKey = credentialKey;
                    App.Log("[CloudBackup] Scheduled upload skipped: no credential for the configured endpoint.");
                }

                return CloudBackupRunResult.MissingCredential;
            }

            _loggedMissingCredentialKey = null;

            if (DateTimeOffset.UtcNow - options.LastSuccessUtc < TimeSpan.FromMinutes(options.IntervalMinutes))
            {
                return CloudBackupRunResult.NotDue;
            }

            // Editing the remote path invalidates LastSuccessUtc on every
            // debounced save — without an attempt-level floor, typing the
            // path would fire a full upload per keystroke.
            if (DateTimeOffset.UtcNow - _lastScheduledAttemptUtc <
                MinimumScheduledAttemptSpacing)
            {
                return CloudBackupRunResult.NotDue;
            }

            _lastScheduledAttemptUtc = DateTimeOffset.UtcNow;
            return await RunBackupCoreAsync(options, cancellationToken, scheduled: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Dedup by signature so a persistently broken endpoint logs
            // once instead of once per tick; a different failure re-logs.
            string failureKey = $"{ex.GetType().Name}:{ex.Message}";
            if (!string.Equals(_loggedScheduledFailureKey, failureKey, StringComparison.Ordinal))
            {
                _loggedScheduledFailureKey = failureKey;
                App.Log($"[CloudBackup] Scheduled upload failed: {ex}");
            }

            bool streakStart = await RecordFailureAsync(options);
            NotifyRunCompleted(new CloudBackupRunCompletedInfo(
                Uploaded: false,
                WasScheduled: true,
                IsFirstFailureSinceSuccess: streakStart,
                RemoteFilePath: null,
                Endpoint: options.Endpoint));
            return CloudBackupRunResult.Failure;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Test seam: clears the scheduled-attempt spacing floor so a test can
    /// exercise back-to-back scheduled runs without waiting out the
    /// 10-minute window.
    /// </summary>
    internal void ResetScheduledAttemptSpacingForTesting() =>
        _lastScheduledAttemptUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Uploads one scoped snapshot now. Throws on failure — the manual path
    /// (PR-3 "backup now") surfaces the error to the user.
    /// </summary>
    internal async Task<CloudBackupRunResult> RunBackupNowAsync(CancellationToken cancellationToken = default)
    {
        // Skip rather than queue behind an in-flight scheduled upload:
        // blocking here would freeze the whole settings section for the
        // duration of somebody else's upload with no explanation.
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return CloudBackupRunResult.InProgress;
        }

        try
        {
            CloudBackupOptions options = _options;   // fresh read inside the gate
            if (!options.HasEndpoint)
            {
                return CloudBackupRunResult.NotConfigured;
            }

            // Uploading needs a scope — nothing to send when every domain
            // toggle is off. Named distinctly so the UI can point at the
            // domain switches instead of reporting a generic "not
            // configured" for an endpoint that tests fine.
            if (options.Scope == CloudBackupDomain.None)
            {
                return CloudBackupRunResult.NoScope;
            }

            // Same gate as the scheduled path: uploading now would push a
            // snapshot of state a staged restore is about to replace.
            if (File.Exists(_backupService.PendingRestoreMarkerPath))
            {
                return CloudBackupRunResult.PendingRestore;
            }

            // A missing credential would turn "backup now" into an opaque
            // 401 — name it so the UI can point at the password field.
            if (await _credentialStore.GetSecretAsync(
                    CloudBackupSettingsPolicy.CredentialKey(options),
                    cancellationToken) is null)
            {
                return CloudBackupRunResult.MissingCredential;
            }

            try
            {
                return await RunBackupCoreAsync(options, cancellationToken, scheduled: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Same stamp + event as the scheduled catch — a failed
                // manual run is still "the last upload failed" for the
                // status row, even though the click already shows it.
                bool streakStart = await RecordFailureAsync(options);
                NotifyRunCompleted(new CloudBackupRunCompletedInfo(
                    Uploaded: false,
                    WasScheduled: false,
                    IsFirstFailureSinceSuccess: streakStart,
                    RemoteFilePath: null,
                    Endpoint: options.Endpoint));
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Gate-held upload body shared by the scheduled and manual paths.</summary>
    private async Task<CloudBackupRunResult> RunBackupCoreAsync(
        CloudBackupOptions options,
        CancellationToken cancellationToken,
        bool scheduled)
    {
        string? stagingDirectory = null;
        try
        {
            ICloudBackupTransport transport = await CreateConfiguredTransportAsync(options, cancellationToken);
            await transport.EnsureDirectoryAsync(options.RemotePath, cancellationToken);

            stagingDirectory = Path.Combine(
                Path.GetTempPath(),
                $"deskbox-cloud-upload-{Guid.NewGuid():N}");
            string localArchivePath = await _backupService.ExportScopedBackupAsync(
                stagingDirectory,
                options.Scope,
                ct => Task.FromResult<byte[]?>(
                    WidgetStyleBackupProjection.Serialize(_settingsService.Settings)),
                cancellationToken);

            string remoteFilePath = $"{options.RemotePath}/{BuildRemoteSnapshotName()}";
            await using (FileStream content = File.OpenRead(localArchivePath))
            {
                await transport.UploadAsync(remoteFilePath, content, cancellationToken);
            }

            // UploadAsync returned successfully: the server accepted this
            // archive. A shutdown cancellation may skip verification/pruning,
            // but must not turn that accepted upload into a canceled attempt.
            bool uploadVerified = false;
            try
            {
                uploadVerified = await VerifyUploadAsync(
                    transport, options, remoteFilePath,
                    new FileInfo(localArchivePath).Length, VerificationRetryDelayMs, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                App.Log("[CloudBackup] Upload accepted; verification canceled, recording unverified success.");
            }
            if (!uploadVerified)
            {
                App.Log(
                    $"[CloudBackup] Upload accepted but listing verification failed for '{remoteFilePath}'.");
            }

            // Retention is best-effort: the upload already succeeded, and
            // suppressing the success stamp would make every future tick
            // look overdue and re-upload — remote grows unboundedly on a
            // server that allows PUT but rejects DELETE.
            int? pruned = null;
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    App.Log("[CloudBackup] Retention skipped: the run was canceled after the upload.");
                }
                else if (!uploadVerified)
                {
                    // R19-S10-01: listing never confirmed this archive, so it
                    // may not be on the server at all. Pruning now would delete
                    // the previous snapshots while the only copy of the new one
                    // is unknown — the remote could end up with nothing usable.
                    App.Log(
                        "[CloudBackup] Retention skipped: the upload could not be verified, keeping existing snapshots.");
                }
                else
                {
                    pruned = await ApplyRetentionAsync(
                        transport, options, remoteFilePath.Split('/')[^1], cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                App.Log("[CloudBackup] Retention canceled after the upload was accepted.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log($"[CloudBackup] Retention prune failed: {ex.Message}");
            }

            await MarkSuccessAsync(options, uploadVerified);
            _loggedScheduledFailureKey = null;
            App.Log(pruned is { } count
                ? $"[CloudBackup] Uploaded '{remoteFilePath}' (pruned {count} old snapshots)."
                : $"[CloudBackup] Uploaded '{remoteFilePath}' (retention not completed).");
            NotifyRunCompleted(new CloudBackupRunCompletedInfo(
                Uploaded: true,
                WasScheduled: scheduled,
                IsFirstFailureSinceSuccess: false,
                RemoteFilePath: remoteFilePath,
                UploadUnverified: !uploadVerified,
                Endpoint: options.Endpoint));
            return new CloudBackupRunResult(
                Uploaded: true,
                remoteFilePath,
                pruned ?? 0,
                UploadUnverified: !uploadVerified);
        }
        finally
        {
            if (stagingDirectory is not null)
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
    }

    /// <summary>
    /// Stores the provider secret for the currently configured account in
    /// the OS credential store. The key is scoped by provider+origin+username,
    /// so changing the endpoint or account writes a fresh entry rather than
    /// silently reusing the old one — and stale keys under the same provider
    /// are removed so secrets never linger in the vault.
    /// </summary>
    internal Task SaveCredentialAsync(string secret, CancellationToken cancellationToken = default) =>
        SaveCredentialAsync(_options, secret, cancellationToken);

    internal async Task SaveCredentialAsync(CloudBackupOptions options, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        string key = CloudBackupSettingsPolicy.CredentialKey(options);
        await _credentialStore.SetSecretAsync(key, secret, cancellationToken);
        _loggedMissingCredentialKey = null;

        // A save that completed after an endpoint edit must not sweep the new
        // account's credential from the vault.
        if (cancellationToken.IsCancellationRequested || _options.Endpoint != options.Endpoint) return;

        // Capture the provider before any await: UpdateOptions may swap
        // endpoints mid-call, and the stale-key sweep must not inherit it.
        string prefix = $"{options.Provider}:";
        foreach (string stale in await _credentialStore.ListKeysAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested || _options.Endpoint != options.Endpoint) break;
            if (stale.StartsWith(prefix, StringComparison.Ordinal) &&
                !string.Equals(stale, key, StringComparison.Ordinal))
            {
                await _credentialStore.RemoveSecretAsync(stale, cancellationToken);
            }
        }
    }

    /// <summary>Whether a secret already exists for the selected endpoint.</summary>
    internal Task<bool> HasCredentialAsync(CancellationToken cancellationToken = default) =>
        HasCredentialAsync(_options, cancellationToken);

    internal async Task<bool> HasCredentialAsync(CloudBackupOptions options, CancellationToken cancellationToken = default)
    {
        if (!options.HasEndpoint)
        {
            return false;
        }

        return await _credentialStore.GetSecretAsync(
            CloudBackupSettingsPolicy.CredentialKey(options),
            cancellationToken) is not null;
    }

    /// <summary>
    /// Verifies the configured endpoint; for the PR-3 "test connection"
    /// button. <paramref name="secretOverride"/> lets the caller probe with
    /// a just-typed password before it is saved to the vault.
    /// </summary>
    internal Task ProbeConnectionAsync(string? secretOverride = null, CancellationToken cancellationToken = default) =>
        ProbeConnectionAsync(_options, secretOverride, cancellationToken);

    internal async Task ProbeConnectionAsync(
        CloudBackupOptions options,
        string? secretOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!options.HasEndpoint)
        {
            throw new InvalidOperationException("Cloud backup is not configured.");
        }

        ICloudBackupTransport transport = string.IsNullOrEmpty(secretOverride)
            ? await CreateConfiguredTransportAsync(options, cancellationToken)
            : _transportFactory(options, secretOverride);
        await transport.ProbeAsync(cancellationToken);
    }

    /// <summary>Remote snapshot inventory for the PR-3 restore picker, newest first.</summary>
    internal Task<IReadOnlyList<CloudBackupRemoteEntry>> ListRemoteSnapshotsAsync(
        CancellationToken cancellationToken = default) => ListRemoteSnapshotsAsync(_options, cancellationToken);

    internal async Task<IReadOnlyList<CloudBackupRemoteEntry>> ListRemoteSnapshotsAsync(
        CloudBackupOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.HasEndpoint)
        {
            return Array.Empty<CloudBackupRemoteEntry>();
        }

        ICloudBackupTransport transport = await CreateConfiguredTransportAsync(options, cancellationToken);
        IReadOnlyList<CloudBackupRemoteEntry> entries = await transport.ListAsync(options.RemotePath, cancellationToken);
        return entries
            .Where(e => !e.IsCollection && IsSafeSnapshotBasename(e.Name))
            // Order by the embedded timestamp the row actually displays —
            // raw name order interleaves legacy local-time names wrongly
            // against UTC names. Name is only a tiebreak.
            .OrderByDescending(e => ParseSnapshotTimestamp(e.Name) ??
                                    e.LastModified ??
                                    DateTimeOffset.MinValue)
            .ThenByDescending(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Downloads one remote snapshot into a local directory. The returned
    /// file feeds straight into
    /// <see cref="DeskBoxDataBackupService.PrepareScopedRestoreAsync"/>.
    /// </summary>
    internal Task<string> DownloadSnapshotAsync(string remoteFileName, string destinationDirectory,
        CancellationToken cancellationToken = default) =>
        DownloadSnapshotAsync(_options, remoteFileName, destinationDirectory, cancellationToken);

    internal async Task<string> DownloadSnapshotAsync(
        CloudBackupOptions options,
        string remoteFileName,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!options.HasEndpoint)
        {
            throw new InvalidOperationException("Cloud backup is not configured.");
        }

        // Basename only — never let a remote-supplied name escape the
        // destination directory or reach outside RemotePath.
        if (!IsSafeSnapshotBasename(remoteFileName))
        {
            throw new ArgumentException("Invalid remote snapshot name.", nameof(remoteFileName));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ICloudBackupTransport transport = await CreateConfiguredTransportAsync(options, cancellationToken);
        Directory.CreateDirectory(destinationDirectory);
        string destinationPath = Path.Combine(destinationDirectory, remoteFileName);
        await using (FileStream destination = File.Create(destinationPath))
        {
            await transport.DownloadAsync($"{options.RemotePath}/{remoteFileName}", destination, cancellationToken);
        }

        return destinationPath;
    }

    /// <summary>
    /// Deletes one remote snapshot by basename. Serialized with uploads so
    /// retention never counts a file the user is mid-deleting, and a delete
    /// never slips between PUT and prune.
    /// </summary>
    internal Task DeleteRemoteSnapshotAsync(string remoteFileName,
        CancellationToken cancellationToken = default) =>
        DeleteRemoteSnapshotAsync(_options, remoteFileName, cancellationToken);

    internal async Task DeleteRemoteSnapshotAsync(
        CloudBackupOptions options,
        string remoteFileName,
        CancellationToken cancellationToken = default)
    {
        if (!options.HasEndpoint)
        {
            throw new InvalidOperationException("Cloud backup is not configured.");
        }

        if (!IsSafeSnapshotBasename(remoteFileName))
        {
            throw new ArgumentException("Invalid remote snapshot name.", nameof(remoteFileName));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ICloudBackupTransport transport = await CreateConfiguredTransportAsync(options, cancellationToken);
            await transport.DeleteAsync($"{options.RemotePath}/{remoteFileName}", cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// A 2xx PUT alone doesn't prove the server stored what we sent — a
    /// proxy can ACK early or truncate silently. Confirm the snapshot
    /// shows up in the directory listing at the expected size before the
    /// run counts as fully verified. A listing that never converges is
    /// NOT a failed upload: eventually-consistent WebDAV backends
    /// (Nextcloud, several NAS firmwares) accept the PUT and lag on
    /// listing it, so the run degrades to "uploaded but unverified"
    /// instead of stamping a failure that toasts and re-uploads. Returns
    /// false for that case; PUT failures still throw from the transport.
    /// </summary>
    private static async Task<bool> VerifyUploadAsync(
        ICloudBackupTransport transport,
        CloudBackupOptions options,
        string remoteFilePath,
        long expectedBytes,
        int[] retryDelayMs,
        CancellationToken cancellationToken)
    {
        string remoteName = remoteFilePath[(remoteFilePath.LastIndexOf('/') + 1)..];
        for (int attempt = 0; ; attempt++)
        {
            IReadOnlyList<CloudBackupRemoteEntry> listing =
                await transport.ListAsync(options.RemotePath, cancellationToken);
            CloudBackupRemoteEntry? found = listing.FirstOrDefault(
                e => string.Equals(e.Name, remoteName, StringComparison.Ordinal));
            // A server that reports no length can't be size-checked —
            // presence alone still catches the "never landed" case and
            // never flags the run as unverified.
            if (found is { } entry && (entry.Length is null || entry.Length == expectedBytes))
            {
                return true;
            }

            if (attempt >= retryDelayMs.Length)
            {
                return false;
            }

            await Task.Delay(retryDelayMs[attempt], cancellationToken);
        }
    }

    private async Task<ICloudBackupTransport> CreateConfiguredTransportAsync(
        CloudBackupOptions options,
        CancellationToken cancellationToken)
    {
        string? secret = await _credentialStore.GetSecretAsync(
            CloudBackupSettingsPolicy.CredentialKey(options),
            cancellationToken);
        return _transportFactory(options, secret);
    }

    private static ICloudBackupTransport CreateTransport(CloudBackupOptions options, string? secret) =>
        options.Provider switch
        {
            CloudBackupSettingsPolicy.ProviderWebDav => new WebDavBackupTransport(
                new WebDavBackupTransport.Options(
                    new Uri(options.ServerUrl),
                    options.Username,
                    secret)),
            _ => throw new NotSupportedException(
                $"Cloud backup provider '{options.Provider}' is not supported.")
        };

    /// <summary>
    /// Remote name = UTC timestamp + short device suffix, so two devices
    /// backing up in the same minute can never overwrite each other's
    /// snapshot — and cross-device ordering never depends on a local clock
    /// (legacy local-time names are still parsed on read).
    /// </summary>
    internal static string BuildRemoteSnapshotName()
    {
        string deviceSuffix = CurrentDeviceSuffix;
        return string.Create(CultureInfo.InvariantCulture, $"{SnapshotFilePrefix}{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{deviceSuffix}{SnapshotFileExtension}");
    }

    /// <summary>
    /// Short device suffix embedded in snapshot names this install writes.
    /// </summary>
    internal static string CurrentDeviceSuffix =>
        OwnedDeviceSuffix ?? LegacyDeviceSuffixPlaceholder;

    /// <summary>
    /// R20-V5-03: the suffix this install may claim as its own. Null when the
    /// device identity is missing - the name placeholder is shared by every
    /// install in that state, so claiming it as owned would let two machines
    /// prune each other's backups.
    /// </summary>
    internal static string? OwnedDeviceSuffix =>
        DeviceIdentity.Id is { Length: >= 8 } id ? id[..8] : null;

    /// <summary>
    /// True when a remote snapshot was written by this install. Retention may
    /// only delete owned snapshots: another device's archive is not ours to
    /// prune (R19-S1-06), and a legacy name without a device suffix could
    /// belong to any device, so it is left alone.
    /// </summary>
    internal static bool IsOwnedSnapshotName(string name)
    {
        if (!IsSafeSnapshotBasename(name) || OwnedDeviceSuffix is not { } suffix)
        {
            return false;
        }

        string stem = name[..^SnapshotFileExtension.Length];
        return stem.EndsWith("-" + suffix, StringComparison.Ordinal);
    }

    internal static bool IsSnapshotName(string name) =>
        name.StartsWith(SnapshotFilePrefix, StringComparison.Ordinal) &&
        name.EndsWith(SnapshotFileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A snapshot name that is also safe to join into a remote path —
    /// server-supplied names must never reach Delete/Download carrying
    /// separators or traversal segments.
    /// </summary>
    internal static bool IsSafeSnapshotBasename(string name) =>
        !string.IsNullOrEmpty(name) &&
        IsSnapshotName(name) &&
        !name.Contains('/') &&
        !name.Contains('\\') &&
        !name.Contains("..");

    /// <summary>
    /// Creation timestamp embedded in a remote snapshot name. New format:
    /// <c>DeskBox-CloudBackup-20260918T124500Z-&lt;device8&gt;.zip</c> (UTC).
    /// Legacy names embed local time (<c>20260918-210000</c>) and parse as
    /// UTC on a best-effort basis.
    /// </summary>
    internal static DateTimeOffset? ParseSnapshotTimestamp(string name)
    {
        if (!IsSnapshotName(name))
        {
            return null;
        }

        string stem = name[..^SnapshotFileExtension.Length];
        string[] parts = stem.Split('-');
        if (parts.Length >= 4 &&
            DateTimeOffset.TryParseExact(
                parts[^2],
                "yyyyMMdd'T'HHmmss'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTimeOffset utc))
        {
            return utc;
        }

        if (parts.Length >= 5 &&
            DateTimeOffset.TryParseExact(
                $"{parts[^3]}-{parts[^2]}",
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTimeOffset legacy))
        {
            return legacy;
        }

        // Pre-suffix legacy names end directly in the timestamp.
        if (parts.Length >= 4 &&
            DateTimeOffset.TryParseExact(
                $"{parts[^2]}-{parts[^1]}",
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTimeOffset unsuffixed))
        {
            return unsuffixed;
        }

        return null;
    }

    /// <summary>
    /// Prunes this device's own snapshots beyond the retention budget.
    /// Another device's snapshots are never deleted and never consume the
    /// budget (R19-S1-06), and the newest owned snapshot that predates this run
    /// is always kept, so an unverifiable new archive can never leave the
    /// remote without a usable backup (R19-S10-01).
    /// </summary>
    private async Task<int> ApplyRetentionAsync(
        ICloudBackupTransport transport,
        CloudBackupOptions options,
        string uploadedName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CloudBackupRemoteEntry> entries = await transport.ListAsync(options.RemotePath, cancellationToken);
        List<CloudBackupRemoteEntry> snapshots = entries
            .Where(e => !e.IsCollection && IsSafeSnapshotBasename(e.Name))
            // Server-reported LastModified first, embedded timestamp as
            // fallback — never raw name order, which legacy local-time
            // names skew across devices.
            .OrderByDescending(e => e.LastModified ??
                                    ParseSnapshotTimestamp(e.Name) ??
                                    DateTimeOffset.MinValue)
            .ToList();

        // The last known-good copy: the newest owned snapshot that is not the
        // archive this run just uploaded.
        string? lastKnownGood = snapshots
            .Where(e => IsOwnedSnapshotName(e.Name) &&
                        !string.Equals(e.Name, uploadedName, StringComparison.Ordinal))
            .Select(e => e.Name)
            .FirstOrDefault();

        // Clamp defensively: a caller that skipped GetOptions' 1-50 clamp
        // would otherwise delete every remote snapshot including this one.
        int retention = Math.Max(1, options.RetentionCount);
        int pruned = 0;
        int ownedSeen = 0;
        int keptProtected = 0;
        int keptForeign = 0;
        foreach (CloudBackupRemoteEntry entry in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsOwnedSnapshotName(entry.Name))
            {
                // R19-S1-06: another device's snapshot (or a legacy name with
                // no device suffix) is not ours to prune. R20-V5-02 reports the
                // resulting unbounded growth; deleting a peer machine's archive
                // is the worse failure, so this stays a documented trade-off
                // (see the R20 verification record) rather than a pruning rule.
                keptForeign++;
                continue;
            }

            // Retention counts this device's snapshots only, so a second
            // device's history can never push our own budget around.
            ownedSeen++;
            if (ownedSeen <= retention)
            {
                continue;
            }

            if (string.Equals(entry.Name, uploadedName, StringComparison.Ordinal))
            {
                continue;
            }

            if (lastKnownGood is not null &&
                string.Equals(entry.Name, lastKnownGood, StringComparison.Ordinal))
            {
                keptProtected++;
                continue;
            }

            await transport.DeleteAsync($"{options.RemotePath}/{entry.Name}", cancellationToken);
            pruned++;
        }

        if (keptProtected > 0)
        {
            App.Log($"[CloudBackup] Retention kept the last known-good snapshot '{lastKnownGood}'.");
        }

        if (keptForeign > 0)
        {
            App.Log($"[CloudBackup] Retention kept {keptForeign} snapshot(s) that this device does not own.");
        }

        return pruned;
    }

    private void NotifyRunCompleted(CloudBackupRunCompletedInfo info)
    {
        foreach (Action<CloudBackupRunCompletedInfo> observer in BackupRunCompleted?.GetInvocationList() ?? [])
        {
            try { observer(info); }
            catch (Exception ex) { App.Log($"[CloudBackup] Completion observer failed: {ex}"); }
        }
    }

    private async Task MarkSuccessAsync(
        CloudBackupOptions completedOptions,
        bool uploadVerified = true)
    {
        // The success belongs to the destination this run actually used.
        // If the user reconfigured mid-upload, stamping it onto the NEW
        // options would mark a destination that has never been backed up —
        // false assurance that delays its first real backup by a full
        // interval. Upload already succeeded; just skip the stamp.
        if (!_options.IsConfigured ||
            DestinationIdentityChanged(_options, completedOptions))
        {
            App.Log("[CloudBackup] Upload succeeded against superseded options; last-success not stamped.");
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _options = _options with { LastSuccessUtc = now };
        _settingsService.Settings.CloudBackup.CloudBackupLastSuccessUtcTicks = now.UtcTicks;
        // A success ends the failure streak — the status row should go
        // back to plain "last success" without a stale failure tail.
        _settingsService.Settings.CloudBackup.CloudBackupLastFailureUtcTicks = 0;
        // An upload the listing never confirmed is still a success, but the
        // status row marks it as unverified so a silently-dropped snapshot
        // can't hide behind a plain "last success" stamp. A verified run
        // clears the flag.
        _settingsService.Settings.CloudBackup.CloudBackupLastUnverifiedUtcTicks =
            uploadVerified ? 0 : now.UtcTicks;
        try
        {
            await _settingsService.SaveAsync(notifySubscribers: false);
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Failed to persist last-success timestamp: {ex}");
        }
    }

    /// <summary>
    /// Stamps the failed-attempt timestamp so the settings status row can
    /// surface a silently-failing backup instead of only showing a stale
    /// "last success". Returns true when this failure starts a new streak
    /// (first failure since the last success) — callers use it to toast
    /// once per streak rather than once per interval tick.
    /// </summary>
    private async Task<bool> RecordFailureAsync(CloudBackupOptions attemptedOptions)
    {
        // Same superseded-options rule as MarkSuccessAsync: a failure from
        // an upload started against the OLD destination must not flag the
        // new one that never ran.
        if (DestinationIdentityChanged(_options, attemptedOptions))
        {
            return false;
        }

        CloudBackupSettingsSlice slice = _settingsService.Settings.CloudBackup;
        bool streakStart = slice.CloudBackupLastFailureUtcTicks == 0;
        slice.CloudBackupLastFailureUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
        try
        {
            await _settingsService.SaveAsync(notifySubscribers: false);
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Failed to persist last-failure timestamp: {ex}");
        }

        return streakStart;
    }

    /// <summary>Best-effort temp directory cleanup — shared by staging and the UI restore path.</summary>
    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort — temp staging must not fail the backup.
        }
    }
}

/// <summary>One finished upload attempt, for <see cref="CloudBackupService.BackupRunCompleted"/>.</summary>
internal sealed record CloudBackupRunCompletedInfo(
    bool Uploaded,
    bool WasScheduled,
    bool IsFirstFailureSinceSuccess,
    string? RemoteFilePath,
    bool UploadUnverified = false,
    BackupEndpoint? Endpoint = null);
