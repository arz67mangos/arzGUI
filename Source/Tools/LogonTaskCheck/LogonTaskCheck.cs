// Covers the logon task behind Settings > "Start with Windows as administrator": that the XML it
// registers is a definition Windows accepts, that the settings Task Scheduler actually applies are
// the ones a tray program needs (elevated, no battery conditions, no time limit), and that reading
// the task back says what it starts - including when it points somewhere else, which is the state a
// hand-written schtasks command usually leaves behind.
//
// Registering a task at HighestAvailable needs administrator rights, so run this elevated to cover
// everything; run as a normal user and the writing half is skipped and reported as such. It uses its
// own task name and deletes it afterwards, so an existing arzGUI logon task is never touched.
//
//   $csc = "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe"; $out = ".\Source\Debug_x64"
//   & $csc /platform:x64 /langversion:7.3 /out:"$out\LogonTaskCheck.exe" -r:"$out\arzGUI.exe" `
//       -r:"$out\ArzGUI.Foundation.dll" -r:System.dll -r:System.Core.dll `
//       .\Source\Tools\LogonTaskCheck\LogonTaskCheck.cs
//   Push-Location $out; .\LogonTaskCheck.exe; Pop-Location
using AutoActions.Windows;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

static class LogonTaskCheck
{
    static int failures = 0;
    const string ProbeName = "arzGUI probe (safe to delete)";

    static void Check(bool condition, string what)
    {
        Console.WriteLine((condition ? "  PASS  " : "  FAIL  ") + what);
        if (!condition)
            failures++;
    }

    [STAThread]
    static int Main()
    {
        bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Console.WriteLine("running " + (elevated ? "as administrator" : "as a normal user"));

        Console.WriteLine("== what the task would point at ==");
        string program = LogonTask.ProgramPath;
        Check(File.Exists(program), "the program path is a file that exists: " + program);
        Check(Path.GetFileName(program).Equals("arzGUI.exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(program).Equals("LogonTaskCheck.exe", StringComparison.OrdinalIgnoreCase),
            "and it is the running program, not the working directory");
        Check(!program.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System), StringComparison.OrdinalIgnoreCase),
            "and not System32, which is where the hand-written %CD% command ended up");

        Console.WriteLine("== the definition ==");
        string xml = Definition();
        Check(xml.Contains("<RunLevel>HighestAvailable</RunLevel>"), "asks for administrator rights");
        Check(xml.Contains("<LogonTrigger>"), "at logon");
        Check(xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"), "starts on battery");
        Check(xml.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"), "and is not stopped by unplugging");
        Check(xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"), "with no run-time limit (the default kills it after 72 hours)");
        Check(xml.Contains("<UserId>" + WindowsIdentity.GetCurrent().Name + "</UserId>"), "for this user");
        Check(xml.Contains("<Command>" + program + "</Command>"), "starting this copy");

        Console.WriteLine("== reading a task back ==");
        string registered = LogonTask.RegisteredProgram();
        Console.WriteLine("  the real arzGUI task starts: " + (registered.Length == 0 ? "(no task registered)" : registered));

        if (!elevated)
        {
            // Registering needs rights this process does not have, but schtasks parses the file
            // before it checks them: reaching "access is denied" means the XML, its encoding and the
            // quoting of the command line are all right, and only the elevation is missing.
            Console.WriteLine("== without administrator rights, it gets as far as the rights check ==");
            string path = Path.Combine(Path.GetTempPath(), "arzGUI-logon-task-probe.xml");
            File.WriteAllText(path, xml, Encoding.Unicode);
            string denied = Output("schtasks.exe", "/create /tn \"" + ProbeName + "\" /xml \"" + path + "\" /f");
            Check(denied.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0,
                "the definition is parsed and only the rights are missing: " + denied.Trim());
            File.WriteAllText(path, "<Task><not-a-task></Task>", Encoding.Unicode);
            string broken = Output("schtasks.exe", "/create /tn \"" + ProbeName + "\" /xml \"" + path + "\" /f");
            Check(broken.IndexOf("denied", StringComparison.OrdinalIgnoreCase) < 0,
                "and a broken definition fails earlier, at the XML: " + broken.Trim().Split('\n')[0]);
            File.Delete(path);
            Check(Query(ProbeName).Length == 0, "nothing was registered");
            Console.WriteLine("  (run this elevated to cover registering the task for real)");
            return Finish();
        }

        Console.WriteLine("== registering it, and what Task Scheduler made of it ==");
        string file = Path.Combine(Path.GetTempPath(), "arzGUI-logon-task-probe.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        Check(Schtasks("/create /tn \"" + ProbeName + "\" /xml \"" + file + "\" /f") == 0,
            "Windows accepted the definition");
        File.Delete(file);

        string back = Query(ProbeName);
        Check(back.Contains("<RunLevel>HighestAvailable</RunLevel>"), "it is registered as elevated");
        Check(back.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"), "battery conditions really are off");
        Check(back.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"), "and the time limit really is gone");
        Match command = Regex.Match(back, "<Command>(.*?)</Command>", RegexOptions.Singleline);
        Check(command.Success && command.Groups[1].Value.Trim().Trim('"')
                .Equals(program, StringComparison.OrdinalIgnoreCase),
            "and it starts the program the definition named");

        Console.WriteLine("== a task that points somewhere else is not this copy ==");
        Check(Schtasks("/create /tn \"" + ProbeName + "\" /tr \"\\\"C:\\Windows\\System32\\arzGUI.exe\\\"\" /sc onlogon /f") == 0,
            "registered one pointing at System32, the way the old instructions did");
        Match wrong = Regex.Match(Query(ProbeName), "<Command>(.*?)</Command>", RegexOptions.Singleline);
        Check(wrong.Success && wrong.Groups[1].Value.Trim().Trim('"').IndexOf("System32", StringComparison.OrdinalIgnoreCase) >= 0,
            "reading it back gives that path, which is what the settings page reports");
        Check(!wrong.Groups[1].Value.Trim().Trim('"').Equals(program, StringComparison.OrdinalIgnoreCase),
            "and it is not this copy, so the box stays unticked");

        Console.WriteLine("== cleaning up ==");
        Check(Schtasks("/delete /tn \"" + ProbeName + "\" /f") == 0, "the probe task is removed");
        Check(Query(ProbeName).Length == 0, "and querying it finds nothing");

        return Finish();
    }

    /// <summary>The same definition the app registers - reached through the private method it uses.</summary>
    static string Definition()
    {
        MethodInfo method = typeof(LogonTask).GetMethod("Definition", BindingFlags.NonPublic | BindingFlags.Static);
        return (string)method.Invoke(null, null);
    }

    static string Query(string name)
    {
        string output;
        return Run("schtasks.exe", "/query /tn \"" + name + "\" /xml ONE", out output) == 0 ? output : string.Empty;
    }

    static int Schtasks(string arguments)
    {
        string ignored;
        return Run("schtasks.exe", arguments, out ignored);
    }

    /// <summary>schtasks says why it refused on stderr, which is the interesting half here.</summary>
    static string Output(string program, string arguments)
    {
        using (Process process = new Process())
        {
            process.StartInfo = new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.Start();
            string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return text;
        }
    }

    static int Run(string program, string arguments, out string output)
    {
        using (Process process = new Process())
        {
            process.StartInfo = new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Unicode
            };
            process.Start();
            output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    static int Finish()
    {
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
