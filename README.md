# arzGUI

arzGUI is an independent, community-maintained modified version of
[Codectory/AutoActions](https://github.com/Codectory/AutoActions). Codectory and the original
contributors built the foundation; this project builds on it with new workflows, reliability work,
and a redesigned Windows 11 interface. It is not affiliated with or endorsed by Codectory.

I made arzGUI to remove the repetitive setup that my friends and I were doing every time we opened
a game. Different games suit different digital-vibrance, gamma, colour, resolution, audio, and
display-device settings. arzGUI watches applications from the tray, applies the matching profile on
start or focus, then restores the previous state on close or lost focus.

Repository: <https://github.com/arz67mangos/arzGUI>
Upstream: <https://github.com/Codectory/AutoActions>

## Example workflows

- **Valorant true stretched:** disable a secondary display device, switch to a stretched resolution,
  apply the preferred vibrance and gamma, then restore every setting when Valorant closes.
- **Per-game competitive presets:** give each game its own resolution, refresh rate, colour depth,
  digital vibrance, hue, brightness, contrast, and gamma. NVIDIA vibrance and hue require an NVIDIA
  GPU; the other colour controls use Windows display APIs.
- **HDR and couch gaming:** enable HDR, select a TV-friendly mode and audio output, then return to the
  desktop monitor and speakers afterwards.
- **Voice-chat setup:** switch playback or recording devices and enable microphone monitoring only
  for games or apps that need it.
- **Launch a complete setup:** start companion programs, apply a saved preset, or run any action from
  a global hotkey instead of opening the full app.

## Getting it

Download the latest `arzGUI-<version>-x64.zip` from
[Releases](https://github.com/arz67mangos/arzGUI/releases), unzip it anywhere and run
`arzGUI.exe`. It lives in the tray; closing the window only hides it. `README.txt` in the zip covers
the same ground as this section.

Settings live in `%AppData%\arzGUI\UserSettings.json`, **not** next to the exe, so updating is:
exit arzGUI, unzip the new version, run it. Profiles, applications, hotkeys and presets are all
still there. A `UserSettings.json` dropped next to `arzGUI.exe` is imported once and renamed — that
is how a setup moves from an older version or from another PC. Settings written by upstream
AutoActions and by ArzFlow (the fork's previous name) load unchanged.

## What this fork adds

### Actions

- **Display colour per application.** Digital vibrance, hue, brightness, contrast and gamma, applied
  on start or on focus and restored on close. Vibrance and hue go through NVAPI; brightness,
  contrast and gamma through the GDI gamma ramp, with the registry unlock for the full gamma range
  available from the action when Windows clamps it.
- **Enable or disable a monitor device.** The same toggle as Device Manager, per application: a
  disabled monitor device is what makes a true stretched resolution work in some games, at the cost
  of the GDI gamma ramp on that display. The previous state is restored when the application closes.
  This one needs arzGUI itself to run elevated — the action's view offers "restart as administrator",
  and the README in the zip has the logon task for doing it unattended.
- **Microphone monitoring, built in.** Windows' "Listen to this device" is a mute and a volume on the
  *Microphone* line of a playback device. arzGUI controls exactly those: a card on the Status page
  and a tray entry for manual use, a *Microphone monitoring* profile action for per-game use, device
  and line pickers in Settings. The state a profile changes on start is put back on close.
- **Programs started by an action are never elevated.** A child process inherits its parent's token,
  so a run-program action fired from an elevated arzGUI would start the program as administrator,
  which breaks programs that refuse to run that way (OpenTabletDriver). The action starts them as
  the logged-on user instead, and falls back to a normal start with a line in the log if it cannot.

### Using it

- **Quick settings.** A page for driving display colour, audio devices, mic monitoring and HDR by
  hand, without building a profile. Any combination can be saved as a named preset.
- **Action shortcuts.** Any profile action can be bound to a global hotkey and run on demand.
- **A redesigned UI with light and dark themes.** Design tokens, a left sidebar, card-based Status
  page, proper hover/pressed/focus/disabled states everywhere. The theme follows Windows by default
  and can be forced in Settings.

### Reliability

- **Display state that actually comes back.** Upstream could leave the desktop at the game's
  resolution after the game closed. The restore reused the current display mode with the size patched
  in, and from a GPU-scaled custom resolution that produces a mode Windows rejects (`BadMode`); the
  refresh-rate call that followed then re-applied the wrong mode. Resolution and refresh rate are now a
  single, verified mode change with retries, and every step is logged with its Win32 result.
- **The process watcher can no longer take the app down.** Every failure path in the watcher loop is
  caught and logged (throttled), `Process` handles are disposed, and unhandled exceptions are written
  to `arzGUI.crash.log` next to the exe before the process dies.
- **Exit means exit.** Actions run on the watcher thread under a lock; pressing Exit used to wait for
  that lock, so a run action with *wait for end* ticked kept an invisible arzGUI.exe alive — holding
  its own folder open. Shutdown now has a budget and the process ends regardless. The same lock no
  longer freezes the window on every settings change.
- **Built only from source that ships with it.** The closed-source helper binaries the upstream
  build carried are replaced by `Source/ArzGUI.Foundation`, and audio device switching runs on the
  MIT-licensed CoreAudio package rather than vendored Ms-PL wrappers, so everything distributed has
  corresponding source under the GPL.
- **No upstream updater.** The original app checked Codectory's releases and would have replaced this
  build with theirs; that code is removed.

## Starting with Windows as administrator

The monitor device action needs administrator rights for the whole program, and the Auto-Start
setting goes through the Run key, which is never elevated. Use a logon task instead — from a Command
Prompt opened as administrator, already `cd`'d into the arzGUI folder:

```
schtasks /create /tn arzGUI /tr "\"%CD%\arzGUI.exe\"" /sc onlogon /rl highest /f
```

Type it as its own command: `%CD%` is expanded when the line is read, so chaining it after a `cd` on
the same line records the wrong folder. Turn arzGUI's own Auto-Start off afterwards, or two copies
start at logon.

## Building

Visual Studio 2022 (Community) or the Visual Studio **Build Tools** with:

- **.NET desktop build tools** including the **.NET Framework 4.8 targeting pack**
- **Desktop development with C++** (MSVC v143, any Windows 10/11 SDK) — only to rebuild the native
  `HDRController.dll` or to produce an x86 build

The solution targets .NET Framework 4.8 / C# 7.3, uses `packages.config`, and has separate `x64` and
`x86` configurations.

```powershell
# from the repo root; MSBuild is not on PATH with Build Tools, so locate it first
$msbuild = (Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\amd64\MSBuild.exe" | Select-Object -Last 1).FullName

& $msbuild Source\AutoActions.sln -t:Restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64
& $msbuild Source\AutoActions.sln -t:Build -m -p:Configuration=Debug -p:Platform=x64
```

Output goes to `Source\Debug_x64\`; run `arzGUI.exe` from there. Notes:

- `Source\Externals\x64\` and `x86\` hold the native `HDRController.dll`, built from
  `Source\HDRController\HDRController.sln` (Release, x64 and x86). The pre-build step copies the
  matching one into the output. Rebuild and re-copy both if you change the C++ code.
- `Source\AutoActions\Controls\AppResources.xaml` is generated on every build from the numbered
  dictionaries next to it — don't edit it. Theme colours live in `Source\AutoActions\Theming\`.
- Internal namespaces, the other assemblies and the mutex still say `AutoActions`; the program
  assembly builds as `arzGUI.exe`, and settings written under the old assembly name are migrated on
  load.
- There are no automated tests. Run the app and read `arzGUI.log` next to the exe: every
  application event, mode change and mic-monitoring operation is logged. `Source\Tools\` holds
  stand-alone checks for the parts that need a real machine — see below.

## Diagnostics

`arzGUI.log` (on by default, toggle in Settings) records application events as
`[app] Started|Closed|GotFocus|LostFocus: profile '…', N action(s)` and every display change with
its result code. `arzGUI.crash.log` is written only if the process dies from an unhandled exception.

Three checks ship in the release zip. Run them from the arzGUI folder; each waits for Enter at the
end.

| | |
|---|---|
| `RunProgramCheck.exe` | run-program actions, and that a started program is *not* elevated — run it as administrator, that is the case that matters |
| `MonitorDeviceCheck.exe` | the monitor enable/disable action; run it as administrator to include the live state change |
| `GammaProbe.exe` | why gamma, vibrance or brightness will not change on a display |

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) for the supported toolchain and pull-request checks. Report
security problems privately as described in [SECURITY.md](SECURITY.md).

## Licence and attribution

arzGUI is a modified version of AutoActions by [Codectory](https://github.com/Codectory) and is
distributed under the same licence, the GNU General Public License v3 — see [LICENSE](LICENSE).
[NOTICE.md](NOTICE.md) records the upstream attribution and modification notice, while
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) lists bundled dependencies and their licences.
Release archives include these files, and each release tag is the corresponding source code.
