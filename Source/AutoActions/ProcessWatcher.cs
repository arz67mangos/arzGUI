using AutoActions.Threading;
using AutoActions.UWP;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActions
{
    public class ProcessWatcher : IManagedThread
    {
        public bool OneProcessIsRunning { get; private set; } = false;
        public bool OneProcessIsFocused { get; private set; } = false;

        Thread _watchProcessThread = null;

        readonly object _applicationsLock = new object();
        readonly object _accessLock = new object();

        Dictionary<ApplicationItem, ApplicationState> _applications = new Dictionary<ApplicationItem, ApplicationState>();
        public IReadOnlyDictionary<ApplicationItem, ApplicationState> Applications
        {
            get
            {
                lock (_applicationsLock)
                    return new ReadOnlyDictionary<ApplicationItem, ApplicationState>(_applications.ToDictionary(entry => entry.Key, entry => entry.Value));
            }
        }

        bool _stopRequested = false;
        bool _isRunning = false;
        public bool IsRunning { get => _isRunning; private set => _isRunning = value; }

        public bool ManagedThreadIsActive => IsRunning;

        public event EventHandler<string> NewLog;
        //public event EventHandler OneProcessIsRunningChanged;
        //public event EventHandler FocusedProcessChanged;
        public event EventHandler<ApplicationChangedEventArgs> ApplicationChanged;

        // An identical error is logged at most once per interval, so a persistent fault
        // (e.g. a UWP handler failing on every tick) cannot flood the log at two ticks per second.
        static readonly TimeSpan ErrorLogThrottle = TimeSpan.FromSeconds(60);
        readonly Dictionary<string, DateTime> _lastErrorLog = new Dictionary<string, DateTime>();


        public ProcessWatcher()

        {
        }


        private void CallNewLog(string logMessage)
        {
            NewLog?.Invoke(this, logMessage);
        }

        private void LogError(string context, Exception ex)
        {
            string key = $"{context}|{ex.GetType().FullName}|{ex.Message}";
            DateTime now = DateTime.UtcNow;
            lock (_lastErrorLog)
            {
                DateTime last;
                if (_lastErrorLog.TryGetValue(key, out last) && now - last < ErrorLogThrottle)
                    return;
                _lastErrorLog[key] = now;
            }
            CallNewLog($"{context}: {ex}");
        }

        public void AddProcess(ApplicationItem application)
        {
            lock (_applicationsLock)
            {
                if (!_applications.ContainsKey(application))
                {
                    _applications.Add(application, ApplicationState.None);
                    CallNewLog($"Application added to process watcher: {application}");
                }
            }
        }

        public void RemoveProcess(ApplicationItem application)
        {
            lock (_applicationsLock)
            {
                if (_applications.ContainsKey(application))
                {
                    _applications.Remove(application);
                    CallNewLog($"Application removed process watcher: {application}");
                }
            }
        }


        public void StartManagedThread()
        {
            if (_stopRequested || IsRunning)
                return;
            lock (_accessLock)
            {
                CallNewLog($"Starting process watcher...");
                //startWatch.Start();
                //stopWatch.Start();
                _isRunning = true;
                _watchProcessThread = new Thread(WatchProcessLoop);
                _watchProcessThread.IsBackground = true;
                _watchProcessThread.Name = "ProcessWatcher";
                _watchProcessThread.Start();
                CallNewLog($"Process watcher started");
            }
        }

        public void StopManagedThread()
        {
            if (_stopRequested || !IsRunning)
                return;
            lock (_accessLock)
            {
                CallNewLog($"Stopping process watcher...");
                _stopRequested = true;
                _watchProcessThread.Join();
                _stopRequested = false;
                _isRunning = false;
                _watchProcessThread = null;
                CallNewLog($"Process watcher stopped.");
            }
        }

        private void WatchProcessLoop()
        {
            while (!_stopRequested)
            {
                try
                {
                    lock (_applicationsLock)
                        UpdateApplications();
                }
                catch (Exception ex)
                {
                    // Nothing may escape this loop: an unhandled exception on this thread
                    // terminates the whole process, silently, with no Closed action ever firing.
                    LogError("Process watcher tick failed (watcher keeps running)", ex);
                }
                Thread.Sleep(Globals.GlobalRefreshInterval);
            }
        }

        private void CallApplicationChanged(ApplicationItem application, ApplicationChangedType changedType)
        {
            ApplicationChanged?.Invoke(this, new ApplicationChangedEventArgs(application,changedType));

        }
        private void UpdateApplications()
        {

            lock (_applicationsLock)
            {
                Process[] processes = null;
                try
                {
                    List<ApplicationItem> applications = _applications.Select(a => a.Key).ToList();

                    processes = Process.GetProcesses();

                    // At most once per tick, and only if some application actually matches: the
                    // lookup costs a GetProcessById (and sometimes an EnumChildWindows), and the
                    // common case - nothing the user registered is running - must not pay for it.
                    int? foregroundProcessId = null;
                    Func<int> foregroundProcess = () =>
                    {
                        if (!foregroundProcessId.HasValue)
                            foregroundProcessId = GetForegroundProcessId();
                        return foregroundProcessId.Value;
                    };
                    DateTime now = DateTime.UtcNow;
                    TimeSpan debounce = FocusDebounce;

                    foreach (ApplicationItem application in applications)
                    {
                        ApplicationState oldState = _applications[application];
                        ApplicationState rawState = ApplicationState.None;
                        foreach (var process in processes)
                        {
                            string processName;
                            if (process.ProcessName == "WWAHost")
                            {
                                processName = UWP.WWAHostHandler.GetProcessName(process.Id);
                            }
                            else
                                processName = process.ProcessName;
                            if (application.ApplicationName.ToUpperInvariant().Equals(processName.ToUpperInvariant())
                                || (application.IsUWP && !string.IsNullOrEmpty(application.UWPIdentity) && processName.Contains(application.UWPIdentity)))
                            {
                                // Any instance in the foreground counts as focused. Testing every
                                // match and keeping the strongest answer matters when an application
                                // runs as several processes - otherwise a background sibling seen
                                // after the focused one would report the application as unfocused.
                                int foreground = foregroundProcess();
                                if (foreground != 0 && process.Id == foreground)
                                {
                                    rawState = ApplicationState.Focused;
                                    break;
                                }
                                rawState = ApplicationState.Running;
                            }
                        }

                        ApplicationState state = ApplyFocusDebounce(application, oldState, rawState, now, debounce);
                        _applications[application] = state;

                        // Kept in the original order, and deliberately not symmetrical: an
                        // application that disappears reports Closed only, never Lost focus first.
                        if (oldState == ApplicationState.None && state != ApplicationState.None)
                            CallApplicationChanged(application, ApplicationChangedType.Started);
                        if (state == ApplicationState.Focused && oldState != ApplicationState.Focused)
                            CallApplicationChanged(application, ApplicationChangedType.GotFocus);
                        if (state == ApplicationState.Running && oldState == ApplicationState.Focused)
                            CallApplicationChanged(application, ApplicationChangedType.LostFocus);
                        if (state == ApplicationState.None && oldState != ApplicationState.None)
                            CallApplicationChanged(application, ApplicationChangedType.Closed);
                    }
                }
                catch (Exception ex)
                {
                    LogError("Updating application states failed", ex);
                }
                finally
                {
                    // Each Process holds a handle. At two enumerations per second these must be
                    // released explicitly instead of waiting for the finalizer.
                    if (processes != null)
                        foreach (Process process in processes)
                            process.Dispose();
                }
            }
        }

        #region Focus debounce

        /// <summary>A focus change that has not held long enough to act on yet.</summary>
        private class PendingFocusChange
        {
            public ApplicationState State;
            public DateTime Since;
        }

        private readonly Dictionary<ApplicationItem, PendingFocusChange> _pendingFocusChanges = new Dictionary<ApplicationItem, PendingFocusChange>();

        private static TimeSpan FocusDebounce
        {
            get
            {
                UserAppSettings settings = Globals.Instance != null ? Globals.Instance.Settings : null;
                return TimeSpan.FromSeconds(settings != null ? settings.FocusDebounceSeconds : 0);
            }
        }

        /// <summary>
        /// Holds a Running/Focused flip until it has been stable for the configured time, so glancing
        /// at another window mid-game does not run the Lost focus list and then the Got focus list a
        /// moment later. Started and Closed are never delayed: a process appearing or disappearing is
        /// not ambiguous, and delaying Closed would leave the display in the application's state.
        /// </summary>
        private ApplicationState ApplyFocusDebounce(ApplicationItem application, ApplicationState oldState, ApplicationState rawState, DateTime now, TimeSpan debounce)
        {
            bool isFocusFlip = rawState != oldState
                && rawState != ApplicationState.None
                && oldState != ApplicationState.None;

            if (!isFocusFlip || debounce <= TimeSpan.Zero)
            {
                _pendingFocusChanges.Remove(application);
                return rawState;
            }

            PendingFocusChange pending;
            if (!_pendingFocusChanges.TryGetValue(application, out pending) || pending.State != rawState)
            {
                _pendingFocusChanges[application] = new PendingFocusChange { State = rawState, Since = now };
                return oldState;
            }
            if (now - pending.Since < debounce)
                return oldState;

            _pendingFocusChanges.Remove(application);
            return rawState;
        }

        #endregion

        /// <summary>
        /// Process id of the foreground window's owner, or 0 when there is none. Called once per tick.
        /// </summary>
        private int GetForegroundProcessId()
        {
            using (Process foregroundProcess = GetForegroundProcess())
                return foregroundProcess == null ? 0 : foregroundProcess.Id;
        }


        /// <summary>
        /// Returns the process owning the foreground window, or null when there is none
        /// (lock screen, UAC prompt, exclusive-fullscreen transitions) or it can no longer
        /// be opened. The caller owns the returned Process and must dispose it.
        /// </summary>
        private Process GetForegroundProcess()
        {
            IntPtr foregroundWindow = WinAPIFunctions.GetforegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return null;
            int pid = WinAPIFunctions.GetWindowProcessId(foregroundWindow);
            if (pid <= 0)
                return null;

            Process foregroundProcess;
            try
            {
                foregroundProcess = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                // Process exited between GetWindowThreadProcessId and GetProcessById.
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            if (foregroundProcess.ProcessName == "ApplicationFrameHost")
            {
                Process realProcess = GetRealProcess(foregroundProcess);
                foregroundProcess.Dispose();
                return realProcess;
            }
            return foregroundProcess;
        }

        private Process GetRealProcess(Process foregroundProcess)
        {
            Process realActiveProcess = null;

            WinAPIFunctions.WindowEnumProc callback = (hwnd, lparam) =>
            {
                // This runs inside native EnumChildWindows; an exception crossing that
                // boundary is fatal, so everything here is contained.
                try
                {
                    int pid = WinAPIFunctions.GetWindowProcessId(hwnd);
                    if (pid <= 0)
                        return true;
                    Process process = Process.GetProcessById(pid);
                    if (process.ProcessName != "ApplicationFrameHost")
                    {
                        if (realActiveProcess != null)
                            realActiveProcess.Dispose();
                        realActiveProcess = process;
                    }
                    else
                        process.Dispose();
                }
                catch (Exception)
                {
                }
                return true;
            };

            IntPtr mainWindowHandle = IntPtr.Zero;
            try
            {
                mainWindowHandle = foregroundProcess.MainWindowHandle;
            }
            catch (Exception)
            {
            }
            // With a null parent EnumChildWindows would walk every top-level window instead.
            if (mainWindowHandle != IntPtr.Zero)
                WinAPIFunctions.EnumChildWindows(mainWindowHandle, callback, IntPtr.Zero);
            return realActiveProcess;
        }
    }
}
