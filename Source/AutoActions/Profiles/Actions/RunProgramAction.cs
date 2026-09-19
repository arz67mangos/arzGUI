using AutoActions.Displays;
using AutoActions.ProjectResources;
using CodectoryCore.UI.Wpf;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace AutoActions.Profiles.Actions
{
    [JsonObject(MemberSerialization.OptIn)]
    public class RunProgramAction : ProfileActionBase
    {

        public override bool CanSave => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
        public override string CannotSaveMessage => ProjectLocales.MessageMissingFile;
        public override string ActionTypeName => ProjectResources.ProjectLocales.RunAction;


        private string _filePath = "";

        [JsonProperty]
        public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(); } }

        private string _arguments = "";

        [JsonProperty]
        public string Arguments { get => _arguments; set { _arguments = value; OnPropertyChanged(); } }

        private bool _waitForEnd = false;

        [JsonProperty]
        public bool WaitForEnd { get => _waitForEnd; set { _waitForEnd = value; OnPropertyChanged(); } }

        private bool _onlyIfNotRunning = true;

        /// <summary>
        /// Skip the action when the program is already running - starting a second OBS, launcher or
        /// tray helper is either an error dialog or a duplicate. On by default, including for settings
        /// files written before this existed, because a second copy is almost never what was meant.
        /// </summary>
        [JsonProperty]
        public bool OnlyIfNotRunning { get => _onlyIfNotRunning; set { _onlyIfNotRunning = value; OnPropertyChanged(); } }

        private bool _runAsAdministrator = false;

        /// <summary>
        /// Keep arzGUI's administrator rights instead of handing the program back to the logged-on
        /// user. For the programs that want them (OBS Studio, for frame-drop-free encoding); when
        /// arzGUI is not elevated this asks for elevation, which means a UAC prompt.
        /// </summary>
        [JsonProperty]
        public bool RunAsAdministrator { get => _runAsAdministrator; set { _runAsAdministrator = value; OnPropertyChanged(); } }


        public override string ActionDescription => $"{Path.GetFileName(FilePath)} {Arguments}";

        /// <summary>The name <see cref="OnlyIfNotRunning"/> looks for: the file name without its extension.</summary>
        private string ProcessName => Path.GetFileNameWithoutExtension(FilePath);

        public RelayCommand GetFileCommand { get; private set; }

        public RelayCommand UseObsCommand { get; private set; }

        /// <summary>
        /// Whether the button that fills in OBS is worth showing. Read once when the action is opened
        /// - OBS is not going to be installed halfway through editing an action.
        /// </summary>
        public bool ObsIsInstalled => !string.IsNullOrEmpty(Obs.ObsInstall.FindExecutable());

        public RunProgramAction() : base()
        {
            GetFileCommand = new RelayCommand(GetFile);
            UseObsCommand = new RelayCommand(UseObs);
        }

        /// <summary>
        /// Fills in the installed OBS, so nobody has to find obs64.exe four folders deep. It ticks
        /// "only if not running" too, because starting a second OBS is an error dialog, not a second
        /// OBS - but leaves administrator alone: that is a choice about performance, and it costs a
        /// UAC prompt when arzGUI is not already elevated.
        /// </summary>
        private void UseObs()
        {
            string path = Obs.ObsInstall.FindExecutable();
            if (string.IsNullOrEmpty(path))
                return;
            FilePath = path;
            OnlyIfNotRunning = true;
        }

        public override ActionEndResult RunAction(ApplicationChangedType applicationChangedType)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    CallNewLog(new CodectoryCore.Logging.LogEntry($"File {FilePath} doesn't exist.", CodectoryCore.Logging.LogEntryType.Error));
                    return new ActionEndResult(false);
                }
                if (OnlyIfNotRunning && IsRunning())
                {
                    CallNewLog(new CodectoryCore.Logging.LogEntry($"{ProcessName} is already running, not starting it again."));
                    return new ActionEndResult(true);
                }
                CallNewLog(new CodectoryCore.Logging.LogEntry($"Starting {FilePath}"));

                // A child inherits our token, so an elevated arzGUI would start the program as
                // administrator - which breaks programs that refuse to run that way.
                int processId = 0;
                if (!RunAsAdministrator && MonitorDeviceControl.IsElevated)
                {
                    string error;
                    processId = Windows.UnelevatedProcess.Start(FilePath, Arguments, out error);
                    if (processId == 0)
                        CallNewLog(new CodectoryCore.Logging.LogEntry($"Could not start {FilePath} as the logged-on user ({error}). It will inherit arzGUI's administrator rights.", CodectoryCore.Logging.LogEntryType.Error));
                    else
                        CallNewLog(new CodectoryCore.Logging.LogEntry($"Started {FilePath} as the logged-on user, not as administrator."));
                }

                if (processId == 0)
                {
                    using (Process proc = new Process())
                    {
                        proc.StartInfo = new ProcessStartInfo(FilePath);
                        if (!string.IsNullOrEmpty(Arguments))
                            proc.StartInfo.Arguments = Arguments;
                        // Otherwise the program inherits arzGUI's directory; OBS for one refuses to
                        // start from anywhere but its own.
                        string folder = Path.GetDirectoryName(FilePath);
                        if (!string.IsNullOrEmpty(folder))
                            proc.StartInfo.WorkingDirectory = folder;
                        if (RunAsAdministrator)
                        {
                            // Silent when arzGUI is already elevated, a UAC prompt when it is not.
                            proc.StartInfo.UseShellExecute = true;
                            proc.StartInfo.Verb = "runas";
                        }

                        proc.Start();
                        if (WaitForEnd)
                        {
                            CallNewLog(new CodectoryCore.Logging.LogEntry($"Wait for end of {FilePath}"));
                            proc.WaitForExit();
                            CallNewLog(new CodectoryCore.Logging.LogEntry($"Process {FilePath} ended."));
                        }
                    }
                }
                else if (WaitForEnd)
                {
                    CallNewLog(new CodectoryCore.Logging.LogEntry($"Wait for end of {FilePath}"));
                    try
                    {
                        // Gone already is the same as ended, and that is all we are waiting for.
                        Process.GetProcessById(processId).WaitForExit();
                    }
                    catch (ArgumentException) { }
                    CallNewLog(new CodectoryCore.Logging.LogEntry($"Process {FilePath} ended."));
                }
                return new ActionEndResult(true);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                CallNewLog(new CodectoryCore.Logging.LogEntry($"Starting {FilePath} as administrator was refused at the UAC prompt.", CodectoryCore.Logging.LogEntryType.Error));
                return new ActionEndResult(false, ex.Message, ex);
            }
            catch (Exception ex)
            {
                CallNewLog(new CodectoryCore.Logging.LogEntry($"{ ex.Message }\r\n{ ex.StackTrace}", CodectoryCore.Logging.LogEntryType.Error));
                return new ActionEndResult(false, ex.Message, ex);
            }
        }

        private bool IsRunning()
        {
            string name = ProcessName;
            // By name only: the full path of an elevated process cannot be read from a normal one,
            // and OBS is exactly that case.
            return !string.IsNullOrEmpty(name) && Process.GetProcessesByName(name).Length > 0;
        }

        public void GetFile()
        {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".exe";
            fileDialog.Filter = "Executables (.exe)|*.exe| All Files| *.*";
            Nullable<bool> result = fileDialog.ShowDialog();
            string filePath = string.Empty;
            if (result == true)
                filePath = fileDialog.FileName;
            else
                return;
            if (!File.Exists(filePath))
                throw new Exception("Invalid file path.");
            FilePath = filePath;
        }

    }
}
