using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NvDisplay = NvAPIWrapper.Display.Display;

namespace AutoActions.Displays
{
    /// <summary>
    /// Per-display colour controls, behind one managed API. Two unrelated mechanisms live here:
    ///
    /// * Digital vibrance and hue are NVIDIA-only and go through NVAPI (NvAPIWrapper). There is no
    ///   equivalent on other vendors, so everything below is a logged no-op off NVIDIA.
    /// * Brightness, contrast and gamma are NOT in NVAPI - NVIDIA's own sliders write the Windows GDI
    ///   gamma ramp, and so do we, through Get/SetDeviceGammaRamp on a DC for the display.
    ///
    /// Nothing here throws: an unsupported display, a missing driver or a ramp Windows refuses is a
    /// no-op plus a NewLog line, because this runs on the process watcher thread inside the daemon lock.
    /// </summary>
    public static class DisplayColorControl
    {
        /// <summary>Ramp entries per channel; a GDI ramp is 3 * 256 unsigned shorts.</summary>
        public const int RampEntries = 256;

        public const double MinimumGamma = 0.4;
        public const double MaximumGamma = 2.8;

        public static event EventHandler<string> NewLog;

        private static void Log(string message)
        {
            try { NewLog?.Invoke(null, message); } catch { }
        }

        /// <summary>
        /// What this class last wrote to each display, keyed by display UID. Windows throws the gamma
        /// ramp away on a mode change, on resume and when an exclusive-fullscreen application takes
        /// over, and it never says so - <see cref="ReapplyLast"/> puts it back.
        /// </summary>
        private static readonly Dictionary<uint, AppliedColor> _lastApplied = new Dictionary<uint, AppliedColor>();
        private static readonly object _lastAppliedLock = new object();

        private class AppliedColor
        {
            public double? Vibrance;
            public int? Hue;
            public double? Brightness;
            public double? Contrast;
            public double? Gamma;

            public override string ToString()
            {
                List<string> parts = new List<string>();
                if (Vibrance.HasValue)
                    parts.Add($"vibrance {Vibrance.Value:0.00}");
                if (Hue.HasValue)
                    parts.Add($"hue {Hue.Value}");
                if (Gamma.HasValue)
                    parts.Add($"b/c/g {Brightness.Value:0.00}/{Contrast.Value:0.00}/{Gamma.Value:0.00}");
                return string.Join(", ", parts);
            }
        }

        private static void Remember(Display display, Action<AppliedColor> update)
        {
            if (display == null)
                return;
            lock (_lastAppliedLock)
            {
                AppliedColor applied;
                if (!_lastApplied.TryGetValue(display.UID, out applied))
                {
                    applied = new AppliedColor();
                    _lastApplied[display.UID] = applied;
                }
                update(applied);
            }
        }

        /// <summary>Anything worth putting back after Windows has cleared it.</summary>
        public static bool HasAppliedColor
        {
            get { lock (_lastAppliedLock) return _lastApplied.Count > 0; }
        }

        /// <summary>
        /// Writes the last applied settings again. Used after a display mode change, a resume or a
        /// session unlock, any of which silently drop the gamma ramp.
        /// </summary>
        public static bool ReapplyLast()
        {
            Dictionary<uint, AppliedColor> snapshot;
            lock (_lastAppliedLock)
            {
                if (_lastApplied.Count == 0)
                    return false;
                snapshot = _lastApplied.ToDictionary(e => e.Key, e => e.Value);
            }
            List<Display> current = DisplayManagerHandler.Instance.GetActiveMonitors();
            bool reapplied = false;
            foreach (KeyValuePair<uint, AppliedColor> entry in snapshot)
            {
                Display display = current.FirstOrDefault(d => d.UID.Equals(entry.Key));
                if (display == null)
                    continue;
                AppliedColor applied = entry.Value;
                if (applied.Vibrance.HasValue)
                    reapplied |= SetVibrance(display, applied.Vibrance.Value);
                if (applied.Hue.HasValue)
                    reapplied |= SetHue(display, applied.Hue.Value);
                if (applied.Gamma.HasValue)
                    reapplied |= SetGammaRamp(display, applied.Brightness.Value, applied.Contrast.Value, applied.Gamma.Value);
                if (reapplied)
                    Log($"Re-applied colour settings to {display.Name}: {applied}");
            }
            return reapplied;
        }

        /// <summary>Stops the watchdog putting anything back; called when a snapshot is restored.</summary>
        public static void ForgetApplied()
        {
            lock (_lastAppliedLock)
                _lastApplied.Clear();
        }

        #region NVIDIA - digital vibrance and hue

        public static bool VibranceAndHueSupported
        {
            get { return DisplayManagerHandler.Instance.GraphicsCardType == GraphicsCardType.NVIDIA; }
        }

        /// <summary>Digital vibrance, -1..1 with 0 = driver default. Null when it cannot be read.</summary>
        public static double? GetVibrance(Display display)
        {
            NvDisplay nvDisplay = FindNvidiaDisplay(display);
            if (nvDisplay == null)
                return null;
            try
            {
                return nvDisplay.DigitalVibranceControl.NormalizedLevel;
            }
            catch (Exception ex)
            {
                Log($"Could not read digital vibrance for {display.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Digital vibrance, -1..1 with 0 = driver default.</summary>
        public static bool SetVibrance(Display display, double normalizedLevel)
        {
            NvDisplay nvDisplay = FindNvidiaDisplay(display);
            if (nvDisplay == null)
                return false;
            try
            {
                double level = Clamp(normalizedLevel, -1d, 1d);
                nvDisplay.DigitalVibranceControl.NormalizedLevel = level;
                Remember(display, a => a.Vibrance = level);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Could not set digital vibrance for {display.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Hue angle in degrees, 0..359.</summary>
        public static int? GetHue(Display display)
        {
            NvDisplay nvDisplay = FindNvidiaDisplay(display);
            if (nvDisplay == null)
                return null;
            try
            {
                return nvDisplay.HUEControl.CurrentAngle;
            }
            catch (Exception ex)
            {
                Log($"Could not read hue for {display.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Hue angle in degrees, 0..359.</summary>
        public static bool SetHue(Display display, int angle)
        {
            NvDisplay nvDisplay = FindNvidiaDisplay(display);
            if (nvDisplay == null)
                return false;
            try
            {
                int clamped = (int)Clamp(angle, 0, 359);
                nvDisplay.HUEControl.CurrentAngle = clamped;
                Remember(display, a => a.Hue = clamped);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Could not set hue for {display.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Matches one of our displays to an NVAPI one. Both sides name displays with the GDI device
        /// name, so that is the join; the trailing DISPLAYn token is the fallback for drivers that
        /// report the name with a different prefix.
        /// </summary>
        private static NvDisplay FindNvidiaDisplay(Display display)
        {
            if (display == null)
                return null;
            if (!VibranceAndHueSupported)
            {
                Log($"Digital vibrance and hue need an NVIDIA GPU; skipped for {display.Name}.");
                return null;
            }
            try
            {
                NvDisplay[] nvDisplays = NvDisplay.GetDisplays();
                NvDisplay match = nvDisplays.FirstOrDefault(d => string.Equals(d.Name, display.Name, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    string token = DeviceToken(display.Name);
                    match = nvDisplays.FirstOrDefault(d => string.Equals(DeviceToken(d.Name), token, StringComparison.OrdinalIgnoreCase));
                }
                if (match == null)
                    Log($"No NVIDIA display matches {display.Name} (NVAPI reports: {string.Join(", ", nvDisplays.Select(d => d.Name))}).");
                return match;
            }
            catch (Exception ex)
            {
                Log($"Could not enumerate NVIDIA displays: {ex.Message}");
                return null;
            }
        }

        /// <summary>Strips the device-name prefix, leaving the DISPLAYn token.</summary>
        private static string DeviceToken(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;
            int separator = name.LastIndexOf('\\');
            return separator >= 0 ? name.Substring(separator + 1) : name;
        }

        #endregion

        #region GDI gamma ramp - brightness, contrast and gamma

        /// <summary>
        /// Windows validates every ramp handed to SetDeviceGammaRamp and rejects anything far from
        /// linear unless this key allows the full range. Without it, large gamma changes silently
        /// fail - which looks exactly like the feature being broken.
        /// </summary>
        public const string GammaRangeKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
        public const string GammaRangeValueName = "GdiIcmGammaRange";
        public const int GammaRangeUnlockedValue = 256;

        /// <summary>True when the ICM GdiIcmGammaRange value allows the full ramp range.</summary>
        public static bool GammaRangeIsUnlocked
        {
            get
            {
                try
                {
                    // Always the 64-bit view: on the x86 build HKLM\SOFTWARE would otherwise redirect
                    // to Wow6432Node, where this value does not live.
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (RegistryKey key = baseKey.OpenSubKey(GammaRangeKeyPath))
                    {
                        if (key == null)
                            return false;
                        object value = key.GetValue(GammaRangeValueName);
                        return value is int && (int)value == GammaRangeUnlockedValue;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Could not read {GammaRangeValueName}: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>The display's current ramp (768 entries: R, then G, then B), or null.</summary>
        public static ushort[] GetGammaRamp(Display display)
        {
            if (display == null)
                return null;
            IntPtr hdc = CreateDisplayDC(display);
            if (hdc == IntPtr.Zero)
                return null;
            try
            {
                ushort[] ramp = new ushort[RampEntries * 3];
                if (NativeMethods.GetDeviceGammaRamp(hdc, ramp))
                    return ramp;
                Log($"Could not read the gamma ramp of {display.Name}.");
                return null;
            }
            finally
            {
                NativeMethods.DeleteDC(hdc);
            }
        }

        /// <summary>
        /// brightness and contrast are 0..1 with 0.5 neutral, gamma is 0.4..2.8 with 1.0 neutral -
        /// the same curve NVIDIA's "Adjust desktop color settings" sliders produce.
        /// </summary>
        public static bool SetGammaRamp(Display display, double brightness, double contrast, double gamma)
        {
            if (!ApplyRamp(display, BuildRamp(brightness, contrast, gamma), "gamma ramp"))
                return false;
            Remember(display, a =>
            {
                a.Brightness = brightness;
                a.Contrast = contrast;
                a.Gamma = gamma;
            });
            return true;
        }

        /// <summary>
        /// Puts back a ramp captured with GetGammaRamp. Always the exact array that was read - never a
        /// computed neutral one, which would throw away a calibrated monitor profile.
        /// </summary>
        public static bool RestoreGammaRamp(Display display, ushort[] originalRamp)
        {
            if (originalRamp == null || originalRamp.Length != RampEntries * 3)
                return false;
            return ApplyRamp(display, originalRamp, "original gamma ramp");
        }

        private static bool ApplyRamp(Display display, ushort[] ramp, string what)
        {
            if (display == null)
                return false;
            IntPtr hdc = CreateDisplayDC(display);
            if (hdc == IntPtr.Zero)
                return false;
            try
            {
                if (NativeMethods.SetDeviceGammaRamp(hdc, ramp))
                    return true;
                Log(GammaRangeIsUnlocked
                    ? $"Windows rejected the {what} for {display.Name}."
                    : $"Windows rejected the {what} for {display.Name}; {GammaRangeValueName} is not {GammaRangeUnlockedValue}, so ramps far from linear are refused.");
                return false;
            }
            finally
            {
                NativeMethods.DeleteDC(hdc);
            }
        }

        /// <summary>Builds the 768-entry ramp: gamma, then contrast around mid grey, then brightness.</summary>
        public static ushort[] BuildRamp(double brightness, double contrast, double gamma)
        {
            brightness = Clamp(brightness, 0d, 1d);
            contrast = Clamp(contrast, 0d, 1d);
            gamma = Clamp(gamma, MinimumGamma, MaximumGamma);

            double contrastFactor = contrast * 2d;      // 0.5 -> 1.0, unchanged
            double brightnessOffset = brightness - 0.5; // 0.5 -> 0.0, unchanged

            ushort[] ramp = new ushort[RampEntries * 3];
            for (int i = 0; i < RampEntries; i++)
            {
                double value = Math.Pow(i / (double)(RampEntries - 1), 1d / gamma);
                value = (value - 0.5d) * contrastFactor + 0.5d;
                value += brightnessOffset;
                ushort entry = (ushort)Math.Round(Clamp(value, 0d, 1d) * ushort.MaxValue);
                ramp[i] = entry;
                ramp[RampEntries + i] = entry;
                ramp[RampEntries * 2 + i] = entry;
            }
            return ramp;
        }

        private static IntPtr CreateDisplayDC(Display display)
        {
            try
            {
                IntPtr hdc = NativeMethods.CreateDC(display.Name, display.Name, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero)
                    Log($"Could not open a device context for {display.Name}.");
                return hdc;
            }
            catch (Exception ex)
            {
                Log($"Could not open a device context for {display.Name}: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        #endregion

        #region Capture and restore

        /// <summary>Everything this class can change, for every display, as it is right now.</summary>
        public static DisplayColorSnapshot Capture(IEnumerable<Display> displays)
        {
            DisplayColorSnapshot snapshot = new DisplayColorSnapshot();
            if (displays == null)
                return snapshot;
            foreach (Display display in displays)
            {
                if (display == null || display.IsAllDisplay())
                    continue;
                snapshot.Displays.Add(new DisplayColorState(
                    display.UID,
                    display.Name,
                    GetVibrance(display),
                    GetHue(display),
                    GetGammaRamp(display)));
            }
            return snapshot;
        }

        /// <summary>Puts a captured state back; displays that have gone away are skipped.</summary>
        public static void Restore(DisplayColorSnapshot snapshot)
        {
            if (snapshot == null || snapshot.IsEmpty)
                return;
            // Whatever was applied is being undone, so the watchdog must not put it back.
            ForgetApplied();
            List<Display> current = DisplayManagerHandler.Instance.GetActiveMonitors();
            foreach (DisplayColorState state in snapshot.Displays)
            {
                Display display = current.FirstOrDefault(d => d.UID.Equals(state.DisplayUID))
                    ?? current.FirstOrDefault(d => string.Equals(d.Name, state.DisplayName, StringComparison.OrdinalIgnoreCase));
                if (display == null)
                {
                    Log($"Display {state.DisplayName} is gone; its colour settings were not restored.");
                    continue;
                }
                if (state.Vibrance.HasValue)
                    SetVibrance(display, state.Vibrance.Value);
                if (state.Hue.HasValue)
                    SetHue(display, state.Hue.Value);
                if (state.GammaRamp != null)
                    RestoreGammaRamp(display, state.GammaRamp);
            }
        }

        #endregion

        private static double Clamp(double value, double minimum, double maximum)
        {
            return value < minimum ? minimum : (value > maximum ? maximum : value);
        }

        private static class NativeMethods
        {
            [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateDC(string driver, string device, string port, IntPtr deviceMode);

            [DllImport("gdi32.dll")]
            public static extern bool DeleteDC(IntPtr hdc);

            [DllImport("gdi32.dll", SetLastError = true)]
            public static extern bool GetDeviceGammaRamp(IntPtr hdc, [In, Out] ushort[] ramp);

            [DllImport("gdi32.dll", SetLastError = true)]
            public static extern bool SetDeviceGammaRamp(IntPtr hdc, [In] ushort[] ramp);
        }
    }

    /// <summary>Colour state of every display before a profile's actions touched it.</summary>
    public class DisplayColorSnapshot
    {
        public List<DisplayColorState> Displays { get; private set; }

        public bool IsEmpty { get { return Displays.Count == 0; } }

        public DisplayColorSnapshot()
        {
            Displays = new List<DisplayColorState>();
        }

        public override string ToString()
        {
            if (IsEmpty)
                return "nothing captured";
            return string.Join("; ", Displays.Select(d => d.ToString()));
        }
    }

    /// <summary>One display's captured colour state.</summary>
    public class DisplayColorState
    {
        public uint DisplayUID { get; private set; }
        public string DisplayName { get; private set; }
        public double? Vibrance { get; private set; }
        public int? Hue { get; private set; }
        /// <summary>The exact ramp that was read back, or null if it could not be read.</summary>
        public ushort[] GammaRamp { get; private set; }

        public DisplayColorState(uint displayUID, string displayName, double? vibrance, int? hue, ushort[] gammaRamp)
        {
            DisplayUID = displayUID;
            DisplayName = displayName;
            Vibrance = vibrance;
            Hue = hue;
            GammaRamp = gammaRamp;
        }

        public override string ToString()
        {
            return $"{DisplayName}: vibrance {(Vibrance.HasValue ? Vibrance.Value.ToString("0.00") : "?")}, hue {(Hue.HasValue ? Hue.Value.ToString() : "?")}, ramp {(GammaRamp != null ? "captured" : "unavailable")}";
        }
    }
}
