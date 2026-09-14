using AutoActions.Audio;
using AutoActions.ProjectResources;
using CodectoryCore.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AutoActions.Profiles.Actions
{
    /// <summary>
    /// Turns microphone monitoring ("listen to this device" on the playback side) on or off and/or
    /// sets its level. The playback device and input line come from the global settings
    /// (UserAppSettings.MicMonitoringDeviceId / MicMonitoringLineId), the same ones the status card uses.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class MicMonitoringAction : ProfileActionBase
    {
        private bool _changeState = true;
        private bool _enableMonitoring = true;
        private bool _changeVolume = false;
        private double _volume = 100;

        [JsonProperty]
        public bool ChangeState { get => _changeState; set { _changeState = value; OnPropertyChanged(); } }

        [JsonProperty]
        public bool EnableMonitoring { get => _enableMonitoring; set { _enableMonitoring = value; OnPropertyChanged(); } }

        [JsonProperty]
        public bool ChangeVolume { get => _changeVolume; set { _changeVolume = value; OnPropertyChanged(); } }

        /// <summary>0-100 across the line's dB range.</summary>
        [JsonProperty]
        public double Volume { get => _volume; set { _volume = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); } }

        public override bool CanSave => ChangeState || ChangeVolume;
        public override string CannotSaveMessage => ProjectLocales.MessageMissingMicMonitoringSetting;
        public override string ActionTypeName => ProjectLocales.MicMonitoring;

        public override string ActionDescription
        {
            get
            {
                List<string> parts = new List<string>();
                if (ChangeState)
                    parts.Add($"{ProjectLocales.MicMonitoringEnable}: {(EnableMonitoring ? ProjectLocales.On : ProjectLocales.Off)}");
                if (ChangeVolume)
                    parts.Add($"{ProjectLocales.MicMonitoringVolume}: {Volume:0}%");
                return $"[{string.Join(", ", parts)}]";
            }
        }

        public MicMonitoringAction() : base()
        {
        }

        public override ActionEndResult RunAction(ApplicationChangedType applicationChangedType)
        {
            try
            {
                string deviceId = Globals.Instance.Settings != null ? Globals.Instance.Settings.MicMonitoringDeviceId : string.Empty;
                string lineId = Globals.Instance.Settings != null ? Globals.Instance.Settings.MicMonitoringLineId : string.Empty;
                bool applied = true;
                if (ChangeState)
                {
                    CallNewLog(new LogEntry($"Turning microphone monitoring {(EnableMonitoring ? "on" : "off")}"));
                    applied &= MicMonitoring.Instance.SetMonitoringState(deviceId, lineId, EnableMonitoring);
                }
                if (ChangeVolume)
                {
                    CallNewLog(new LogEntry($"Setting microphone monitoring volume to {Volume:0}%"));
                    applied &= MicMonitoring.Instance.SetMonitoringVolume(deviceId, lineId, Volume);
                }
                if (!applied)
                {
                    // The service has already logged why; on hardware without a controllable line this is a no-op.
                    CallNewLog(new LogEntry($"Microphone monitoring action skipped: {ProjectLocales.MicMonitoringUnavailable}"));
                    return new ActionEndResult(false, ProjectLocales.MicMonitoringUnavailable, null);
                }
                return new ActionEndResult(true);
            }
            catch (Exception ex)
            {
                return new ActionEndResult(false, ex.Message, ex);
            }
        }
    }
}
