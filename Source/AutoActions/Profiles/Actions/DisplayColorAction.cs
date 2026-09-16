using AutoActions.Displays;
using AutoActions.ProjectResources;
using CodectoryCore.Logging;
using CodectoryCore.UI.Wpf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace AutoActions.Profiles.Actions
{
    /// <summary>
    /// Per-application display colour: digital vibrance and hue (NVIDIA only, through NVAPI) plus
    /// brightness, contrast and gamma (the Windows gamma ramp, any GPU). The same settings NVIDIA
    /// Control Panel's "Adjust desktop color settings" page writes.
    ///
    /// The slider values here are the ones the user sees - 0-100 for vibrance, brightness and
    /// contrast, exactly like NVIDIA's own sliders with 50 as the neutral middle. They are mapped to
    /// the driver's ranges on the way out, in <see cref="RunAction"/>.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class DisplayColorAction : ProfileActionBase
    {
        private uint _displayUID = uint.MaxValue;
        private bool _changeVibrance = false;
        private double _vibrance = 50;
        private bool _changeHue = false;
        private int _hue = 0;
        private bool _changeBrightness = false;
        private double _brightness = 50;
        private bool _changeContrast = false;
        private double _contrast = 50;
        private bool _changeGamma = false;
        private double _gamma = 1.0;

        public List<Display> AllDisplays
        {
            get
            {
                List<Display> displays = new List<Display>();
                displays.Add(Display.AllDisplays);
                displays.AddRange(DisplayManagerHandler.Instance.GetActiveMonitors());
                return displays;
            }
        }

        [JsonProperty]
        public uint DisplayUID
        {
            get => _displayUID;
            set
            {
                _displayUID = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Display));
            }
        }

        public Display Display
        {
            get => AllDisplays.FirstOrDefault(d => d.UID.Equals(DisplayUID));
            set
            {
                if (value != null)
                    DisplayUID = value.UID;
            }
        }

        [JsonProperty]
        public bool ChangeVibrance { get => _changeVibrance; set { _changeVibrance = value; OnPropertyChanged(); } }

        /// <summary>0-100, 50 = driver default. Mapped to NVAPI's -1..1 normalized level.</summary>
        [JsonProperty]
        public double Vibrance { get => _vibrance; set { _vibrance = Clamp(value, 0, 100); OnPropertyChanged(); } }

        [JsonProperty]
        public bool ChangeHue { get => _changeHue; set { _changeHue = value; OnPropertyChanged(); } }

        /// <summary>Hue angle in degrees, 0-359.</summary>
        [JsonProperty]
        public int Hue { get => _hue; set { _hue = (int)Clamp(value, 0, 359); OnPropertyChanged(); } }

        [JsonProperty]
        public bool ChangeBrightness { get => _changeBrightness; set { _changeBrightness = value; OnPropertyChanged(); OnPropertyChanged(nameof(GammaRangeWarningIsVisible)); } }

        /// <summary>0-100, 50 = neutral. Mapped to the gamma ramp's 0..1.</summary>
        [JsonProperty]
        public double Brightness { get => _brightness; set { _brightness = Clamp(value, 0, 100); OnPropertyChanged(); } }

        [JsonProperty]
        public bool ChangeContrast { get => _changeContrast; set { _changeContrast = value; OnPropertyChanged(); OnPropertyChanged(nameof(GammaRangeWarningIsVisible)); } }

        /// <summary>0-100, 50 = neutral. Mapped to the gamma ramp's 0..1.</summary>
        [JsonProperty]
        public double Contrast { get => _contrast; set { _contrast = Clamp(value, 0, 100); OnPropertyChanged(); } }

        [JsonProperty]
        public bool ChangeGamma { get => _changeGamma; set { _changeGamma = value; OnPropertyChanged(); } }

        /// <summary>
        /// 0.4-2.8, 1.0 = neutral. NVIDIA's own slider starts at 0.3, but the ramp maths below that
        /// produces a curve Windows will not accept, so the floor here is 0.4.
        /// </summary>
        [JsonProperty]
        public double Gamma { get => _gamma; set { _gamma = Clamp(value, DisplayColorControl.MinimumGamma, DisplayColorControl.MaximumGamma); OnPropertyChanged(); } }

        public double MinimumGamma => DisplayColorControl.MinimumGamma;
        public double MaximumGamma => DisplayColorControl.MaximumGamma;

        /// <summary>Digital vibrance and hue have no equivalent outside NVAPI.</summary>
        public bool VibranceAndHueSupported => DisplayColorControl.VibranceAndHueSupported;

        public bool GammaRangeIsUnlocked => DisplayColorControl.GammaRangeIsUnlocked;

        /// <summary>
        /// Only brightness and contrast can realistically trip the clamp. Measured with the value
        /// unset: gamma applies exactly across its whole 0.4-2.8 range, because it cannot move the
        /// ends of the ramp, while brightness 100% and contrast 100% are both refused. So a
        /// gamma-only action never sees this warning.
        /// </summary>
        public bool GammaRangeWarningIsVisible => (ChangeBrightness || ChangeContrast) && !GammaRangeIsUnlocked;

        private bool TouchesGammaRamp => ChangeBrightness || ChangeContrast || ChangeGamma;

        public RelayCommand UnlockGammaRangeCommand { get; private set; }

        public override bool CanSave => ChangeVibrance || ChangeHue || ChangeBrightness || ChangeContrast || ChangeGamma;
        public override string CannotSaveMessage => ProjectLocales.MessageMissingDisplayColorSetting;
        public override string ActionTypeName => ProjectLocales.DisplayColorAction;

        public override string ActionDescription
        {
            get
            {
                List<string> parts = new List<string>();
                if (ChangeVibrance)
                    parts.Add($"{ProjectLocales.DisplayColorVibrance} {Vibrance:0}%");
                if (ChangeHue)
                    parts.Add($"{ProjectLocales.DisplayColorHue} {Hue}°");
                if (ChangeBrightness)
                    parts.Add($"{ProjectLocales.DisplayColorBrightness} {Brightness:0}%");
                if (ChangeContrast)
                    parts.Add($"{ProjectLocales.DisplayColorContrast} {Contrast:0}%");
                if (ChangeGamma)
                    parts.Add($"{ProjectLocales.DisplayColorGamma} {Gamma:0.00}");
                Display display = Display;
                string name = display != null ? display.Name : string.Empty;
                return $"[{name}] {string.Join(", ", parts)}";
            }
        }

        public DisplayColorAction() : base()
        {
            UnlockGammaRangeCommand = new RelayCommand(UnlockGammaRange);
        }

        public override ActionEndResult RunAction(ApplicationChangedType applicationChangedType)
        {
            try
            {
                Display target = Display;
                if (target == null)
                    return new ActionEndResult(false, ProjectLocales.MessageInvalidSettings, null);

                List<Display> targets = target.IsAllDisplay()
                    ? DisplayManagerHandler.Instance.GetActiveMonitors()
                    : new List<Display>() { target };

                bool applied = true;
                foreach (Display display in targets)
                {
                    if (ChangeVibrance)
                    {
                        CallNewLog(new LogEntry($"Setting digital vibrance to {Vibrance:0}% for display {display.Name}"));
                        applied &= DisplayColorControl.SetVibrance(display, (Vibrance - 50d) / 50d);
                    }
                    if (ChangeHue)
                    {
                        CallNewLog(new LogEntry($"Setting hue to {Hue}° for display {display.Name}"));
                        applied &= DisplayColorControl.SetHue(display, Hue);
                    }
                    if (TouchesGammaRamp)
                    {
                        // One ramp carries all three, so they go in a single SetDeviceGammaRamp call;
                        // whatever the action does not change keeps its neutral value.
                        double brightness = ChangeBrightness ? Brightness / 100d : 0.5d;
                        double contrast = ChangeContrast ? Contrast / 100d : 0.5d;
                        double gamma = ChangeGamma ? Gamma : 1.0d;
                        CallNewLog(new LogEntry($"Setting brightness {brightness * 100:0}%, contrast {contrast * 100:0}%, gamma {gamma:0.00} for display {display.Name}"));
                        applied &= DisplayColorControl.SetGammaRamp(display, brightness, contrast, gamma);
                    }
                }

                if (!applied)
                {
                    // DisplayColorControl has already logged why - unsupported hardware, a display that
                    // went away, or a ramp Windows refused.
                    return new ActionEndResult(false, ProjectLocales.DisplayColorPartlyUnavailable, null);
                }
                return new ActionEndResult(true);
            }
            catch (Exception ex)
            {
                return new ActionEndResult(false, ex.Message, ex);
            }
        }

        /// <summary>
        /// Asks for elevation and writes the one HKLM value that stops Windows clamping gamma ramps.
        /// Never silent: this is only ever reached from the button in the action's view.
        /// </summary>
        private void UnlockGammaRange()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("reg.exe",
                    $"add \"HKLM\\{DisplayColorControl.GammaRangeKeyPath}\" /v {DisplayColorControl.GammaRangeValueName} /t REG_DWORD /d {DisplayColorControl.GammaRangeUnlockedValue} /f /reg:64");
                startInfo.UseShellExecute = true;
                startInfo.Verb = "runas";
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    Globals.Logs.Add($"{DisplayColorControl.GammaRangeValueName} write exited with {process.ExitCode}.", false);
                }
            }
            catch (Win32Exception)
            {
                // The user declined the elevation prompt.
            }
            catch (Exception ex)
            {
                Globals.Logs.AddException(ex);
            }
            OnPropertyChanged(nameof(GammaRangeIsUnlocked));
            OnPropertyChanged(nameof(GammaRangeWarningIsVisible));
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return value < minimum ? minimum : (value > maximum ? maximum : value);
        }
    }
}
