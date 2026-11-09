using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

[assembly: InternalsVisibleTo("DeskBox.Tests")]

namespace DeskBox.Updater;

internal enum DirectInstallScope
{
    CurrentUser,
    AllUsers
}

internal static class Program
{
    private const int InstallRegistrationMismatchExitCode = 20;
    private const int InstallerTimeoutExitCode = 8;
    private const int InstallerHashMismatchExitCode = 9;
    private const string UpdateInstallResultArgument = "--update-install-result";

    // Canonical names from the [Languages] sections of installer/DeskBox.iss
    // and DeskBox.arm64.iss (both list English first). The app maps its
    // effective UI culture to one of these; anything else is dropped here so
    // a stale or malformed --installer-lang value can never reach the
    // installer command line.
    internal static readonly FrozenSet<string> KnownInstallerLanguages = new[]
    {
        "english",
        "chinesesimplified",
        "chinesetraditional",
        "japanese",
        "german",
        "brazilianportuguese",
        "hindi",
        "spanish",
        "french",
        "arabic",
        "bengali",
        "russian",
        "korean",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    private const string InstallStateRegistryPath = @"Software\DeskBox\DirectInstall";
    private const string UninstallRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{5E052824-3456-427E-9759-3BCAE078A1D3}_is1";
    private const string WowUninstallRegistryPath =
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{5E052824-3456-427E-9759-3BCAE078A1D3}_is1";
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskBox",
        "DeskBox.Updater.log");

    private static int Main(string[] args)
    {
        UpdateOptions? options = null;
        try
        {
            options = UpdateOptions.Parse(args);
            if (options.RestartOnly)
            {
                if (string.IsNullOrWhiteSpace(options.AppPath) || !File.Exists(options.AppPath))
                {
                    Log("App executable not found for restart.");
                    return 2;
                }

                WaitForParentExit(options.ParentProcessId);
                return RestartApp(options.AppPath) ? 0 : 3;
            }

            // Wait for the old instance to release the single-instance
            // mutex before any restart attempt, including the failure path:
            // RestartAfterIncompleteUpdate starts a new DeskBox process, and
            // starting it while the parent is still shutting down makes the
            // new instance exit silently against the parent's mutex.
            WaitForParentExit(options.ParentProcessId);

            if (string.IsNullOrWhiteSpace(options.InstallerPath) || !File.Exists(options.InstallerPath))
            {
                Log("Installer not found.");
                RestartAfterIncompleteUpdate(options, "failed");
                return 2;
            }

            // R1-059 second stage: the app verified the installer hash just
            // before starting this helper, but the bytes could still change
            // while the app is shutting down. Refuse to run a file that no
            // longer matches the digest captured at download time.
            // R20-S10-02: fail closed. An empty --installer-sha256 used to mean
            // "nothing to verify", so a launch that omitted the argument ran
            // whatever file sat at that path. The app now always supplies one.
            if (string.IsNullOrWhiteSpace(options.InstallerSha256))
            {
                Log("Missing --installer-sha256; refusing to run an unverified installer.");
                RestartAfterIncompleteUpdate(options, "failed");
                return InstallerHashMismatchExitCode;
            }

            if (!VerifyInstallerSha256(options.InstallerPath, options.InstallerSha256))
            {
                Log($"Installer hash mismatch after handoff: '{options.InstallerPath}'.");
                RestartAfterIncompleteUpdate(options, "failed");
                return InstallerHashMismatchExitCode;
            }

            int exitCode = RunInstaller(options);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(options.AppPath) && File.Exists(options.AppPath))
            {
                if (!RestartApp(options.AppPath))
                {
                    // DEF-113: a failed restart after a successful install
                    // would leave the machine without a running DeskBox and
                    // with no --update-install-result signal at all. Report
                    // it so the next launch can surface the failure dialog.
                    RestartAfterIncompleteUpdate(options, "restart-failed");
                }
            }
            else if (exitCode != 0)
            {
                RestartAfterIncompleteUpdate(options, GetIncompleteUpdateOutcome(exitCode));
            }
            else
            {
                // Install succeeded but there is no executable to restart:
                // the parent has already exited, so without this branch the
                // updater would return 0 with no restart and no signal. The
                // next launch cannot be told either way, so classify the
                // outcome and leave the log as the only diagnostic.
                Log($"Install succeeded (exit code 0) but the app executable was missing: '{options.AppPath}'.");
                RestartAfterIncompleteUpdate(options, "restart-failed");
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            Log($"Fatal: {ex}");
            if (options is not null)
            {
                RestartAfterIncompleteUpdate(options, "failed");
            }

            return 1;
        }
    }

    /// <summary>
    /// R1-059 second stage: recomputes the installer's SHA256 and compares
    /// it with the hex digest captured at download time. The digest arrives
    /// already normalized by the app; whitespace is tolerated defensively.
    /// </summary>
    private static bool VerifyInstallerSha256(string installerPath, string expectedSha256)
    {
        try
        {
            string expected = expectedSha256.Trim();
            using var stream = new FileStream(
                installerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128);
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] buffer = new byte[1024 * 128];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
            }

            string actual = Convert.ToHexString(hash.GetHashAndReset());
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log($"Installer hash verification failed for '{installerPath}': {ex.Message}");
            return false;
        }
    }

    private static void WaitForParentExit(int parentProcessId)
    {
        if (parentProcessId <= 0)
        {
            return;
        }

        try
        {
            using var parentProcess = Process.GetProcessById(parentProcessId);
            // Snapshot the identity so a timeout can distinguish "still the
            // parent" from "pid was reused after the parent exited" — the
            // latter must not stall the update a full two minutes.
            DateTime parentStartTime = parentProcess.StartTime;
            Log($"Waiting for DeskBox process {parentProcessId} to exit.");
            // R2 ⑧-SH02: the app's graceful shutdown budget is six 10s
            // bounded steps plus an exit backstop (~65s worst case for a
            // slow-but-healthy shutdown); 120s clears it with margin. A
            // shorter wait made healthy updates look wedged and let the
            // installer's /FORCECLOSEAPPLICATIONS race the settings flush.
            // R01-S7-02: the app now arms a 150s exit backstop (App.xaml.cs
            // ArmShutdownExitBackstop), so 120s could expire while the parent
            // was still legitimately shutting down; wait past the backstop.
            if (!parentProcess.WaitForExit(milliseconds: 180_000))
            {
                try
                {
                    using Process? current = Process.GetProcessById(parentProcessId);
                    if (current is null || current.StartTime != parentStartTime)
                    {
                        Log($"Process {parentProcessId} was reused; original parent already exited.");
                    }
                }
                catch (ArgumentException)
                {
                    // Parent exited between the timeout and the recheck.
                }
            }
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex)
        {
            Log($"Wait parent failed: {ex.Message}");
        }
    }

    private static int RunInstaller(UpdateOptions options)
    {
        if (!TryGetInstallDirectory(options, out string installDirectory))
        {
            Log("Current installation directory is missing or invalid; refusing to run a silent update without /DIR.");
            return 4;
        }

        DirectInstallScope installScope = ResolveInstallScope(installDirectory);
        string installerLanguage = ResolveInstallerLanguage(options.InstallerLanguage);
        if (!string.IsNullOrWhiteSpace(options.InstallerLanguage) && installerLanguage.Length == 0)
        {
            Log($"Ignoring unrecognized installer language '{options.InstallerLanguage}'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = options.InstallerPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(options.InstallerPath) ?? Environment.CurrentDirectory
        };

        // Inno Setup otherwise falls back to DefaultDirName during a silent
        // install. Always pin an in-app update to the directory containing
        // the currently running DeskBox.exe.
        foreach (string argument in BuildInstallerArguments(
                     installDirectory,
                     options.Silent,
                     installScope,
                     installerLanguage))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Log(
            $"Starting installer: {options.InstallerPath}; " +
            $"target directory: {installDirectory}; scope: {installScope}; " +
            $"language: {(installerLanguage.Length == 0 ? "<installer default>" : installerLanguage)}");
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Log("Installer process did not start.");
            return 3;
        }

        // An Inno installer normally finishes in minutes; a wedged one (a
        // modal prompt in a broken silent run, stuck custom code) must not
        // hold the updater hostage forever. Thirty minutes accommodates the
        // slowest real hardware; past that the installer is killed.
        if (!process.WaitForExit(milliseconds: 30 * 60 * 1000))
        {
            Log("Installer did not exit within 30 minutes; killing it.");
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Log($"Failed to kill the stalled installer: {ex.Message}");
            }

            return InstallerTimeoutExitCode;
        }

        Log($"Installer exited with code {process.ExitCode}.");
        if (process.ExitCode != 0)
        {
            return process.ExitCode;
        }

        if (!IsInstallRegistrationConsistent(installDirectory, installScope))
        {
            Log($"Installer reported success, but the registered installation path does not match {installDirectory}.");
            return InstallRegistrationMismatchExitCode;
        }

        return 0;
    }

    internal static IReadOnlyList<string> BuildInstallerArguments(
        string installDirectory,
        bool silent,
        DirectInstallScope installScope,
        string installerLanguage = "")
    {
        var arguments = new List<string>
        {
            $"/DIR={installDirectory}",
            installScope == DirectInstallScope.AllUsers
                ? "/ALLUSERS"
                : "/CURRENTUSER"
        };

        if (silent)
        {
            // Keep Inno's progress window visible while hiding the wizard.
            // Error messages remain enabled so a blocked update is actionable.
            arguments.Add("/SILENT");
            // R01-S7-R2-01: /SILENT still shows message boxes, and the
            // installer's direct-install guards only suppress their "multiple
            // installations" / "/DIR mismatch" MsgBox when /SUPPRESSMSGBOXES
            // is present. Without it an unattended update blocks on a modal
            // box until the 30-minute installer timeout kills a half install.
            arguments.Add("/SUPPRESSMSGBOXES");
            arguments.Add("/SP-");
            arguments.Add("/NORESTART");
            arguments.Add("/FORCECLOSEAPPLICATIONS");
            // /LANG is deliberately silent-only: a matching /LANG suppresses
            // Inno's Select Language dialog even in interactive runs, which
            // would take the language choice away from a user running Setup
            // manually. In a silent run it pins the language to what the app
            // is currently showing and overrides any stale
            // "Inno Setup: Language" value left by an old install.
            if (!string.IsNullOrWhiteSpace(installerLanguage))
            {
                arguments.Add($"/LANG={installerLanguage}");
            }
        }

        return arguments;
    }

    internal static string ResolveInstallerLanguage(string? value) =>
        !string.IsNullOrWhiteSpace(value) && KnownInstallerLanguages.Contains(value)
            ? value.ToLowerInvariant()
            : string.Empty;

    internal static DirectInstallScope ResolveInstallScope(string installDirectory)
    {
        if (RegisteredLocationMatches(Registry.LocalMachine, InstallStateRegistryPath, installDirectory) ||
            RegisteredLocationMatches(Registry.LocalMachine, UninstallRegistryPath, installDirectory) ||
            RegisteredLocationMatches(Registry.LocalMachine, WowUninstallRegistryPath, installDirectory))
        {
            return DirectInstallScope.AllUsers;
        }

        if (RegisteredLocationMatches(Registry.CurrentUser, InstallStateRegistryPath, installDirectory) ||
            RegisteredLocationMatches(Registry.CurrentUser, UninstallRegistryPath, installDirectory) ||
            RegisteredLocationMatches(Registry.CurrentUser, WowUninstallRegistryPath, installDirectory))
        {
            return DirectInstallScope.CurrentUser;
        }

        // Compatibility fallback for older machine-wide builds that predate the
        // explicit InstallScope registration.
        if (IsDirectoryWithinKnownFolder(
                installDirectory,
                Environment.SpecialFolder.ProgramFiles) ||
            IsDirectoryWithinKnownFolder(
                installDirectory,
                Environment.SpecialFolder.ProgramFilesX86))
        {
            return DirectInstallScope.AllUsers;
        }

        return DirectInstallScope.CurrentUser;
    }

    private static bool IsInstallRegistrationConsistent(
        string expectedInstallDirectory,
        DirectInstallScope installScope)
    {
        try
        {
            RegistryKey root = installScope == DirectInstallScope.AllUsers
                ? Registry.LocalMachine
                : Registry.CurrentUser;
            using RegistryKey? key = root.OpenSubKey(InstallStateRegistryPath, writable: false);
            string? registeredPath = key?.GetValue("InstallLocation") as string;
            string? registeredScope = key?.GetValue("InstallScope") as string;
            if (string.IsNullOrWhiteSpace(registeredPath))
            {
                return false;
            }

            string expectedScope = installScope == DirectInstallScope.AllUsers
                ? "all-users"
                : "current-user";
            return PathsEqual(registeredPath, expectedInstallDirectory) &&
                   string.Equals(
                       registeredScope?.Trim(),
                       expectedScope,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            Log($"Install registration verification failed: {ex.Message}");
            return false;
        }
    }

    private static bool RegisteredLocationMatches(
        RegistryKey root,
        string registryPath,
        string expectedInstallDirectory)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(registryPath, writable: false);
            return key?.GetValue("InstallLocation") is string registeredPath &&
                   PathsEqual(registeredPath, expectedInstallDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsDirectoryWithinKnownFolder(
        string directory,
        Environment.SpecialFolder specialFolder)
    {
        string knownFolder = Environment.GetFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(knownFolder))
        {
            return false;
        }

        try
        {
            string normalizedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string normalizedKnownFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(knownFolder));
            return string.Equals(
                       normalizedDirectory,
                       normalizedKnownFolder,
                       StringComparison.OrdinalIgnoreCase) ||
                   normalizedDirectory.StartsWith(
                       normalizedKnownFolder + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryGetInstallDirectory(UpdateOptions options, out string installDirectory)
    {
        installDirectory = string.Empty;
        string candidate = options.InstallDirectory;
        if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(options.AppPath))
        {
            candidate = Path.GetDirectoryName(options.AppPath) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            string normalizedDirectory = Path.GetFullPath(candidate);
            string expectedAppPath = Path.Combine(normalizedDirectory, "DeskBox.exe");
            if (!Directory.Exists(normalizedDirectory) || !File.Exists(expectedAppPath))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.AppPath) &&
                !string.Equals(
                    Path.GetFullPath(options.AppPath),
                    expectedAppPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            installDirectory = normalizedDirectory;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static void RestartAfterIncompleteUpdate(UpdateOptions options, string outcome)
    {
        if (string.IsNullOrWhiteSpace(options.AppPath) || !File.Exists(options.AppPath))
        {
            Log($"Cannot restart DeskBox after incomplete update ({outcome}); app executable is missing.");
            return;
        }

        _ = RestartApp(options.AppPath, outcome);
    }

    internal static string GetIncompleteUpdateOutcome(int exitCode)
    {
        if (exitCode is 2 or 5)
        {
            return "cancelled";
        }

        return exitCode == InstallRegistrationMismatchExitCode
            ? "path-mismatch"
            : "failed";
    }

    private static bool RestartApp(string appPath, string? updateInstallOutcome = null)
    {
        // An elevated updater (it inherits the app's token, and app-update
        // flows on fully elevated desktops run elevated) must not hand that
        // elevation to the restarted app: a post-update DeskBox above the
        // shell loses drag/drop and triggers the elevation notice on every
        // update. But the hand-down is only safe when the shell itself
        // offers a standard-user context — on a fully elevated desktop
        // matching the shell is what keeps tray and drag working. That
        // guard, and the failure fallback to the historical inherited
        // restart, live in ShellUnelevatedLaunch.
        if (DeskBox.UpdaterPlatform.ShellUnelevatedLaunch.ShouldRestartAtShellIntegrity())
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (DeskBox.UpdaterPlatform.ShellUnelevatedLaunch.TryStartAtShellIntegrity(
                        appPath,
                        BuildRestartArguments(updateInstallOutcome),
                        Log))
                {
                    Log($"Restarting app at shell integrity: {appPath} (attempt {attempt})");
                    return true;
                }

                if (attempt < 3)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                }
            }

            Log("Shell-integrity restart attempts failed; falling back to inherited restart.");
            // If the fallback lands elevated under a Medium shell, the
            // app-side launch gate surfaces its one-click fix notice.
        }

        // A direct start can fail transiently (the installer tail still
        // holding a handle, an on-access scanner touching the fresh
        // executable), so retry before falling back to the Start Menu
        // shortcut, which resolves through a different shell path. If
        // everything fails the user must launch manually; the ERROR tag
        // keeps that outcome searchable in the log.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (TryStartApp(appPath, Path.GetDirectoryName(appPath), updateInstallOutcome))
            {
                Log($"Restarting app: {appPath} (attempt {attempt})");
                return true;
            }

            if (attempt < 3)
            {
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }
        }

        return TryRestartViaStartMenuShortcut(appPath, updateInstallOutcome);
    }

    private static string? BuildRestartArguments(string? updateInstallOutcome)
    {
        return string.IsNullOrWhiteSpace(updateInstallOutcome)
            ? null
            : $"{UpdateInstallResultArgument} {updateInstallOutcome.Trim()}";
    }

    private static bool TryStartApp(
        string targetPath,
        string? workingDirectory,
        string? updateInstallOutcome)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
            };
            if (!string.IsNullOrWhiteSpace(updateInstallOutcome))
            {
                startInfo.ArgumentList.Add(UpdateInstallResultArgument);
                startInfo.ArgumentList.Add(updateInstallOutcome);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Starting '{targetPath}' failed: {ex.Message}");
            return false;
        }
    }

    private static bool TryRestartViaStartMenuShortcut(string appPath, string? updateInstallOutcome)
    {
        // {autoprograms} resolves per install scope: the user's Programs
        // folder for current-user installs, the common one for all-users
        // installs (see the [Icons] section in installer/DeskBox.iss). Try
        // both locations; the one Setup wrote is the one that exists.
        string? userPrograms = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        string? commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        foreach (string menuRoot in new[] { userPrograms, commonPrograms })
        {
            if (string.IsNullOrWhiteSpace(menuRoot))
            {
                continue;
            }

            string shortcutPath = Path.Combine(menuRoot, "Programs", "DeskBox.lnk");
            if (!File.Exists(shortcutPath))
            {
                continue;
            }

            if (TryStartApp(shortcutPath, workingDirectory: null, updateInstallOutcome))
            {
                Log($"Restarted app via Start Menu shortcut: {shortcutPath}");
                return true;
            }
        }

        Log($"[ERROR] Could not restart DeskBox after update; user launch required. Path: {appPath}");
        return false;
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            // Two-file rotation keeps a runaway update loop from growing the
            // log without bound; 1 MiB per file is years of normal traffic.
            FileInfo logFile = new(LogPath);
            if (logFile.Exists && logFile.Length > 1024 * 1024)
            {
                try
                {
                    File.Delete(LogPath + ".old");
                    logFile.MoveTo(LogPath + ".old");
                }
                catch (IOException)
                {
                }
            }

            File.AppendAllText(
                LogPath,
                string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}"));
        }
        catch
        {
        }
    }

    private sealed class UpdateOptions
    {
        public int ParentProcessId { get; private init; }
        public string InstallerPath { get; private init; } = string.Empty;
        public string InstallerSha256 { get; private init; } = string.Empty;
        public string AppPath { get; private init; } = string.Empty;
        public string InstallDirectory { get; private init; } = string.Empty;
        public string InstallerLanguage { get; private init; } = string.Empty;
        public bool Silent { get; private init; }
        public bool RestartOnly { get; private init; }

        public static UpdateOptions Parse(IReadOnlyList<string> args)
        {
            int parentProcessId = 0;
            string installerPath = string.Empty;
            string installerSha256 = string.Empty;
            string appPath = string.Empty;
            string installDirectory = string.Empty;
            string installerLanguage = string.Empty;
            bool silent = false;
            bool restartOnly = false;

            for (int index = 0; index < args.Count; index++)
            {
                string arg = args[index];
                if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase))
                {
                    silent = true;
                    continue;
                }

                if (string.Equals(arg, "--restart-only", StringComparison.OrdinalIgnoreCase))
                {
                    restartOnly = true;
                    continue;
                }

                if (index + 1 >= args.Count)
                {
                    continue;
                }

                string value = args[++index];
                if (string.Equals(arg, "--pid", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPid))
                {
                    parentProcessId = parsedPid;
                }
                else if (string.Equals(arg, "--installer", StringComparison.OrdinalIgnoreCase))
                {
                    installerPath = value;
                }
                else if (string.Equals(arg, "--installer-sha256", StringComparison.OrdinalIgnoreCase))
                {
                    installerSha256 = value;
                }
                else if (string.Equals(arg, "--app", StringComparison.OrdinalIgnoreCase))
                {
                    appPath = value;
                }
                else if (string.Equals(arg, "--install-dir", StringComparison.OrdinalIgnoreCase))
                {
                    installDirectory = value;
                }
                else if (string.Equals(arg, "--installer-lang", StringComparison.OrdinalIgnoreCase))
                {
                    installerLanguage = value;
                }
            }

            return new UpdateOptions
            {
                ParentProcessId = parentProcessId,
                InstallerPath = installerPath,
                InstallerSha256 = installerSha256,
                AppPath = appPath,
                InstallDirectory = installDirectory,
                InstallerLanguage = installerLanguage,
                Silent = silent,
                RestartOnly = restartOnly
            };
        }
    }
}
