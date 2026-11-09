using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeskBox.Models;
using DeskBox.Platform;

namespace DeskBox.Helpers;

public enum ElevatedFileLaunchStatus
{
    Launched,
    AlreadyRunningUnelevated,
    NoNewProcess,
    NotElevated,
    Cancelled,
    Failed
}

public readonly record struct ElevatedFileLaunchResult(
    ElevatedFileLaunchStatus Status,
    uint ProcessId = 0,
    int ErrorCode = 0);

internal readonly record struct ElevatedFileLaunchRequest(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    string OriginalTargetPath,
    bool DetectExistingProcess);

/// <summary>
/// Starts only the selected executable with the Windows <c>runas</c> verb.
/// DeskBox itself remains an ordinary-user/medium-integrity process.
/// </summary>
public static class ElevatedFileLauncher
{
    private const uint ShellExecuteMaskNoCloseProcess = 0x00000040;
    private const uint ShellExecuteMaskNoAsync = 0x00000100;
    private const uint ShellExecuteMaskFlagNoUi = 0x00000400;
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationInformationClass = 20;
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Extensions Windows treats as executables; the Open With dialog
    /// refuses them and the association list is empty, so open-with UI
    /// must be hidden for these types (shared with ShellOpenWithHelper).
    /// </summary>
    internal static readonly HashSet<string> ExecutableExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe",
            ".com",
            ".scr",
            ".msi",
            ".bat",
            ".cmd"
        };

    public static bool CanRunAsAdministrator(WidgetItem item) =>
        item is not null && TryCreateLaunchRequest(item, out _);

    /// <summary>
    /// Path-only variant for surfaces that hold a plain file path (for
    /// example search results) instead of a <see cref="WidgetItem"/>.
    /// </summary>
    public static bool CanRunAsAdministrator(string filePath) =>
        TryCreateLaunchRequest(filePath, null, out _);

    public static ElevatedFileLaunchResult RunAsAdministrator(
        IntPtr ownerWindow,
        WidgetItem item)
    {
        if (item is null ||
            !TryCreateLaunchRequest(item, out ElevatedFileLaunchRequest request))
        {
            return new ElevatedFileLaunchResult(
                ElevatedFileLaunchStatus.Failed);
        }

        return RunAsAdministrator(ownerWindow, in request);
    }

    public static ElevatedFileLaunchResult RunAsAdministrator(
        IntPtr ownerWindow,
        string filePath)
    {
        if (!TryCreateLaunchRequest(
                filePath,
                null,
                out ElevatedFileLaunchRequest request))
        {
            return new ElevatedFileLaunchResult(
                ElevatedFileLaunchStatus.Failed);
        }

        return RunAsAdministrator(ownerWindow, in request);
    }

    private static ElevatedFileLaunchResult RunAsAdministrator(
        IntPtr ownerWindow,
        in ElevatedFileLaunchRequest request)
    {
        if (request.DetectExistingProcess &&
            TryFindRunningUnelevatedTarget(
                request.OriginalTargetPath,
                out uint existingProcessId))
        {
            App.Log(
                $"[ElevatedLaunch] blocked existing unelevated target " +
                $"pid={existingProcessId} target='{request.OriginalTargetPath}'");
            return new ElevatedFileLaunchResult(
                ElevatedFileLaunchStatus.AlreadyRunningUnelevated,
                existingProcessId);
        }

        var executeInfo = new ElevatedLaunchNativeMethods.ShellExecuteInfo
        {
            Size = (uint)Marshal.SizeOf<ElevatedLaunchNativeMethods.ShellExecuteInfo>(),
            Mask = ShellExecuteMaskNoCloseProcess |
                   ShellExecuteMaskNoAsync |
                   ShellExecuteMaskFlagNoUi,
            OwnerWindow = ownerWindow,
            Verb = "runas",
            FileName = request.FileName,
            Parameters = string.IsNullOrWhiteSpace(request.Arguments)
                ? null
                : request.Arguments,
            Directory = request.WorkingDirectory,
            Show = 1
        };

        try
        {
            if (!ElevatedLaunchNativeMethods.ShellExecuteEx(ref executeInfo))
            {
                int errorCode = Marshal.GetLastWin32Error();
                ElevatedFileLaunchStatus status = errorCode == ErrorCancelled
                    ? ElevatedFileLaunchStatus.Cancelled
                    : ElevatedFileLaunchStatus.Failed;
                App.Log(
                    $"[ElevatedLaunch] ShellExecuteEx failed status={status} " +
                    $"error={errorCode} target='{request.OriginalTargetPath}'");
                return new ElevatedFileLaunchResult(
                    status,
                    ErrorCode: errorCode);
            }

            if (executeInfo.ProcessHandle == IntPtr.Zero)
            {
                App.Log(
                    $"[ElevatedLaunch] no new process handle target=" +
                    $"'{request.OriginalTargetPath}'");
                return new ElevatedFileLaunchResult(
                    ElevatedFileLaunchStatus.NoNewProcess);
            }

            uint processId = ElevatedLaunchNativeMethods.GetProcessId(executeInfo.ProcessHandle);
            bool? elevated = TryGetTokenElevation(
                executeInfo.ProcessHandle,
                out bool tokenElevated)
                    ? tokenElevated
                    : null;
            ElevatedFileLaunchStatus resultStatus = elevated == false
                ? ElevatedFileLaunchStatus.NotElevated
                : ElevatedFileLaunchStatus.Launched;
            App.Log(
                $"[ElevatedLaunch] completed status={resultStatus} " +
                $"pid={processId} tokenElevated=" +
                $"{(elevated.HasValue ? elevated.Value.ToString() : "unknown")} " +
                $"target='{request.OriginalTargetPath}' " +
                $"launcher='{request.FileName}'");
            return new ElevatedFileLaunchResult(resultStatus, processId);
        }
        catch (Win32Exception ex)
        {
            ElevatedFileLaunchStatus status = ex.NativeErrorCode == ErrorCancelled
                ? ElevatedFileLaunchStatus.Cancelled
                : ElevatedFileLaunchStatus.Failed;
            if (status != ElevatedFileLaunchStatus.Cancelled)
            {
                App.Log($"[ElevatedLaunch] runas failed: {ex.Message}");
            }

            return new ElevatedFileLaunchResult(
                status,
                ErrorCode: ex.NativeErrorCode);
        }
        catch (Exception ex)
        {
            App.Log($"[ElevatedLaunch] runas failed: {ex.Message}");
            return new ElevatedFileLaunchResult(
                ElevatedFileLaunchStatus.Failed);
        }
        finally
        {
            if (executeInfo.ProcessHandle != IntPtr.Zero)
            {
                ElevatedLaunchNativeMethods.CloseHandle(executeInfo.ProcessHandle);
            }
        }
    }

    internal static bool TryCreateLaunchRequest(
        WidgetItem item,
        out ElevatedFileLaunchRequest request)
    {
        request = default;
        if (item is null)
        {
            return false;
        }

        return TryCreateLaunchRequest(
            item.Path,
            item.TargetPath,
            out request);
    }

    internal static bool TryCreateLaunchRequest(
        string? candidatePath,
        string? targetPathHint,
        out ElevatedFileLaunchRequest request)
    {
        request = default;
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        string candidate = candidatePath.Trim();
        string arguments = string.Empty;
        string workingDirectory = string.Empty;
        if (ShortcutHelper.IsShellLinkPath(candidate))
        {
            ShortcutInfo? metadata = ShortcutHelper.ReadStoredMetadata(candidate);
            candidate = !string.IsNullOrWhiteSpace(metadata?.TargetPath)
                ? metadata.TargetPath.Trim()
                : targetPathHint?.Trim() ?? string.Empty;
            arguments = metadata?.Arguments?.Trim() ?? string.Empty;
            workingDirectory = metadata?.WorkingDirectory?.Trim() ?? string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(targetPathHint) &&
                 ShortcutHelper.IsShortcutPath(candidate))
        {
            candidate = targetPathHint.Trim();
        }

        candidate = Environment.ExpandEnvironmentVariables(candidate);
        if (candidate.Length == 0 ||
            Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
            !uri.IsFile)
        {
            return false;
        }

        try
        {
            candidate = Path.GetFullPath(candidate);
        }
        catch
        {
            return false;
        }

        string extension = Path.GetExtension(candidate);
        if (!ExecutableExtensions.Contains(extension) || !File.Exists(candidate))
        {
            return false;
        }

        workingDirectory = ResolveWorkingDirectory(
            workingDirectory,
            candidate);
        try
        {
            request = CreateLaunchRequest(
                candidate,
                arguments,
                workingDirectory);
        }
        catch (InvalidOperationException)
        {
            // R1-053: cmd.exe targets that cannot be quoted safely are
            // refused loudly here — the caller falls back to the normal
            // (ShellExecute) launch path instead of a silently-wrong
            // elevated command line.
            App.Log(
                "[ElevatedLaunch] refused unsafe cmd.exe quoting for " +
                $"'{candidate}'");
            request = default;
            return false;
        }
        return true;
    }

    internal static ElevatedFileLaunchRequest CreateLaunchRequest(
        string targetPath,
        string arguments,
        string workingDirectory)
    {
        string extension = Path.GetExtension(targetPath);
        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            // R1-053/R2-S1: cmd.exe has no complete quoting rules — inside
            // double quotes it still expands %VAR%, and the CRT-style \"
            // escape this helper emits (correct for msiexec) means nothing to
            // cmd. R2-S1: the script target is wrapped in quotes, so cmd
            // metacharacters stay literal there; the raw arguments are
            // appended UNQUOTED, and after cmd strips the outer /c quotes
            // those characters regain separator/control semantics. A quote
            // or % in either part is unsafe for the same reason. Rather than
            // build a silently-wrong command line, refuse to elevate.
            if (!IsSafeForCmdQuoting(targetPath) ||
                (!string.IsNullOrWhiteSpace(arguments) &&
                 !IsSafeForRawCmdArgument(arguments)))
            {
                throw new InvalidOperationException(
                    "cmd.exe cannot safely quote this script target or its " +
                    "arguments; refusing to build an elevated command line");
            }

            string commandInterpreter =
                Environment.GetEnvironmentVariable("ComSpec") ??
                Path.Combine(Environment.SystemDirectory, "cmd.exe");
            string scriptCommand = QuoteCommandLineArgument(targetPath);
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                scriptCommand += " " + arguments.Trim();
            }

            return new ElevatedFileLaunchRequest(
                commandInterpreter,
                $"/d /s /c \"{scriptCommand}\"",
                workingDirectory,
                targetPath,
                DetectExistingProcess: false);
        }

        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            string installer = Path.Combine(
                Environment.SystemDirectory,
                "msiexec.exe");
            string installerArguments =
                "/i " + QuoteCommandLineArgument(targetPath);
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                installerArguments += " " + arguments.Trim();
            }

            return new ElevatedFileLaunchRequest(
                installer,
                installerArguments,
                workingDirectory,
                targetPath,
                DetectExistingProcess: false);
        }

        return new ElevatedFileLaunchRequest(
            targetPath,
            arguments.Trim(),
            workingDirectory,
            targetPath,
            DetectExistingProcess: true);
    }

    internal static string QuoteCommandLineArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// R1-053: characters cmd.exe cannot be protected against inside double
    /// quotes. <c>%</c> always expands environment variables, a quote ends
    /// the quoted span (re-arming separators), and control characters break
    /// the line. Everything else — including &amp; | &lt; &gt; ^, which stay
    /// literal inside a quoted span — is allowed ("Tom &amp; Jerry.bat").
    /// Only valid for values this helper wraps in quotes (the script target).
    /// </summary>
    private static bool IsSafeForCmdQuoting(string value)
    {
        foreach (char character in value)
        {
            if (character == '"' || character == '%' || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// R2-S1: strict set for values appended to the cmd command line RAW
    /// (no surrounding quotes). After cmd strips the outer /c quotes, the
    /// command operators &amp; | &lt; &gt; ^ ( ) chain or redirect commands,
    /// a quote re-arms the whole separator family, % expands variables, and
    /// @ is command-prefix syntax — so none of them may appear. Comma,
    /// semicolon and equals are argument delimiters, not commands, and stay
    /// allowed.
    /// </summary>
    private static bool IsSafeForRawCmdArgument(string value)
    {
        foreach (char character in value)
        {
            if (character is '&' or '|' or '<' or '>' or '^' or '(' or ')' or '"' or '%' or '@' ||
                char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveWorkingDirectory(
        string configuredWorkingDirectory,
        string targetPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory))
        {
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(
                    configuredWorkingDirectory);
                string normalized = Path.GetFullPath(expanded);
                if (Directory.Exists(normalized))
                {
                    return normalized;
                }
            }
            catch
            {
                // Fall back to the target's containing directory.
            }
        }

        return Path.GetDirectoryName(targetPath) ?? string.Empty;
    }

    private static bool TryFindRunningUnelevatedTarget(
        string targetPath,
        out uint processId)
    {
        processId = 0;
        string processName = Path.GetFileNameWithoutExtension(targetPath);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return false;
        }

        foreach (Process process in processes)
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                IntPtr processHandle = ElevatedLaunchNativeMethods.OpenProcess(
                    ProcessQueryLimitedInformation,
                    inheritHandle: false,
                    (uint)process.Id);
                if (processHandle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (!TryGetProcessPath(processHandle, out string runningPath) ||
                        !string.Equals(
                            Path.GetFullPath(runningPath),
                            Path.GetFullPath(targetPath),
                            StringComparison.OrdinalIgnoreCase) ||
                        !TryGetTokenElevation(
                            processHandle,
                            out bool isElevated) ||
                        isElevated)
                    {
                        continue;
                    }

                    processId = (uint)process.Id;
                    return true;
                }
                catch
                {
                    // A process can exit while it is being inspected.
                }
                finally
                {
                    ElevatedLaunchNativeMethods.CloseHandle(processHandle);
                }
            }
        }

        return false;
    }

    private static bool TryGetProcessPath(
        IntPtr processHandle,
        out string processPath)
    {
        var path = new StringBuilder(32_768);
        uint length = (uint)path.Capacity;
        if (!ElevatedLaunchNativeMethods.QueryFullProcessImageName(
                processHandle,
                flags: 0,
                path,
                ref length))
        {
            processPath = string.Empty;
            return false;
        }

        processPath = path.ToString();
        return processPath.Length > 0;
    }

    private static bool TryGetTokenElevation(
        IntPtr processHandle,
        out bool isElevated)
    {
        isElevated = false;
        if (!ElevatedLaunchNativeMethods.OpenProcessToken(
                processHandle,
                TokenQuery,
                out IntPtr tokenHandle))
        {
            return false;
        }

        try
        {
            int size = Marshal.SizeOf<ElevatedLaunchNativeMethods.TokenElevation>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!ElevatedLaunchNativeMethods.GetTokenInformation(
                        tokenHandle,
                        TokenElevationInformationClass,
                        buffer,
                        size,
                        out _))
                {
                    return false;
                }

                isElevated = Marshal.PtrToStructure<ElevatedLaunchNativeMethods.TokenElevation>(buffer)
                    .TokenIsElevated != 0;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ElevatedLaunchNativeMethods.CloseHandle(tokenHandle);
        }
    }

    // Native entry points and their marshaling structures live in
    // DeskBox.Platform.ElevatedLaunchNativeMethods.
}
