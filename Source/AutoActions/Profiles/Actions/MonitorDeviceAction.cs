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
using System.Windows;

namespace AutoActions.Profiles.Actions
{
    /// <summary>
    /// Enables or disables a monitor device - the Device Manager operation, per application.
    ///
    /// The use for it: disabling the monitor device is how a true stretched resolution is made to
    /// stick in a game that would otherwise override the scaling mode. It is not free - with no
    /// monitor device Windows has no gamma ramp for that display, so a <see cref="DisplayColorAction"/>
    /// can no longer change brightness, contrast or gamma (vibrance and hue still work, they are
    /// NVAPI). Putting the disable on the game's Started actions and letting the daemon restore the
    /// state on Closed is what keeps both usable on one machine.
    ///
    /// This is the only action that needs AutoActions to run as administrator; without it the device
    /// change is refused and the action reports that.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class MonitorDeviceAction : ProfileActionBase
    {
        private string _instanceId = string.Empty;
        private string _deviceName = string.Empty;
        private bool _enable = false;

        /// <summary>The device instance id, e.g. DISPLAY\LEN8910\5&amp;181de175&amp;0&amp;UID256.</summary>
        [JsonProperty]
        public string InstanceId
        {
            get => _instanceId;
            set
            {
                _instanceId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Device));
            }
        }

        /// <summary>
        /// Remembered alongside the id so the action still describes itself when the monitor is
        /// unplugged and no longer in the list.
        /// </summary>
        [JsonProperty]
        public string DeviceName { get => _deviceName; set { _deviceName = value; OnPropertyChanged(); } }

        /// <summary>True enables the device, false disables it. Disabling is the point of the action.</summary>
        [JsonProperty]
        public bool Enable { get => _enable; set { _enable = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActionDescription)); } }

        public List<MonitorDevice> MonitorDevices
        {
            get { return MonitorDeviceControl.GetMonitorDevices(); }
        }

        public MonitorDevice Device
        {
            get { return MonitorDevices.FirstOrDefault(d => string.Equals(d.InstanceId, InstanceId, StringComparison.OrdinalIgnoreCase)); }
            set
            {
                if (value == null)
                    return;
                DeviceName = value.Name;
                InstanceId = value.InstanceId;
            }
        }

        /// <summary>Device Manager changes need elevation; everything else in this app does not.</summary>
        public bool IsElevated => MonitorDeviceControl.IsElevated;

        public bool ElevationWarningIsVisible => !IsElevated;

        public RelayCommand RestartAsAdministratorCommand { get; private set; }

        public override bool CanSave => !string.IsNullOrEmpty(InstanceId);
        public override string CannotSaveMessage => ProjectLocales.MessageMissingMonitorDevice;
        public override string ActionTypeName => ProjectLocales.MonitorDeviceAction;

        public override string ActionDescription
        {
            get
            {
                MonitorDevice device = Device;
                string name = device != null ? device.Name : DeviceName;
                string state = Enable ? ProjectLocales.MonitorDeviceEnable : ProjectLocales.MonitorDeviceDisable;
                return $"[{name}] {state}";
            }
        }

        public MonitorDeviceAction() : base()
        {
            RestartAsAdministratorCommand = new RelayCommand(RestartAsAdministrator);
        }

        public override ActionEndResult RunAction(ApplicationChangedType applicationChangedType)
        {
            try
            {
                if (string.IsNullOrEmpty(InstanceId))
                    return new ActionEndResult(false, ProjectLocales.MessageInvalidSettings, null);
                if (!MonitorDeviceControl.IsElevated)
                    return new ActionEndResult(false, ProjectLocales.MonitorDeviceNeedsAdministrator, null);

                CallNewLog(new LogEntry($"{(Enable ? "Enabling" : "Disabling")} monitor device {DeviceName} ({InstanceId})"));
                if (!MonitorDeviceControl.SetEnabled(InstanceId, Enable))
                {
                    // MonitorDeviceControl has already logged why.
                    return new ActionEndResult(false, ProjectLocales.MonitorDeviceFailed, null);
                }
                return new ActionEndResult(true);
            }
            catch (Exception ex)
            {
                return new ActionEndResult(false, ex.Message, ex);
            }
        }

        /// <summary>
        /// Starts a second copy elevated and closes this one. The new process waits for the single
        /// instance mutex instead of reporting that AutoActions is already running - see App.Main.
        /// </summary>
        private void RestartAsAdministrator()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, App.RestartArgument);
                startInfo.UseShellExecute = true;
                startInfo.Verb = "runas";
                Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                // The user declined the elevation prompt.
                return;
            }
            catch (Exception ex)
            {
                Globals.Logs.AddException(ex);
                return;
            }
            Application.Current.Shutdown();
        }
    }
}
