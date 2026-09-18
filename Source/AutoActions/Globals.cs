using AutoActions.Info;
using AutoActions.Profiles;
using CodectoryCore;
using CodectoryCore.Logging;
using CodectoryCore.UI.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AutoActions
{
    public class Globals : BaseViewModel
    {

        public static Logs Logs = new Logs($"{System.AppDomain.CurrentDomain.BaseDirectory}arzGUI.log", "arzGUI", Assembly.GetExecutingAssembly().GetName().Version.ToString(), false);

        /// <summary>
        /// Process watcher poll interval. The enumeration itself is the app's whole idle cost, so
        /// this trades reaction time against it: at 1000 ms the watcher sits at roughly 1.5% of one
        /// core with nothing registered running, half what 500 ms cost, and notices an application
        /// starting or closing up to a second later.
        /// </summary>
        public static int GlobalRefreshInterval = 1000;

        /// <summary>
        /// Settings live in %AppData%\arzGUI, not next to the exe. Every update is a new folder,
        /// and profiles, applications, hotkeys and presets have to survive that.
        /// </summary>
        public static string SettingsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "arzGUI");

        private string SettingsPathCompatible => $"{System.AppDomain.CurrentDomain.BaseDirectory}UserSettings.xml";

        private string SettingsPath => Path.Combine(SettingsFolder, "UserSettings.json");

        /// <summary>Where older versions kept their settings, and where a file can be dropped to import it.</summary>
        private string LocalSettingsPath => $"{System.AppDomain.CurrentDomain.BaseDirectory}UserSettings.json";

        /// <summary>What the same folder was called while the program was named ArzFlow.</summary>
        private string PreviousSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArzFlow", "UserSettings.json");


        public static Globals Instance = new Globals();

        private UserAppSettings _settings;
        public UserAppSettings Settings { get => _settings; set { _settings = value; OnPropertyChanged(); } }
        public bool SettingsLoadedOnce { get; private set; } = false;

        public event EventHandler SettingsLoaded;

        public void SaveSettings(bool force = false)
        {
            if (!force && !SettingsLoadedOnce)
                return;
            Globals.Logs.Add("Saving settings..", false);
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                Settings.SaveSettings(SettingsPath);
                Globals.Logs.Add("Settings saved", false);
            }
            catch (Exception ex)
            {
                Globals.Logs.AddException(ex);
            }
        }

        public void LoadSettings()
        {
            try
            {
                Globals.Logs.Add("Loading settings...", false);
                ImportLocalSettings();
                if (File.Exists(SettingsPath))
                {
                    Settings = UserAppSettings.ReadSettings(SettingsPath);
                    SettingsLoadedOnce = true;
                }
                else if (File.Exists(SettingsPathCompatible))
                {
                    Settings = UserAppSettings.ReadSettings(SettingsPathCompatible);
                    SettingsLoadedOnce = true;
                }
                else
                {
                    Globals.Logs.Add("No settings found. Creating settings file...", false);
                    Settings = new UserAppSettings();
                    Settings.ApplicationProfiles.Add(Profile.DefaultProfile());
                   SettingsLoadedOnce = true;
                }
                FixAssignments();
                SaveSettings();
                SettingsLoaded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                string backupFile = Path.Combine(SettingsFolder, $"Backup_Settings_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.json");
                if (File.Exists(SettingsPath))
                {
                    File.Move(SettingsPath, backupFile);
                    Globals.Logs.Add($"Created backup of invalid settings file: {backupFile}", false);
                    File.Delete(SettingsPath);
                }
                Globals.Logs.Add("Failed to load settings", false);
                Globals.Logs.AddException(ex);
                Settings = new UserAppSettings();
                SaveSettings(true);
                Globals.Logs.Add("Created new settings file", false);
            }
            Globals.Logs.LogFileEnabled = Settings.CreateLogFile;
            Globals.Logs.Add("Settings loaded", false);
        }

        /// <summary>
        /// Takes over a UserSettings.json sitting next to the exe: one an older version wrote, or
        /// one copied there on purpose to carry a setup over. It is renamed afterwards, so this
        /// happens once and an update never silently overwrites the settings it just imported.
        /// </summary>
        private void ImportLocalSettings()
        {
            try
            {
                if (!File.Exists(LocalSettingsPath))
                {
                    // Renaming the program renamed its settings folder; bring the old one across.
                    if (!File.Exists(SettingsPath) && File.Exists(PreviousSettingsPath))
                    {
                        Directory.CreateDirectory(SettingsFolder);
                        File.Copy(PreviousSettingsPath, SettingsPath, false);
                        Globals.Logs.Add($"Took over the settings of the previous name from {PreviousSettingsPath}", false);
                    }
                    return;
                }
                Directory.CreateDirectory(SettingsFolder);
                File.Copy(LocalSettingsPath, SettingsPath, true);
                string imported = $"{System.AppDomain.CurrentDomain.BaseDirectory}UserSettings.imported.json";
                if (File.Exists(imported))
                    File.Delete(imported);
                File.Move(LocalSettingsPath, imported);
                Globals.Logs.Add($"Imported settings from {LocalSettingsPath} into {SettingsPath}", false);
            }
            catch (Exception ex)
            {
                Globals.Logs.Add("Could not import the settings file next to the program.", false);
                Globals.Logs.AddException(ex);
            }
        }

        private void FixAssignments()
        {
            int count = Settings.ApplicationProfileAssignments.Count;
            for (int i = 0; i < count; i++)
            {
                int positionCount = Settings.ApplicationProfileAssignments.Count(a => a.Position == i);
                if (positionCount == 0)
                {
                    int u = i;
                    while (Settings.ApplicationProfileAssignments.Count(a => a.Position == i) == 0)
                    {
                        var assignemnt = Settings.ApplicationProfileAssignments.FirstOrDefault(a => a.Position == u);
                        if (assignemnt != null)
                            assignemnt.Position = i;
                        u++;
                    }
                }
                if (positionCount > 1)
                    Settings.ApplicationProfileAssignments.First(a => a.Position == i).Position = i + 1;
            }
            while (Settings.ApplicationProfileAssignments.Any(a => a.Position >= count))
            {
                foreach (var assignment in Settings.ApplicationProfileAssignments)
                    if (assignment.Position >= count)
                        do
                        {
                            assignment.Position = assignment.Position - 1;
                        } while (Settings.ApplicationProfileAssignments.Count(a => a.Position == assignment.Position) > 1);
            }
        }



        public void ShowInfo()
        {
            if (DialogService != null)
                DialogService.ShowDialogModal(new AutoActionsInfo(), new System.Drawing.Size(460, 440));
        }
    }
}
