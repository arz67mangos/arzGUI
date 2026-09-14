using AutoActions.Audio;
using AutoActions.ProjectResources;
using CodectoryCore.UI.Wpf;
using System;
using System.Linq;

namespace AutoActions
{
    /// <summary>
    /// Live view of the configured microphone-monitoring line for the status card and the tray menu.
    /// Nothing is cached: Refresh() reads the hardware, and setting IsEnabled / Volume writes it.
    /// </summary>
    public class MicMonitoringStatus : BaseViewModel
    {
        private bool _isAvailable;
        private bool _isEnabled;
        private double _volume;
        private string _targetDescription = string.Empty;
        private bool _refreshing;

        public bool IsAvailable { get => _isAvailable; private set { _isAvailable = value; OnPropertyChanged(); } }

        /// <summary>Device and line being controlled, or why nothing can be.</summary>
        public string TargetDescription { get => _targetDescription; private set { _targetDescription = value; OnPropertyChanged(); } }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                    return;
                _isEnabled = value;
                OnPropertyChanged();
                if (!_refreshing)
                    Apply(value, null);
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                if (Math.Abs(_volume - value) < 0.5)
                    return;
                _volume = value;
                OnPropertyChanged();
                if (!_refreshing)
                    Apply(null, value);
            }
        }

        public RelayCommand RefreshCommand { get; private set; }
        public RelayCommand ToggleCommand { get; private set; }

        public MicMonitoringStatus()
        {
            RefreshCommand = new RelayCommand(Refresh);
            ToggleCommand = new RelayCommand(Toggle);
        }

        private static string DeviceId => Globals.Instance.Settings != null ? Globals.Instance.Settings.MicMonitoringDeviceId : string.Empty;
        private static string LineId => Globals.Instance.Settings != null ? Globals.Instance.Settings.MicMonitoringLineId : string.Empty;

        public void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        /// <summary>Re-reads state, level and target from the hardware.</summary>
        public void Refresh()
        {
            _refreshing = true;
            try
            {
                MonitoringLine line = MicMonitoring.Instance.ResolveLine(DeviceId, LineId);
                if (line == null)
                {
                    IsAvailable = false;
                    TargetDescription = ProjectLocales.MicMonitoringUnavailable;
                    return;
                }
                bool? state = MicMonitoring.Instance.GetMonitoringState(DeviceId, LineId);
                double? volume = MicMonitoring.Instance.GetMonitoringVolume(DeviceId, LineId);
                if (state.HasValue)
                {
                    _isEnabled = state.Value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
                if (volume.HasValue)
                {
                    _volume = volume.Value;
                    OnPropertyChanged(nameof(Volume));
                }
                IsAvailable = state.HasValue || volume.HasValue;
                MonitoringEndpoint endpoint = MicMonitoring.Instance.GetRenderEndpoints()
                    .FirstOrDefault(e => string.Equals(e.Id, DeviceId, StringComparison.OrdinalIgnoreCase));
                string device = endpoint != null ? endpoint.Name : ProjectLocales.MicMonitoringDefaultDevice;
                TargetDescription = $"{device} / {line.Description}";
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                TargetDescription = ex.Message;
                Globals.Logs.AddException(ex);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void Apply(bool? enabled, double? volume)
        {
            if (enabled.HasValue)
            {
                Globals.Logs.Add($"Microphone monitoring {(enabled.Value ? "on" : "off")} (manual)", false);
                if (!MicMonitoring.Instance.SetMonitoringState(DeviceId, LineId, enabled.Value))
                    IsAvailable = false;
            }
            if (volume.HasValue)
            {
                if (!MicMonitoring.Instance.SetMonitoringVolume(DeviceId, LineId, volume.Value))
                    IsAvailable = false;
            }
        }

        /// <summary>Current hardware state, for putting it back later.</summary>
        public MicMonitoringSnapshot Capture()
        {
            return new MicMonitoringSnapshot(
                MicMonitoring.Instance.GetMonitoringState(DeviceId, LineId),
                MicMonitoring.Instance.GetMonitoringVolume(DeviceId, LineId));
        }

        public void Restore(MicMonitoringSnapshot snapshot)
        {
            if (snapshot == null || snapshot.IsEmpty)
                return;
            if (snapshot.Enabled.HasValue)
                MicMonitoring.Instance.SetMonitoringState(DeviceId, LineId, snapshot.Enabled.Value);
            if (snapshot.Volume.HasValue)
                MicMonitoring.Instance.SetMonitoringVolume(DeviceId, LineId, snapshot.Volume.Value);
            Refresh();
        }
    }

    /// <summary>Mic monitoring state as it was before a profile's Started actions touched it.</summary>
    public class MicMonitoringSnapshot
    {
        public bool? Enabled { get; }
        public double? Volume { get; }
        public bool IsEmpty => !Enabled.HasValue && !Volume.HasValue;

        public MicMonitoringSnapshot(bool? enabled, double? volume)
        {
            Enabled = enabled;
            Volume = volume;
        }

        public override string ToString()
        {
            if (IsEmpty)
                return "unavailable";
            return $"{(Enabled.HasValue ? (Enabled.Value ? "on" : "off") : "?")}, {(Volume.HasValue ? Volume.Value.ToString("0") + "%" : "?")}";
        }
    }
}
