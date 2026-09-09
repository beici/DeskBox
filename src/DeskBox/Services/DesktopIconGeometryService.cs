// Copyright (c) DeskBox. All rights reserved.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeskBox.Helpers;
using Windows.Graphics;

namespace DeskBox.Services;

/// <summary>
/// Reads the screen rectangles of the desktop icons Explorer is showing.
/// <para>
/// Margins are supposed to describe the gap to whatever is actually next to a
/// widget, and on a desktop that is usually an icon or a folder rather than
/// another widget or the screen edge. LVM_GETITEMRECT carries a process-local
/// RECT pointer, so - exactly like the blank-desktop hit test - the buffer has
/// to live inside Explorer and be read back with ReadProcessMemory.
/// </para>
/// <para>
/// Results are cached briefly: icon positions only change when the user moves
/// an icon or the shell re-arranges them, while a margin dialog re-reads the
/// reference geometry on every keystroke.
/// </para>
/// </summary>
internal static partial class DesktopIconGeometryService
{
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint ListViewGetItemCount = 0x1000 + 4;
    private const uint ListViewGetItemRect = 0x1000 + 14;
    private const uint SendMessageAbortIfHung = 0x0002;
    private const uint SendMessageTimeoutMs = 120;

    /// <summary>LVIR_BOUNDS: icon glyph plus its label.</summary>
    private const int ListViewItemBounds = 0;

    /// <summary>
    /// Upper bound on the icons inspected in one pass. A desktop with more
    /// items than this is already unusable as a margin reference, and the cap
    /// keeps the cross-process round trips bounded.
    /// </summary>
    internal const int MaximumInspectedIcons = 512;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMilliseconds(1500);
    private static readonly object CacheGate = new();

    private static IReadOnlyList<RectInt32> s_cachedRects = [];
    private static long s_cachedTimestamp;
    private static bool s_hasCachedResult;

    /// <summary>
    /// Desktop icon rectangles in physical screen pixels, or an empty list when
    /// Explorer's desktop view cannot be read. Callers must treat "empty" as
    /// "no icon reference available", never as "no icons exist".
    /// </summary>
    public static IReadOnlyList<RectInt32> GetIconRects()
    {
        lock (CacheGate)
        {
            if (s_hasCachedResult &&
                Stopwatch.GetElapsedTime(s_cachedTimestamp) < CacheLifetime)
            {
                return s_cachedRects;
            }
        }

        IReadOnlyList<RectInt32> rects = ReadIconRects();
        lock (CacheGate)
        {
            s_cachedRects = rects;
            s_cachedTimestamp = Stopwatch.GetTimestamp();
            s_hasCachedResult = true;
        }

        return rects;
    }

    /// <summary>
    /// Drops the cache so the next read reflects icons the user just moved.
    /// </summary>
    public static void Invalidate()
    {
        lock (CacheGate)
        {
            s_hasCachedResult = false;
            s_cachedRects = [];
        }
    }

    private static IReadOnlyList<RectInt32> ReadIconRects()
    {
        IntPtr listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            return [];
        }

        Win32Helper.GetWindowThreadProcessId(listView, out uint processId);
        if (processId == 0)
        {
            return [];
        }

        if (Win32Helper.SendMessageTimeout(
                listView,
                ListViewGetItemCount,
                UIntPtr.Zero,
                IntPtr.Zero,
                SendMessageAbortIfHung,
                SendMessageTimeoutMs,
                out UIntPtr countResult) == IntPtr.Zero)
        {
            return [];
        }

        int count = Math.Min((int)countResult, MaximumInspectedIcons);
        if (count <= 0)
        {
            return [];
        }

        IntPtr process = OpenProcess(
            ProcessVmOperation | ProcessVmRead | ProcessVmWrite,
            false,
            processId);
        if (process == IntPtr.Zero)
        {
            return [];
        }

        int structureSize = Marshal.SizeOf<Win32Helper.RECT>();
        IntPtr localBuffer = IntPtr.Zero;
        IntPtr remoteBuffer = IntPtr.Zero;
        try
        {
            localBuffer = Marshal.AllocHGlobal(structureSize);
            remoteBuffer = VirtualAllocEx(
                process,
                IntPtr.Zero,
                (UIntPtr)structureSize,
                MemCommit | MemReserve,
                PageReadWrite);
            if (remoteBuffer == IntPtr.Zero)
            {
                return [];
            }

            var rects = new List<RectInt32>(count);
            for (int index = 0; index < count; index++)
            {
                if (!TryReadItemRect(
                        listView,
                        process,
                        localBuffer,
                        remoteBuffer,
                        structureSize,
                        index,
                        out RectInt32 rect))
                {
                    continue;
                }

                rects.Add(rect);
            }

            App.LogVerbose(
                $"[DesktopIcons] Icon geometry read items={count} usable={rects.Count} " +
                $"listView=0x{listView.ToInt64():X}");
            return rects;
        }
        catch (Exception ex)
        {
            App.Log($"[DesktopIcons] Icon geometry read failed: {ex.Message}");
            return [];
        }
        finally
        {
            if (remoteBuffer != IntPtr.Zero)
            {
                VirtualFreeEx(process, remoteBuffer, UIntPtr.Zero, MemRelease);
            }

            if (localBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(localBuffer);
            }

            CloseHandle(process);
        }
    }

    private static bool TryReadItemRect(
        IntPtr listView,
        IntPtr process,
        IntPtr localBuffer,
        IntPtr remoteBuffer,
        int structureSize,
        int index,
        out RectInt32 rect)
    {
        rect = default;

        // LVM_GETITEMRECT reads the requested part code out of RECT.left.
        var request = new Win32Helper.RECT { Left = ListViewItemBounds };
        Marshal.StructureToPtr(request, localBuffer, false);
        if (!WriteProcessMemory(
                process,
                remoteBuffer,
                localBuffer,
                (UIntPtr)structureSize,
                out _))
        {
            return false;
        }

        if (Win32Helper.SendMessageTimeout(
                listView,
                ListViewGetItemRect,
                (UIntPtr)(uint)index,
                remoteBuffer,
                SendMessageAbortIfHung,
                SendMessageTimeoutMs,
                out UIntPtr result) == IntPtr.Zero ||
            result == UIntPtr.Zero)
        {
            return false;
        }

        if (!ReadProcessMemory(
                process,
                remoteBuffer,
                localBuffer,
                (UIntPtr)structureSize,
                out _))
        {
            return false;
        }

        Win32Helper.RECT clientRect = Marshal.PtrToStructure<Win32Helper.RECT>(localBuffer);
        int width = clientRect.Right - clientRect.Left;
        int height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var topLeft = new Win32Helper.POINT
        {
            X = clientRect.Left,
            Y = clientRect.Top
        };
        if (!ClientToScreen(listView, ref topLeft))
        {
            return false;
        }

        rect = new RectInt32(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    private static IntPtr FindDesktopListView()
    {
        // One discovery path, shared with the layer service: it enumerates the
        // real parents instead of assuming Progman, and it never forces WorkerW
        // creation (which during login can race Explorer's icon-layout
        // restoration and rearrange the user's icons).
        IntPtr defView = WidgetLayerService.GetDesktopIconViewHandle();
        return defView == IntPtr.Zero
            ? IntPtr.Zero
            : Win32Helper.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(IntPtr hWnd, ref Win32Helper.POINT point);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr VirtualAllocEx(
        IntPtr process,
        IntPtr address,
        UIntPtr size,
        uint allocationType,
        uint protect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualFreeEx(
        IntPtr process,
        IntPtr address,
        UIntPtr size,
        uint freeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        IntPtr buffer,
        UIntPtr size,
        out UIntPtr written);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        IntPtr buffer,
        UIntPtr size,
        out UIntPtr read);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
