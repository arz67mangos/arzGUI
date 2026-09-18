using System;
using System.Reflection;

// Checks the two pieces of new logic that fail quietly rather than loudly: the focus debounce state
// machine in ProcessWatcher, and hotkey parse/format round-tripping. Deliberately not in the
// solution - there is no test project here, and this needs a built Debug_x64 to run against.
//
//   $csc = "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe"; $out = ".\Source\Debug_x64"
//   & $csc /platform:x64 /langversion:7.3 /out:"$out\BehaviourCheck.exe" /r:"$out\arzGUI.exe" `
//       /r:"$out\CodectoryCore.dll" /r:"$out\CodectoryCore.UI.Wpf.dll" `
//       /r:"<ref>\PresentationCore.dll" /r:"<ref>\WindowsBase.dll" `
//       .\Source\Tools\BehaviourCheck\BehaviourCheck.cs
//   Push-Location $out; .\BehaviourCheck.exe; Pop-Location
static class BehaviourCheck
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
        CheckHotkeyParsing();
        CheckFocusDebounce();
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    static void CheckHotkeyParsing()
    {
        Console.WriteLine("== hotkey parsing ==");
        uint modifiers, key;

        Check(AutoActions.HotkeyManager.TryParse("Ctrl+Alt+G", out modifiers, out key), "Ctrl+Alt+G parses");
        Check(modifiers == (0x0002 | 0x0001), "Ctrl+Alt gives the control and alt bits, got " + modifiers);
        Check(key == 0x47, "G is virtual key 0x47, got 0x" + key.ToString("X"));

        Check(AutoActions.HotkeyManager.TryParse("ctrl+shift+f12", out modifiers, out key), "parsing ignores case");
        Check(key == 0x7B, "F12 is virtual key 0x7B, got 0x" + key.ToString("X"));

        // A bare key would swallow that key for every application on the machine.
        Check(!AutoActions.HotkeyManager.TryParse("G", out modifiers, out key), "a bare key is rejected");
        Check(!AutoActions.HotkeyManager.TryParse("Ctrl", out modifiers, out key), "modifiers alone are rejected");
        Check(!AutoActions.HotkeyManager.TryParse("", out modifiers, out key), "empty is rejected");
        Check(!AutoActions.HotkeyManager.TryParse("Ctrl+Nonsense", out modifiers, out key), "an unknown key name is rejected");

        string formatted = AutoActions.HotkeyManager.Format(
            System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt,
            System.Windows.Input.Key.G);
        Check(formatted == "Ctrl+Alt+G", "Format produces \"Ctrl+Alt+G\", got \"" + formatted + "\"");
        Check(AutoActions.HotkeyManager.TryParse(formatted, out modifiers, out key), "what Format writes, TryParse reads");

        Check(AutoActions.HotkeyManager.Format(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.LeftCtrl) == null,
            "a modifier-only press produces no hotkey");
        Check(AutoActions.HotkeyManager.Format(System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.G) == null,
            "a key with no modifier produces no hotkey");
    }

    static void CheckFocusDebounce()
    {
        Console.WriteLine("== focus debounce ==");
        Assembly assembly = typeof(AutoActions.ProcessWatcher).Assembly;
        object watcher = Activator.CreateInstance(typeof(AutoActions.ProcessWatcher));
        MethodInfo apply = typeof(AutoActions.ProcessWatcher)
            .GetMethod("ApplyFocusDebounce", BindingFlags.Instance | BindingFlags.NonPublic);
        if (apply == null)
        {
            Check(false, "ApplyFocusDebounce found");
            return;
        }

        Type stateType = assembly.GetType("AutoActions.ApplicationState");
        object none = Enum.Parse(stateType, "None");
        object running = Enum.Parse(stateType, "Running");
        object focused = Enum.Parse(stateType, "Focused");
        // ApplicationItem has no parameterless constructor and the debounce only uses it as a
        // dictionary key, so an uninitialised instance is enough.
        object application = System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(assembly.GetType("AutoActions.ApplicationItem"));

        TimeSpan debounce = TimeSpan.FromSeconds(2);
        DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Func<object, object, DateTime, TimeSpan, object> call = (oldState, rawState, now, window) =>
            apply.Invoke(watcher, new object[] { application, oldState, rawState, now, window });

        // Started and Closed are never delayed.
        Check(call(none, running, t0, debounce).Equals(running), "None -> Running is immediate (Started)");
        Check(call(focused, none, t0, debounce).Equals(none), "Focused -> None is immediate (Closed)");
        Check(call(none, focused, t0, debounce).Equals(focused), "None -> Focused is immediate (start while focused)");

        // A focus flip is held until it has been stable for the whole window.
        Check(call(running, focused, t0, debounce).Equals(running), "Running -> Focused is held at first sight");
        Check(call(running, focused, t0.AddSeconds(1), debounce).Equals(running), "still held after 1s of a 2s window");
        Check(call(running, focused, t0.AddSeconds(2.1), debounce).Equals(focused), "committed once the window has passed");

        // Flipping back before the window expires must leave the state alone - this is the alt-tab
        // glance the debounce exists for.
        Check(call(running, focused, t0.AddSeconds(10), debounce).Equals(running), "a new flip starts a new window");
        Check(call(running, running, t0.AddSeconds(10.5), debounce).Equals(running), "flipping back is a no-op");
        Check(call(running, focused, t0.AddSeconds(11), debounce).Equals(running), "and the window restarts, not resumes");
        Check(call(running, focused, t0.AddSeconds(12.5), debounce).Equals(running), "so 1.5s after the restart it is still held");
        Check(call(running, focused, t0.AddSeconds(13.1), debounce).Equals(focused), "and commits 2s after the restart");

        // Zero disables it.
        Check(call(running, focused, t0, TimeSpan.Zero).Equals(focused), "a zero window applies the change immediately");
    }
}
