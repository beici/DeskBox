using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskBox.Models;

namespace DeskBox.Services;

public sealed partial class AppUpdateService : IAppUpdateService
{
    public const string DefaultManifestUrl = "https://deskbox.fun/update/stable.json";
    public const string DefaultGitHubLatestReleaseApiUrl = "https://api.github.com/repos/Tianyu199509/DeskBox/releases/latest";
    public const string DefaultManualDownloadUrl = "https://deskbox.fun/download";

    private readonly HttpClient _httpClient;
    private readonly string _manifestUrl;
    private readonly string _githubLatestReleaseApiUrl;
    private readonly string _updateRootPath;

    // Display-only manifest fetches are static calls (no service instance on
    // surfaces like the release-notes window), so they share one client
    // instead of allocating per call.
    private static readonly HttpClient s_contentHttpClient = CreateContentHttpClient();

    // Bounds a single update download. HttpClient.Timeout (20s) only covers
    // request/header arrival once the body is streamed under
    // ResponseHeadersRead, and the background auto-download passes no
    // CancellationToken — without these bounds a stalled body stream would
    // park ReadAsync forever, the finally that releases _downloadInProgress
    // would never run, and every later download would silently no-op as
    // "cancelled". The per-read bound catches a dead connection mid-body;
    // the overall bound catches a drip-feed that never triggers it.
    private static readonly TimeSpan OverallDownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReadChunkTimeout = TimeSpan.FromSeconds(30);

    private static HttpClient CreateContentHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DeskBox/{GetCurrentAppVersion()}");
        return client;
    }

    public AppUpdateService(
        HttpClient? httpClient = null,
        string? manifestUrl = null,
        string? updateRootPath = null,
        string? githubLatestReleaseApiUrl = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DeskBox", GetCurrentAppVersion()));

        _manifestUrl = string.IsNullOrWhiteSpace(manifestUrl)
            ? DefaultManifestUrl
            : manifestUrl;
        _githubLatestReleaseApiUrl = string.IsNullOrWhiteSpace(githubLatestReleaseApiUrl)
            ? DefaultGitHubLatestReleaseApiUrl
            : githubLatestReleaseApiUrl;
        _updateRootPath = string.IsNullOrWhiteSpace(updateRootPath)
            ? DeskBoxDataPathService.Current.UpdatesDirectory
            : updateRootPath;
    }

    public AppUpdateCheckResult? LastCheckResult { get; private set; }
    public DateTime? LastCheckTimeUtc { get; private set; }
    public event Action<AppUpdateCheckResult>? CheckCompleted;
    public event Action<AppUpdateDownloadProgress>? DownloadProgressChanged;
    public event Action<AppUpdateDownloadResult>? DownloadCompleted;

    private string? _lastDownloadedInstallerPath;
    private string? _lastDownloadedVersion;
    private string? _lastDownloadedSha256;
    private int _downloadInProgress;
    // R1-060: set the moment an installer helper launch succeeded. The
    // retire branches in SetLastCheckResult must not delete a directory the
    // just-started updater is about to run an installer from — the check
    // loop can land inside that window because installation is a user click
    // while checks run on a timer. Instance field without reset: the worst
    // a stale true does is delay retirement to the next process.
    private bool _installerLaunchInProgress;

    public string? LastDownloadedInstallerPath => _lastDownloadedInstallerPath;
    public bool IsDownloadInProgress => Volatile.Read(ref _downloadInProgress) != 0;

    public void ClearLastDownloadedInstaller()
    {
        _lastDownloadedInstallerPath = null;
        _lastDownloadedVersion = null;
        _lastDownloadedSha256 = null;
    }

    // Test seam for R1-060: production code sets the flag only after a
    // helper launch succeeded, which a unit test cannot drive end-to-end.
    internal void MarkInstallerLaunchInProgressForTests() =>
        _installerLaunchInProgress = true;

    // B1: TryDeleteRetiredInstallerDirectory only runs in the process that
    // downloaded the installer — after an update-installed restart the
    // in-memory fields are null and the previous updates\<version>\ folders
    // accumulate (~one full installer per release). This startup sweep
    // reclaims them. Call once after the startup lifeline is established,
    // fire-and-forget (never a startup step): it keeps the current
    // version's folder, the helper staging folder, and any download this
    // process still holds, and it is a no-op while a launched updater may
    // still be running an installer (R1-060 gate).
    internal void SweepStaleInstallerDirectories(string currentVersion)
    {
        if (_installerLaunchInProgress)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(_updateRootPath))
            {
                return;
            }

            string keepCurrent = SanitizePathSegment(currentVersion);
            string? keepDownloaded = _lastDownloadedInstallerPath is null
                ? null
                : Path.GetDirectoryName(_lastDownloadedInstallerPath);
            foreach (string directory in Directory.EnumerateDirectories(_updateRootPath))
            {
                string name = Path.GetFileName(directory);
                if (string.Equals(name, keepCurrent, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "helper", StringComparison.OrdinalIgnoreCase) ||
                    (keepDownloaded is not null && string.Equals(
                        directory, keepDownloaded, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Only version-shaped directories are ours to reclaim; a
                // foreign folder someone placed under the updates root is
                // left alone.
                if (!TryParseVersion(name, out _))
                {
                    continue;
                }

                Directory.Delete(directory, recursive: true);
                App.Log($"[Update] Swept stale installer directory '{directory}'");
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Update] Stale installer sweep failed: {ex.Message}");
        }
    }

    // Retiring a downloaded installer must also reclaim its files: each
    // download lives in a version-scoped folder under the updates root, and
    // without this the folder grows by one installer with every release.
    // The expected-path guard pins the delete to that layout even if the
    // fields ever drift; the helper folder is never touched.
    private void TryDeleteRetiredInstallerDirectory()
    {
        string? retiredPath = _lastDownloadedInstallerPath;
        string? retiredVersion = _lastDownloadedVersion;
        if (string.IsNullOrWhiteSpace(retiredPath) ||
            string.IsNullOrWhiteSpace(retiredVersion))
        {
            return;
        }

        try
        {
            string expectedDirectory = Path.Combine(
                _updateRootPath,
                SanitizePathSegment(retiredVersion));
            if (!string.Equals(
                    Path.GetDirectoryName(retiredPath),
                    expectedDirectory,
                    StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(expectedDirectory))
            {
                return;
            }

            Directory.Delete(expectedDirectory, recursive: true);
            App.Log($"[Update] Deleted retired installer directory '{expectedDirectory}'");
        }
        catch (Exception ex)
        {
            App.Log($"[Update] Failed to delete retired installer directory: {ex.Message}");
        }
    }

    public string ManifestUrl => _manifestUrl;
    public AppUpdateDeliveryKind DeliveryKind => AppUpdateDeliveryKind.DirectInstaller;
    internal static string CurrentInstallerArchitectureSuffix =>
        GetInstallerArchitectureSuffix(RuntimeInformation.ProcessArchitecture);

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        return await CheckForUpdatesAsync(GetCurrentAppVersion(), cancellationToken);
    }

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var manifestResult = await CheckManifestForUpdatesAsync(currentVersion, cancellationToken);
        if (manifestResult.Status is AppUpdateCheckStatus.UpdateAvailable or AppUpdateCheckStatus.UpToDate)
        {
            return SetLastCheckResult(manifestResult);
        }

        var githubResult = await CheckGitHubLatestReleaseForUpdatesAsync(currentVersion, cancellationToken);
        if (githubResult.Status is AppUpdateCheckStatus.UpdateAvailable or AppUpdateCheckStatus.UpToDate)
        {
            return SetLastCheckResult(githubResult);
        }

        return SetLastCheckResult(manifestResult);
    }

    private async Task<AppUpdateCheckResult> CheckManifestForUpdatesAsync(string currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(_manifestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync(
                stream,
                AppUpdateJsonContext.Default.UpdateManifest,
                cancellationToken);
            if (manifest is null)
            {
                return new AppUpdateCheckResult(
                    AppUpdateCheckStatus.InvalidManifest,
                    currentVersion,
                    manifest,
                    "The update manifest is missing installer metadata for this processor architecture.");
            }

            // FARC-05: validate the architecture-selected view, but cache the
            // manifest exactly as it was received.
            AppUpdateManifest? selected = SelectInstallerForArchitecture(
                manifest,
                CurrentInstallerArchitectureSuffix);
            if (selected is null ||
                !IsManifestUsable(selected) ||
                string.IsNullOrWhiteSpace(selected.Sha256))
            {
                return new AppUpdateCheckResult(
                    AppUpdateCheckStatus.InvalidManifest,
                    currentVersion,
                    manifest,
                    "The update manifest is missing installer metadata for this processor architecture.");
            }

            return IsRemoteVersionNewer(currentVersion, selected.Version)
                ? new AppUpdateCheckResult(AppUpdateCheckStatus.UpdateAvailable, currentVersion, manifest)
                : new AppUpdateCheckResult(AppUpdateCheckStatus.UpToDate, currentVersion, manifest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new AppUpdateCheckResult(AppUpdateCheckStatus.Failed, currentVersion, errorMessage: ex.Message);
        }
    }

    private async Task<AppUpdateCheckResult> CheckGitHubLatestReleaseForUpdatesAsync(string currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(_githubLatestReleaseApiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync(
                stream,
                AppUpdateJsonContext.Default.GitHubRelease,
                cancellationToken);
            var manifest = await CreateManifestFromGitHubReleaseAsync(release, cancellationToken);
            if (!IsManifestUsable(manifest) || string.IsNullOrWhiteSpace(manifest!.Sha256))
            {
                return new AppUpdateCheckResult(
                    AppUpdateCheckStatus.InvalidManifest,
                    currentVersion,
                    manifest,
                    "The GitHub release metadata is missing required fields.");
            }

            return IsRemoteVersionNewer(currentVersion, manifest.Version)
                ? new AppUpdateCheckResult(AppUpdateCheckStatus.UpdateAvailable, currentVersion, manifest)
                : new AppUpdateCheckResult(AppUpdateCheckStatus.UpToDate, currentVersion, manifest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new AppUpdateCheckResult(AppUpdateCheckStatus.Failed, currentVersion, errorMessage: ex.Message);
        }
    }

    public async Task<AppUpdateDownloadResult> DownloadUpdateAsync(
        AppUpdateManifest manifest,
        IProgress<AppUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // One download at a time across all initiators (settings one-click
        // and the background auto-download); the loser gets a cancelled-kind
        // result, which every caller already treats as a quiet no-op.
        if (Interlocked.Exchange(ref _downloadInProgress, 1) == 1)
        {
            return AppUpdateDownloadResult.Failed(
                AppUpdateDownloadFailureKind.Cancelled,
                "Another update download is already in progress.");
        }

        AppUpdateDownloadResult result;
        try
        {
            var reportingProgress = new Progress<AppUpdateDownloadProgress>(p =>
            {
                progress?.Report(p);
                DownloadProgressChanged?.Invoke(p);
            });
            result = await DownloadUpdateCoreAsync(manifest, reportingProgress, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _downloadInProgress, 0);
        }

        if (result.Success && !string.IsNullOrWhiteSpace(result.FilePath))
        {
            _lastDownloadedInstallerPath = result.FilePath;
            _lastDownloadedVersion = manifest.Version;
            // R1-059: remember what the bytes hashed to at download time so
            // StartInstallerHelperAsync can prove the on-disk file still
            // matches before it is ever launched (and hand the hash to the
            // updater for a second verification right before it runs).
            //
            // R18-S1-002: the recorded hash MUST be the one the download core
            // actually verified against. DownloadUpdateCoreAsync re-selects the
            // installer for this architecture (:357-365) and verifies the bytes
            // against that clone's Sha256 (:444); taking the caller's manifest
            // hash here instead records the x64 hash for an arm64 payload, so
            // the pre-launch re-verification below would reject (and delete) a
            // perfectly good installer. Keep both sides on the same selection.
            _lastDownloadedSha256 = NormalizeSha256(
                SelectInstallerForArchitecture(manifest, CurrentInstallerArchitectureSuffix)?.Sha256
                ?? manifest.Sha256);

            // R20-S10-02: the digest above lives only in memory, so a restart
            // between "download" and "install" used to erase it — and the
            // updater treats a missing --installer-sha256 as "nothing to
            // verify". Persist it beside the installer and let InstallAsync
            // refuse to launch without one.
            WriteInstallerSidecarHash(result.FilePath, _lastDownloadedSha256);
        }

        DownloadCompleted?.Invoke(result);
        return result;
    }

    private async Task<AppUpdateDownloadResult> DownloadUpdateCoreAsync(
        AppUpdateManifest manifest,
        IProgress<AppUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // FARC-05: the substitution lands on a clone; the caller's manifest
        // (typically the cached LastCheckResult payload) stays untouched.
        AppUpdateManifest? selected = SelectInstallerForArchitecture(
            manifest,
            CurrentInstallerArchitectureSuffix);
        if (selected is null || !IsManifestUsable(selected))
        {
            return AppUpdateDownloadResult.Failed(AppUpdateDownloadFailureKind.InvalidManifest);
        }

        manifest = selected;

        if (string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            return AppUpdateDownloadResult.Failed(AppUpdateDownloadFailureKind.HashMissing);
        }

        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri))
        {
            return AppUpdateDownloadResult.Failed(AppUpdateDownloadFailureKind.InvalidManifest);
        }

        if (!IsInstallerDownloadCompatibleWithArchitecture(
                manifest.DownloadUrl,
                CurrentInstallerArchitectureSuffix))
        {
            return AppUpdateDownloadResult.Failed(
                AppUpdateDownloadFailureKind.InvalidManifest,
                "The installer architecture does not match this DeskBox process.");
        }

        string targetDirectory = Path.Combine(_updateRootPath, SanitizePathSegment(manifest.Version));
        string fileName = GetInstallerFileName(downloadUri, manifest.Version);
        string targetPath = Path.Combine(targetDirectory, fileName);
        string tempPath = targetPath + ".tmp";

        try
        {
            Directory.CreateDirectory(targetDirectory);
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overallCts.CancelAfter(OverallDownloadTimeout);
            CancellationToken linkedToken = overallCts.Token;

            using var response = await _httpClient.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                linkedToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            if (totalBytes is null && manifest.Size > 0)
            {
                totalBytes = manifest.Size;
            }

            long bytesReceived = 0;
            string actualSha256;
            await using (var remoteStream = await response.Content.ReadAsStreamAsync(linkedToken))
            await using (var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[1024 * 128];
                int bytesRead;
                while ((bytesRead = await remoteStream
                           .ReadAsync(buffer.AsMemory(0, buffer.Length), linkedToken)
                           .AsTask()
                           .WaitAsync(ReadChunkTimeout)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), linkedToken);
                    hash.AppendData(buffer, 0, bytesRead);
                    bytesReceived += bytesRead;
                    progress?.Report(new AppUpdateDownloadProgress(bytesReceived, totalBytes));
                }

                await fileStream.FlushAsync(linkedToken);
                actualSha256 = Convert.ToHexString(hash.GetHashAndReset());
            }

            if (!string.Equals(actualSha256, NormalizeSha256(manifest.Sha256), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(tempPath);
                return AppUpdateDownloadResult.Failed(AppUpdateDownloadFailureKind.HashMismatch);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            progress?.Report(new AppUpdateDownloadProgress(bytesReceived, totalBytes ?? bytesReceived));
            return AppUpdateDownloadResult.Completed(targetPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(tempPath);
            return AppUpdateDownloadResult.Failed(AppUpdateDownloadFailureKind.Cancelled);
        }
        catch (TimeoutException)
        {
            // A single read exceeded ReadChunkTimeout: the connection went
            // silent mid-body. Classified with the other network failures so
            // the caller's retry paths treat it like any dropped transfer.
            TryDelete(tempPath);
            return AppUpdateDownloadResult.Failed(
                AppUpdateDownloadFailureKind.Network,
                "The download stalled and was aborted.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            TryDelete(tempPath);
            return AppUpdateDownloadResult.Failed(AppUpdateDownloadFailureKind.Network, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // R20-S1-01: the overall download deadline cancels the linked token,
            // not the caller's, and stream writes surface that as a bare
            // OperationCanceledException - which matched none of the filters
            // above and escaped the method entirely (no result, no temp cleanup).
            TryDelete(tempPath);
            return AppUpdateDownloadResult.Failed(
                AppUpdateDownloadFailureKind.Network,
                "The download timed out and was aborted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            return AppUpdateDownloadResult.Failed(AppUpdateDownloadFailureKind.FileSystem, ex.Message);
        }
    }

    public async Task<AppUpdateInstallResult> StartInstallerHelperAsync(
        string installerPath,
        bool silent = true,
        string? appCultureName = null)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return AppUpdateInstallResult.Failed("The downloaded installer no longer exists.");
        }

        // R1-059: the download-time hash only proves the bytes as they left
        // the network. The file sits in a user-writable directory, so the
        // hash is recomputed before launch; a mismatch deletes the installer
        // (reusing the counted TryDelete helper) and refuses to start it
        // rather than running replaced code through the updater.
        // R20-S10-02: fail closed. This used to verify only when the digest was
        // still in memory AND the path matched the last download, so installing
        // after a restart skipped verification entirely and the updater's
        // second stage skipped it too. Resolve the digest from the sidecar the
        // download wrote and refuse to run an installer nobody can vouch for.
        string expectedSha256 = !string.IsNullOrWhiteSpace(_lastDownloadedSha256)
            ? _lastDownloadedSha256!
            : ReadInstallerSidecarHash(installerPath);
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            App.Log($"[Update] Refusing to install '{installerPath}': no digest is known for it.");
            return AppUpdateInstallResult.Failed(
                "The update package could not be verified. Please download the update again.");
        }

        if (!await TryVerifyInstallerHashAsync(installerPath, expectedSha256))
        {
            App.Log($"[Update] Installer hash changed on disk; deleting '{installerPath}'.");
            TryDelete(installerPath);
            ClearLastDownloadedInstaller();
            return AppUpdateInstallResult.Failed(
                "The downloaded installer changed on disk after the download and was removed. Please download the update again.");
        }

        string? appPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(appPath) || !File.Exists(appPath))
        {
            appPath = Path.Combine(AppContext.BaseDirectory, "DeskBox.exe");
        }

        if (!TryGetInstallDirectory(appPath, out string installDirectory))
        {
            return AppUpdateInstallResult.Failed(
                "DeskBox could not determine the current installation directory. Please use the manual installer instead.");
        }

        try
        {
            string helperPath = PrepareDetachedUpdaterHelper(AppContext.BaseDirectory, _updateRootPath);
            if (!File.Exists(helperPath))
            {
                return AppUpdateInstallResult.Failed("DeskBox.Updater.exe was not found in the application directory.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--installer");
            startInfo.ArgumentList.Add(installerPath);
            // R1-059 second stage: the updater re-verifies the hash right
            // before it launches the installer, closing the residual window
            // between the app-side verification above and the parent exiting.
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                startInfo.ArgumentList.Add("--installer-sha256");
                startInfo.ArgumentList.Add(expectedSha256);
            }
            startInfo.ArgumentList.Add("--app");
            startInfo.ArgumentList.Add(appPath);
            startInfo.ArgumentList.Add("--install-dir");
            startInfo.ArgumentList.Add(installDirectory);
            if (silent)
            {
                startInfo.ArgumentList.Add("--silent");
                // The silent update's installer language follows the language
                // the app is currently showing, so a user running DeskBox in
                // English never gets the Chinese progress page replayed from
                // a stale "Inno Setup: Language" value left by an old
                // install. Never sent for interactive runs: a /LANG match
                // suppresses Inno's Select Language dialog, which a manual
                // Setup run must keep.
                if (MapAppCultureToInstallerLanguage(appCultureName) is { } installerLanguage)
                {
                    startInfo.ArgumentList.Add("--installer-lang");
                    startInfo.ArgumentList.Add(installerLanguage);
                }
            }

            Process.Start(startInfo);
            // R1-060: only after a successful helper start — the updater now
            // owns this installer directory, so check-loop retirement below
            // must stand down for the rest of this process's life.
            _installerLaunchInProgress = true;
            return AppUpdateInstallResult.Started();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return AppUpdateInstallResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Maps the app's effective UI culture (LocalizationService.CurrentCultureName)
    /// to the installer language names declared in installer/DeskBox.iss — the
    /// exact reverse of InstallLanguageCode in that script, which writes
    /// HKCU\Software\DeskBox\InstallLanguage after each install. Both sides
    /// and the [Languages] sections must be extended together when a language
    /// is added; DeskBox.Updater.KnownInstallerLanguages whitelists the same
    /// names before they reach the installer command line.
    /// </summary>
    internal static string? MapAppCultureToInstallerLanguage(string? appCultureName)
    {
        return appCultureName switch
        {
            LocalizationService.LanguageChinese => "chinesesimplified",
            LocalizationService.LanguageChineseTraditional => "chinesetraditional",
            LocalizationService.LanguageEnglish => "english",
            LocalizationService.LanguageJapanese => "japanese",
            LocalizationService.LanguageGerman => "german",
            LocalizationService.LanguagePortuguese => "brazilianportuguese",
            LocalizationService.LanguageHindi => "hindi",
            LocalizationService.LanguageSpanish => "spanish",
            LocalizationService.LanguageFrench => "french",
            LocalizationService.LanguageArabic => "arabic",
            LocalizationService.LanguageBengali => "bengali",
            LocalizationService.LanguageRussian => "russian",
            LocalizationService.LanguageKorean => "korean",
            _ => null,
        };
    }

    internal static bool TryGetInstallDirectory(string? appPath, out string installDirectory)
    {
        installDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(appPath) || !File.Exists(appPath))
        {
            return false;
        }

        try
        {
            string fullAppPath = Path.GetFullPath(appPath);
            if (!string.Equals(
                    Path.GetFileName(fullAppPath),
                    "DeskBox.exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? directory = Path.GetDirectoryName(fullAppPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            installDirectory = directory;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static string PrepareDetachedUpdaterHelper(string appDirectory, string updateRootPath)
    {
        string sourceHelperPath = Path.Combine(appDirectory, "DeskBox.Updater.exe");
        if (!File.Exists(sourceHelperPath))
        {
            return sourceHelperPath;
        }

        string helperRoot = Path.Combine(updateRootPath, "helper");

        // Clean up stale helper directories from previous updates.
        try
        {
            if (Directory.Exists(helperRoot))
            {
                foreach (var oldDir in Directory.GetDirectories(helperRoot))
                {
                    try
                    {
                        Directory.Delete(oldDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[Update] Failed to clean old helper directory '{oldDir}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Update] Failed to enumerate helper directories: {ex.Message}");
        }

        string helperDirectory = Path.Combine(
            helperRoot,
            string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Environment.ProcessId}"));
        Directory.CreateDirectory(helperDirectory);

        foreach (var sourcePath in Directory.EnumerateFiles(appDirectory, "DeskBox.Updater.*", SearchOption.TopDirectoryOnly))
        {
            string targetPath = Path.Combine(helperDirectory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, targetPath, overwrite: true);
        }

        return Path.Combine(helperDirectory, "DeskBox.Updater.exe");
    }

    public static string GetCurrentAppVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0] ??
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ??
            "0.0.0";
    }

    public static bool IsRemoteVersionNewer(string currentVersion, string remoteVersion)
    {
        if (!TryParseVersion(currentVersion, out var current) ||
            !TryParseVersion(remoteVersion, out var remote))
        {
            return false;
        }

        return remote > current;
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        int suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return Version.TryParse(normalized, out version!);
    }

    public static bool IsManifestUsable(AppUpdateManifest? manifest)
    {
        return manifest is not null &&
            manifest.SchemaVersion == 1 &&
            !string.IsNullOrWhiteSpace(manifest.Version) &&
            Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out Uri? downloadUri) &&
            IsTrustedInstallerDownloadUri(downloadUri) &&
            (string.IsNullOrWhiteSpace(manifest.Arm64DownloadUrl) ||
                (Uri.TryCreate(manifest.Arm64DownloadUrl, UriKind.Absolute, out Uri? arm64Uri) &&
                    IsTrustedInstallerDownloadUri(arm64Uri)));
    }

    /// <summary>
    /// Fetches the published manifest purely for display surfaces (release
    /// notes, update summary). Unlike <see cref="IsManifestUsable"/>, this
    /// validation only requires the content fields: the installer-pinning
    /// rules exist to protect the download/install path and must not gate
    /// plain text display — that gate is what left Store builds (whose
    /// synthesized manifest carries a ms-windowsstore:// download target)
    /// with an eternally empty release-notes window.
    /// </summary>
    public static async Task<AppUpdateManifest?> FetchContentManifestAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await FetchContentManifestAsync(DefaultManifestUrl, s_contentHttpClient, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    internal static async Task<AppUpdateManifest?> FetchContentManifestAsync(
        string manifestUrl,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(manifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync(
            stream,
            AppUpdateJsonContext.Default.UpdateManifest,
            cancellationToken);
        return IsContentManifestUsable(manifest) ? manifest : null;
    }

    internal static bool IsContentManifestUsable(AppUpdateManifest? manifest)
    {
        return manifest is not null &&
            manifest.SchemaVersion == 1 &&
            !string.IsNullOrWhiteSpace(manifest.Version) &&
            manifest.HasReleaseNotesOrUrl;
    }

    /// <summary>
    /// Builds a display-only manifest that resolves notes for an exact
    /// version through the project's GitHub release tag; the release-notes
    /// loader converts github.com tag pages to the API endpoint and caches
    /// the body locally for offline viewing. Used when no full manifest is
    /// reachable or when the published manifest already moved on to a newer
    /// version than the one that needs showing.
    /// </summary>
    public static AppUpdateManifest CreateReleaseNotesOnlyManifest(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return new AppUpdateManifest
        {
            Channel = "direct",
            Version = version.Trim(),
            ReleaseNotesUrl = $"https://github.com/Tianyu199509/DeskBox/releases/tag/v{version.Trim()}",
        };
    }

    /// <summary>
    /// The installer may only come from DeskBox's own distribution hosts over
    /// HTTPS. The manifest hash proves integrity but not authenticity: a
    /// compromised feed can serve a malicious installer together with its own
    /// correct hash, so the origin itself has to be pinned.
    /// </summary>
    private static bool IsTrustedInstallerDownloadUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsHostOrSubdomain(uri, "deskbox.fun"))
        {
            return true;
        }

        // GitHub hosts third-party repositories, so trusting the whole domain
        // would let a tampered manifest point at any attacker repository with
        // a matching hash. Only this project's own release downloads count;
        // the actual asset bytes arrive via redirect from
        // objects.githubusercontent.com, which the HTTP client follows and
        // which therefore must never be trusted as a manifest origin itself.
        return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith(
                "/Tianyu199509/DeskBox/releases/download/",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHostOrSubdomain(Uri uri, string domain)
    {
        return uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the installer metadata for the requested architecture.
    /// FARC-05: the ARM64 promotion happens on a clone — the manifest
    /// instance flows into the <see cref="LastCheckResult"/> cache and must
    /// keep its original payload intact, so <see cref="DownloadUpdateAsync"/>
    /// works on the returned copy while the cached original is never
    /// rewritten in place. Returns null when no compatible installer exists.
    /// </summary>
    internal static AppUpdateManifest? SelectInstallerForArchitecture(
        AppUpdateManifest manifest,
        string architectureSuffix)
    {
        if (string.Equals(architectureSuffix, "x64", StringComparison.OrdinalIgnoreCase))
        {
            return IsInstallerDownloadCompatibleWithArchitecture(
                manifest.DownloadUrl,
                architectureSuffix)
                ? manifest
                : null;
        }

        if (!string.Equals(architectureSuffix, "arm64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Arm64DownloadUrl))
        {
            // Backward compatibility for an ARM64-only manifest that already
            // used the primary installer fields.
            return IsInstallerDownloadCompatibleWithArchitecture(
                manifest.DownloadUrl,
                architectureSuffix)
                ? manifest
                : null;
        }

        // R18-S1-001: a manifest that advertises an arm64 installer but omits
        // its hash cannot be used by this architecture at all — the caller
        // rejects the empty Sha256 and falls back to the GitHub release check.
        // Say so explicitly, otherwise a support ticket only ever sees
        // "could not check for updates" with no actionable cause.
        if (string.IsNullOrWhiteSpace(manifest.Arm64Sha256))
        {
            App.Log(
                "[AppUpdate] The update manifest carries arm64DownloadUrl but no arm64Sha256; " +
                "this arm64 process rejects it as InvalidManifest and falls back to the GitHub " +
                "release check. Publish arm64Sha256 (and arm64Size) together with arm64DownloadUrl.");
        }

        var selected = new AppUpdateManifest
        {
            SchemaVersion = manifest.SchemaVersion,
            Channel = manifest.Channel,
            Version = manifest.Version,
            ReleaseDate = manifest.ReleaseDate,
            MinimumSupportedVersion = manifest.MinimumSupportedVersion,
            Mandatory = manifest.Mandatory,
            DownloadUrl = manifest.Arm64DownloadUrl,
            Arm64DownloadUrl = manifest.Arm64DownloadUrl,
            ManualDownloadUrl = manifest.ManualDownloadUrl,
            MirrorUrl = manifest.MirrorUrl,
            Sha256 = manifest.Arm64Sha256,
            Arm64Sha256 = manifest.Arm64Sha256,
            Size = manifest.Arm64Size,
            Arm64Size = manifest.Arm64Size,
            ReleaseNotesUrl = manifest.ReleaseNotesUrl,
            Summary = manifest.Summary,
            ReleaseNotes = manifest.ReleaseNotes
        };
        return IsInstallerDownloadCompatibleWithArchitecture(
            selected.DownloadUrl,
            architectureSuffix)
            ? selected
            : null;
    }

    private async Task<AppUpdateManifest?> CreateManifestFromGitHubReleaseAsync(
        GitHubReleaseResponse? release,
        CancellationToken cancellationToken)
    {
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            return null;
        }

        var installerAsset = release.Assets.FirstOrDefault(IsInstallerAsset);
        if (installerAsset is null || string.IsNullOrWhiteSpace(installerAsset.BrowserDownloadUrl))
        {
            return null;
        }

        string sha256 = ExtractSha256FromDigest(installerAsset.Digest);
        if (string.IsNullOrWhiteSpace(sha256))
        {
            var shaAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, installerAsset.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
            sha256 = await DownloadSha256FromAssetAsync(shaAsset, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(sha256))
        {
            return null;
        }

        string version = NormalizeVersionText(release.TagName);
        return new AppUpdateManifest
        {
            SchemaVersion = 1,
            Channel = "stable",
            Version = version,
            DownloadUrl = installerAsset.BrowserDownloadUrl,
            ManualDownloadUrl = DefaultManualDownloadUrl,
            MirrorUrl = DefaultManualDownloadUrl,
            Sha256 = sha256,
            Size = Math.Max(0, installerAsset.Size),
            ReleaseNotesUrl = release.HtmlUrl,
            ReleaseNotes = string.IsNullOrWhiteSpace(release.Body)
                ? []
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en-US"] = release.Body
                },
            Summary = []
        };
    }

    private async Task<string> DownloadSha256FromAssetAsync(GitHubReleaseAsset? shaAsset, CancellationToken cancellationToken)
    {
        if (shaAsset is null || string.IsNullOrWhiteSpace(shaAsset.BrowserDownloadUrl))
        {
            return string.Empty;
        }

        using var response = await _httpClient.GetAsync(shaAsset.BrowserDownloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ExtractSha256FromText(content);
    }

    private AppUpdateCheckResult SetLastCheckResult(AppUpdateCheckResult result)
    {
        LastCheckResult = result;
        LastCheckTimeUtc = DateTime.UtcNow;

        // A downloaded installer only stays valid for the version it was
        // downloaded for: a check offering a different version retires it,
        // and so does an up-to-date answer (the running build IS the
        // downloaded one). A periodic re-check offering the SAME version
        // must keep it — that is exactly the downloaded-but-not-yet-
        // installed state the 24h loop would otherwise strand.
        // R1-060: the whole retire branch (delete + bookkeeping clear) is
        // gated on no installer launch being in flight — deleting the
        // directory out from under a started updater races the installer
        // run itself, and clearing only the fields would strand the files.
        if (_installerLaunchInProgress)
        {
            // Refuse to retire while a launched updater may still be
            // preparing to run this installer; the next launch retires it.
        }
        else if (result.IsUpdateAvailable && result.Manifest is { } offered &&
            !string.Equals(offered.Version, _lastDownloadedVersion, StringComparison.Ordinal))
        {
            TryDeleteRetiredInstallerDirectory();
            ClearLastDownloadedInstaller();
        }
        else if (result.Status == AppUpdateCheckStatus.UpToDate && _lastDownloadedInstallerPath is not null)
        {
            // B3: an up-to-date answer normally means the running build IS
            // the downloaded one, so retiring is right. But a degraded or
            // channel-switched check can call the RUNNING build up-to-date
            // while a genuinely newer download is still pending install —
            // keep the download in that case.
            if (!IsRemoteVersionNewer(result.CurrentVersion, _lastDownloadedVersion!))
            {
                TryDeleteRetiredInstallerDirectory();
                ClearLastDownloadedInstaller();
            }
        }

        CheckCompleted?.Invoke(result);
        return result;
    }

    internal static string GetInstallerFileName(Uri downloadUri, string version)
    {
        string fileName = Path.GetFileName(downloadUri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? $"DeskBox_Setup_{SanitizePathSegment(version)}_{CurrentInstallerArchitectureSuffix}.exe"
            : SanitizeFileName(fileName);
    }

    internal static string GetInstallerArchitectureSuffix(Architecture architecture)
    {
        return architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => string.Empty
        };
    }

    private static string SanitizePathSegment(string value)
    {
        string sanitized = SanitizeFileName(value);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "latest";
        }

        // "." and ".." survive the filename sanitizer (dots are legal name
        // chars) but combine into path traversal; a remote version string
        // must never resolve outside the update root.
        return sanitized is "." or ".." ? "_" : sanitized;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
    }

    private static string NormalizeSha256(string sha256)
    {
        return sha256.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }

    private static bool IsInstallerAsset(GitHubReleaseAsset asset)
    {
        return IsInstallerAssetName(asset.Name, CurrentInstallerArchitectureSuffix);
    }

    internal static bool IsInstallerAssetName(string assetName, string architectureSuffix)
    {
        return !string.IsNullOrWhiteSpace(architectureSuffix) &&
            assetName.StartsWith("DeskBox_Setup_", StringComparison.OrdinalIgnoreCase) &&
            assetName.EndsWith($"_{architectureSuffix}.exe", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsInstallerDownloadCompatibleWithArchitecture(
        string downloadUrl,
        string architectureSuffix)
    {
        if (string.IsNullOrWhiteSpace(architectureSuffix) ||
            !Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        string fileName = Path.GetFileName(uri.LocalPath);
        if (!fileName.StartsWith("DeskBox_Setup_", StringComparison.OrdinalIgnoreCase))
        {
            // Keep compatibility with a server endpoint that returns a
            // redirect or content-disposition filename instead of exposing
            // the final installer name in the URL.
            return true;
        }

        return fileName.EndsWith($"_{architectureSuffix}.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractSha256FromDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return string.Empty;
        }

        const string prefix = "sha256:";
        string value = digest.Trim();
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[prefix.Length..];
        }

        return IsSha256Hex(value) ? NormalizeSha256(value) : string.Empty;
    }

    private static string ExtractSha256FromText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = NormalizeSha256(token);
            if (IsSha256Hex(normalized))
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// R1-059: streams the file and compares its SHA256 against the expected
    /// hex digest. False means "do not launch this" — either the content
    /// changed or the file could not be read.
    /// </summary>
    internal static async Task<bool> TryVerifyInstallerHashAsync(
        string installerPath,
        string expectedSha256)
    {
        try
        {
            await using var stream = new FileStream(
                installerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[1024 * 128];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
            }

            return string.Equals(
                Convert.ToHexString(hash.GetHashAndReset()),
                NormalizeSha256(expectedSha256),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log($"[Update] Installer hash verification could not read '{installerPath}': {ex.Message}");
            return false;
        }
    }

    private static bool IsSha256Hex(string value)
    {
        return value.Length == 64 && value.All(Uri.IsHexDigit);
    }

    // R20-S10-02: durable home for the download-time digest. The updater must
    // never be asked to run an installer whose digest nobody remembers.
    internal static string InstallerSidecarPath(string installerPath) => installerPath + ".sha256";

    private static void WriteInstallerSidecarHash(string installerPath, string? sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
        {
            return;
        }

        try
        {
            File.WriteAllText(InstallerSidecarPath(installerPath), NormalizeSha256(sha256));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log($"[Update] Could not persist the installer digest beside '{installerPath}': {ex.Message}");
        }
    }

    internal static string ReadInstallerSidecarHash(string installerPath)
    {
        try
        {
            string sidecar = InstallerSidecarPath(installerPath);
            if (!File.Exists(sidecar))
            {
                return string.Empty;
            }

            string raw = File.ReadAllText(sidecar).Trim();
            return IsSha256Hex(raw) ? NormalizeSha256(raw) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log($"[Update] Could not read the installer digest beside '{installerPath}': {ex.Message}");
            return string.Empty;
        }
    }

    private static string NormalizeVersionText(string value)
    {
        string normalized = value.Trim();
        return normalized.StartsWith('v') || normalized.StartsWith('V')
            ? normalized[1..]
            : normalized;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        public GitHubReleaseAsset[] Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAsset
    {
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        public long Size { get; set; }

        public string Digest { get; set; } = string.Empty;
    }

    [JsonSourceGenerationOptions(
        JsonSerializerDefaults.Web,
        GenerationMode = JsonSourceGenerationMode.Metadata,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(AppUpdateManifest), TypeInfoPropertyName = "UpdateManifest")]
    [JsonSerializable(typeof(GitHubReleaseResponse), TypeInfoPropertyName = "GitHubRelease")]
    private sealed partial class AppUpdateJsonContext : JsonSerializerContext
    {
    }
}
