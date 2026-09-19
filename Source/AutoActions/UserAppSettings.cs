using AutoActions.Displays;
using AutoActions.Audio;
using AutoActions.Obs;
using AutoActions.Profiles;
using AutoActions.Profiles.Actions;
using AutoActions.Theming;
using AutoActions.Windows;
using CodectoryCore;
using CodectoryCore.UI.Wpf;
using Newtonsoft.Json;
using System;
using AutoActions.ProjectResources;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Serialization;

namespace AutoActions
{
    [JsonObject(MemberSerialization.OptIn)]
    public class UserAppSettings : BaseViewModel
    {
        public static readonly object _settingsLock = new object();

        private bool _globalAutoActions = true;
        private bool _createLogFile = true;
        private bool _autoStart = false;
        // Off by default in this fork: the updater still points at Codectory/AutoActions and would
        // replace this build with the upstream release (see Phase 5 in AUTOACTIONS_PLAN.md).
        private bool _autoUpdate = false;
        private bool _startMinimizedToTray;
        private bool _closeToTray;
        private bool _checkForNewVersion = false;
        // The splash bitmap still carries the upstream branding; off by default in the fork.
        private bool _hideSplashScreenOnStartup = true;
        private bool _hideSplashScreenOnAutoUpdate = false;

        readonly object _audioDevicesLock = new object();
        private Guid _defaultProfileGuid = Guid.Empty;
        private Size _windowSize = new Size(1280, 800);


        private SortableObservableCollection<ApplicationProfileAssignment> _applicationProfileAssignments;
        private DispatchingObservableCollection<Profile> _applicationProfiles;
        private DispatchingObservableCollection<Display> _displays;
        private DispatchingObservableCollection<ProfileActionShortcut> _actionShortcuts;


        [JsonProperty]
        public Guid DefaultProfileGuid { get => _defaultProfileGuid; set { _defaultProfileGuid = value; OnPropertyChanged(); OnPropertyChanged(nameof(DefaultProfile)); } }

        public Profile DefaultProfile { get => ApplicationProfiles.FirstOrDefault(p => p.GUID.Equals(DefaultProfileGuid)); set { DefaultProfileGuid = value == null ? Guid.Empty : value.GUID; } }


        [JsonProperty]
        public bool GlobalAutoActions { get => _globalAutoActions; set { _globalAutoActions = value; OnPropertyChanged(); } }

        [JsonProperty]
        public bool AutoStart { get => _autoStart; set { _autoStart = value; OnPropertyChanged(); } }
        [JsonProperty]
        public bool AutoUpdate { get => _autoUpdate; set { _autoUpdate = value; OnPropertyChanged(); } }

        [JsonProperty]
        public bool HideSplashScreenOnStartup { get => _hideSplashScreenOnStartup; set { _hideSplashScreenOnStartup = value; OnPropertyChanged(); } }


        [JsonProperty]
        public bool HideSplashScreenOnAutoUpdate { get => _hideSplashScreenOnAutoUpdate; set { _hideSplashScreenOnAutoUpdate = value; OnPropertyChanged(); } }


        [JsonProperty]
        public bool CreateLogFile { get => _createLogFile; set { _createLogFile = value; OnPropertyChanged(); } }

        [JsonProperty]
        public bool StartMinimizedToTray { get => _startMinimizedToTray; set { _startMinimizedToTray = value; OnPropertyChanged(); } }

        [JsonProperty]
        public bool CloseToTray { get => _closeToTray; set { _closeToTray = value; OnPropertyChanged(); } }

        [JsonProperty]
        public bool CheckForNewVersion { get => _checkForNewVersion; set { _checkForNewVersion = value; OnPropertyChanged(); } }

        private int _focusDebounceSeconds = 2;

        /// <summary>
        /// How long a focus change has to hold before Got focus / Lost focus actions run. Without it,
        /// glancing at another window mid-game runs the whole Lost focus list and then the Got focus
        /// list again a second later. 0 disables the delay. Missing in old settings files -> 2.
        /// </summary>
        [JsonProperty]
        public int FocusDebounceSeconds
        {
            get => _focusDebounceSeconds;
            set { _focusDebounceSeconds = Math.Max(0, Math.Min(60, value)); OnPropertyChanged(); }
        }

        private bool _reapplyDisplayColor = true;

        /// <summary>
        /// Re-apply the colour settings an action last wrote after Windows drops them - a display
        /// mode change, a resume or a session unlock all clear the gamma ramp silently.
        /// Missing in old settings files -> on.
        /// </summary>
        [JsonProperty]
        public bool ReapplyDisplayColor { get => _reapplyDisplayColor; set { _reapplyDisplayColor = value; OnPropertyChanged(); } }

        private ThemeSetting _theme = ThemeSetting.System;

        /// <summary>System follows Windows' app mode; applied live by ThemeManager. Missing in old settings files -> System.</summary>
        [JsonProperty]
        public ThemeSetting Theme { get => _theme; set { _theme = value; OnPropertyChanged(); } }

        private string _micMonitoringDeviceId = string.Empty;
        private string _micMonitoringLineId = string.Empty;

        /// <summary>Core Audio endpoint ID of the playback device carrying the monitored input line; empty = default playback device.</summary>
        [JsonProperty]
        public string MicMonitoringDeviceId
        {
            get => _micMonitoringDeviceId;
            set
            {
                _micMonitoringDeviceId = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MicMonitoringEndpoint));
                OnPropertyChanged(nameof(MicMonitoringLines));
                OnPropertyChanged(nameof(MicMonitoringLine));
            }
        }

        /// <summary>Connector ID of the input line on that device; empty = pick the microphone line automatically.</summary>
        [JsonProperty]
        public string MicMonitoringLineId
        {
            get => _micMonitoringLineId;
            set
            {
                _micMonitoringLineId = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MicMonitoringLine));
            }
        }

        private int _obsWebSocketPort = 4455;
        private string _obsWebSocketPassword = string.Empty;
        private string _obsTestResult = string.Empty;

        /// <summary>Port of the obs-websocket server built into OBS 28+; 4455 is its default.</summary>
        [JsonProperty]
        public int ObsWebSocketPort { get => _obsWebSocketPort; set { _obsWebSocketPort = value; OnPropertyChanged(); } }

        /// <summary>
        /// The obs-websocket password as stored: DPAPI-encrypted for this Windows account. A settings
        /// file carried to another machine keeps working, minus this one value, which has to be
        /// re-entered there.
        /// </summary>
        [JsonProperty]
        public string ObsWebSocketPassword
        {
            get => _obsWebSocketPassword;
            set { _obsWebSocketPassword = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(ObsPassword)); }
        }

        /// <summary>The same password in the clear, for the settings box and the connection itself.</summary>
        [JsonIgnore]
        public string ObsPassword
        {
            get => ProtectedText.Unprotect(_obsWebSocketPassword);
            set { ObsWebSocketPassword = ProtectedText.Protect(value); }
        }

        [JsonIgnore]
        public string ObsTestResult { get => _obsTestResult; set { _obsTestResult = value; OnPropertyChanged(); } }

        private bool _startWithWindowsElevated = false;
        private string _logonTaskStatus = string.Empty;
        private bool _logonTaskBusy = false;

        /// <summary>
        /// Whether the logon task that starts this copy as administrator is registered. Not a stored
        /// setting: the truth lives in Task Scheduler, this is the last answer it gave. Writing it
        /// asks for administrator rights, so it happens off the UI thread and the property corrects
        /// itself afterwards - a declined prompt puts the box back.
        /// </summary>
        [JsonIgnore]
        public bool StartWithWindowsElevated
        {
            get => _startWithWindowsElevated;
            set
            {
                if (value == _startWithWindowsElevated || _logonTaskBusy)
                    return;
                _logonTaskBusy = true;
                LogonTaskStatus = value ? "Asking Windows to register the logon task..." : "Removing the logon task...";
                System.Threading.Tasks.Task.Run(() =>
                {
                    string error;
                    bool ok = value ? LogonTask.Create(out error) : LogonTask.Delete(out error);
                    // Two copies at logon otherwise: the Run key one is not elevated and wins the mutex.
                    if (ok && value)
                        AutoStart = false;
                    _logonTaskBusy = false;
                    RefreshLogonTask(ok ? null : error);
                });
            }
        }

        [JsonIgnore]
        public string LogonTaskStatus { get => _logonTaskStatus; set { _logonTaskStatus = value; OnPropertyChanged(); } }

        /// <summary>
        /// Re-reads Task Scheduler. Says so when a task exists but starts something else - an older
        /// copy in another folder, or the path a hand-written command recorded by mistake - because
        /// that looks like autostart being on while nothing starts.
        /// </summary>
        public void RefreshLogonTask(string error = null)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                string registered = LogonTask.RegisteredProgram();
                bool mine = !string.IsNullOrEmpty(registered) && LogonTask.StartsThisCopy();
                _startWithWindowsElevated = mine;
                OnPropertyChanged(nameof(StartWithWindowsElevated));
                if (!string.IsNullOrEmpty(error))
                    LogonTaskStatus = "That did not work: " + error + ".";
                else if (mine)
                    LogonTaskStatus = "arzGUI starts as administrator when you log on.";
                else if (!string.IsNullOrEmpty(registered))
                    LogonTaskStatus = "A logon task exists, but it starts " + registered + ". Turn this on to point it at this copy.";
                else
                    LogonTaskStatus = "Off. Auto-Start above never runs as administrator, which the monitor device action needs.";
            });
        }

        private string _obsInstallStatus = string.Empty;

        /// <summary>Where OBS is and what it is doing, so the card can say it instead of asking.</summary>
        [JsonIgnore]
        public string ObsInstallStatus { get => _obsInstallStatus; set { _obsInstallStatus = value; OnPropertyChanged(); } }

        public RelayCommand ObsTestCommand { get; private set; }

        // The four members below are the picker-facing view of the two IDs above (same pattern as DefaultProfile / DefaultProfileGuid).
        public IReadOnlyList<MonitoringEndpoint> MicMonitoringEndpoints
        {
            get
            {
                List<MonitoringEndpoint> endpoints = new List<MonitoringEndpoint>();
                endpoints.Add(new MonitoringEndpoint(string.Empty, ProjectLocales.MicMonitoringDefaultDevice));
                endpoints.AddRange(MicMonitoring.Instance.GetRenderEndpoints());
                return endpoints;
            }
        }

        public MonitoringEndpoint MicMonitoringEndpoint
        {
            get => MicMonitoringEndpoints.FirstOrDefault(e => string.Equals(e.Id, MicMonitoringDeviceId, StringComparison.OrdinalIgnoreCase));
            set
            {
                // WPF pushes null when the list is rebuilt without the current item (device unplugged); keep the setting then.
                if (value == null || string.Equals(value.Id, MicMonitoringDeviceId, StringComparison.OrdinalIgnoreCase))
                    return;
                MicMonitoringLineId = string.Empty;
                MicMonitoringDeviceId = value.Id;
            }
        }

        public IReadOnlyList<MonitoringLine> MicMonitoringLines => MicMonitoring.Instance.GetControllableLines(MicMonitoringDeviceId);

        public MonitoringLine MicMonitoringLine
        {
            get => MicMonitoringLines.FirstOrDefault(l => l.LineId == MicMonitoringLineId);
            set
            {
                if (value == null || value.LineId == MicMonitoringLineId)
                    return;
                MicMonitoringLineId = value.LineId;
            }
        }


        [JsonProperty(Order = 2)]
        public SortableObservableCollection<ApplicationProfileAssignment> ApplicationProfileAssignments { get => _applicationProfileAssignments; set { _applicationProfileAssignments = value; OnPropertyChanged(); } }

        [JsonProperty(Order = 1)]
        public DispatchingObservableCollection<Profile> ApplicationProfiles { get => _applicationProfiles; set { _applicationProfiles = value; OnPropertyChanged(); } }


        [JsonProperty]
        public DispatchingObservableCollection<Display> Displays { get => _displays; set { _displays = value; OnPropertyChanged(); } }

        [JsonProperty]
        public DispatchingObservableCollection<ProfileActionShortcut> ActionShortcuts { get => _actionShortcuts; set { _actionShortcuts = value; OnPropertyChanged(); } }

        [JsonProperty]
        public Size WindowSize { get => _windowSize; set { _windowSize = value; OnPropertyChanged(); } }


        public UserAppSettings()
        {
            ApplicationProfileAssignments = new SortableObservableCollection<ApplicationProfileAssignment>(new ObservableCollection<ApplicationProfileAssignment>());
            ApplicationProfiles = new DispatchingObservableCollection<Profile>();
            ActionShortcuts = new DispatchingObservableCollection<ProfileActionShortcut>();
            Displays = new DispatchingObservableCollection<Display>();
            ObsTestCommand = new RelayCommand(TestObsConnection);
        }

        /// <summary>Off the UI thread: reaching OBS has a timeout measured in seconds.</summary>
        private void TestObsConnection()
        {
            ObsTestResult = ProjectLocales.ObsTesting;
            System.Threading.Tasks.Task.Run(() =>
            {
                string message;
                ObsStudio.Test(out message);
                ObsTestResult = message;
            });
            RefreshObsInstall();
        }

        /// <summary>
        /// Re-reads where OBS is installed and whether it is running. Off the UI thread as well: it
        /// reads the registry and enumerates processes. Called when the settings page opens and
        /// whenever the connection is tested.
        /// </summary>
        public void RefreshObsInstall()
        {
            System.Threading.Tasks.Task.Run(() => { ObsInstallStatus = ObsInstall.Describe(); });
        }

        public static UserAppSettings ReadSettings(string path)
        {
            UserAppSettings settings = null;

            lock (_settingsLock)
            {

                try
            {
                    string serializedJson = File.ReadAllText(path);
                    serializedJson = UpgradeJson(serializedJson);
                    settings = (UserAppSettings)JsonConvert.DeserializeObject<UserAppSettings>(serializedJson, new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Objects,
                        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple
                    });
                }
                catch (Exception ex)
                {
                    try
                    {
                        settings = TryReadXML(path);
                        Globals.Logs.Add("Loaded deprecated xml settings.", false);
                        return settings;
                    }
                    catch (Exception)
                    {
                    }
                    Globals.Logs.AddException(ex);
                    throw;
                }
            }
            return settings;
        }

        private static string UpgradeJson(string serializedJson)
        {
            serializedJson = serializedJson.Replace("AutoHDR", "AutoActions");
            serializedJson = serializedJson.Replace("\"$type\": \"AutoActions.Displays.Display, AutoActions\"", "\"$type\": \"AutoActions.Displays.Display, AutoActions.Displays\"");
            // The program assembly is arzGUI.exe now; everything an older version wrote names
            // the types in it "..., AutoActions". Runs after the Displays line above, which is
            // the one type that moved to its own assembly.
            serializedJson = serializedJson.Replace(", AutoActions\"", ", arzGUI\"");
            serializedJson = serializedJson.Replace("\"Monitors\": [", "\"Displays\": [");
            serializedJson = serializedJson.Replace("\"SetHDR\":", "\"ChangeHDR\":");
            serializedJson = serializedJson.Replace("\"SetResolution\":", "\"ChangeResolution\":");
            serializedJson = serializedJson.Replace("\"SetRefreshRate\":", "\"ChangeRefreshRate\":");
            serializedJson = serializedJson.Replace("\"SetColorDepth\":", "\"ChangeColorDepth\":");
            serializedJson = serializedJson.Replace("\"SetOutput\":", "\"ChangePlaybackDevice\":");
            serializedJson = serializedJson.Replace("\"SetInput\":", "\"ChangeRecordDevice\":");
            serializedJson = serializedJson.Replace("\"OutputDeviceID\":", "\"PlaybackDeviceID\":");
            serializedJson = serializedJson.Replace("\"InputDeviceID\":", "\"RecordDeviceID\":");

            return serializedJson;
        }

        private static UserAppSettings TryReadXML(string path)
        {
            UserAppSettings settings = null;
            XmlSerializer serializer = new XmlSerializer(typeof(UserAppSettings));
            using (TextReader reader = new StreamReader(path))
            {
                settings = (UserAppSettings)serializer.Deserialize(reader);
            }
            return settings;
        }

        public static void SaveSettings(UserAppSettings settings, string path)
        {
            lock (_settingsLock)
            {
                try
                {
                    string serializedJson = JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Objects,
                        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple
                    });
                    File.WriteAllText(path, serializedJson);
                }
                catch (Exception ex)
                {
                    Globals.Logs.AddException(ex);
                    throw;
                }
            }
        }
    }

    public static class UserAppSettingsExtension
    {

        public static void SaveSettings(this UserAppSettings settings, string path)
        {
            UserAppSettings.SaveSettings(settings, path);
        }

    }

}
