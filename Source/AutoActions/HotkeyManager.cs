using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;

namespace AutoActions
{
    /// <summary>
    /// System-wide hotkeys, so an action shortcut can be fired from inside a fullscreen game.
    ///
    /// RegisterHotKey binds to the thread that owns the window, so everything here runs on the WPF
    /// dispatcher thread; the handler itself is pushed onto the thread pool, because an action can
    /// take a second and the UI must not sit still for it.
    /// </summary>
    public class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        /// <summary>Without this a held key repeats the action at the keyboard repeat rate.</summary>
        private const uint MOD_NOREPEAT = 0x4000;

        private readonly Dictionary<int, Action> _handlers = new Dictionary<int, Action>();
        private readonly object _lock = new object();
        private HwndSource _source;
        private int _nextId = 1;

        public event EventHandler<string> NewLog;

        private void Log(string message)
        {
            try { NewLog?.Invoke(this, message); } catch { }
        }

        /// <summary>Creates the message-only window that receives WM_HOTKEY. UI thread only.</summary>
        public void Initialize()
        {
            if (_source != null)
                return;
            HwndSourceParameters parameters = new HwndSourceParameters("ArzFlowHotkeys")
            {
                // HWND_MESSAGE: a window that exists only to receive messages.
                ParentWindow = new IntPtr(-3),
                WindowStyle = 0
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY)
                return IntPtr.Zero;
            Action handler;
            lock (_lock)
            {
                if (!_handlers.TryGetValue(wParam.ToInt32(), out handler))
                    return IntPtr.Zero;
            }
            handled = true;
            Task.Run(() =>
            {
                try { handler(); }
                catch (Exception ex) { Log($"Hotkey action failed: {ex}"); }
            });
            return IntPtr.Zero;
        }

        /// <summary>
        /// Binds one combination. False means the combination is unusable - most often another
        /// program already owns it, which Windows reports no more specifically than a failure.
        /// </summary>
        public bool Register(string hotkey, Action handler)
        {
            uint modifiers;
            uint virtualKey;
            if (!TryParse(hotkey, out modifiers, out virtualKey))
                return false;
            if (_source == null)
                Initialize();

            int id;
            lock (_lock)
                id = _nextId++;
            if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | MOD_NOREPEAT, virtualKey))
            {
                Log($"Hotkey {hotkey} could not be registered; another program is probably using it.");
                return false;
            }
            lock (_lock)
                _handlers[id] = handler;
            return true;
        }

        public void UnregisterAll()
        {
            if (_source == null)
                return;
            List<int> ids;
            lock (_lock)
            {
                ids = _handlers.Keys.ToList();
                _handlers.Clear();
            }
            foreach (int id in ids)
                NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        public void Dispose()
        {
            UnregisterAll();
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source.Dispose();
                _source = null;
            }
        }

        #region Parsing and formatting

        /// <summary>
        /// "Ctrl+Alt+G" and friends. Format() produces exactly what TryParse() accepts, so a stored
        /// hotkey round-trips through the settings file unchanged.
        /// </summary>
        public static bool TryParse(string hotkey, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(hotkey))
                return false;

            string[] parts = hotkey.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                    return false;
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= MOD_CONTROL;
                        continue;
                    case "ALT":
                        modifiers |= MOD_ALT;
                        continue;
                    case "SHIFT":
                        modifiers |= MOD_SHIFT;
                        continue;
                    case "WIN":
                    case "WINDOWS":
                        modifiers |= MOD_WIN;
                        continue;
                }
                Key key;
                if (!Enum.TryParse(part, true, out key) || key == Key.None)
                    return false;
                virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            }
            // A bare key would swallow that key system-wide; a modifier is required.
            return virtualKey != 0 && modifiers != 0;
        }

        /// <summary>Builds the stored form from a key press. Null when the press is only modifiers.</summary>
        public static string Format(ModifierKeys modifiers, Key key)
        {
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin
                || key == Key.System || key == Key.None)
                return null;
            if (modifiers == ModifierKeys.None)
                return null;

            List<string> parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
                parts.Add("Win");
            parts.Add(key.ToString(CultureInfo.InvariantCulture));
            return string.Join("+", parts);
        }

        #endregion

        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }
    }
}
