using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AutoActions.ProjectResources;
using AutoActions.Theming;

namespace AutoActions
{
    /// <summary>
    /// Interaktionslogik für "App.xaml"
    /// </summary>
    ///
    public partial class App : Application
    {
        public static Theme Theme { get; set; } = Theme.Light;

        static Mutex mutex;

        /// <summary>Passed to the elevated copy by MonitorDeviceAction.RestartAsAdministrator.</summary>
        public const string RestartArgument = "--restart";

        static readonly string CrashFilePath = $"{AppDomain.CurrentDomain.BaseDirectory}arzGUI.crash.log";

        [STAThread]
        public static void Main()
         {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
         bool createNew = false;
            mutex = new Mutex(true, "{2846416C-610B-4A6B-A31C-A4AA6826E9BE}", out createNew);
            // A copy started by "restart as administrator" waits for the one that launched it to let
            // go of the mutex, instead of telling the user AutoActions is already running.
            bool restarting = Environment.GetCommandLineArgs().Any(a => string.Equals(a, RestartArgument, StringComparison.OrdinalIgnoreCase));
            if (mutex.WaitOne(restarting ? TimeSpan.FromSeconds(20) : TimeSpan.Zero, true))
            {
                var application = new App();
                application.DispatcherUnhandledException += Application_DispatcherUnhandledException;
                application.InitializeComponent();
                Globals.Instance.LoadSettings();
                application.Run();
            }
            else
            {
                MessageBox.Show(ProjectLocales.AlreadyRunning);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeManager.Apply(Globals.Instance.Settings.Theme);
            ThemeManager.FollowSystem(() => Globals.Instance.Settings.Theme);
            Views.AutoActionsMainView mainView = new Views.AutoActionsMainView();
            if (!Globals.Instance.Settings.StartMinimizedToTray)
                mainView.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (mutex.WaitOne(TimeSpan.Zero, true))
                mutex.ReleaseMutex();

            // The user asked to quit, so quit: a native or COM thread that outlives the dispatcher
            // would otherwise leave a running arzGUI.exe holding its own folder open, which
            // looks like the app ignored Exit.
            Environment.Exit(e.ApplicationExitCode);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            string description = ex != null ? ex.ToString() : Convert.ToString(e.ExceptionObject);
            WriteCrashReport(e.IsTerminating ? "AppDomain.UnhandledException (terminating)" : "AppDomain.UnhandledException", description);
        }

        private static void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Deliberately not marked Handled: WPF still shuts the application down as before,
            // but the log and the crash file now record why.
            WriteCrashReport("Dispatcher.UnhandledException", e.Exception.ToString());
        }

        /// <summary>
        /// Records a fatal exception in the normal log and in arzGUI.crash.log next to the exe.
        /// The crash file is written unconditionally, because the normal log file is optional and
        /// the process is about to die.
        /// </summary>
        private static void WriteCrashReport(string source, string description)
        {
            try
            {
                Globals.Logs.Add($"FATAL [{source}]: {description}", false);
            }
            catch (Exception)
            {
            }
            try
            {
                string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                Thread thread = Thread.CurrentThread;
                StringBuilder report = new StringBuilder();
                report.AppendLine("==================================================");
                report.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  AutoActions {version}  {source}");
                report.AppendLine($"Thread {thread.ManagedThreadId} \"{thread.Name}\"");
                report.AppendLine(description);
                report.AppendLine();
                File.AppendAllText(CrashFilePath, report.ToString());
            }
            catch (Exception)
            {
            }
        }
    }
}
