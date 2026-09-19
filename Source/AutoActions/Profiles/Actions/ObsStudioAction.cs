using AutoActions.Obs;
using AutoActions.ProjectResources;
using CodectoryCore.Logging;
using CodectoryCore.UI.Wpf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutoActions.Profiles.Actions
{
    /// <summary>
    /// Switches the OBS Studio profile, scene collection and/or program scene over obs-websocket.
    /// A name left empty is left alone. Works whether or not OBS runs as administrator - see
    /// <see cref="ObsWebSocket"/> for why that is not a problem here.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ObsStudioAction : ProfileActionBase
    {
        private string _profileName = string.Empty;
        private string _sceneCollectionName = string.Empty;
        private string _sceneName = string.Empty;
        private int _waitForObsSeconds = 15;

        private List<string> _obsProfiles = new List<string>();
        private List<string> _obsSceneCollections = new List<string>();
        private List<string> _obsScenes = new List<string>();
        private string _refreshStatus = string.Empty;
        private bool _isRefreshing = false;
        private string _currentProfile = string.Empty;
        private string _currentSceneCollection = string.Empty;
        private string _currentScene = string.Empty;

        [JsonProperty]
        public string ProfileName { get => _profileName; set { _profileName = value ?? string.Empty; OnPropertyChanged(); } }

        [JsonProperty]
        public string SceneCollectionName { get => _sceneCollectionName; set { _sceneCollectionName = value ?? string.Empty; OnPropertyChanged(); } }

        [JsonProperty]
        public string SceneName { get => _sceneName; set { _sceneName = value ?? string.Empty; OnPropertyChanged(); } }

        /// <summary>
        /// How long to keep trying to reach OBS. A profile that starts OBS and then switches its scene
        /// gets here while OBS is still loading. 0 means give up at once.
        /// </summary>
        [JsonProperty]
        public int WaitForObsSeconds { get => _waitForObsSeconds; set { _waitForObsSeconds = Math.Max(0, Math.Min(120, value)); OnPropertyChanged(); } }

        public List<string> ObsProfiles { get => _obsProfiles; private set { _obsProfiles = value; OnPropertyChanged(); } }

        public List<string> ObsSceneCollections { get => _obsSceneCollections; private set { _obsSceneCollections = value; OnPropertyChanged(); } }

        public List<string> ObsScenes { get => _obsScenes; private set { _obsScenes = value; OnPropertyChanged(); } }

        public string RefreshStatus { get => _refreshStatus; private set { _refreshStatus = value; OnPropertyChanged(); } }

        public bool IsRefreshing { get => _isRefreshing; private set { _isRefreshing = value; OnPropertyChanged(); } }

        public string CurrentProfile { get => _currentProfile; private set { _currentProfile = value; OnPropertyChanged(); OnPropertyChanged(nameof(ObsIsOn)); } }

        public string CurrentSceneCollection { get => _currentSceneCollection; private set { _currentSceneCollection = value; OnPropertyChanged(); OnPropertyChanged(nameof(ObsIsOn)); } }

        public string CurrentScene { get => _currentScene; private set { _currentScene = value; OnPropertyChanged(); OnPropertyChanged(nameof(ObsIsOn)); } }

        /// <summary>What OBS was on at the last refresh - the quickest way to see it answered at all.</summary>
        public string ObsIsOn => string.IsNullOrEmpty(CurrentScene) && string.IsNullOrEmpty(CurrentProfile)
            ? string.Empty
            : $"{ProjectLocales.ObsIsOn} {CurrentProfile} / {CurrentSceneCollection} / {CurrentScene}";

        public RelayCommand RefreshCommand { get; private set; }

        public override bool CanSave => !string.IsNullOrWhiteSpace(ProfileName)
            || !string.IsNullOrWhiteSpace(SceneCollectionName)
            || !string.IsNullOrWhiteSpace(SceneName);

        public override string CannotSaveMessage => ProjectLocales.MessageMissingObsSetting;
        public override string ActionTypeName => ProjectLocales.ObsStudioAction;

        public override string ActionDescription
        {
            get
            {
                List<string> parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(ProfileName))
                    parts.Add($"{ProjectLocales.ObsProfile}: {ProfileName}");
                if (!string.IsNullOrWhiteSpace(SceneCollectionName))
                    parts.Add($"{ProjectLocales.ObsSceneCollection}: {SceneCollectionName}");
                if (!string.IsNullOrWhiteSpace(SceneName))
                    parts.Add($"{ProjectLocales.ObsScene}: {SceneName}");
                return $"[{string.Join(", ", parts)}]";
            }
        }

        public ObsStudioAction() : base()
        {
            RefreshCommand = new RelayCommand(Refresh);
        }

        /// <summary>
        /// Fills the drop-downs from a running OBS. Off the UI thread: the connection has a timeout
        /// measured in seconds. The lists are plain List&lt;string&gt;, so WPF marshals the change for us.
        /// </summary>
        public void Refresh()
        {
            RefreshStatus = ProjectLocales.ObsRefreshing;
            IsRefreshing = true;
            Task.Run(() =>
            {
                try
                {
                    string error;
                    ObsSnapshot snapshot = ObsStudio.Read(out error);
                    if (snapshot == null)
                    {
                        RefreshStatus = error;
                        return;
                    }
                    ObsProfiles = snapshot.Profiles;
                    ObsSceneCollections = snapshot.SceneCollections;
                    ObsScenes = snapshot.Scenes;
                    CurrentProfile = snapshot.CurrentProfile;
                    CurrentSceneCollection = snapshot.CurrentSceneCollection;
                    CurrentScene = snapshot.CurrentScene;
                    RefreshStatus = string.Empty;
                }
                finally
                {
                    IsRefreshing = false;
                }
            });
        }

        public override ActionEndResult RunAction(ApplicationChangedType applicationChangedType)
        {
            try
            {
                string error;
                if (ObsStudio.Apply(ProfileName, SceneCollectionName, SceneName, WaitForObsSeconds,
                        message => CallNewLog(new LogEntry(message)), out error))
                    return new ActionEndResult(true);

                CallNewLog(new LogEntry($"OBS Studio action skipped: {error}", LogEntryType.Error));
                return new ActionEndResult(false, error, null);
            }
            catch (Exception ex)
            {
                CallNewLog(new LogEntry($"{ex.Message}\r\n{ex.StackTrace}", LogEntryType.Error));
                return new ActionEndResult(false, ex.Message, ex);
            }
        }
    }
}
