using AutoActions.Audio;
using AutoActions.Displays;
using CodectoryCore.UI.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoActions
{
    /// <summary>
    /// Everything a profile action can change, but live and by hand: move a control and the hardware
    /// changes immediately, so trying a setting out does not mean opening Windows settings or the
    /// NVIDIA control panel.
    ///
    /// Nothing here is persisted. A profile action that runs later will happily overwrite anything
    /// set from this page, which is the same thing that happens if you change it in any other tool.
    /// </summary>
    public class QuickSettings : BaseViewModel
    {
        private readonly AutoActionsDaemon _daemon;

        /// <summary>
        /// Colour state as it was before this page first touched it. Restored by Reset, and it is the
        /// array that was read back rather than a computed neutral ramp, so a calibrated monitor
        /// profile survives a round trip through this page.
        /// </summary>
        private DisplayColorSnapshot _colorBaseline;

        /// <summary>Set while loading values from hardware, so setters do not write them straight back.</summary>
        private bool _loading;

        private Display _selectedDisplay;
        private double _vibrance = 50;
        private int _hue;
        private double _brightness = 50;
        private double _contrast = 50;
        private double _gamma = 1.0;

        public QuickSettings(AutoActionsDaemon daemon)
        {
            _daemon = daemon;
            RefreshCommand = new RelayCommand(Refresh);
            ResetColorCommand = new RelayCommand(ResetColor);
            Refresh();
        }

        public RelayCommand RefreshCommand { get; private set; }
        public RelayCommand ResetColorCommand { get; private set; }

        #region Display colour

        public List<Display> Displays
        {
            get { return DisplayManagerHandler.Instance.GetActiveMonitors(); }
        }

        public Display SelectedDisplay
        {
            get => _selectedDisplay;
            set
            {
                _selectedDisplay = value;
                OnPropertyChanged();
                LoadColorFromHardware();
            }
        }

        public bool VibranceAndHueSupported => DisplayColorControl.VibranceAndHueSupported;

        public double MinimumGamma => DisplayColorControl.MinimumGamma;
        public double MaximumGamma => DisplayColorControl.MaximumGamma;

        /// <summary>0-100 with 50 the driver default, the same scale the profile action uses.</summary>
        public double Vibrance
        {
            get => _vibrance;
            set
            {
                if (Math.Abs(_vibrance - value) < 0.5)
                    return;
                _vibrance = value;
                OnPropertyChanged();
                if (!_loading)
                {
                    CaptureBaseline();
                    DisplayColorControl.SetVibrance(SelectedDisplay, (value - 50d) / 50d);
                }
            }
        }

        public int Hue
        {
            get => _hue;
            set
            {
                if (_hue == value)
                    return;
                _hue = value;
                OnPropertyChanged();
                if (!_loading)
                {
                    CaptureBaseline();
                    DisplayColorControl.SetHue(SelectedDisplay, value);
                }
            }
        }

        public double Brightness
        {
            get => _brightness;
            set
            {
                if (Math.Abs(_brightness - value) < 0.5)
                    return;
                _brightness = value;
                OnPropertyChanged();
                ApplyGammaRamp();
            }
        }

        public double Contrast
        {
            get => _contrast;
            set
            {
                if (Math.Abs(_contrast - value) < 0.5)
                    return;
                _contrast = value;
                OnPropertyChanged();
                ApplyGammaRamp();
            }
        }

        public double Gamma
        {
            get => _gamma;
            set
            {
                if (Math.Abs(_gamma - value) < 0.005)
                    return;
                _gamma = value;
                OnPropertyChanged();
                ApplyGammaRamp();
            }
        }

        /// <summary>
        /// Brightness, contrast and gamma share one ramp, so any of the three rewrites all three.
        /// </summary>
        private void ApplyGammaRamp()
        {
            if (_loading || SelectedDisplay == null)
                return;
            CaptureBaseline();
            DisplayColorControl.SetGammaRamp(SelectedDisplay, Brightness / 100d, Contrast / 100d, Gamma);
        }

        private void CaptureBaseline()
        {
            if (_colorBaseline != null)
                return;
            _colorBaseline = DisplayColorControl.Capture(DisplayManagerHandler.Instance.GetActiveMonitors());
            Globals.Logs.Add($"Quick settings: colour state before the first manual change: {_colorBaseline}", false);
        }

        /// <summary>
        /// Vibrance and hue can be read back from the driver. The gamma ramp cannot be decoded into a
        /// brightness/contrast/gamma triple, so those three show neutral until they are moved - the
        /// same thing the NVIDIA control panel does after another program has written the ramp.
        /// </summary>
        private void LoadColorFromHardware()
        {
            _loading = true;
            try
            {
                double? vibrance = DisplayColorControl.GetVibrance(SelectedDisplay);
                _vibrance = vibrance.HasValue ? (vibrance.Value * 50d) + 50d : 50d;
                int? hue = DisplayColorControl.GetHue(SelectedDisplay);
                _hue = hue.HasValue ? hue.Value : 0;
                _brightness = 50;
                _contrast = 50;
                _gamma = 1.0;
                OnPropertyChanged(nameof(Vibrance));
                OnPropertyChanged(nameof(Hue));
                OnPropertyChanged(nameof(Brightness));
                OnPropertyChanged(nameof(Contrast));
                OnPropertyChanged(nameof(Gamma));
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>Puts every display back the way it was before this page changed anything.</summary>
        private void ResetColor()
        {
            if (_colorBaseline != null)
            {
                Globals.Logs.Add($"Quick settings: restoring colour to {_colorBaseline}", false);
                DisplayColorControl.Restore(_colorBaseline);
                _colorBaseline = null;
            }
            LoadColorFromHardware();
        }

        #endregion

        #region HDR

        public bool HDRIsActive
        {
            get { return DisplayManagerHandler.Instance.GlobalHDRIsActive; }
            set
            {
                if (value)
                    DisplayManagerHandler.Instance.ActivateHDR();
                else
                    DisplayManagerHandler.Instance.DeactivateHDR();
                OnPropertyChanged();
            }
        }

        #endregion

        #region Audio

        public MicMonitoringStatus MicMonitoring => _daemon != null ? _daemon.MicMonitoringStatus : null;

        public IReadOnlyList<AudioDevice> PlaybackDevices => AudioController.Instance.OutputAudioDevices;
        public IReadOnlyList<AudioDevice> RecordingDevices => AudioController.Instance.InputAudioDevices;

        public AudioDevice SelectedPlaybackDevice
        {
            get { return PlaybackDevices.FirstOrDefault(d => d.IsDefaultDevice); }
            set
            {
                if (value == null || value.IsDefaultDevice)
                    return;
                Globals.Logs.Add($"Quick settings: playback device to {value.Name}", false);
                value.SetAsDefault();
                OnPropertyChanged();
            }
        }

        public AudioDevice SelectedRecordingDevice
        {
            get { return RecordingDevices.FirstOrDefault(d => d.IsDefaultDevice); }
            set
            {
                if (value == null || value.IsDefaultDevice)
                    return;
                Globals.Logs.Add($"Quick settings: recording device to {value.Name}", false);
                value.SetAsDefault();
                OnPropertyChanged();
            }
        }

        #endregion

        /// <summary>Re-reads everything this page shows; the display list can change under it.</summary>
        public void Refresh()
        {
            try
            {
                AudioController.Instance.UpdateDevices();
                List<Display> displays = Displays;
                if (_selectedDisplay == null || !displays.Any(d => d.UID.Equals(_selectedDisplay.UID)))
                    _selectedDisplay = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
                OnPropertyChanged(nameof(Displays));
                OnPropertyChanged(nameof(SelectedDisplay));
                OnPropertyChanged(nameof(VibranceAndHueSupported));
                OnPropertyChanged(nameof(PlaybackDevices));
                OnPropertyChanged(nameof(RecordingDevices));
                OnPropertyChanged(nameof(SelectedPlaybackDevice));
                OnPropertyChanged(nameof(SelectedRecordingDevice));
                OnPropertyChanged(nameof(HDRIsActive));
                LoadColorFromHardware();
                if (MicMonitoring != null)
                    MicMonitoring.Refresh();
            }
            catch (Exception ex)
            {
                Globals.Logs.AddException(ex);
            }
        }
    }
}
