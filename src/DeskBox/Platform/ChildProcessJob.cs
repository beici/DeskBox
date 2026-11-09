using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskBox.Platform;

/// <summary>
/// Assigns helper child processes to a single app-lifetime job object so a
/// crashed host cannot leave them running as orphans (R1-050). Today the
/// only assigned children are the two thumbnail-proxy launches in
/// ShellThumbnailProxy; the right-click menu proxy and user applications
/// launched via its INVOKE path are intentionally NOT assigned (they must
/// survive a host crash). The job handle is never closed while the process
/// is alive; when the process dies the kernel closes the handle and kills
/// every still-assigned child.
/// </summary>
internal static class ChildProcessJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const uint JOB_OBJECT_LIMIT_BREAKAWAY_OK = 0x0800;

    private static IntPtr s_jobHandle;
    private static readonly object Gate = new();

    /// <summary>
    /// Creates the shared job on first use (KILL_ON_JOB_CLOSE so children die
    /// with the host, BREAKAWAY_OK so a child that explicitly breaks away —
    /// e.g. an elevated relaunch — is not trapped) and assigns the process.
    /// Failures are logged by the caller's tolerance: assignment is best
    /// effort; an unassigned child merely keeps today's orphan behavior.
    /// </summary>
    public static bool TryAssignToAppLifetimeJob(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        lock (Gate)
        {
            try
            {
                if (s_jobHandle == IntPtr.Zero)
                {
                    s_jobHandle = CreateJobObjectW(IntPtr.Zero, null);
                    if (s_jobHandle == IntPtr.Zero)
                    {
                        return false;
                    }

                    var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                    {
                        BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                        {
                            LimitFlags =
                                JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                                JOB_OBJECT_LIMIT_BREAKAWAY_OK
                        }
                    };

                    if (!SetInformationJobObject(
                            s_jobHandle,
                            JobObjectExtendedLimitInformation,
                            ref limits,
                            (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                    {
                        // Keep the harmless empty job rather than leaking a
                        // fresh handle per call; assignment below still
                        // isolates nothing extra, matching old behavior.
                        return false;
                    }
                }

                return AssignProcessToJobObject(s_jobHandle, process.Handle);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInformationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
