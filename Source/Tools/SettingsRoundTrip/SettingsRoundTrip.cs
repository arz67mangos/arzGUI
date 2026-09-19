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

        Console.WriteLine("== reordering actions, which is the order they run in ==");
        Profile ordering = new Profile() { Name = "Ordering" };
        ObsStudioAction first = new ObsStudioAction() { SceneName = "one" };
        RunProgramAction second = new RunProgramAction() { FilePath = @"C:\Windows\System32\cmd.exe" };
        ObsStudioAction third = new ObsStudioAction() { SceneName = "three" };
        ordering.ApplicationStarted.Add(first);
        ordering.ApplicationStarted.Add(second);
        ordering.ApplicationStarted.Add(third);
        // Also in another lane, to prove a move finds the list the action is actually in.
        ordering.ApplicationClosed.Add(new ObsStudioAction() { SceneName = "closed" });

        ordering.MoveProfileAction(third, -1);
        Check(ordering.ApplicationStarted.IndexOf(third) == 1, "moving up swaps with the one above");
        Check(ordering.ApplicationStarted.IndexOf(second) == 2, "and the one above comes down");
        ordering.MoveProfileAction(third, 1);
        Check(ordering.ApplicationStarted.IndexOf(third) == 2, "moving down puts it back");
        ordering.MoveProfileAction(first, -1);
        Check(ordering.ApplicationStarted.IndexOf(first) == 0, "moving the top one up does nothing");
        ordering.MoveProfileAction(third, 1);
        Check(ordering.ApplicationStarted.IndexOf(third) == 2, "moving the bottom one down does nothing");
        Check(ordering.ApplicationClosed.Count == 1, "the other lane was not touched");
        ordering.MoveProfileAction(null, -1);
        Check(true, "moving nothing does not throw");

        // Leaves the lane as: cmd, "one", "three".
        ordering.MoveProfileAction(first, 1);
        string orderPath = path + ".order";
        UserAppSettings ordered = new UserAppSettings();
        ordered.ApplicationProfiles.Add(ordering);
        ordered.SaveSettings(orderPath);
        Profile reread = UserAppSettings.ReadSettings(orderPath).ApplicationProfiles.FirstOrDefault();
        Check(reread != null && reread.ApplicationStarted.Count == 3, "the lane comes back with all three");
        if (reread != null && reread.ApplicationStarted.Count == 3)
        {
            string[] order = reread.ApplicationStarted
                .Select(a => a is RunProgramAction ? "cmd" : ((ObsStudioAction)a).SceneName).ToArray();
            Check(order[0] == "cmd" && order[1] == "one" && order[2] == "three",
                "in the order it was left in: " + string.Join(", ", order));
        }
        File.Delete(orderPath);

        Console.WriteLine("== duplicating an action and a whole profile ==");
        ObsStudioAction original = (ObsStudioAction)ordering.ApplicationStarted.First(a => a is ObsStudioAction && ((ObsStudioAction)a).SceneName == "one");
        int before = ordering.ApplicationStarted.Count;
        ordering.DuplicateProfileAction(original);
        Check(ordering.ApplicationStarted.Count == before + 1, "the lane grew by one");
        ObsStudioAction copy = ordering.ApplicationStarted[ordering.ApplicationStarted.IndexOf(original) + 1] as ObsStudioAction;
        Check(copy != null, "the copy sits directly under the original");
        if (copy != null)
        {
            Check(!ReferenceEquals(copy, original), "and is not the same object");
            Check(copy.SceneName == "one", "with the same settings");
            copy.SceneName = "changed";
            Check(original.SceneName == "one", "editing the copy leaves the original alone");
        }
        ordering.RemoveProfileAction(copy);

        Profile duplicated = DeepCopy.Of(ordering);
        duplicated.GUID = Guid.NewGuid();
        Check(duplicated.GUID != ordering.GUID, "a duplicated profile gets its own GUID");
        Check(duplicated.ApplicationStarted.Count == ordering.ApplicationStarted.Count
            && duplicated.ApplicationClosed.Count == ordering.ApplicationClosed.Count, "with every lane copied");
        Check(!duplicated.ApplicationStarted.Any(a => ordering.ApplicationStarted.Contains(a)),
            "and no action shared with the profile it came from");

        Console.WriteLine("== an action can be switched off without deleting it ==");
        ObsStudioAction off = new ObsStudioAction() { SceneName = "off", Enabled = false };
        Profile switching = new Profile() { Name = "Switching" };
        switching.ApplicationStarted.Add(off);
        Check(new ObsStudioAction().Enabled, "a new action is on");
        string offPath = path + ".off";
        UserAppSettings offSettings = new UserAppSettings();
        offSettings.ApplicationProfiles.Add(switching);
        offSettings.SaveSettings(offPath);
        Profile readBack = UserAppSettings.ReadSettings(offPath).ApplicationProfiles.FirstOrDefault();
        Check(readBack != null && readBack.ApplicationStarted.Count == 1 && !readBack.ApplicationStarted[0].Enabled,
            "and off survives a round trip");
        File.Delete(offPath);

        Console.WriteLine("== the arrows know where the ends of a lane are ==");
        Check(!ordering.CanMoveProfileAction((ProfileActionBase)ordering.ApplicationStarted[0], -1), "the top action cannot move up");
        Check(ordering.CanMoveProfileAction((ProfileActionBase)ordering.ApplicationStarted[0], 1), "but can move down");
        Check(!ordering.CanMoveProfileAction((ProfileActionBase)ordering.ApplicationStarted.Last(), 1), "the bottom action cannot move down");
        Check(!ordering.CanMoveProfileAction(null, -1), "and nothing selected cannot move at all");

        Console.WriteLine("== a settings file written before these existed ==");
        string olderPath = path + ".older";
        string older = Regex.Replace(File.ReadAllText(path),
            "[ \\t]*\"(OnlyIfNotRunning|RunAsAdministrator|Enabled)\": (true|false),?\\r?\\n", string.Empty);
        Check(!older.Contains("RunAsAdministrator") && !older.Contains("OnlyIfNotRunning") && !older.Contains("Enabled"),
            "the simulated older file really has none of the keys");
        File.WriteAllText(olderPath, older);
        UserAppSettings legacy = UserAppSettings.ReadSettings(olderPath);
        RunProgramAction legacyRun = legacy == null ? null
            : legacy.ApplicationProfiles.FirstOrDefault().ApplicationStarted.OfType<RunProgramAction>().FirstOrDefault();
        Check(legacyRun != null, "it still loads");
        if (legacyRun != null)
        {
            Check(legacyRun.OnlyIfNotRunning, "an action with no OnlyIfNotRunning key gets the new default (on)");
            Check(!legacyRun.RunAsAdministrator, "and does not suddenly ask for administrator rights");
            Check(legacyRun.Enabled, "and an action with no Enabled key still runs");
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
