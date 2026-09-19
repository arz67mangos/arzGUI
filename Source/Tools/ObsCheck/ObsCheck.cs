using AutoActions;
using AutoActions.Obs;
using AutoActions.Profiles.Actions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

// Exercises the OBS Studio action against a real OBS: the obs-websocket handshake (including the
// SHA256 authentication), the name look-ups behind the drop-downs, switching the program scene, and
// every way it is supposed to fail without hanging the watcher thread.
//
// OBS has to be running with its WebSocket server enabled (Tools > WebSocket Server Settings), and
// the password has to be in arzGUI's settings. The check puts the scene back the way it found it and
// never saves settings.
//
//   $csc = "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe"; $out = ".\Source\Debug_x64"
//   & $csc /platform:x64 /langversion:7.3 /out:"$out\ObsCheck.exe" -r:"$out\arzGUI.exe" `
//       -r:"$out\ArzGUI.Foundation.dll" -r:System.dll -r:System.Core.dll .\Source\Tools\ObsCheck\ObsCheck.cs
//   Push-Location $out; .\ObsCheck.exe; Pop-Location
static class ObsCheck
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
        Globals.Instance.LoadSettings();
        UserAppSettings settings = Globals.Instance.Settings;
        Console.WriteLine("port " + settings.ObsWebSocketPort + ", password "
            + (string.IsNullOrEmpty(settings.ObsPassword) ? "not set" : "set"));

        Console.WriteLine("== finding the OBS installation, so an action can offer it ==");
        string obsPath = ObsInstall.FindExecutable();
        Check(!string.IsNullOrEmpty(obsPath) && System.IO.File.Exists(obsPath), "obs64.exe found: " + obsPath);
        bool obsElevated;
        bool obsRunning = ObsInstall.IsRunning(out obsElevated);
        Check(obsRunning, "and OBS is running" + (obsRunning && obsElevated ? ", as administrator" : string.Empty));
        Check(!string.IsNullOrEmpty(ObsInstall.Describe()), "the settings card has a line to show: " + ObsInstall.Describe());
        RunProgramAction runObs = new RunProgramAction();
        Check(runObs.ObsIsInstalled, "the run action offers to fill OBS in");
        runObs.UseObsCommand.Execute(null);
        Check(runObs.FilePath == obsPath, "and filling it in sets the path");
        Check(runObs.OnlyIfNotRunning, "with 'only if not running' ticked, since OBS is already up");
        Check(runObs.CanSave, "which is an action that can be saved");

        Console.WriteLine("== the password is stored encrypted ==");
        string storedBefore = settings.ObsWebSocketPassword;
        settings.ObsPassword = "not-the-real-one";
        Check(settings.ObsWebSocketPassword != "not-the-real-one", "what goes to disk is not the plain text");
        Check(settings.ObsPassword == "not-the-real-one", "and it reads back as itself");
        settings.ObsWebSocketPassword = storedBefore;
        Check(settings.ObsPassword.Length > 0, "the real password decrypts again");

        Console.WriteLine("== reading the lists (what the drop-downs show) ==");
        ObsStudioAction action = new ObsStudioAction();
        List<string> scenes = Refresh(action);
        Check(action.RefreshStatus.Length == 0, "the refresh reported no error: " + action.RefreshStatus);
        Check(action.ObsProfiles.Count > 0, "OBS named " + action.ObsProfiles.Count + " profile(s): " + Join(action.ObsProfiles));
        Check(action.ObsSceneCollections.Count > 0, "OBS named " + action.ObsSceneCollections.Count + " scene collection(s): " + Join(action.ObsSceneCollections));
        Check(scenes.Count > 0, "OBS named " + scenes.Count + " scene(s): " + Join(scenes));
        if (scenes.Count == 0 || action.ObsProfiles.Count == 0)
            return Finish();

        string startingProfile = action.CurrentProfile;
        string startingCollection = action.CurrentSceneCollection;
        string startingScene = action.CurrentScene;
        Stopwatch clockOnce;

        Console.WriteLine("== a name OBS does not know fails, and says so ==");
        ObsStudioAction missing = new ObsStudioAction() { SceneName = "no such scene " + Guid.NewGuid(), WaitForObsSeconds = 0 };
        ActionEndResult result = missing.RunAction(ApplicationChangedType.Started);
        Check(!result.Success, "an unknown scene is a failure, not an exception");
        Check(result.ErrorInfo != null && result.ErrorInfo.Contains("no scene named"), "the reason names the scene: " + result.ErrorInfo);

        Console.WriteLine("== switching the program scene ==");
        // Switch to something else and back, so OBS is left as it was found.
        string other = scenes.FirstOrDefault(s => s != startingScene);
        if (other == null)
        {
            Console.WriteLine("  (only one scene, nothing to switch between)");
        }
        else
        {
            Check(Run(new ObsStudioAction() { SceneName = other }), "switched to '" + other + "'");
            Check(Current() == other, "OBS is on '" + other + "'");
            Check(Run(new ObsStudioAction() { SceneName = other }), "switching to the scene it is already on is a no-op, not an error");
            Check(Run(new ObsStudioAction() { SceneName = startingScene }), "switched back to '" + startingScene + "'");
            Check(Current() == startingScene, "OBS is back on '" + startingScene + "'");
        }

        Console.WriteLine("== the profile it is already on is left alone ==");
        Check(Run(new ObsStudioAction() { ProfileName = startingProfile }), "setting the current profile succeeds without a switch");

        Console.WriteLine("== switching profile, and waiting for OBS to have done it ==");
        string otherProfile = action.ObsProfiles.FirstOrDefault(p => p != startingProfile);
        if (otherProfile == null)
        {
            Console.WriteLine("  (only one profile)");
        }
        else
        {
            Check(Run(new ObsStudioAction() { ProfileName = otherProfile }), "switched to profile '" + otherProfile + "'");
            Check(Probe().CurrentProfile == otherProfile, "OBS is on profile '" + otherProfile + "'");
            Check(Run(new ObsStudioAction() { ProfileName = startingProfile }), "switched back to profile '" + startingProfile + "'");
            Check(Probe().CurrentProfile == startingProfile, "OBS is back on profile '" + startingProfile + "'");
        }

        Console.WriteLine("== switching scene collection (the slow one: every source is reloaded) ==");
        string otherCollection = action.ObsSceneCollections.FirstOrDefault(c => c != startingCollection);
        if (otherCollection == null)
        {
            Console.WriteLine("  (only one scene collection)");
        }
        else
        {
            clockOnce = Stopwatch.StartNew();
            Check(Run(new ObsStudioAction() { SceneCollectionName = otherCollection }), "switched to collection '" + otherCollection + "'");
            clockOnce.Stop();
            Check(Probe().CurrentSceneCollection == otherCollection, "OBS is on collection '" + otherCollection + "'");
            Console.WriteLine("        the switch took " + clockOnce.Elapsed.TotalSeconds.ToString("0.0") + "s");
            Check(Run(new ObsStudioAction() { SceneCollectionName = startingCollection }), "switched back to collection '" + startingCollection + "'");
            Check(Probe().CurrentSceneCollection == startingCollection, "OBS is back on collection '" + startingCollection + "'");
        }

        Console.WriteLine("== the replay buffer ==");
        // Whatever it is doing now is what it goes back to at the end.
        bool bufferWasRunning = ReplayBufferRunning();
        Console.WriteLine("  it is " + (bufferWasRunning ? "running" : "not running") + " to begin with");
        ObsStudioAction start = new ObsStudioAction() { ReplayBuffer = ObsOutputChange.Start };
        Check(start.CanSave, "a replay-buffer-only action is savable, without any names");
        ActionEndResult started = start.RunAction(ApplicationChangedType.Started);
        if (!started.Success && started.ErrorInfo != null && started.ErrorInfo.Contains("Settings > Output"))
        {
            Console.WriteLine("  SKIP  the replay buffer is turned off in OBS, so it cannot be started:");
            Console.WriteLine("        " + started.ErrorInfo);
        }
        else
        {
            Check(started.Success, "starting it succeeds" + (started.Success ? "" : ": " + started.ErrorInfo));
            Check(ReplayBufferRunning(), "and OBS says it is running");
            Check(Run(new ObsStudioAction() { ReplayBuffer = ObsOutputChange.Start }),
                "starting one that is already running is a no-op, not an error");
            Check(Run(new ObsStudioAction() { ReplayBuffer = ObsOutputChange.Stop }), "stopping it succeeds");
            Check(!ReplayBufferRunning(), "and OBS says it stopped");
            Check(Run(new ObsStudioAction() { ReplayBuffer = ObsOutputChange.Stop }),
                "stopping one that is already stopped is a no-op too");
            if (bufferWasRunning)
                Run(new ObsStudioAction() { ReplayBuffer = ObsOutputChange.Start });
            Check(ReplayBufferRunning() == bufferWasRunning, "it is back the way it was found");
        }
        Check(!new ObsStudioAction().CanSave, "an action that changes nothing at all is not savable");

        Console.WriteLine("== names are matched the way a user types them ==");
        Check(Run(new ObsStudioAction() { SceneName = startingScene.ToUpperInvariant() }), "a differently cased scene name still resolves");

        Console.WriteLine("== OBS unreachable fails fast, it does not block the watcher ==");
        int realPort = settings.ObsWebSocketPort;
        settings.ObsWebSocketPort = 45561; // nothing listens here
        Stopwatch clock = Stopwatch.StartNew();
        result = new ObsStudioAction() { SceneName = startingScene, WaitForObsSeconds = 0 }.RunAction(ApplicationChangedType.Started);
        clock.Stop();
        settings.ObsWebSocketPort = realPort;
        Check(!result.Success, "a closed port is a failure");
        Check(clock.Elapsed.TotalSeconds < 5, "and it gives up in " + clock.Elapsed.TotalSeconds.ToString("0.0") + "s");
        Check(result.ErrorInfo != null && result.ErrorInfo.Contains("cannot reach OBS"), "the reason is legible: " + result.ErrorInfo);

        Console.WriteLine("== waiting for OBS is bounded by the setting ==");
        settings.ObsWebSocketPort = 45561;
        clock = Stopwatch.StartNew();
        new ObsStudioAction() { SceneName = startingScene, WaitForObsSeconds = 3 }.RunAction(ApplicationChangedType.Started);
        clock.Stop();
        settings.ObsWebSocketPort = realPort;
        Check(clock.Elapsed.TotalSeconds >= 2.5 && clock.Elapsed.TotalSeconds < 9,
            "a 3s wait took " + clock.Elapsed.TotalSeconds.ToString("0.0") + "s");

        Console.WriteLine("== a wrong password is not retried for the whole wait ==");
        settings.ObsPassword = "wrong-password";
        clock = Stopwatch.StartNew();
        result = new ObsStudioAction() { SceneName = startingScene, WaitForObsSeconds = 20 }.RunAction(ApplicationChangedType.Started);
        clock.Stop();
        settings.ObsWebSocketPassword = storedBefore;
        Check(!result.Success, "a wrong password is a failure");
        Check(clock.Elapsed.TotalSeconds < 10, "and it gives up at once (" + clock.Elapsed.TotalSeconds.ToString("0.0") + "s), instead of retrying for 20s");

        Console.WriteLine("== OBS is as it was found ==");
        ObsStudioAction final = Probe();
        Check(final.CurrentProfile == startingProfile, "profile is '" + startingProfile + "'");
        Check(final.CurrentSceneCollection == startingCollection, "scene collection is '" + startingCollection + "'");
        if (final.CurrentScene != startingScene)
        {
            // A collection round-trip can land on that collection's remembered scene; put it back.
            Run(new ObsStudioAction() { SceneName = startingScene });
        }
        Check(Current() == startingScene, "scene is '" + startingScene + "'");

        return Finish();
    }

    static bool Run(ObsStudioAction action)
    {
        ActionEndResult result = action.RunAction(ApplicationChangedType.Started);
        if (!result.Success)
            Console.WriteLine("        " + result.ErrorInfo);
        return result.Success;
    }

    static List<string> Refresh(ObsStudioAction action)
    {
        action.Refresh();
        for (int i = 0; i < 150 && action.IsRefreshing; i++)
            Thread.Sleep(100);
        return action.ObsScenes;
    }

    /// <summary>Asks OBS what it is showing, without changing anything.</summary>
    static string Current()
    {
        return Probe().CurrentScene;
    }

    static bool ReplayBufferRunning()
    {
        return Probe().ReplayBufferRunning;
    }

    static ObsStudioAction Probe()
    {
        ObsStudioAction probe = new ObsStudioAction();
        Refresh(probe);
        return probe;
    }

    static string Join(List<string> names)
    {
        return string.Join(", ", names);
    }

    static int Finish()
    {
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
