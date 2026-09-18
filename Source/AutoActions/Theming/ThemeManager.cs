using AutoActions.Windows;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AutoActions.Theming
{
    /// <summary>
    /// Swaps the colour token dictionary (Theming/0_LightColors.xaml or 0_DarkColors.xaml) in
    /// Application.Resources at runtime. Every style references the tokens through DynamicResource,
    /// so the whole UI follows the swap live. Replaces the never-wired ThemeResourceDirectory, which
    /// derived from the WinRT ResourceDictionary instead of the WPF one.
    /// </summary>
    public static class ThemeManager
    {
        // Built from this assembly's own name: the exe is arzGUI.exe now, and a relative pack uri
        // would resolve against whichever assembly happens to be the entry point.
        static readonly string Component =
            $"pack://application:,,,/{typeof(ThemeManager).Assembly.GetName().Name};component/";

        static readonly Uri LightSource = new Uri(Component + "Theming/0_LightColors.xaml", UriKind.Absolute);
        static readonly Uri DarkSource = new Uri(Component + "Theming/0_DarkColors.xaml", UriKind.Absolute);

        static ResourceDictionary _current;
        static Func<ThemeSetting> _settingProvider;
        static bool _followingSystem;

        public static Theme Current { get; private set; } = Theme.Light;

        public static event EventHandler ThemeChanged;

        public static Theme Resolve(ThemeSetting setting)
        {
            switch (setting)
            {
                case ThemeSetting.Light:
                    return Theme.Light;
                case ThemeSetting.Dark:
                    return Theme.Dark;
                default:
                    return UI.GetWindowsTheme() == WindowsTheme.Dark ? Theme.Dark : Theme.Light;
            }
        }

        public static void Apply(ThemeSetting setting)
        {
            Apply(Resolve(setting));
        }

        public static void Apply(Theme theme)
        {
            Application app = Application.Current;
            if (app == null)
                return;
            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.Invoke(() => Apply(theme));
                return;
            }

            ResourceDictionary dictionary = new ResourceDictionary { Source = theme == Theme.Dark ? DarkSource : LightSource };
            var merged = app.Resources.MergedDictionaries;
            if (_current != null)
                merged.Remove(_current);
            merged.Add(dictionary); // last one wins for every shared key
            _current = dictionary;
            Current = theme;
            App.Theme = theme;

            foreach (Window window in app.Windows)
                ApplyTitleBar(window);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Re-applies the theme when Windows' app mode changes while the setting is System, and
        /// paints the title bar of every window (including dialogs opened later) to match.
        /// </summary>
        public static void FollowSystem(Func<ThemeSetting> settingProvider)
        {
            _settingProvider = settingProvider;
            if (_followingSystem)
                return;
            _followingSystem = true;
            SystemEvents.UserPreferenceChanged += (o, e) =>
            {
                if (e.Category != UserPreferenceCategory.General || _settingProvider == null)
                    return;
                if (_settingProvider() == ThemeSetting.System)
                    Apply(ThemeSetting.System);
            };
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((o, e) => ApplyTitleBar(o as Window)));
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int DWMWCP_ROUND = 2;
        const int DWMSBT_TRANSIENTWINDOW = 3;

        /// <summary>Uses Windows 11 Desktop Acrylic behind the WPF client area.</summary>
        public static void ApplyDesktopAcrylic(Window window)
        {
            if (window == null)
                return;
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                    return;
                HwndSource source = HwndSource.FromHwnd(handle);
                if (source != null && source.CompositionTarget != null)
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                int backdrop = DWMSBT_TRANSIENTWINDOW;
                int corners = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            }
            catch
            {
            }
        }

        /// <summary>Dark title bar on Windows 10 20H1+/11; silently ignored elsewhere.</summary>
        static void ApplyTitleBar(Window window)
        {
            if (window == null)
                return;
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                    return;
                int dark = Current == Theme.Dark ? 1 : 0;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch
            {
            }
        }
    }
}
