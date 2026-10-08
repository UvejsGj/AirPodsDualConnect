// Windows calls the tray makes itself. All are user-mode and need no admin rights:
//  - RegisterHotKey / UnregisterHotKey (user32) for the global hotkeys
//  - Core Audio's IMMNotificationClient, read-only, so the icon updates when an audio endpoint changes
//  - IShellLinkW, to write or delete one shortcut in your own Startup folder (no registry change)
// Connecting and disconnecting the AirPods is left entirely to DualConnect from stage1-tools.
// Untested on Windows. C# 5.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;

namespace DualConnectTray
{
    internal static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessDPIAware();
    }

    // ------------------------------------------------------------------
    // A message-only window that receives WM_HOTKEY.
    // ------------------------------------------------------------------
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        private readonly System.Collections.Generic.List<int> registered = new System.Collections.Generic.List<int>();

        public event Action<int> HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        }

        // Returns null on success, otherwise a sentence for the user.
        public string Register(int id, Hotkey hotkey)
        {
            if (NativeMethods.RegisterHotKey(Handle, id, hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, hotkey.VirtualKey))
            {
                registered.Add(id);
                return null;
            }
            int err = Marshal.GetLastWin32Error();
            if (err == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED)
                return hotkey.Display + " is already used by Windows or another app.";
            return hotkey.Display + " could not be registered (Windows error " + err + ").";
        }

        public void UnregisterAll()
        {
            foreach (int id in registered) NativeMethods.UnregisterHotKey(Handle, id);
            registered.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                Action<int> handler = HotkeyPressed;
                if (handler != null) handler(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
        }
    }

    // ------------------------------------------------------------------
    // Core Audio endpoint notifications (read-only). Windows calls these on its own
    // worker thread; the tray only posts a "something changed" signal to its UI thread.
    // Definitions follow mmdeviceapi.h; method order matters.
    // ------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        // Only the last two are called; the first three hold their places in the vtable.
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorCoClass
    {
    }

    internal sealed class AudioEndpointWatcher : IDisposable
    {
        private IMMDeviceEnumerator enumerator;
        private EndpointNotificationSink sink;

        // Raised on a Windows worker thread with the endpoint id ("" for default-device changes).
        public event Action<string, string> EndpointChanged;

        // Returns null on success, otherwise why notifications are unavailable.
        public string Start()
        {
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
                sink = new EndpointNotificationSink(Raise);
                int hr = enumerator.RegisterEndpointNotificationCallback(sink);
                if (hr != 0)
                {
                    Release();
                    return "Audio change notifications unavailable (HRESULT 0x" + hr.ToString("X8") + ").";
                }
                return null;
            }
            catch (Exception ex)
            {
                Release();
                return "Audio change notifications unavailable: " + ex.Message;
            }
        }

        private void Raise(string what, string deviceId)
        {
            Action<string, string> handler = EndpointChanged;
            if (handler != null) handler(what, deviceId ?? "");
        }

        public void Dispose()
        {
            if (enumerator != null && sink != null)
            {
                try { enumerator.UnregisterEndpointNotificationCallback(sink); } catch (Exception) { }
            }
            Release();
        }

        private void Release()
        {
            if (enumerator != null)
            {
                try { Marshal.ReleaseComObject(enumerator); } catch (Exception) { }
            }
            enumerator = null;
            sink = null;
        }
    }

    // The callback object Windows holds. Public and COM-visible so Windows can query it for
    // IMMNotificationClient.
    [ComVisible(true)]
    public sealed class EndpointNotificationSink : IMMNotificationClient
    {
        private readonly Action<string, string> raise;

        internal EndpointNotificationSink(Action<string, string> raise) { this.raise = raise; }

        // Never call back into Core Audio from here: Microsoft documents that as a deadlock risk.
        public void OnDeviceStateChanged(string deviceId, int newState) { raise("state", deviceId); }
        public void OnDeviceAdded(string deviceId) { raise("added", deviceId); }
        public void OnDeviceRemoved(string deviceId) { raise("removed", deviceId); }
        public void OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId) { raise("default", defaultDeviceId); }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }

    // ------------------------------------------------------------------
    // "Start with Windows": a shortcut in your own Startup folder
    // (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup). No registry value is written.
    // Undo: untick the menu item, or delete the shortcut from that folder.
    // ------------------------------------------------------------------
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkCoClass
    {
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    internal static class StartupShortcut
    {
        public const string ShortcutName = "DualConnect Tray.lnk";

        public static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName); }
        }

        public static bool Exists
        {
            get { return File.Exists(ShortcutPath); }
        }

        // Reads where an existing shortcut points, or null.
        public static string Target()
        {
            if (!Exists) return null;
            object link = null;
            try
            {
                link = new ShellLinkCoClass();
                ((IPersistFile)link).Load(ShortcutPath, 0);
                var sb = new StringBuilder(1024);
                ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (link != null) Marshal.ReleaseComObject(link);
            }
        }

        public static void Create(string exePath)
        {
            object link = new ShellLinkCoClass();
            try
            {
                var shell = (IShellLinkW)link;
                shell.SetPath(exePath);
                shell.SetWorkingDirectory(Path.GetDirectoryName(exePath));
                shell.SetDescription("DualConnect Tray: AirPods switch for this laptop");
                ((IPersistFile)link).Save(ShortcutPath, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        public static void Delete()
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        }
    }
}
