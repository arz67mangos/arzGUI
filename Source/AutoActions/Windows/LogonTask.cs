using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoActions.Windows
{
    /// <summary>
    /// The logon task that starts arzGUI with administrator rights. arzGUI's own Auto-Start goes
    /// through the Run key, which is never elevated, so the monitor device action - which needs the
    /// whole process elevated - cannot work unattended without one of these.
    ///
    /// Writing it by hand is where this went wrong often enough to be worth automating: the
    /// documented command used %CD%, and an administrator Command Prompt opens in System32, so the
    /// task ends up pointing at C:\Windows\System32\arzGUI.exe and fails at every logon with
    /// "file not found". The task is registered from XML rather than with schtasks' own switches
    /// because the switches cannot turn off "do not start on batteries" or the 72-hour kill, both
    /// of which are on by default and both of which are wrong for a tray program on a laptop.
    /// </summary>
    public static class LogonTask
    {
        public const string TaskName = "arzGUI";

        /// <summary>The exe this copy of arzGUI is running from - what the task has to point at.</summary>
        public static string ProgramPath
        {
            get
            {
                Assembly entry = Assembly.GetEntryAssembly();
                if (entry != null && !string.IsNullOrEmpty(entry.Location))
                    return entry.Location;
                using (Process self = Process.GetCurrentProcess())
                    return self.MainModule.FileName;
            }
        }

        /// <summary>
        /// What the registered task starts, or empty when there is no task. Reading does not need
        /// administrator rights, only writing does.
        /// </summary>
        public static string RegisteredProgram()
        {
            string output;
            if (Run("schtasks.exe", "/query /tn \"" + TaskName + "\" /xml ONE", out output) != 0)
                return string.Empty;
            // Its own XML, so a match on the element is enough - and it avoids handing a UTF-16
            // declaration to a parser that has already been given a decoded string.
            Match command = Regex.Match(output, "<Command>(.*?)</Command>", RegexOptions.Singleline);
            return command.Success ? command.Groups[1].Value.Trim().Trim('"') : string.Empty;
        }

        /// <summary>True when a task exists and it starts <em>this</em> copy of arzGUI.</summary>
        public static bool StartsThisCopy()
        {
            string registered = RegisteredProgram();
            return !string.IsNullOrEmpty(registered)
                && string.Equals(Path.GetFullPath(registered), Path.GetFullPath(ProgramPath), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Registers (or re-points) the task. Asks for administrator rights unless arzGUI already has them.</summary>
        public static bool Create(out string error)
        {
            string file = Path.Combine(Path.GetTempPath(), "arzGUI-logon-task.xml");
            try
            {
                // schtasks reads the definition as Unicode.
                File.WriteAllText(file, Definition(), Encoding.Unicode);
                return Elevated("/create /tn \"" + TaskName + "\" /xml \"" + file + "\" /f", out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch (Exception)
                {
                    // A leftover temp file is not worth failing the operation over.
                }
            }
        }

        public static bool Delete(out string error)
        {
            return Elevated("/delete /tn \"" + TaskName + "\" /f", out error);
        }

        private static string Definition()
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string program = SecurityElement.Escape(ProgramPath);
            string folder = SecurityElement.Escape(Path.GetDirectoryName(ProgramPath));
            return string.Format(
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts arzGUI at logon with administrator rights, which the monitor device action needs.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{0}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{0}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{1}</Command>
      <WorkingDirectory>{2}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>", user, program, folder);
        }

        /// <summary>Runs schtasks as administrator. Silent when arzGUI is elevated already, a UAC prompt when it is not.</summary>
        private static bool Elevated(string arguments, out string error)
        {
            error = null;
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo("schtasks.exe", arguments)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };
                    process.Start();
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                        return true;
                    // Nothing can be read back through ShellExecute, so the code is all there is.
                    error = "schtasks reported error " + process.ExitCode + ".";
                    return false;
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                error = "the administrator prompt was declined";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static int Run(string program, string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo(program, arguments)
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                        // No StandardOutputEncoding: /xml declares UTF-16 but writes the console's
                        // own code page, so the default - which is that same code page - is the one
                        // that reads it back. Forcing Unicode here turned every task into mojibake,
                        // the Command element never matched, and a task that had just been
                        // registered read as "no task", which is what left the box unticked.
                    };
                    process.Start();
                    output = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
