using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NexStrap.Services;

internal static class JumpListService
{
    internal const string AppId = "NexStrap.RobloxLauncher";

    [DllImport("shell32.dll")]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    public static void Initialize()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            var exe = Environment.ProcessPath ?? string.Empty;
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
            key.SetValue("DisplayName", "NexStrap");
            if (!string.IsNullOrEmpty(exe)) key.SetValue("IconUri", exe);
        }
        catch { }
    }

    public static void Update(IEnumerable<(long PlaceId, string Name)> favorites)
    {
        try
        {
            var destList = (ICustomDestinationList)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6"))!)!;
            var collection = (IObjectCollection)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A"))!)!;

            var exe       = Environment.ProcessPath ?? string.Empty;
            var iObjArray = new Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");

            destList.SetAppID(AppId);
            destList.BeginList(out var maxSlots, ref iObjArray, out _);

            var items = favorites.Take((int)maxSlots).ToList();
            foreach (var (placeId, name) in items)
            {
                var link = (IShellLinkW)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
                link.SetPath(exe);
                link.SetArguments($"--launch-game {placeId}");
                link.SetDescription(name);
                link.SetIconLocation(exe, 0);

                var ps   = (IPropertyStore)link;
                var pkey = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
                var pv   = new PropVariant(name);
                ps.SetValue(ref pkey, ref pv);
                ps.Commit();
                pv.Dispose();

                collection.AddObject(link);
                Marshal.ReleaseComObject(link);
            }

            if (items.Count > 0)
                destList.AddUserTasks((IObjectArray)collection);

            destList.CommitList();
            Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(destList);
        }
        catch (Exception ex)
        {
            try
            {
                var log = Path.Combine(Path.GetTempPath(), "nexstrap_jumplist.log");
                File.AppendAllText(log, $"[{DateTime.Now:HH:mm:ss}] {ex}\n");
            }
            catch { }
        }
    }

    // ── COM Interfaces ──────────────────────────────────────────────────────

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint cMaxSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void AppendKnownCategory(int category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint cObjects);
        void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection : IObjectArray
    {
        new void GetCount(out uint cObjects);
        new void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object pvObject);
        void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder psz, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string psz);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder psz, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string psz);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder psz, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string psz);
        void GetHotkey(out short pw);
        void SetHotkey(short w);
        void GetShowCmd(out int pi);
        void SetShowCmd(int i);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder psz, int cch, out int pi);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string psz, int i);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string psz, uint dw);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string psz);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId  = formatId;
        public int  PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant : IDisposable
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pszVal;

        public PropVariant(string value)
        {
            vt     = 31; // VT_LPWSTR
            pszVal = Marshal.StringToCoTaskMemUni(value);
        }

        public void Dispose()
        {
            if (vt == 31 && pszVal != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pszVal);
                pszVal = IntPtr.Zero;
            }
        }
    }
}
