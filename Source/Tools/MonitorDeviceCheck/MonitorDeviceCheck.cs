using AutoActions.Displays;
using AutoActions;
using AutoActions.Profiles;
using AutoActions.Profiles.Actions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

// Check for MonitorDeviceControl and MonitorDeviceAction. Deliberately not in the solution - there
// is no test project here, and this needs a built output to run against. Build and run it from the
// output directory:
//
//   $csc = "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe"; $out = ".\Source\Release_x64"
//   & $csc /platform:x64 /langversion:7.3 /out:"$out\MonitorDeviceCheck.exe" /r:"$out\AutoActions.exe" `
//       /r:"$out\AutoActions.Displays.dll" /r:"$out\CodectoryCore.dll" /r:"$out\CodectoryCore.UI.Wpf.dll" `
//       /r:"$out\Newtonsoft.Json.dll" /r:"$out\AutoActions.ProjectResources.dll" /r:System.dll /r:System.Core.dll `
//       /r:PresentationFramework.dll /r:WindowsBase.dll .\Source\Tools\MonitorDeviceCheck\MonitorDeviceCheck.cs
//   Push-Location $out; .\MonitorDeviceCheck.exe; Pop-Location
//
// It changes nothing: every state-changing path it exercises is one that must refuse. The actual
// enable/disable needs administrator rights and can only be verified by running the app elevated.
static class MonitorDeviceCheck
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
        MonitorDeviceControl.NewLog += (o, m) => Console.WriteLine("  [log] " + m);

        Console.WriteLine("== enumeration ==");
        List<MonitorDevice> devices = MonitorDeviceControl.GetMonitorDevices();
        foreach (MonitorDevice device in devices)
            Console.WriteLine("  '" + device.Description + "'  " + device.InstanceId);
        Check(devices.Count > 0, "at least one monitor device");
        Check(devices.All(d => !string.IsNullOrEmpty(d.InstanceId)), "every device has an instance id");
        Check(devices.All(d => !string.IsNullOrEmpty(d.Name)), "every device has a name");
        if (devices.Count == 0)
            return 1;
        MonitorDevice first = devices[0];
        Check(first.Description.Contains(first.Name), "the description carries the name");
        Check(first.IsEnabled || first.Description.Contains("disabled"), "a disabled device says so in the list");

        Console.WriteLine("== matching ==");
        Check(first.Equals(new MonitorDevice(first.InstanceId.ToLowerInvariant(), "other name", !first.IsEnabled)),
            "devices match on instance id, ignoring case and the rest");
        Check(!first.Equals(new MonitorDevice(first.InstanceId + "X", first.Name, first.IsEnabled)),
            "a different instance id does not match");
        Check(first.GetHashCode() == new MonitorDevice(first.InstanceId.ToUpperInvariant(), "x", false).GetHashCode(),
            "equal devices hash equally");

        Console.WriteLine("== calls that must refuse ==");
        Check(!MonitorDeviceControl.SetEnabled(null, true), "a null instance id is refused");
        Check(!MonitorDeviceControl.SetEnabled(string.Empty, true), "an empty instance id is refused");
        Check(!MonitorDeviceControl.SetEnabled(@"DISPLAY\NOSUCH\0&0&0&UID0", false), "an unknown device is refused");
        Console.WriteLine("  IsElevated = " + MonitorDeviceControl.IsElevated);

        Console.WriteLine("== snapshot ==");
        MonitorDeviceSnapshot snapshot = MonitorDeviceControl.Capture();
        Console.WriteLine("  captured: " + snapshot);
        Check(snapshot.Devices.Count == devices.Count, "the snapshot holds every device");
        Check(snapshot.ToString().Contains(first.Name), "the snapshot describes itself by name");
        // Nothing has changed since the capture, so this must be a no-op - and a no-op must not need
        // elevation, or every Closed would fail on an unelevated run.
        Check(MonitorDeviceControl.Restore(snapshot), "restoring an unchanged snapshot succeeds without elevation");
        Check(MonitorDeviceControl.GetMonitorDevices().All(d => snapshot.Devices.First(s => s.Equals(d)).IsEnabled == d.IsEnabled),
            "restoring an unchanged snapshot changed nothing");
        Check(!MonitorDeviceControl.Restore(null), "a null snapshot is refused");

        Console.WriteLine("== action ==");
        MonitorDeviceAction action = new MonitorDeviceAction();
        Check(!action.CanSave, "a new action cannot be saved - no device picked");
        Check(!action.Enable, "a new action disables, which is what the action is for");
        action.Device = first;
        Check(action.CanSave, "picking a device makes it saveable");
        Check(action.InstanceId == first.InstanceId, "picking a device stores its instance id");
        Check(action.DeviceName == first.Name, "picking a device stores its name");
        Check(action.ActionDescription.Contains(first.Name), "the description names the device");

        ActionEndResult noDevice = new MonitorDeviceAction().RunAction(ApplicationChangedType.Started);
        Check(!noDevice.Success, "running without a device fails instead of throwing");
        if (!MonitorDeviceControl.IsElevated)
        {
            ActionEndResult unelevated = action.RunAction(ApplicationChangedType.Started);
            Check(!unelevated.Success, "running unelevated fails instead of throwing");
            Check(MonitorDeviceControl.GetMonitorDevices().First(d => d.Equals(first)).IsEnabled == first.IsEnabled,
                "running unelevated left the device alone");
        }

        Console.WriteLine("== settings round trip ==");
        action.Enable = true;
        string json = JsonConvert.SerializeObject(action);
        MonitorDeviceAction loaded = JsonConvert.DeserializeObject<MonitorDeviceAction>(json);
        Check(loaded.InstanceId == action.InstanceId, "the instance id survives a save and load");
        Check(loaded.DeviceName == action.DeviceName, "the device name survives a save and load");
        Check(loaded.Enable == action.Enable, "the state survives a save and load");
        MonitorDeviceAction fromOldSettings = JsonConvert.DeserializeObject<MonitorDeviceAction>("{}");
        Check(!fromOldSettings.Enable && fromOldSettings.InstanceId == string.Empty,
            "a settings file without these keys loads with the defaults");

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
