using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskBox.Models;

namespace DeskBox.Services;

internal sealed record SearchFileQueryPage(
    IReadOnlyList<SearchResultItem> Items,
    int TotalMatchedCount,
    int NextOffset)
{
    public static SearchFileQueryPage Empty { get; } = new([], 0, 0);
}

public sealed record EverythingConnectionSnapshot(
    EverythingConnectionState State,
    string? ExecutablePath,
    string? Version,
    bool IsRunning,
    bool UsesManualPath,
    string? DiagnosticCode)
{
    public static EverythingConnectionSnapshot Unknown { get; } = new(
        EverythingConnectionState.Unknown,
        null,
        null,
        false,
        false,
        null);
}

/// <summary>
/// The only DeskBox filename provider. It queries the official Everything SDK over
/// local IPC and never creates, scans, watches, or persists a second filename index.
/// </summary>
public sealed class EverythingSearchService : IDisposable
{
    private const int MaximumInitialListCapacity = 4_000;
    private const long ConnectedProbeTtlMilliseconds = 30_000;
    private const long FailedProbeTtlMilliseconds = 2_000;
    // DEF-045: cap on how long a "not installed / not running" detection
    // result is trusted, so typing in the search box (disabled fast path)
    // cannot re-run the registry + process scan on every keystroke.
    private const long DisabledPathTtlMilliseconds = 30_000;
    // DEF-044: Everything_QueryW blocks until the Everything service answers.
    // If the process hangs (huge index scan, unresponsive kernel filter),
    // the synchronous call would hold _nativeGate forever and every later
    // search would queue behind it. A query that exceeds this budget loses
    // the race, returns an empty page for this attempt, and leaves the
    // in-flight work holding the gate so its eventual completion (or the
    // next Reset) cannot corrupt shared SDK state.
    private static readonly TimeSpan NativeQueryTimeout = TimeSpan.FromSeconds(3);

    private readonly SettingsService _settingsService;
    private readonly SemaphoreSlim _nativeGate = new(1, 1);
    private readonly object _snapshotLock = new();
    private EverythingConnectionSnapshot _snapshot = EverythingConnectionSnapshot.Unknown;
    private EverythingInstallationSnapshot? _lastInstallation;
    private long _lastProbeTick = long.MinValue;
    private long _lastDisabledFastPathTick = long.MinValue;
    private bool _isDisposed;

    public EverythingSearchService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public event Action<EverythingConnectionSnapshot>? ConnectionChanged;

    public EverythingConnectionSnapshot CurrentSnapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>
    /// Detects the executable and process state. IPC is attempted only after the user
    /// has enabled Everything integration and the caller permits a connection probe.
    /// </summary>
    public async Task<EverythingConnectionSnapshot> RefreshConnectionAsync(
        bool allowIpcProbe = true,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        PublishSnapshot(CurrentSnapshot with
        {
            State = EverythingConnectionState.Checking,
            DiagnosticCode = null
        });

        string configuredPath = _settingsService.Settings.SearchEverythingExecutablePath;
        EverythingInstallationSnapshot installation = await Task.Run(
            () => EverythingInstallationDetector.Detect(configuredPath),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _lastInstallation = installation;

        if (string.IsNullOrWhiteSpace(installation.ExecutablePath))
        {
            return PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.NotInstalled,
                installation,
                "executable-not-found"));
        }

        if (!_settingsService.Settings.SearchEverythingEnabled || !allowIpcProbe)
        {
            return PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.NotConfirmed,
                installation,
                "consent-required"));
        }

        if (!installation.IsRunning)
        {
            return PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.NotRunning,
                installation,
                "process-not-running"));
        }

        try
        {
            await _nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                (bool connected, string? version, uint error) = await Task.Run(
                    ProbeNativeConnection,
                    cancellationToken).ConfigureAwait(false);
                Interlocked.Exchange(ref _lastProbeTick, Environment.TickCount64);
                if (!connected)
                {
                    return PublishSnapshot(ClassifyIpcFailure(installation, error));
                }

                return PublishSnapshot(new EverythingConnectionSnapshot(
                    EverythingConnectionState.Connected,
                    installation.ExecutablePath,
                    version ?? installation.Version,
                    true,
                    installation.UsesManualPath,
                    null));
            }
            finally
            {
                _nativeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DllNotFoundException ex)
        {
            App.Log($"[Everything] SDK wrapper is missing: {ex.Message}");
            return PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.SdkUnavailable,
                installation,
                "sdk-missing"));
        }
        catch (BadImageFormatException ex)
        {
            App.Log($"[Everything] SDK architecture mismatch: {ex.Message}");
            return PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.SdkUnavailable,
                installation,
                "sdk-architecture-mismatch"));
        }
        catch (EntryPointNotFoundException ex)
        {
            App.Log($"[Everything] SDK export is unavailable: {ex.Message}");
            return PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.SdkUnavailable,
                installation,
                "sdk-export-missing"));
        }
        catch (Exception ex)
        {
            App.Log($"[Everything] Connection probe failed: {ex}");
            return PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.Error,
                installation,
                "probe-failed"));
        }
    }

    internal async Task<SearchFileQueryPage> SearchPageAsync(
        string query,
        int resultOffset,
        int requestedResults,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(query))
        {
            return SearchFileQueryPage.Empty;
        }

        if (!_settingsService.Settings.SearchEverythingEnabled)
        {
            // DEF-045: the disabled fast path only exists to keep the
            // connection snapshot honest while the user types. Re-running the
            // installation detection (registry views + process enumeration)
            // on every keystroke is wasted work, so reuse the last result
            // within a short TTL. Explicit refresh paths still bypass this.
            long nowTick = Environment.TickCount64;
            long lastTick = _lastDisabledFastPathTick;
            if (_lastInstallation is not null &&
                lastTick != long.MinValue &&
                nowTick - lastTick < DisabledPathTtlMilliseconds)
            {
                return SearchFileQueryPage.Empty;
            }

            _lastDisabledFastPathTick = nowTick;
            await RefreshConnectionAsync(
                allowIpcProbe: false,
                cancellationToken).ConfigureAwait(false);
            return SearchFileQueryPage.Empty;
        }

        EverythingConnectionSnapshot connection = await EnsureConnectionForQueryAsync(
            cancellationToken).ConfigureAwait(false);
        if (connection.State != EverythingConnectionState.Connected)
        {
            return SearchFileQueryPage.Empty;
        }

        int offset = Math.Max(0, resultOffset);
        int resultCount = Math.Max(1, requestedResults);
        string providerQuery = BuildProviderQuery(
            query,
            _settingsService.Settings.SearchEverythingAdvancedSyntaxEnabled);

        await _nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task<SearchFileQueryPage>? inFlightQuery = null;
        try
        {
            // DEF-044: race the blocking native query against a timeout.
            // The delegate unconditionally owns releasing _nativeGate (it
            // acquired nothing but inherited the caller's acquisition), so
            // exactly one release happens no matter which side of the race
            // wins. On timeout this caller returns an empty page immediately
            // instead of wedging every subsequent search behind a hung
            // Everything service; the in-flight native call keeps the gate
            // until the OS call finally returns, and the shared SDK state is
            // never touched by two queries at once.
            inFlightQuery = Task.Run(
                async () =>
                {
                    try
                    {
                        return QueryNative(
                            providerQuery,
                            query.Trim(),
                            offset,
                            resultCount,
                            cancellationToken);
                    }
                    finally
                    {
                        _nativeGate.Release();
                    }
                },
                CancellationToken.None);
            Task completed = await Task.WhenAny(
                inFlightQuery,
                Task.Delay(NativeQueryTimeout, cancellationToken)).ConfigureAwait(false);
            if (completed != inFlightQuery)
            {
                // A canceled user token also completes the delay task first;
                // distinguish that from a genuine timeout.
                cancellationToken.ThrowIfCancellationRequested();
                PublishSnapshot(CurrentSnapshot with
                {
                    DiagnosticCode = "query-timeout"
                });
                App.Log("[Everything] Query timed out; leaving the in-flight query to finish on its own.");
                return SearchFileQueryPage.Empty;
            }

            return await inFlightQuery.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EverythingIpcException ex)
        {
            EverythingInstallationSnapshot installation = _lastInstallation ??
                await Task.Run(
                    () => EverythingInstallationDetector.Detect(
                        _settingsService.Settings.SearchEverythingExecutablePath),
                    CancellationToken.None).ConfigureAwait(false);
            _lastInstallation = installation;
            PublishSnapshot(ClassifyIpcFailure(installation, ex.ErrorCode));
            App.Log($"[Everything] Query IPC failed with error {ex.ErrorCode}.");
            return SearchFileQueryPage.Empty;
        }
        catch (Exception ex) when (
            ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            EverythingInstallationSnapshot installation = _lastInstallation ??
                EverythingInstallationDetector.Detect(
                    _settingsService.Settings.SearchEverythingExecutablePath);
            PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.SdkUnavailable,
                installation,
                "sdk-unavailable"));
            App.Log($"[Everything] SDK query failed: {ex.Message}");
            return SearchFileQueryPage.Empty;
        }
        finally
        {
            // The gate is released by the query delegate itself exactly once
            // (it always runs - Task.Run was given CancellationToken.None).
            // The only case where this caller must release is when Task.Run
            // itself failed to produce a running delegate.
            if (inFlightQuery is null)
            {
                _nativeGate.Release();
            }
        }
    }

    public async Task<bool> SetExecutablePathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!EverythingInstallationDetector.IsValidExecutablePath(path))
        {
            return false;
        }

        _settingsService.Settings.SearchEverythingExecutablePath = Path.GetFullPath(path);
        _settingsService.SaveDebounced();
        await RefreshConnectionAsync(
            allowIpcProbe: _settingsService.Settings.SearchEverythingEnabled,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task UseAutomaticDetectionAsync(
        CancellationToken cancellationToken = default)
    {
        _settingsService.Settings.SearchEverythingExecutablePath = string.Empty;
        _settingsService.SaveDebounced();
        await RefreshConnectionAsync(
            allowIpcProbe: _settingsService.Settings.SearchEverythingEnabled,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> LaunchEverythingAsync(
        CancellationToken cancellationToken = default)
    {
        EverythingInstallationSnapshot installation = await Task.Run(
            () => EverythingInstallationDetector.Detect(
                _settingsService.Settings.SearchEverythingExecutablePath),
            cancellationToken).ConfigureAwait(false);
        _lastInstallation = installation;
        if (!EverythingInstallationDetector.IsValidExecutablePath(installation.ExecutablePath))
        {
            PublishSnapshot(CreateSnapshot(
                EverythingConnectionState.NotInstalled,
                installation,
                "executable-not-found"));
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installation.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(installation.ExecutablePath),
                UseShellExecute = true
            });
            await Task.Delay(TimeSpan.FromMilliseconds(800), cancellationToken).ConfigureAwait(false);
            EverythingConnectionSnapshot snapshot = await RefreshConnectionAsync(
                allowIpcProbe: _settingsService.Settings.SearchEverythingEnabled,
                cancellationToken).ConfigureAwait(false);
            return snapshot.IsRunning;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            App.Log($"[Everything] Failed to launch '{installation.ExecutablePath}': {ex.Message}");
            return false;
        }
    }

    internal static string BuildProviderQuery(string query, bool advancedSyntax)
    {
        string normalized = query.Trim();
        if (advancedSyntax || normalized.Length == 0)
        {
            return normalized;
        }

        // Everything uses quotes to make operators and spaces literal. A literal quote
        // cannot occur in a Windows filename, but escaping it keeps malformed input from
        // changing the parser state.
        return $"\"{normalized.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    internal static double ComputeRelevance(string fileName, string query)
    {
        if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 90;
        }

        if (fileName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (stem.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 70;
        }

        return fileName.Contains(query, StringComparison.OrdinalIgnoreCase) ? 50 : 10;
    }

    private async Task<EverythingConnectionSnapshot> EnsureConnectionForQueryAsync(
        CancellationToken cancellationToken)
    {
        EverythingConnectionSnapshot current = CurrentSnapshot;
        long lastProbe = Interlocked.Read(ref _lastProbeTick);
        long elapsed = Environment.TickCount64 - lastProbe;
        long ttl = current.State == EverythingConnectionState.Connected
            ? ConnectedProbeTtlMilliseconds
            : FailedProbeTtlMilliseconds;
        if (lastProbe != long.MinValue && elapsed >= 0 && elapsed < ttl)
        {
            return current;
        }

        return await RefreshConnectionAsync(
            allowIpcProbe: true,
            cancellationToken).ConfigureAwait(false);
    }

    private static (bool Connected, string? Version, uint Error) ProbeNativeConnection()
    {
        EverythingNativeMethods.Reset();
        int loaded = EverythingNativeMethods.IsDatabaseLoaded();
        uint error = EverythingNativeMethods.GetLastError();
        if (loaded == 0)
        {
            return (false, null, error);
        }

        string version = string.Join(
            '.',
            EverythingNativeMethods.GetMajorVersion(),
            EverythingNativeMethods.GetMinorVersion(),
            EverythingNativeMethods.GetRevision(),
            EverythingNativeMethods.GetBuildNumber());
        return (true, version, EverythingNativeMethods.ErrorOk);
    }

    private static SearchFileQueryPage QueryNative(
        string providerQuery,
        string rankingQuery,
        int resultOffset,
        int resultCount,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResultItem>(Math.Min(
            resultCount,
            MaximumInitialListCapacity));
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalMatches = 0;
        uint nativeCount = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EverythingNativeMethods.Reset();
            EverythingNativeMethods.SetSearch(providerQuery);
            EverythingNativeMethods.SetMatchPath(0);
            EverythingNativeMethods.SetMatchCase(0);
            EverythingNativeMethods.SetMatchWholeWord(0);
            EverythingNativeMethods.SetRegex(0);
            EverythingNativeMethods.SetOffset(checked((uint)resultOffset));
            EverythingNativeMethods.SetMax(checked((uint)resultCount));
            EverythingNativeMethods.SetSort(EverythingNativeMethods.SortNameAscending);
            EverythingNativeMethods.SetRequestFlags(
                EverythingNativeMethods.RequestFileName |
                EverythingNativeMethods.RequestPath |
                EverythingNativeMethods.RequestSize |
                EverythingNativeMethods.RequestDateModified);

            if (EverythingNativeMethods.Query(1) == 0)
            {
                throw new EverythingIpcException(EverythingNativeMethods.GetLastError());
            }

            nativeCount = EverythingNativeMethods.GetNumResults();
            totalMatches = ClampToInt(EverythingNativeMethods.GetTotalResults());
            for (uint index = 0; index < nativeCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Marshal.PtrToStringUni(
                    EverythingNativeMethods.GetResultFileName(index)) ?? string.Empty;
                string directory = Marshal.PtrToStringUni(
                    EverythingNativeMethods.GetResultPath(index)) ?? string.Empty;
                if (fileName.Length == 0)
                {
                    continue;
                }

                string fullPath = BuildFullPath(directory, fileName);
                if (!seenPaths.Add(fullPath))
                {
                    continue;
                }

                bool isFolder = EverythingNativeMethods.IsFolderResult(index) != 0;
                long? size = !isFolder &&
                             EverythingNativeMethods.GetResultSize(index, out long nativeSize) != 0
                    ? nativeSize
                    : null;
                DateTimeOffset? modified = null;
                if (EverythingNativeMethods.GetResultDateModified(index, out long fileTime) != 0 &&
                    fileTime > 0)
                {
                    try
                    {
                        modified = DateTimeOffset.FromFileTime(fileTime);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Keep the result even if an unusual filesystem timestamp is invalid.
                    }
                }

                results.Add(new SearchResultItem
                {
                    Kind = isFolder ? SearchResultKind.Folder : SearchResultKind.File,
                    Title = fileName,
                    Subtitle = directory,
                    DetailPath = fullPath,
                    FileSize = size,
                    ModifiedAt = modified,
                    RelevanceScore = ComputeRelevance(fileName, rankingQuery)
                });
            }
        }
        finally
        {
            EverythingNativeMethods.Reset();
        }

        int nextOffset = nativeCount == 0
            ? totalMatches
            : (int)Math.Min(totalMatches, (long)resultOffset + nativeCount);
        return new SearchFileQueryPage(results, totalMatches, nextOffset);
    }

    private EverythingConnectionSnapshot ClassifyIpcFailure(
        EverythingInstallationSnapshot installation,
        uint error)
    {
        bool permissionMismatch = error == EverythingNativeMethods.ErrorIpc &&
                                  !installation.IsCurrentProcessElevated &&
                                  !installation.HasUnelevatedProcess &&
                                  (installation.HasElevatedProcess ||
                                   installation.ConfiguredToRunAsAdministrator);
        return CreateSnapshot(
            permissionMismatch
                ? EverythingConnectionState.PermissionMismatch
                : EverythingConnectionState.IpcUnavailable,
            installation,
            permissionMismatch ? "integrity-level-mismatch" : $"ipc-error-{error}");
    }

    private static EverythingConnectionSnapshot CreateSnapshot(
        EverythingConnectionState state,
        EverythingInstallationSnapshot installation,
        string? diagnosticCode) => new(
        state,
        installation.ExecutablePath,
        installation.Version,
        installation.IsRunning,
        installation.UsesManualPath,
        diagnosticCode);

    private EverythingConnectionSnapshot PublishSnapshot(
        EverythingConnectionSnapshot snapshot)
    {
        EverythingConnectionSnapshot previous;
        lock (_snapshotLock)
        {
            previous = _snapshot;
            _snapshot = snapshot;
        }

        if (previous != snapshot)
        {
            ConnectionChanged?.Invoke(snapshot);
        }

        return snapshot;
    }

    private static string BuildFullPath(string directory, string fileName)
    {
        if (directory.Length == 0)
        {
            return fileName.Length == 2 && fileName[1] == ':'
                ? fileName + Path.DirectorySeparatorChar
                : fileName;
        }

        return directory.EndsWith(Path.DirectorySeparatorChar) ||
               directory.EndsWith(Path.AltDirectorySeparatorChar)
            ? directory + fileName
            : Path.Combine(directory, fileName);
    }

    private static int ClampToInt(uint value) =>
        value > int.MaxValue ? int.MaxValue : (int)value;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        bool acquired = _nativeGate.Wait(TimeSpan.FromSeconds(1));
        if (acquired)
        {
            try
            {
                try
                {
                    EverythingNativeMethods.CleanUp();
                }
                catch (Exception ex) when (
                    ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                {
                    // The provider may never have loaded in this process.
                }
            }
            finally
            {
                _nativeGate.Release();
            }

            _nativeGate.Dispose();
        }
    }

    private sealed class EverythingIpcException(uint errorCode) : Exception
    {
        public uint ErrorCode { get; } = errorCode;
    }
}
