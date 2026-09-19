using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AutoActions.Obs
{
    /// <summary>
    /// Where OBS Studio is installed, and what the copy that is running looks like from here. Setting
    /// a run action up by hand means knowing a path four folders deep; this is so the program can
    /// offer it instead of asking for it.
    /// </summary>
    public static class ObsInstall
    {
        /// <summary>The process name OBS runs under, which is also what a run action matches on.</summary>
        public const string ProcessName = "obs64";

        /// <summary>Full path of obs64.exe, or empty when OBS is not installed where it says it is.</summary>
        public static string FindExecutable()
        {
            foreach (string folder in InstallFolders())
            {
                if (string.IsNullOrEmpty(folder))
                    continue;
                string path = Path.Combine(folder, @"bin\64bit\obs64.exe");
                if (File.Exists(path))
                    return path;
            }
            return string.Empty;
        }

        private static IEnumerable<string> InstallFolders()
        {
            // The uninstall entry is what the installer writes, in both registry views: a 64-bit OBS
            // on a 64-bit Windows still registers under WOW6432Node.
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                string folder = null;
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio"))
                        if (key != null)
                            folder = key.GetValue("InstallLocation") as string;
                }
                catch (Exception)
                {
                    // A locked-down or missing key is just "not found".
                }
                if (!string.IsNullOrEmpty(folder))
                    yield return folder;
            }

            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "obs-studio");
        }

        /// <summary>Whether OBS is running, and whether this process can see into it.</summary>
        public static bool IsRunning(out bool elevated)
        {
            elevated = false;
            Process[] processes = Process.GetProcessesByName(ProcessName);
            if (processes.Length == 0)
                return false;
            try
            {
                // ponytail: reading the module list of a higher-integrity process is refused, and that
                // refusal is the signal. It also reads as elevated when arzGUI itself is elevated and
                // something else denies the handle, which only makes the hint less useful, never wrong
                // about the thing that matters - the websocket does not care either way.
                elevated = string.IsNullOrEmpty(processes[0].MainModule.FileName);
            }
            catch (Exception)
            {
                elevated = true;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
            return true;
        }

        /// <summary>One line for the settings card: what was found and what state it is in.</summary>
        public static string Describe()
        {
            string path = FindExecutable();
            bool elevated;
            bool running = IsRunning(out elevated);
            string where = string.IsNullOrEmpty(path) ? "OBS Studio was not found on this PC" : "Found " + path;
            if (!running)
                return where + ". It is not running.";
            return where + (elevated
                ? ". It is running as administrator - which the connection below does not mind."
                : ". It is running.");
        }
    }
}
