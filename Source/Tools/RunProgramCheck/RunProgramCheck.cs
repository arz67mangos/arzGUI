using AutoActions;
using AutoActions.Displays;
using AutoActions.Profiles;
using AutoActions.Profiles.Actions;
using System;
using System.IO;
using System.Linq;

// Check for RunProgramAction: a program started by an elevated arzGUI must run at the logged-on
// user's integrity level, not as administrator. Deliberately not in the solution - there is no test
// project here, and this needs a built output to run against. Build and run it from the output
// directory, then run it a second time as administrator, which is the case that matters:
//
//   $csc = "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe"; $out = ".\Source\Release_x64"
//   & $csc /platform:x64 /langversion:7.3 /out:"$out\RunProgramCheck.exe" /r:"$out\arzGUI.exe" `
//       /r:"$out\AutoActions.Displays.dll" /r:"$out\CodectoryCore.dll" /r:"$out\CodectoryCore.UI.Wpf.dll" `
//       /r:"$out\AutoActions.ProjectResources.dll" /r:System.dll /r:System.Core.dll `
//       /r:PresentationFramework.dll /r:WindowsBase.dll .\Source\Tools\RunProgramCheck\RunProgramCheck.cs
//   Push-Location $out; .\RunProgramCheck.exe; Pop-Location
//
// It starts cmd.exe, which writes its own token's integrity level to a file in %TEMP%.
static class RunProgramCheck
{
    static int failures = 0;

    static void Check(bool condition, string what)
    {
        Console.WriteLine((condition ? "  PASS  " : "  FAIL  ") + what);
        if (!condition)
            failures++;
    }

    static int Main()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string report = Path.Combine(Path.GetTempPath(), "arzgui-runprogram-check.txt");
        if (File.Exists(report))
            if (failures == 0)
            File.Delete(report);

        Console.WriteLine("  arzGUI is elevated: " + MonitorDeviceControl.IsElevated);

        RunProgramAction action = new RunProgramAction
        {
            FilePath = Path.Combine(system, "cmd.exe"),
            // The full path: a bash shell on PATH brings its own whoami, and cmd mangles a leading quote.
            Arguments = "/c " + Path.Combine(system, "whoami.exe") + " /groups > \"" + report + "\" 2>&1",
            WaitForEnd = true
        };
        action.NewLog += (o, e) => Console.WriteLine("  [log] " + e);

        Console.WriteLine("== running ==");
        ActionEndResult result = action.RunAction(ApplicationChangedType.Started);
        Check(result.Success, "the action reports success");
        Check(File.Exists(report), "the program ran and its arguments reached it");
        if (!File.Exists(report))
            return 1;

        string line = File.ReadAllLines(report).FirstOrDefault(l => l.Contains("Mandatory Level"));
        Console.WriteLine("== the started program's token ==");
        Console.WriteLine("  " + (line == null ? "no integrity level in the output" : line.Trim()));
        Check(line != null, "the output names an integrity level");
        if (line != null && MonitorDeviceControl.IsElevated)
            Check(!line.Contains("High Mandatory Level") && !line.Contains("System Mandatory Level"),
                "the program did NOT inherit administrator rights");
        else if (line != null)
            Console.WriteLine("  (run this as administrator to check the case that matters)");

        Console.WriteLine("== a missing file ==");
        Check(!new RunProgramAction { FilePath = @"C:\nosuch\nosuch.exe" }.RunAction(ApplicationChangedType.Started).Success,
            "a file that does not exist fails instead of throwing");

        if (failures == 0)
            File.Delete(report);
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        Console.WriteLine();
        Console.WriteLine("Press enter to close.");
        Console.ReadLine();
        return failures == 0 ? 0 : 1;
    }
}
