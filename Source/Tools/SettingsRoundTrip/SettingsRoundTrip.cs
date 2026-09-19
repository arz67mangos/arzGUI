// A new action type has to survive the settings file: Newtonsoft resolves it by assembly-qualified
// name, and the assembly is called arzGUI while the namespace still says AutoActions. It also checks
// that a settings file written before a property existed still loads, and lands on the intended
// default. Writes to a temp file only - it never touches %AppData%\arzGUI.
//
//   $csc = "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe"; $out = ".\Source\Debug_x64"
//   $ref = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
//   & $csc /platform:x64 /langversion:7.3 /out:"$out\SettingsRoundTrip.exe" -r:"$out\arzGUI.exe" `
//       -r:"$out\ArzGUI.Foundation.dll" -r:"$out\AutoActions.Displays.dll" -r:System.dll -r:System.Core.dll `
//       -r:"$ref\PresentationFramework.dll" -r:"$ref\WindowsBase.dll" `
//       .\Source\Tools\SettingsRoundTrip\SettingsRoundTrip.cs
//   Push-Location $out; .\SettingsRoundTrip.exe; Pop-Location
using AutoActions;
using AutoActions.Profiles;
using AutoActions.Profiles.Actions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

static class SettingsRoundTrip
{
    static int failures = 0;

    static void Check(bool condition, string what)
    {
        Console.WriteLine((condition ? "  PASS  " : "  FAIL  ") + what);
        if (!condition)
            failures++;
    }

    [STAThread]
    static int Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "arzgui-roundtrip-" + Guid.NewGuid().ToString("N") + ".json");

        UserAppSettings settings = new UserAppSettings();
        settings.ObsWebSocketPort = 4456;
        settings.ObsPassword = "a-password";

        Profile profile = new Profile() { Name = "OBS round trip" };
        profile.ApplicationStarted.Add(new ObsStudioAction()
        {
            ProfileName = "camera",
            SceneCollectionName = "gd",
            SceneName = "display cap",
            WaitForObsSeconds = 7
        });
        profile.ApplicationStarted.Add(new RunProgramAction()
        {
            FilePath = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
            OnlyIfNotRunning = true,
            RunAsAdministrator = true
        });
        settings.ApplicationProfiles.Add(profile);
        settings.SaveSettings(path);
        Console.WriteLine("  wrote " + new FileInfo(path).Length + " bytes");

        UserAppSettings read = UserAppSettings.ReadSettings(path);
        Check(read != null, "the file reads back");
        if (read == null)
            return Finish(path);

        Check(read.ObsWebSocketPort == 4456, "the port survived");
        Check(read.ObsPassword == "a-password", "the password decrypts after a round trip");
        Check(!File.ReadAllText(path).Contains("a-password"), "and the plain text is not in the file");

        Profile readProfile = read.ApplicationProfiles.FirstOrDefault();
        Check(readProfile != null && readProfile.ApplicationStarted.Count == 2, "both actions came back");
        if (readProfile == null || readProfile.ApplicationStarted.Count != 2)
            return Finish(path);

        ObsStudioAction obs = readProfile.ApplicationStarted.OfType<ObsStudioAction>().FirstOrDefault();
        Check(obs != null, "the OBS action came back as an OBS action");
        if (obs != null)
        {
            Check(obs.ProfileName == "camera" && obs.SceneCollectionName == "gd" && obs.SceneName == "display cap",
                "with its names: " + obs.ActionDescription);
            Check(obs.WaitForObsSeconds == 7, "and its wait");
        }

        RunProgramAction run = readProfile.ApplicationStarted.OfType<RunProgramAction>().FirstOrDefault();
        Check(run != null && run.OnlyIfNotRunning && run.RunAsAdministrator, "the run-program flags came back");

        Console.WriteLine("== a settings file written before these existed ==");
        string olderPath = path + ".older";
        string older = Regex.Replace(File.ReadAllText(path),
            "[ \\t]*\"(OnlyIfNotRunning|RunAsAdministrator)\": (true|false),?\\r?\\n", string.Empty);
        Check(!older.Contains("RunAsAdministrator") && !older.Contains("OnlyIfNotRunning"),
            "the simulated older file really has neither key");
        File.WriteAllText(olderPath, older);
        UserAppSettings legacy = UserAppSettings.ReadSettings(olderPath);
        RunProgramAction legacyRun = legacy == null ? null
            : legacy.ApplicationProfiles.FirstOrDefault().ApplicationStarted.OfType<RunProgramAction>().FirstOrDefault();
        Check(legacyRun != null, "it still loads");
        if (legacyRun != null)
        {
            Check(legacyRun.OnlyIfNotRunning, "an action with no OnlyIfNotRunning key gets the new default (on)");
            Check(!legacyRun.RunAsAdministrator, "and does not suddenly ask for administrator rights");
        }
        File.Delete(olderPath);

        return Finish(path);
    }

    static int Finish(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
