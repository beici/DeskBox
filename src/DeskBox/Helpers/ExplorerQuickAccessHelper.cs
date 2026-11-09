using System.Diagnostics;
#if !DESKBOX_NATIVE_AOT
using System.Runtime.InteropServices;
#endif

namespace DeskBox.Helpers;

public enum QuickAccessPinState
{
    Unknown,
    NotPinned,
    Pinned
}

public readonly record struct QuickAccessStateResult(QuickAccessPinState State, string? Error);

public readonly record struct QuickAccessOperationResult(bool Succeeded, string? Error);

/// <summary>
/// Shell helpers for exposing DeskBox folders in File Explorer entry points.
/// </summary>
public static class ExplorerQuickAccessHelper
{
#if !DESKBOX_NATIVE_AOT
    private const string QuickAccessNamespace = "shell:::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
    private const string PinVerb = "pintohome";
    private const string UnpinVerb = "unpinfromhome";
    private const string IsPinnedProperty = "System.IsPinnedToNameSpaceTree";
#endif

    /// <summary>
    /// Upper bound a caller waits for a Shell quick-access verb. The STA thread that
    /// runs the verb cannot be aborted safely, so it is intentionally left to finish
    /// (see <c>BoundedStaOperationRunner</c>); the timeout only releases the caller so
    /// a hung <c>pintohome</c> cannot leave a settings toggle stuck forever.
    /// </summary>
    private static readonly TimeSpan ShellStaOperationTimeout = TimeSpan.FromSeconds(15);

    public static Task<QuickAccessStateResult> GetQuickAccessPinStateAsync(string folderPath)
    {
        return RunShellStaAsync(
            () =>
            {
                QuickAccessPinState state = GetQuickAccessPinState(folderPath, out string? error);
                return new QuickAccessStateResult(state, error);
            },
            "GetPinState");
    }

    public static QuickAccessPinState GetQuickAccessPinState(string folderPath, out string? error)
    {
#if DESKBOX_NATIVE_AOT
        return GetQuickAccessPinStateRust(folderPath, out error);
#else
        return QuickAccessBackendPolicy.Current == QuickAccessBackendMode.Rust
            ? GetQuickAccessPinStateRust(folderPath, out error)
            : GetQuickAccessPinStateCSharp(folderPath, out error);
#endif
    }

    public static Task<QuickAccessOperationResult> TryPinFolderToQuickAccessAsync(string folderPath)
    {
        return RunShellStaAsync(
            () =>
            {
                bool succeeded = TryPinFolderToQuickAccess(folderPath, out string? error);
                return new QuickAccessOperationResult(succeeded, error);
            },
            "PinFolder");
    }

    public static bool TryPinFolderToQuickAccess(string folderPath, out string? error)
    {
#if DESKBOX_NATIVE_AOT
        return TryInvokeFolderVerbRust(
            folderPath,
            QuickAccessNativeOperation.Pin,
            createFolder: true,
            out error);
#else
        return QuickAccessBackendPolicy.Current == QuickAccessBackendMode.Rust
            ? TryInvokeFolderVerbRust(
                folderPath,
                QuickAccessNativeOperation.Pin,
                createFolder: true,
                out error)
            : TryInvokeFolderVerbCSharp(folderPath, PinVerb, createFolder: true, out error);
#endif
    }

    public static Task<QuickAccessOperationResult> TryUnpinFolderFromQuickAccessAsync(string folderPath)
    {
        return RunShellStaAsync(
            () =>
            {
                bool succeeded = TryUnpinFolderFromQuickAccess(folderPath, out string? error);
                return new QuickAccessOperationResult(succeeded, error);
            },
            "UnpinFolder");
    }

    public static bool TryUnpinFolderFromQuickAccess(string folderPath, out string? error)
    {
#if DESKBOX_NATIVE_AOT
        return TryUnpinFolderFromQuickAccessRust(folderPath, out error);
#else
        return QuickAccessBackendPolicy.Current == QuickAccessBackendMode.Rust
            ? TryUnpinFolderFromQuickAccessRust(folderPath, out error)
            : TryUnpinFolderFromQuickAccessCSharp(folderPath, out error);
#endif
    }

    private static QuickAccessPinState GetQuickAccessPinStateRust(
        string folderPath,
        out string? error)
    {
        error = null;
        if (!TryNormalizeFolderPath(folderPath, out string fullPath, out error))
        {
            return QuickAccessPinState.Unknown;
        }

        if (!Directory.Exists(fullPath))
        {
            return QuickAccessPinState.NotPinned;
        }

        QuickAccessNativeCallResult result = QuickAccessNativeBackend.Invoke(
            QuickAccessNativeOperation.QueryPinState,
            fullPath,
            string.Empty,
            string.Empty);
        if (!result.Success)
        {
            error = result.Detail;
            return QuickAccessPinState.Unknown;
        }

        return result.PinState;
    }

    private static bool TryUnpinFolderFromQuickAccessRust(string folderPath, out string? error)
    {
        QuickAccessPinState state = GetQuickAccessPinStateRust(folderPath, out error);
        if (state == QuickAccessPinState.NotPinned)
        {
            error = null;
            return true;
        }

        return TryInvokeFolderVerbRust(
            folderPath,
            QuickAccessNativeOperation.Unpin,
            createFolder: false,
            out error);
    }

    private static bool TryInvokeFolderVerbRust(
        string folderPath,
        QuickAccessNativeOperation operation,
        bool createFolder,
        out string? error)
    {
        error = null;
        try
        {
            if (!TryPrepareFolderItem(
                    folderPath,
                    createFolder,
                    out string fullPath,
                    out string parentPath,
                    out string folderName,
                    out error))
            {
                return false;
            }

            QuickAccessNativeCallResult result = QuickAccessNativeBackend.Invoke(
                operation,
                fullPath,
                parentPath,
                folderName);
            if (!result.Success)
            {
                error = result.Detail;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryPrepareFolderItem(
        string folderPath,
        bool createFolder,
        out string fullPath,
        out string parentPath,
        out string folderName,
        out string? error)
    {
        parentPath = string.Empty;
        folderName = string.Empty;
        if (!TryNormalizeFolderPath(folderPath, out fullPath, out error))
        {
            return false;
        }

        if (createFolder)
        {
            Directory.CreateDirectory(fullPath);
        }
        else if (!Directory.Exists(fullPath))
        {
            error = "Folder does not exist.";
            return false;
        }

        parentPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
        folderName = Path.GetFileName(
            fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(folderName))
        {
            error = "Folder path is invalid.";
            return false;
        }

        return true;
    }

    private static Task<T> RunShellStaAsync<T>(Func<T> action, string operationName)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                T result = action();
                LogShellTiming(operationName, stopwatch.Elapsed);
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                App.Log($"[QuickAccess] {operationName} failed after {stopwatch.ElapsedMilliseconds}ms: {ex}");
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = $"DeskBox QuickAccess {operationName}"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(ShellStaOperationTimeout);
    }

    private static void LogShellTiming(string operationName, TimeSpan elapsed)
    {
        string message = $"[QuickAccess] {operationName} completed in {elapsed.TotalMilliseconds:0}ms";
        if (elapsed >= TimeSpan.FromSeconds(1))
        {
            App.Log(message);
            return;
        }

        App.LogVerbose(message);
    }

    private static bool TryNormalizeFolderPath(string folderPath, out string fullPath, out string? error)
    {
        fullPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            error = "Folder path is empty.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(folderPath);
            string trimmedPath = fullPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(trimmedPath) &&
                !string.Equals(
                    trimmedPath,
                    Path.GetPathRoot(fullPath)?.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                fullPath = trimmedPath;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

#if !DESKBOX_NATIVE_AOT
    private static QuickAccessPinState GetQuickAccessPinStateCSharp(
        string folderPath,
        out string? error)
    {
        error = null;

        if (!TryNormalizeFolderPath(folderPath, out string fullPath, out error))
        {
            return QuickAccessPinState.Unknown;
        }

        if (!Directory.Exists(fullPath))
        {
            return QuickAccessPinState.NotPinned;
        }

        try
        {
            if (!TryCreateShellApplication(out object? shellObject, out error) ||
                shellObject is null)
            {
                return QuickAccessPinState.Unknown;
            }

            object? quickAccessFolderObject = null;
            try
            {
                dynamic shell = shellObject;
                quickAccessFolderObject = shell.NameSpace(QuickAccessNamespace);
                if (quickAccessFolderObject is null)
                {
                    error = "Quick Access namespace is unavailable.";
                    return QuickAccessPinState.Unknown;
                }

                dynamic quickAccessFolder = quickAccessFolderObject;
                object? items = null;
                try
                {
                    items = quickAccessFolder.Items();
                    foreach (dynamic item in (dynamic)items)
                    {
                        if (!TryGetFolderItemPath(item, out string itemPath) ||
                            !PathsEqual(itemPath, fullPath))
                        {
                            continue;
                        }

                        return TryGetPinnedProperty(item, out bool isPinned)
                            ? isPinned ? QuickAccessPinState.Pinned : QuickAccessPinState.NotPinned
                            : QuickAccessPinState.Unknown;
                    }
                }
                finally
                {
                    // The items collection RCW's single reference lives in
                    // this scope's local. Loop item RCWs belong to the
                    // enumerator and are deliberately left alone.
                    ReleaseCom(items);
                }

                return QuickAccessPinState.NotPinned;
            }
            finally
            {
                ReleaseCom(quickAccessFolderObject);
                ReleaseCom(shellObject);
            }
        }
        catch (COMException ex)
        {
            error = ex.Message;
            return QuickAccessPinState.Unknown;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return QuickAccessPinState.Unknown;
        }
    }

    private static bool TryUnpinFolderFromQuickAccessCSharp(
        string folderPath,
        out string? error)
    {
        error = null;

        QuickAccessPinState state = GetQuickAccessPinStateCSharp(folderPath, out error);
        if (state == QuickAccessPinState.NotPinned)
        {
            error = null;
            return true;
        }

        try
        {
            if (!TryNormalizeFolderPath(folderPath, out string fullPath, out error) ||
                !TryCreateShellApplication(out object? shellObject, out error))
            {
                return false;
            }

            object? quickAccessFolderObject = null;
            try
            {
                if (shellObject is null)
                {
                    return false;
                }

                dynamic shell = shellObject;
                quickAccessFolderObject = shell.NameSpace(QuickAccessNamespace);
                if (quickAccessFolderObject is not null)
                {
                    dynamic quickAccessFolder = quickAccessFolderObject;
                    object? items = null;
                    try
                    {
                        items = quickAccessFolder.Items();
                        foreach (dynamic item in (dynamic)items)
                        {
                            if (TryGetFolderItemPath(item, out string itemPath) &&
                                PathsEqual(itemPath, fullPath))
                            {
                                item.InvokeVerb(UnpinVerb);
                                return true;
                            }
                        }
                    }
                    finally
                    {
                        ReleaseCom(items);
                    }
                }
            }
            finally
            {
                ReleaseCom(quickAccessFolderObject);
                ReleaseCom(shellObject);
            }

            return TryInvokeFolderVerbCSharp(
                folderPath,
                UnpinVerb,
                createFolder: false,
                out error);
        }
        catch (COMException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // R1-055: explicit RCW release for the JIT oracle path only (AOT builds
    // take the Rust layer). The COM objects here would otherwise wait for GC
    // on the dedicated STA thread that dies right after each call. Only
    // objects with a provable single held reference are released — the
    // foreach enumerator and its compiler temporaries keep theirs, and
    // ReleaseComObject (unlike FinalReleaseComObject) only decrements one
    // reference, so the loop's hidden reference stays valid.
    private static void ReleaseCom(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch (InvalidOperationException)
            {
                // Already detached from the RCW — nothing left to release.
            }
        }
    }

    private static bool TryInvokeFolderVerbCSharp(
        string folderPath,
        string verb,
        bool createFolder,
        out string? error)
    {
        error = null;

        try
        {
            if (!TryCreateShellApplication(out object? shellObject, out error) ||
                shellObject is null ||
                !TryGetFolderItem(
                    shellObject,
                    folderPath,
                    createFolder,
                    out object? folderItemObject,
                    out error))
            {
                ReleaseCom(shellObject);
                return false;
            }

            try
            {
                if (folderItemObject is null)
                {
                    return false;
                }

                dynamic folderItem = folderItemObject;
                folderItem.InvokeVerb(verb);
                return true;
            }
            finally
            {
                ReleaseCom(folderItemObject);
                ReleaseCom(shellObject);
            }
        }
        catch (COMException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryCreateShellApplication(out object? shell, out string? error)
    {
        shell = null;
        error = null;

        Type? shellApplicationType = Type.GetTypeFromProgID("Shell.Application");
        if (shellApplicationType is null)
        {
            error = "Shell.Application is unavailable.";
            return false;
        }

        shell = Activator.CreateInstance(shellApplicationType);
        if (shell is null)
        {
            error = "Could not create Shell.Application.";
            return false;
        }

        return true;
    }

    private static bool TryGetFolderItem(
        object shellObject,
        string folderPath,
        bool createFolder,
        out object? folderItem,
        out string? error)
    {
        folderItem = null;
        error = null;

        if (!TryNormalizeFolderPath(folderPath, out string fullPath, out error))
        {
            return false;
        }

        if (createFolder)
        {
            Directory.CreateDirectory(fullPath);
        }
        else if (!Directory.Exists(fullPath))
        {
            error = "Folder does not exist.";
            return false;
        }

        string? parentPath = Path.GetDirectoryName(fullPath);
        string folderName = Path.GetFileName(
            fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(folderName))
        {
            error = "Folder path is invalid.";
            return false;
        }

        dynamic shell = shellObject;
        object? parentFolderObject = shell.NameSpace(parentPath);
        try
        {
            if (parentFolderObject is null)
            {
                error = "Could not open the parent folder in File Explorer.";
                return false;
            }

            dynamic parentFolder = parentFolderObject;
            folderItem = parentFolder.ParseName(folderName);
            if (folderItem is null)
            {
                error = "Could not locate the folder in File Explorer.";
                return false;
            }

            return true;
        }
        finally
        {
            // folderItem crosses to the caller (its own finally releases it);
            // only the parent folder RCW is owned by this scope.
            ReleaseCom(parentFolderObject);
        }
    }

    private static bool TryGetFolderItemPath(dynamic item, out string path)
    {
        path = string.Empty;

        try
        {
            string? itemPath = item.Path;
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return false;
            }

            path = Path.GetFullPath(itemPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPinnedProperty(dynamic item, out bool isPinned)
    {
        isPinned = false;

        try
        {
            object? value = item.ExtendedProperty(IsPinnedProperty);
            switch (value)
            {
                case bool boolValue:
                    isPinned = boolValue;
                    return true;
                case int intValue:
                    isPinned = intValue != 0;
                    return true;
                case string stringValue when bool.TryParse(stringValue, out bool boolValue):
                    isPinned = boolValue;
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
#endif
}
