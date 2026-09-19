using AutoActions.Displays;
using AutoActions.Profiles.Actions;
using CodectoryCore.UI.Wpf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace AutoActions.Profiles
{
    public enum ProfileActionListType
    {
        None,
        Started,
        Closed,
        GotFocus,
        LostFocus
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Profile : BaseViewModel, IEquatable<Profile>
    {

        public static Profile DefaultProfile()
        {
            Profiles.Profile defaultProfile = new Profiles.Profile();
            defaultProfile.Name = "HDR";
            Profiles.Actions.DisplayAction startAction = new Profiles.Actions.DisplayAction();
            startAction.Display = Display.AllDisplays;
            startAction.ChangeHDR = true;
            startAction.EnableHDR = true;
            Profiles.Actions.DisplayAction endAction = new Profiles.Actions.DisplayAction();
            endAction.Display = Display.AllDisplays;
            endAction.ChangeHDR = true;
            endAction.EnableHDR = false;
            defaultProfile.ApplicationStarted.Add(startAction);
            defaultProfile.ApplicationClosed.Add(endAction);
            return defaultProfile;
        }


        public RelayCommand AddStartedActionCommand { get; private set; }
        public RelayCommand AddClosedActionCommand { get; private set; }
        public RelayCommand AddGotFocusActionCommand { get; private set; }
        public RelayCommand AddLostFocusActionCommand { get; private set; }

        public RelayCommand<ProfileActionBase> EditProfileActionCommand { get; private set; }
        public RelayCommand<ProfileActionBase> RemoveProfileActionCommand { get; private set; }
        public RelayCommand<ProfileActionBase> MoveProfileActionUpCommand { get; private set; }
        public RelayCommand<ProfileActionBase> MoveProfileActionDownCommand { get; private set; }
        public RelayCommand<ProfileActionBase> DuplicateProfileActionCommand { get; private set; }
        public RelayCommand<ProfileActionListType> TestProfileActionsCommand { get; private set; }



        public Profile()
        {
            _guid = Guid.NewGuid();
            AddStartedActionCommand = new RelayCommand(() => AddProfileAction(ProfileActionListType.Started));
            AddClosedActionCommand = new RelayCommand(() => AddProfileAction(ProfileActionListType.Closed));
            AddGotFocusActionCommand = new RelayCommand(() => AddProfileAction(ProfileActionListType.GotFocus));
            AddLostFocusActionCommand = new RelayCommand(() => AddProfileAction(ProfileActionListType.LostFocus));
            EditProfileActionCommand = new RelayCommand<ProfileActionBase>((pa) => EditProfileAction(pa));
            RemoveProfileActionCommand = new RelayCommand<ProfileActionBase>((pa) => RemoveProfileAction(pa));
            // The predicates grey out the arrows at the ends of a lane, where the button would be
            // live but do nothing. RelayCommand's CanExecuteChanged is the CommandManager's, so a
            // move re-queries them by itself.
            MoveProfileActionUpCommand = new RelayCommand<ProfileActionBase>((pa) => MoveProfileAction(pa, -1), (pa) => CanMoveProfileAction(pa as ProfileActionBase, -1));
            MoveProfileActionDownCommand = new RelayCommand<ProfileActionBase>((pa) => MoveProfileAction(pa, 1), (pa) => CanMoveProfileAction(pa as ProfileActionBase, 1));
            DuplicateProfileActionCommand = new RelayCommand<ProfileActionBase>((pa) => DuplicateProfileAction(pa));
            TestProfileActionsCommand = new RelayCommand<ProfileActionListType>((lt) => TestProfileActions(lt), (lt) => !IsTesting);
        }

        private Guid _guid = Guid.Empty;

        [JsonProperty]
        public Guid GUID
        {
            get { return _guid; }
            set { if (value.Equals(Guid.Empty)) _guid = Guid.NewGuid(); _guid = value; OnPropertyChanged(); }
        }

        private string _name = "-";

        [JsonProperty]
        public string Name
        {
            get { return _name; }
            set { _name = value; OnPropertyChanged(); }
        }

        private ListOfProfileActions _applicationStarted = new ListOfProfileActions();

        [JsonProperty]
        public ListOfProfileActions ApplicationStarted
        {
            get { return _applicationStarted; }
            set { _applicationStarted = value; OnPropertyChanged(); }
        }

        private ListOfProfileActions _applicationClosed = new ListOfProfileActions();

        [JsonProperty]
        public ListOfProfileActions ApplicationClosed
        {
            get { return _applicationClosed; }
            set { _applicationClosed = value; OnPropertyChanged(); }
        }

        private ListOfProfileActions _applicationGotFocus = new ListOfProfileActions();

        [JsonProperty]
        public ListOfProfileActions ApplicationGotFocus
        {
            get { return _applicationGotFocus; }
            set { _applicationGotFocus = value; OnPropertyChanged(); }
        }

        private ListOfProfileActions _applicationLostFocus = new ListOfProfileActions();

        [JsonProperty]
        public ListOfProfileActions ApplicationLostFocus
        {
            get { return _applicationLostFocus; }
            set { _applicationLostFocus = value; OnPropertyChanged(); }


        }

        private bool _restartApplication = false;

        [JsonProperty]
        public bool RestartApplication { get => _restartApplication; set { _restartApplication = value; OnPropertyChanged(); } }




        public void AddProfileAction(ProfileActionListType listType)
        {
            ProfileActionAdder adder = new ProfileActionAdder();
            adder.DialogService = DialogService;
            adder.OKClicked += (o, e) =>
            {
                switch (listType)
                {
                    case ProfileActionListType.Started:
                        ApplicationStarted.Add(adder.ProfileAction);
                        break;
                    case ProfileActionListType.Closed:
                        ApplicationClosed.Add(adder.ProfileAction);
                        break;
                    case ProfileActionListType.GotFocus:
                        ApplicationGotFocus.Add(adder.ProfileAction);
                        break;
                    case ProfileActionListType.LostFocus:
                        ApplicationLostFocus.Add(adder.ProfileAction);
                        break;

                }
            };
            if (DialogService != null)
                DialogService.ShowDialogModal(adder, new System.Drawing.Size(800, 600));
        }

        public void EditProfileAction(ProfileActionBase profileAction)
        {
            ProfileActionListType listType = GetProfileActionListType(profileAction);
            ProfileActionAdder adder = new ProfileActionAdder(profileAction);

            adder.DialogService = DialogService;
            if (DialogService != null)
                DialogService.ShowDialogModal(adder, new System.Drawing.Size(800, 600));
        }

        private ProfileActionListType GetProfileActionListType(ProfileActionBase profileAction)
        {
            if (ApplicationStarted.Contains(profileAction))
                return ProfileActionListType.Started;
            if (ApplicationClosed.Contains(profileAction))
                return ProfileActionListType.Closed;
            if (ApplicationGotFocus.Contains(profileAction))
                return ProfileActionListType.GotFocus;
            if (ApplicationLostFocus.Contains(profileAction))
                return ProfileActionListType.LostFocus;
            return ProfileActionListType.None;
        }

        public ListOfProfileActions GetProfileActions(ProfileActionListType listType)
        {
            switch (listType)
            {
                case ProfileActionListType.Started:
                    return ApplicationStarted;
                case ProfileActionListType.Closed:
                    return ApplicationClosed;
                case ProfileActionListType.GotFocus:
                    return ApplicationGotFocus;
                case ProfileActionListType.LostFocus:
                    return ApplicationLostFocus;
                default:
                    return new ListOfProfileActions();

            }
        }

        /// <summary>
        /// Moves an action one place up or down inside whichever list it is in. The order of a list is
        /// the order the daemon runs it in, so this is not cosmetic: an OBS action placed above the
        /// run action that starts OBS waits for a program that is not running yet. Moving off either
        /// end does nothing. The collection change saves the settings by itself.
        /// </summary>
        public void MoveProfileAction(ProfileActionBase profileAction, int offset)
        {
            if (profileAction == null)
                return;
            ListOfProfileActions actions = GetProfileActions(GetProfileActionListType(profileAction));
            int index = actions.IndexOf(profileAction);
            int target = index + offset;
            if (index < 0 || target < 0 || target >= actions.Count)
                return;
            actions.Move(index, target);
        }

        /// <summary>Whether there is somewhere for the action to move to. Drives the arrow buttons.</summary>
        public bool CanMoveProfileAction(ProfileActionBase profileAction, int offset)
        {
            if (profileAction == null)
                return false;
            ListOfProfileActions actions = GetProfileActions(GetProfileActionListType(profileAction));
            int index = actions.IndexOf(profileAction);
            int target = index + offset;
            return index >= 0 && target >= 0 && target < actions.Count;
        }

        /// <summary>
        /// Copies an action and puts the copy directly under the original, in the same list. The copy
        /// goes through the serialiser, so it shares nothing with the action it came from - editing
        /// one does not change the other.
        /// </summary>
        public void DuplicateProfileAction(ProfileActionBase profileAction)
        {
            if (profileAction == null)
                return;
            ListOfProfileActions actions = GetProfileActions(GetProfileActionListType(profileAction));
            int index = actions.IndexOf(profileAction);
            if (index < 0)
                return;
            ProfileActionBase copy = DeepCopy.Of(profileAction);
            if (copy != null)
                actions.Insert(index + 1, copy);
        }

        private bool _isTesting = false;

        /// <summary>True while <see cref="TestProfileActions"/> is running, so it cannot be started twice.</summary>
        public bool IsTesting
        {
            get { return _isTesting; }
            private set { _isTesting = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Runs one list of actions now, as though the application had just started, closed or changed
        /// focus - so a profile can be tried without launching the game. Off the UI thread: an action
        /// blocks for seconds (a display mode change, waiting for OBS). It deliberately does not take
        /// the daemon's lock, the same way an action shortcut does not; pressing this at the exact
        /// moment an assigned application starts runs both, which is the user's own doing.
        /// </summary>
        public void TestProfileActions(ProfileActionListType listType)
        {
            if (IsTesting)
                return;
            List<IProfileAction> actions = GetProfileActions(listType).ToList();
            ApplicationChangedType changedType = ChangedTypeOf(listType);
            IsTesting = true;
            Task.Run(() =>
            {
                try
                {
                    Globals.Logs.Add($"Test run of '{Name}': {actions.Count} {listType} action(s).", false);
                    foreach (IProfileAction action in actions)
                    {
                        if (!action.Enabled)
                        {
                            Globals.Logs.Add($"Test run: skipping the disabled action {action.ActionTypeName}.", false);
                            continue;
                        }
                        EventHandler<CodectoryCore.Logging.LogEntry> log = (o, e) => Globals.Logs.AppendLogEntry(e);
                        action.NewLog += log;
                        try
                        {
                            ActionEndResult result = action.RunAction(changedType);
                            if (result != null && !result.Success)
                                Globals.Logs.Add($"Test run: {action.ActionTypeName} failed. {result.ErrorInfo}", false, CodectoryCore.Logging.LogEntryType.Error);
                        }
                        finally
                        {
                            action.NewLog -= log;
                        }
                        System.Threading.Thread.Sleep(100);
                    }
                    Globals.Logs.Add($"Test run of '{Name}' finished.", false);
                }
                catch (Exception ex)
                {
                    Globals.Logs.AddException(ex);
                }
                finally
                {
                    IsTesting = false;
                }
            });
        }

        private static ApplicationChangedType ChangedTypeOf(ProfileActionListType listType)
        {
            switch (listType)
            {
                case ProfileActionListType.Started:
                    return ApplicationChangedType.Started;
                case ProfileActionListType.Closed:
                    return ApplicationChangedType.Closed;
                case ProfileActionListType.GotFocus:
                    return ApplicationChangedType.GotFocus;
                case ProfileActionListType.LostFocus:
                    return ApplicationChangedType.LostFocus;
                default:
                    return ApplicationChangedType.None;
            }
        }

        public void RemoveProfileAction(ProfileActionBase profileAction)
        {
            if (ApplicationStarted.Contains(profileAction))
                ApplicationStarted.Remove(profileAction);
            if (ApplicationClosed.Contains(profileAction))
                ApplicationClosed.Remove(profileAction);
            if (ApplicationGotFocus.Contains(profileAction))
                ApplicationGotFocus.Remove(profileAction);
            if (ApplicationLostFocus.Contains(profileAction))
                ApplicationLostFocus.Remove(profileAction);
        }


        public void RemoveProfileAction(ProfileActionListType listType, ProfileActionBase profileAction)
        {
            switch (listType)
            {
                case ProfileActionListType.Started:
                    ApplicationStarted.Remove(profileAction);
                    break;
                case ProfileActionListType.Closed:
                    ApplicationClosed.Remove(profileAction);
                    break;
                case ProfileActionListType.GotFocus:
                    ApplicationGotFocus.Remove(profileAction);
                    break;
                case ProfileActionListType.LostFocus:
                    ApplicationLostFocus.Remove(profileAction);
                    break;

            }

        }

        public override string ToString()
        {
            return $"{Name} {GetHashCode()}";
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Profile);
        }

        public bool Equals(Profile other)
        {
            return other != null &&
                   GUID == other._guid &&
                   EqualityComparer<ListOfProfileActions>.Default.Equals(ApplicationStarted, other.ApplicationStarted) &&
                   EqualityComparer<ListOfProfileActions>.Default.Equals(ApplicationClosed, other.ApplicationClosed) &&
                   EqualityComparer<ListOfProfileActions>.Default.Equals(ApplicationGotFocus, other.ApplicationGotFocus) &&
                   EqualityComparer<ListOfProfileActions>.Default.Equals(ApplicationLostFocus, other.ApplicationLostFocus);
        }

        public override int GetHashCode()
        {
            int hashCode = 210938521;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(GUID.ToString());
            hashCode = hashCode * -1521134295 + EqualityComparer<ListOfProfileActions>.Default.GetHashCode(ApplicationStarted);
            hashCode = hashCode * -1521134295 + EqualityComparer<ListOfProfileActions>.Default.GetHashCode(ApplicationClosed);
            hashCode = hashCode * -1521134295 + EqualityComparer<ListOfProfileActions>.Default.GetHashCode(ApplicationGotFocus);
            hashCode = hashCode * -1521134295 + EqualityComparer<ListOfProfileActions>.Default.GetHashCode(ApplicationLostFocus);
            return hashCode;
        }
    }
}
