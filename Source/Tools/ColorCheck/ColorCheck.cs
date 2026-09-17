using AutoActions.Displays;
using System;
using System.Collections.Generic;
using System.Linq;

// Check for DisplayColorControl: ramp maths, plus a live capture/set/restore round trip against the
// real display. Deliberately not in the solution - there is no test project here, and this needs a
// built Debug_x64 to run against. Build and run it from the output directory:
//
//   $csc = "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe"; $out = ".\Source\Debug_x64"
//   & $csc /platform:x64 /langversion:7.3 /out:"$out\ColorCheck.exe" /r:"$out\AutoActions.Displays.dll" `
//       /r:"$out\CodectoryCore.dll" /r:"$out\CodectoryCore.UI.Wpf.dll" /r:"$out\NvAPIWrapper.dll" `
//       /r:"$out\Newtonsoft.Json.dll" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll `
//       .\Source\Tools\ColorCheck\ColorCheck.cs
//   Push-Location $out; .\ColorCheck.exe test; Pop-Location
//
// Three modes. No argument or "probe" diagnoses the machine it is run on - why a gamma ramp does or
// does not reach the screen - which is what gets handed to someone whose gamma looks stuck. "read"
// only prints the ramp currently in the LUT. "test" is the developer self-check above.
// probe and test briefly change the screen's gamma and put it back.
static class ColorCheck
{
    static int failures = 0;

    static void Check(bool condition, string what)
    {
        Console.WriteLine((condition ? "  PASS  " : "  FAIL  ") + what);
        if (!condition)
            failures++;
    }

    static int Main(string[] args)
    {
        DisplayColorControl.NewLog += (o, m) => Console.WriteLine("  [log] " + m);
        // Default to the probe: this exe is handed to people to diagnose a machine, and it has to do
        // the useful thing on a double click. The developer self-check is "ColorCheck.exe test".
        if (args.Length == 0 || args[0] == "probe")
            return Probe();
        if (args[0] == "read")
            return Read();

        Console.WriteLine("== ramp maths ==");
        ushort[] neutral = DisplayColorControl.BuildRamp(0.5, 0.5, 1.0);
        Check(neutral.Length == 768, "neutral ramp is 768 entries");
        Check(neutral[0] == 0, "neutral starts at 0, got " + neutral[0]);
        Check(neutral[255] == ushort.MaxValue, "neutral ends at 65535, got " + neutral[255]);
        Check(Math.Abs(neutral[128] - 32896) < 2, "neutral midpoint is linear, got " + neutral[128]);
        bool monotonic = true;
        for (int i = 1; i < 256; i++)
            if (neutral[i] < neutral[i - 1]) monotonic = false;
        Check(monotonic, "neutral ramp is monotonic");

        ushort[] brighter = DisplayColorControl.BuildRamp(0.5, 0.5, 1.8);
        Check(brighter[128] > neutral[128], "gamma 1.8 lifts midtones (" + brighter[128] + " > " + neutral[128] + ")");
        ushort[] darker = DisplayColorControl.BuildRamp(0.5, 0.5, 0.5);
        Check(darker[128] < neutral[128], "gamma 0.5 drops midtones (" + darker[128] + " < " + neutral[128] + ")");
        ushort[] lifted = DisplayColorControl.BuildRamp(0.75, 0.5, 1.0);
        Check(lifted[0] > neutral[0], "brightness 75% lifts black (" + lifted[0] + " > " + neutral[0] + ")");
        ushort[] clampedLow = DisplayColorControl.BuildRamp(0.5, 0.5, 0.1);
        ushort[] atFloor = DisplayColorControl.BuildRamp(0.5, 0.5, DisplayColorControl.MinimumGamma);
        Check(clampedLow[128] == atFloor[128], "gamma below the floor is clamped to " + DisplayColorControl.MinimumGamma);

        Console.WriteLine("== displays ==");
        List<Display> displays = DisplayManagerHandler.Instance.GetActiveMonitors();
        Console.WriteLine("  GPU: " + DisplayManagerHandler.Instance.GraphicsCardType);
        foreach (Display d in displays)
            Console.WriteLine("  '" + d.Name + "' UID=" + d.UID + " ID=" + d.ID + " primary=" + d.IsPrimary + " gpu='" + d.GraphicsCard + "'");
        Check(displays.Count > 0, "at least one active display");
        if (displays.Count == 0)
            return 1;

        Console.WriteLine("== vibrance / hue ==");
        Console.WriteLine("  VibranceAndHueSupported = " + DisplayColorControl.VibranceAndHueSupported);
        Console.WriteLine("  GetVibrance = " + (DisplayColorControl.GetVibrance(displays[0]).HasValue ? DisplayColorControl.GetVibrance(displays[0]).Value.ToString("0.00") : "null"));
        Console.WriteLine("  GetHue      = " + (DisplayColorControl.GetHue(displays[0]).HasValue ? DisplayColorControl.GetHue(displays[0]).Value.ToString() : "null"));

        Console.WriteLine("== gamma round trip ==");
        Console.WriteLine("  GammaRangeIsUnlocked = " + DisplayColorControl.GammaRangeIsUnlocked);
        DisplayColorSnapshot snapshot = DisplayColorControl.Capture(displays);
        Console.WriteLine("  captured: " + snapshot);
        DisplayColorState state = snapshot.Displays.First();
        Check(state.GammaRamp != null, "captured a gamma ramp");
        if (state.GammaRamp == null)
            return 1;

        bool set = DisplayColorControl.SetGammaRamp(displays[0], 0.5, 0.5, 1.8);
        Check(set, "SetGammaRamp(brightness .5, contrast .5, gamma 1.8)");
        ushort[] applied = DisplayColorControl.GetGammaRamp(displays[0]);
        Check(applied != null && applied[128] == brighter[128], "the display reports the ramp we asked for (" + (applied == null ? "null" : applied[128].ToString()) + ")");

        System.Threading.Thread.Sleep(1200);

        DisplayColorControl.Restore(snapshot);
        ushort[] restored = DisplayColorControl.GetGammaRamp(displays[0]);
        Check(restored != null && restored.SequenceEqual(state.GammaRamp), "restore puts back the exact original ramp");

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>One line per display: everything that decides whether a gamma ramp is visible.</summary>
    static void Describe(Display display)
    {
        display.UpdateHDRState();
        Console.WriteLine();
        Console.WriteLine("--- " + display.Name + "  UID=" + display.UID + (display.IsPrimary ? "  (primary)" : ""));
        Console.WriteLine("    HDR      : " + (display.HDRState
            ? "ON   <-- Windows ignores the GDI gamma ramp while HDR is on"
            : "off"));
    }

    /// <summary>Midpoint of the ramp currently in the LUT, against a linear one.</summary>
    static void ReportRamp(string label, ushort[] ramp, ushort linearMid)
    {
        Console.WriteLine("    " + label + ": " + (ramp == null
            ? "could not be read"
            : "mid=" + ramp[128] + "  (linear is " + linearMid + ")"));
    }

    /// <summary>
    /// Read-only. Run it, drag the gamma slider in the NVIDIA control panel, run it again: if the
    /// midpoint moves, NVIDIA writes the same GDI ramp we do; if it does not, its sliders go through
    /// a driver path that Get/SetDeviceGammaRamp cannot see or reach.
    /// </summary>
    static int Read()
    {
        ushort linearMid = DisplayColorControl.BuildRamp(0.5, 0.5, 1.0)[128];
        foreach (Display display in DisplayManagerHandler.Instance.GetActiveMonitors())
        {
            Describe(display);
            ReportRamp("ramp now", DisplayColorControl.GetGammaRamp(display), linearMid);
        }
        return 0;
    }

    /// <summary>
    /// Writes gamma 2.2 to every display, reads it back immediately and again after four seconds,
    /// then puts the original ramp back. Prints what happened rather than a guess at why.
    /// </summary>
    static int Probe()
    {
        Console.WriteLine("GPU                 : " + DisplayManagerHandler.Instance.GraphicsCardType);
        Console.WriteLine("GdiIcmGammaRange set: " + DisplayColorControl.GammaRangeIsUnlocked);
        Console.WriteLine("Windows             : " + Environment.OSVersion.Version);

        ushort linearMid = DisplayColorControl.BuildRamp(0.5, 0.5, 1.0)[128];
        ushort wantedMid = DisplayColorControl.BuildRamp(0.5, 0.5, 2.2)[128];

        foreach (Display display in DisplayManagerHandler.Instance.GetActiveMonitors())
        {
            Describe(display);

            ushort[] before = DisplayColorControl.GetGammaRamp(display);
            ReportRamp("ramp now", before, linearMid);
            if (before == null)
            {
                Console.WriteLine("    VERDICT  : no device context for this display - the GDI path cannot reach it.");
                continue;
            }

            Console.WriteLine("    setting gamma 2.2, watch the screen...");
            bool set = DisplayColorControl.SetGammaRamp(display, 0.5, 0.5, 2.2);
            ushort[] now = DisplayColorControl.GetGammaRamp(display);
            Console.WriteLine("    set 2.2  : returned " + set + ", reads back mid=" + (now == null ? "null" : now[128].ToString()) + "  (asked for " + wantedMid + ")");

            System.Threading.Thread.Sleep(4000);
            ushort[] later = DisplayColorControl.GetGammaRamp(display);
            ReportRamp("after 4s ", later, linearMid);

            bool held = later != null && now != null && later.SequenceEqual(now);
            bool stored = now != null && now[128] == wantedMid;
            if (!set)
                Console.WriteLine("    VERDICT  : Windows refused the ramp. Unlock GdiIcmGammaRange.");
            else if (!stored)
                Console.WriteLine("    VERDICT  : the call succeeded but the LUT holds something else - the driver is rewriting it.");
            else if (!held)
                Console.WriteLine("    VERDICT  : the ramp was accepted, then something replaced it within four seconds.");
            else if (display.HDRState)
                Console.WriteLine("    VERDICT  : the ramp is in the LUT and stays there, but HDR is on, so the screen never uses it.");
            else
                Console.WriteLine("    VERDICT  : the ramp is in the LUT and stays there. If the screen did not change, the driver is not applying the LUT.");

            DisplayColorControl.RestoreGammaRamp(display, before);
        }
        Console.WriteLine();
        Console.WriteLine("Original ramps restored. Press enter.");
        Console.ReadLine();
        return 0;
    }
}
