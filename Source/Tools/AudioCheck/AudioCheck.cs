using AutoActions.Audio;
using System;
using System.Collections.Generic;
using System.Linq;

// Check for the audio device layer after it moved from the vendored AudioSwitcher wrappers to the
// CoreAudio package. The part that matters is the device id: settings files store it, so it has to
// come out exactly as before - the first GUID in the endpoint id string.
//
//   $csc = "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe"; $out = ".\Source\Release_x64"
//   & $csc /platform:x64 /langversion:7.3 /out:"$out\AudioCheck.exe" /r:"$out\AutoActions.AudioManager.dll" `
//       /r:"$out\CoreAudio.dll" /r:System.dll /r:System.Core.dll .\Source\Tools\AudioCheck\AudioCheck.cs
//   Push-Location $out; .\AudioCheck.exe; Pop-Location
//
// It changes nothing: the only write it makes is setting the current default device as default,
// which is what it already is.
static class AudioCheck
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
        Console.WriteLine("== enumeration ==");
        AudioController controller = AudioController.Instance;
        IReadOnlyList<AudioDevice> playback = controller.OutputAudioDevices;
        IReadOnlyList<AudioDevice> recording = controller.InputAudioDevices;

        foreach (AudioDevice device in playback)
            Console.WriteLine("  playback   " + device.ID + "  " + (device.IsDefaultDevice ? "* " : "  ") + device.Name);
        foreach (AudioDevice device in recording)
            Console.WriteLine("  recording  " + device.ID + "  " + (device.IsDefaultDevice ? "* " : "  ") + device.Name);

        Check(playback.Count > 0, "at least one playback device");
        Check(playback.Concat(recording).All(d => d.ID != Guid.Empty), "every device has an id");
        Check(playback.Concat(recording).All(d => !string.IsNullOrEmpty(d.Name)), "every device has a name");
        Check(playback.Select(d => d.ID).Distinct().Count() == playback.Count, "playback ids are unique");
        Check(playback.All(d => d.DeviceType == AudioDeviceType.Playback), "playback devices say so");
        Check(recording.All(d => d.DeviceType == AudioDeviceType.Capture), "recording devices say so");

        Console.WriteLine("== ids match what settings files hold ==");
        // AudioSwitcher stored the first GUID of the endpoint id; anything else silently breaks every
        // saved audio action.
        Check(AudioDevice.ToId("{0.0.0.00000000}.{e0f8e0a4-1111-2222-3333-444455556666}")
                == new Guid("e0f8e0a4-1111-2222-3333-444455556666"),
            "an endpoint id reduces to the device guid");
        Check(AudioDevice.ToId("no guid in here") == Guid.Empty, "a malformed id is empty, not an exception");
        Check(AudioDevice.ToId(null) == Guid.Empty, "a null id is empty, not an exception");

        Console.WriteLine("== default device ==");
        AudioDevice standard = playback.FirstOrDefault(d => d.IsDefaultDevice);
        Check(standard != null, "one playback device reports itself as default");
        if (standard == null)
            return Finish();
        Check(playback.Count(d => d.IsDefaultDevice) == 1, "exactly one playback device is default");

        // The write path, without changing anything the user would notice.
        Console.WriteLine("  setting '" + standard.Name + "' as default again");
        standard.SetAsDefault();
        System.Threading.Thread.Sleep(500);
        controller.UpdateDevices();
        AudioDevice afterwards = controller.OutputAudioDevices.FirstOrDefault(d => d.IsDefaultDevice);
        Check(afterwards != null && afterwards.ID == standard.ID,
            "the same device is still default (" + (afterwards == null ? "none" : afterwards.Name) + ")");

        Console.WriteLine("== refresh ==");
        controller.UpdateDevices();
        Check(controller.OutputAudioDevices.Count == playback.Count, "a refresh returns the same playback devices");

        return Finish();
    }

    static int Finish()
    {
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        Console.WriteLine();
        Console.WriteLine("Press enter to close.");
        Console.ReadLine();
        return failures == 0 ? 0 : 1;
    }
}
