#if KUGOU_WINDOWS
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

internal static class WindowsAppIdentity
{
    private const string AppUserModelId = "KugouAvaloniaPlayer";
    private const string ShortcutFileName = "KugouAvaloniaPlayer.lnk";
    private const string DisplayName = "KA Music";

    public static void Register()
    {
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            EnsureStartMenuShortcut();
        }
        catch
        {
            // App identity only affects Windows shell presentation. Startup should continue if it fails.
        }
    }

    private static void EnsureStartMenuShortcut()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return;

        var programsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        if (string.IsNullOrWhiteSpace(programsDirectory))
            return;

        var shortcutPath = Path.Combine(programsDirectory, "Programs", ShortcutFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellLinkObject = Activator.CreateInstance(typeof(CShellLink));
        if (shellLinkObject is not IShellLinkW shellLink)
            return;

        shellLink.SetPath(executablePath);
        shellLink.SetWorkingDirectory(Path.GetDirectoryName(executablePath));
        shellLink.SetDescription(DisplayName);
        shellLink.SetIconLocation(executablePath, 0);

        var propertyStore = (IPropertyStore)shellLink;
        var appUserModelIdKey = PropertyKeys.AppUserModelId;
        var appId = PropVariant.FromString(AppUserModelId);
        try
        {
            propertyStore.SetValue(ref appUserModelIdKey, ref appId);
            propertyStore.Commit();
        }
        finally
        {
            appId.Dispose();
        }

        ((IPersistFile)shellLink).Save(shortcutPath, true);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class CShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory(IntPtr pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string? pszDir);
        void GetArguments(IntPtr pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(IntPtr pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000138-0000-0000-C000-000000000046")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, IntPtr pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        private readonly Guid _formatId = formatId;
        private readonly uint _propertyId = propertyId;
    }

    private static class PropertyKeys
    {
        public static readonly PropertyKey AppUserModelId = new(
            new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            5);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant : IDisposable
    {
        private ushort _vt;
        private ushort _reserved1;
        private ushort _reserved2;
        private ushort _reserved3;
        private IntPtr _value;
        private IntPtr _value2;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                _vt = 31,
                _value = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public void Dispose()
        {
            if (_value != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_value);
                _value = IntPtr.Zero;
            }

            _vt = 0;
            GC.SuppressFinalize(this);
        }
    }
}
#endif
