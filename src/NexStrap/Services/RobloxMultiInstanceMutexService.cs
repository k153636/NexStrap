using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NexStrap.Services;

public sealed class RobloxMultiInstanceMutexService
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutex(IntPtr lpAttr, bool bInitialOwner, string lpName);
    [DllImport("kernel32.dll")] private static extern bool ReleaseMutex(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(uint infoClass, IntPtr buffer, uint bufferSize, out uint returnLength);
    [DllImport("ntdll.dll")]
    private static extern uint NtDuplicateObject(IntPtr srcProcess, IntPtr srcHandle, IntPtr dstProcess, out IntPtr dstHandle, uint access, uint attrs, uint options);
    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(IntPtr handle, uint objInfoClass, IntPtr buffer, uint bufSize, out uint returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemHandleEntry
    {
        public uint   Pid;
        public byte   ObjectType;
        public byte   Flags;
        public ushort Handle;
        public IntPtr Object;
        public uint   Access;
    }

    private const uint SysHandleInfo       = 16;
    private const uint DupCloseSource      = 0x1;
    private const uint DupSameAccess       = 0x2;
    private const uint ProcDupHandle       = 0x0040;
    private const uint StatusInfoLenMismatch = 0xC0000004;

    private IntPtr _multiInstanceMutex = IntPtr.Zero;

    public void AcquireRobloxSingletonMutex()
    {
        CloseRobloxSingletonMutexHandles();

        if (_multiInstanceMutex != IntPtr.Zero) return;
        _multiInstanceMutex = CreateMutex(IntPtr.Zero, true, "ROBLOX_singletonMutex");
        RobloxService.Log("Multi-instance mutex acquired");
    }

    public void ReleaseRobloxSingletonMutex()
    {
        if (_multiInstanceMutex == IntPtr.Zero) return;
        ReleaseMutex(_multiInstanceMutex);
        CloseHandle(_multiInstanceMutex);
        _multiInstanceMutex = IntPtr.Zero;
        RobloxService.Log("Multi-instance mutex released");
    }

    private void CloseRobloxSingletonMutexHandles()
    {
        var robloxPids = new HashSet<uint>(
            Process.GetProcessesByName("RobloxPlayerBeta")
                   .Concat(Process.GetProcessesByName("RobloxPlayer"))
                   .Where(p => !p.HasExited)
                   .Select(p => (uint)p.Id));

        if (robloxPids.Count == 0) return;

        uint bufSize = 4 * 1024 * 1024;
        IntPtr buf = IntPtr.Zero;
        try
        {
            uint needed;
            uint status;
            do
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                buf = Marshal.AllocHGlobal((int)bufSize);
                status = NtQuerySystemInformation(SysHandleInfo, buf, bufSize, out needed);
                bufSize = Math.Max(needed + 4096, bufSize * 2);
            }
            while (status == StatusInfoLenMismatch);

            if (status != 0) return;

            int count    = Marshal.ReadInt32(buf);
            int entSz    = Marshal.SizeOf<SystemHandleEntry>();
            IntPtr start = IntPtr.Add(buf, sizeof(uint));
            var self     = Process.GetCurrentProcess().Handle;

            for (int i = 0; i < count; i++)
            {
                var e = Marshal.PtrToStructure<SystemHandleEntry>(IntPtr.Add(start, i * entSz));
                if (!robloxPids.Contains(e.Pid)) continue;

                IntPtr robloxProc = OpenProcess(ProcDupHandle, false, e.Pid);
                if (robloxProc == IntPtr.Zero) continue;
                try
                {
                    IntPtr dup;
                    if (NtDuplicateObject(robloxProc, (IntPtr)e.Handle, self, out dup, 0, 0, DupSameAccess) != 0)
                        continue;
                    try
                    {
                        if (!IsHandleNamedMutex(dup, "ROBLOX_singletonMutex")) continue;
                        NtDuplicateObject(robloxProc, (IntPtr)e.Handle, IntPtr.Zero, out _, 0, 0, DupCloseSource);
                        RobloxService.Log($"Closed ROBLOX_singletonMutex handle in Roblox PID {e.Pid}");
                    }
                    finally { CloseHandle(dup); }
                }
                finally { CloseHandle(robloxProc); }
            }
        }
        catch (Exception ex) { RobloxService.Log($"CloseRobloxSingletonMutexHandles: {ex.Message}"); }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
    }

    private static bool IsHandleNamedMutex(IntPtr handle, string targetName)
    {
        const int bufSize = 1024;
        IntPtr buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            uint ret;
            if (NtQueryObject(handle, 1 /*ObjectNameInformation*/, buf, bufSize, out ret) != 0)
                return false;
            // OBJECT_NAME_INFORMATION: UNICODE_STRING (Length, MaxLength, Buffer*)
            ushort len = (ushort)Marshal.ReadInt16(buf);
            if (len == 0) return false;
            IntPtr strPtr = Marshal.ReadIntPtr(IntPtr.Add(buf, IntPtr.Size == 8 ? 8 : 4));
            string name = Marshal.PtrToStringUni(strPtr, len / 2);
            return name.EndsWith(targetName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
