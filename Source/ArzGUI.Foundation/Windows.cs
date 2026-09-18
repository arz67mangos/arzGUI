using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CodectoryCore.Windows
{
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Activate(string name, string path)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                key.SetValue(name, "\"" + path + "\"");
        }

        public static void Deactivate(string name, string path)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                key.DeleteValue(name, false);
        }
    }

    public static class Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr handle, int command);

        public static void BringMainWindowToFront(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0 || processes[0].MainWindowHandle == IntPtr.Zero)
                return;
            ShowWindowAsync(processes[0].MainWindowHandle, 9);
            SetForegroundWindow(processes[0].MainWindowHandle);
        }
    }
}

namespace CodectoryCore.Windows.Icons
{
    public static class IconHelper
    {
        public static Bitmap GetFileIcon(string path)
        {
            using (Icon icon = Icon.ExtractAssociatedIcon(path))
                return icon == null ? null : icon.ToBitmap();
        }
    }
}
