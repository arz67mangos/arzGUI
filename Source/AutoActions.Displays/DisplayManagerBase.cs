using AutoActions.Threading;
using CCD;
using CCD.Enum;
using CCD.Struct;
using CodectoryCore;
using CodectoryCore.UI.Wpf;
using Microsoft.Win32;
using NvAPIWrapper.Display;
using NvAPIWrapper.Native;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace AutoActions.Displays
{
    public abstract class DisplayManagerBase : BaseViewModel, IDisplayManagerBase
    {
        public abstract GraphicsCardType GraphicsCardType { get; }
        bool _selectedHDR = false;

        readonly object _lockUpdateDisplays = new object();

        readonly object _lockExceptionThrown = new object();
        public EventHandler<Exception> _exceptionThrown;

        event EventHandler<Exception> IDisplayManagerBase.ExceptionThrown
        {
            add
            {
                lock (_lockExceptionThrown)
                {
                    _exceptionThrown += value;
                }
            }

            remove
            {
                lock (_lockExceptionThrown)
                {
                    _exceptionThrown -= value;
                }
            }
        }

        public bool GlobalHDRIsActive { get; private set; } = false;
        public bool SelectedHDR { get => _selectedHDR; set { _selectedHDR = value; OnPropertyChanged(); } } 

        public event EventHandler HDRIsActiveChanged;

        public event EventHandler<string> NewLog;

        protected void CallNewLog(string message)
        {
            try { NewLog?.Invoke(this, message); } catch { }
        }

        private DispatchingObservableCollection<Display> _monitors = new DispatchingObservableCollection<Display>();
        public DispatchingObservableCollection<Display> Displays { get => _monitors; set { _monitors = value; OnPropertyChanged(); } }

        protected DisplayManagerBase()
        {
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            UpdateDisplays();
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            UpdateDisplays();
        }

        public void LoadKnownDisplays(List<Display> knownMonitors)
        {
            //foreach (var monitor in knownMonitors)
            //    Displays.Add(monitor);
            MergeMonitors(knownMonitors);

            MergeMonitors(GetActiveMonitors());
        }

        public void UpdateDisplays()
        {
            lock (_lockUpdateDisplays)
            {
                bool currentValue = false;
                MergeMonitors(GetActiveMonitors());
                foreach (Display monitor in Displays)
                {
                    monitor.UpdateHDRState();
                    if (monitor.Managed)
                        currentValue = currentValue || monitor.HDRState;
                }
                bool changed = GlobalHDRIsActive != currentValue;
                GlobalHDRIsActive = currentValue;
                if (changed)
                {
                    try { HDRIsActiveChanged?.Invoke(null, EventArgs.Empty); } catch { }
                }
            }
        }

        public void ActivateHDR(Display display)
        {
            HDRController.SetHDRState(display.UID, true);

        }

        public void DeactivateHDR(Display display)
        {
            HDRController.SetHDRState(display.UID, false);
        }

        public void ActivateHDR()
        {
            if (!SelectedHDR)
                HDRController.SetGlobalHDRState(true);
            else
            {
                foreach (Display display in Displays)
                    if (display.Managed)
                        ActivateHDR(display);
            }
        }

        public void DeactivateHDR()
        {
            if (!SelectedHDR)
                HDRController.SetGlobalHDRState(false);
            else
            {
                foreach (Display display in Displays)
                    if (display.Managed)
                        DeactivateHDR(display);

            }
        }

        public virtual List<Display> GetActiveMonitors()
        {
            List<Display> displays = new List<Display>();
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            DEVMODE dm = DEVMODE.Initialized();
            d.cb = Marshal.SizeOf(d);

            uint displayID = 0;
            while (NativeMethods.EnumDisplayDevices(null, displayID, ref d, 0))
            {
                DisplayInformation displayInfo;
                if (0 != NativeMethods.EnumDisplaySettings(d.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm))
                    displayInfo = new DisplayInformation(displayID, d, dm);
                else
                    displayInfo = new DisplayInformation(displayID, d);
                Display display = new Display(displayInfo, GetUID(displayID));
                display.ColorDepth = (ColorDepth)HDRController.GetColorDepth(display.UID);
                if (!displays.Any(m => m.ID.Equals(display.ID)) && display.UID != 0)
                    displays.Add(display);
                displayID++;
            }
            return displays;
        }

        public ColorDepth GetColorDepth(Display display)
        {
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            DEVMODE dm = DEVMODE.Initialized();
            d.cb = Marshal.SizeOf(d);


            NativeMethods.EnumDisplayDevices(null, display.ID, ref d, 0);

            if (0 != NativeMethods.EnumDisplaySettings(
                d.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm))
            {

                return (ColorDepth)dm.dmBitsPerPel;
            }
            return ColorDepth.BPCUnkown;
        }

        public bool GetHDRState(Display display)
        {
            return HDRController.GetHDRState(display.UID);
        }

        public int GetRefreshRate(Display display)
        {
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            DEVMODE dm = DEVMODE.Initialized();
            d.cb = Marshal.SizeOf(d);

            NativeMethods.EnumDisplayDevices(null, display.ID, ref d, 0);

            if (0 != NativeMethods.EnumDisplaySettings(
                d.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm))
            {
                return dm.dmDisplayFrequency;

            }
            else return 0;
        }

        public Size GetResolution(Display display)
        {
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            DEVMODE dm = DEVMODE.Initialized();
            d.cb = Marshal.SizeOf(d);


            NativeMethods.EnumDisplayDevices(null, display.ID, ref d, 0);

            if (0 != NativeMethods.EnumDisplaySettings(
                d.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm))
            {
                Size resolution = new Size(dm.dmPelsWidth, dm.dmPelsHeight);
                return resolution;

            }
            else return Size.Empty;
        }

        public uint GetUID(uint displayID)
        {
            return HDRController.GetUID(displayID);
        }



        private void MergeMonitors(List<Display> activeMonitors)
        {

            try
            {
                List<Display> toRemove = new List<Display>();
                foreach (Display monitor in Displays)
                {
                    if (monitor.UID == 0 || !activeMonitors.Any(m => m.UID.Equals(monitor.UID)))
                        toRemove.Add(monitor);
                }
                foreach (Display monitor in toRemove)
                    Displays.Remove(monitor);
                foreach (Display monitor in activeMonitors)
                {
                    if (monitor.UID != 0 && !Displays.Any(m => m.UID.Equals(monitor.UID)))
                        Displays.Add(monitor);
                    else
                    {
                        Display existingMonitor = Displays.First(m => m.UID.Equals(monitor.UID));
                        existingMonitor.Name = monitor.Name;
                        existingMonitor.ColorDepth = monitor.ColorDepth;
                        existingMonitor.RefreshRate = monitor.RefreshRate;
                        existingMonitor.Resolution = monitor.Resolution;
                        existingMonitor.GraphicsCard = monitor.GraphicsCard;
                        existingMonitor.ID = monitor.ID;
                        existingMonitor.IsPrimary = monitor.IsPrimary;
                        existingMonitor.Resolution = monitor.Resolution;
                        existingMonitor.Tag = monitor.Tag;


                    }

                }
            }
            catch (Exception ex)
            {
                _exceptionThrown?.BeginInvoke(this, ex, null, null);
            }
        }

        public DISP_CHANGE SetRefreshRate(Display display, int refreshRate)
        {
            return SetDisplayMode(display, null, refreshRate);
        }

        public DISP_CHANGE SetResolution(Display display, Size resolution)
        {
            return SetDisplayMode(display, resolution, null);
        }


        public abstract void SetColorDepth(Display display, ColorDepth colorDepth);

        const int ModeChangeMaxAttempts = 3;
        const int ModeChangeVerifyDelayMs = 300;

        /// <summary>
        /// Applies resolution and/or refresh rate in a single ChangeDisplaySettingsEx call, then
        /// re-reads the current mode to verify it, retrying when Windows reports success but the
        /// display has not (yet) changed. A null component keeps its current value.
        ///
        /// The DEVMODE handed to Windows is the display's own enumerated mode whenever one matches,
        /// never the current DEVMODE with width/height patched: a current mode inherited from a
        /// custom (GPU-scaled) resolution carries DM_DISPLAYFIXEDOUTPUT with a scaling value the
        /// native resolution does not offer, and ChangeDisplaySettingsEx answers BadMode.
        /// </summary>
        public DISP_CHANGE SetDisplayMode(Display display, Size? resolution, int? refreshRate)
        {
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);
            if (!NativeMethods.EnumDisplayDevices(null, display.ID, ref d, 0))
            {
                CallNewLog($"EnumDisplayDevices failed for display '{display.Name}' (ID {display.ID}, UID {display.UID}); nothing changed.");
                return DISP_CHANGE.Failed;
            }

            DEVMODE current;
            if (!TryGetCurrentMode(d.DeviceName, out current))
            {
                CallNewLog($"EnumDisplaySettings(ENUM_CURRENT_SETTINGS) failed for {d.DeviceName} '{display.Name}'; nothing changed.");
                return DISP_CHANGE.Failed;
            }

            int width = resolution.HasValue ? resolution.Value.Width : current.dmPelsWidth;
            int height = resolution.HasValue ? resolution.Value.Height : current.dmPelsHeight;
            int frequency = refreshRate.HasValue ? refreshRate.Value : current.dmDisplayFrequency;
            int bitsPerPel = current.dmBitsPerPel;
            string requestedMode = $"{width}x{height} @ {frequency}Hz, {bitsPerPel}bpp";
            string device = $"{d.DeviceName} '{display.Name}'";

            if (ModeMatches(current, width, height, frequency))
            {
                CallNewLog($"{device} is already in {requestedMode}; nothing to do.");
                return DISP_CHANGE.Successful;
            }

            CallNewLog($"{device}: requesting {requestedMode}, current is {current.dmPelsWidth}x{current.dmPelsHeight} @ {current.dmDisplayFrequency}Hz (fixedOutput {current.dmDisplayFixedOutput}).");

            // Primary request: the current DEVMODE with the new values. Keeping the driver's own
            // field set (DM_POSITION, DM_DISPLAYFIXEDOUTPUT, ...) matters: a DEVMODE taken from the
            // enumerated mode list lacks those flags, and an NVIDIA custom resolution requested that
            // way resolves to the nearest real timing instead (observed: 2090x1440 -> 1920x1200).
            // The scaling value itself must be reset, though. Coming out of a custom resolution it is
            // 1 or 2, the native resolution exists only with 0, and ChangeDisplaySettingsEx answers
            // BadMode for "native, centered" - which is how restores used to fail.
            DEVMODE patched = current;
            patched.dmPelsWidth = width;
            patched.dmPelsHeight = height;
            patched.dmDisplayFrequency = frequency;
            patched.dmDisplayFixedOutput = 0;
            List<KeyValuePair<string, DEVMODE>> candidates = new List<KeyValuePair<string, DEVMODE>>();
            candidates.Add(new KeyValuePair<string, DEVMODE>("current mode with new values", patched));
            DEVMODE enumerated;
            if (TryFindEnumeratedMode(d.DeviceName, width, height, frequency, bitsPerPel, out enumerated))
                candidates.Add(new KeyValuePair<string, DEVMODE>("enumerated mode", enumerated));

            DISP_CHANGE result = DISP_CHANGE.Failed;
            foreach (KeyValuePair<string, DEVMODE> candidate in candidates)
            {
                DEVMODE target = candidate.Value;
                for (int attempt = 1; attempt <= ModeChangeMaxAttempts; attempt++)
                {
                    result = NativeMethods.ChangeDisplaySettingsEx(
                        d.DeviceName, ref target, IntPtr.Zero,
                        DisplaySettingsFlags.CDS_UPDATEREGISTRY, IntPtr.Zero);

                    if (result != DISP_CHANGE.Successful)
                    {
                        CallNewLog($"ChangeDisplaySettingsEx FAILED for {device}, requested {requestedMode} via {candidate.Key} (attempt {attempt}/{ModeChangeMaxAttempts}): {result} ({(int)result})");
                        if (result == DISP_CHANGE.BadMode || result == DISP_CHANGE.BadFlags || result == DISP_CHANGE.BadParam || result == DISP_CHANGE.BadDualView)
                            break; // this request is invalid as such; try the next candidate, if any
                        Thread.Sleep(ModeChangeVerifyDelayMs * attempt);
                        continue;
                    }

                    Thread.Sleep(ModeChangeVerifyDelayMs * attempt);
                    DEVMODE actual;
                    if (TryGetCurrentMode(d.DeviceName, out actual) && ModeMatches(actual, width, height, frequency))
                    {
                        CallNewLog($"ChangeDisplaySettingsEx {device} -> {requestedMode} via {candidate.Key}: Successful, verified (attempt {attempt}).");
                        return DISP_CHANGE.Successful;
                    }
                    CallNewLog($"ChangeDisplaySettingsEx {device} -> {requestedMode} via {candidate.Key} reported Successful but the display is at {actual.dmPelsWidth}x{actual.dmPelsHeight} @ {actual.dmDisplayFrequency}Hz (attempt {attempt}/{ModeChangeMaxAttempts}).");
                }
            }

            CallNewLog($"Giving up on {requestedMode} for {device}.");
            return result == DISP_CHANGE.Successful ? DISP_CHANGE.Failed : result;
        }

        private static bool TryGetCurrentMode(string deviceName, out DEVMODE mode)
        {
            mode = DEVMODE.Initialized();
            return 0 != NativeMethods.EnumDisplaySettings(deviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref mode);
        }

        private static bool ModeMatches(DEVMODE mode, int width, int height, int frequency)
        {
            return mode.dmPelsWidth == width && mode.dmPelsHeight == height && mode.dmDisplayFrequency == frequency;
        }

        /// <summary>
        /// Walks the display's mode list for an exact match. Modes that differ only in GPU scaling
        /// (dmDisplayFixedOutput) are listed separately; the driver default (0) is preferred so the
        /// scaling configured in the GPU control panel applies.
        /// </summary>
        private static bool TryFindEnumeratedMode(string deviceName, int width, int height, int frequency, int bitsPerPel, out DEVMODE match)
        {
            bool found = false;
            match = DEVMODE.Initialized();
            DEVMODE candidate = DEVMODE.Initialized();
            for (int i = 0; 0 != NativeMethods.EnumDisplaySettings(deviceName, i, ref candidate); i++)
            {
                if (candidate.dmPelsWidth != width || candidate.dmPelsHeight != height
                    || candidate.dmDisplayFrequency != frequency || candidate.dmBitsPerPel != bitsPerPel)
                    continue;
                if (!found || candidate.dmDisplayFixedOutput == 0)
                {
                    match = candidate;
                    found = true;
                }
                if (candidate.dmDisplayFixedOutput == 0)
                    break;
            }
            return found;
        }

    }
}
