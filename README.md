# ArzActions

A personal fork of [Codectory/AutoActions](https://github.com/Codectory/AutoActions) — a Windows
tray app that watches for running applications and applies a profile of actions when they start,
close, gain or lose focus: switch resolution, refresh rate and HDR, change the default audio device,
control microphone monitoring, run or kill programs.

Repository: <https://github.com/arz67mangos/arz-AutoActions-edit>
Upstream: <https://github.com/Codectory/AutoActions> (all of the original work is theirs)

## What this fork adds

- **Display state that actually comes back.** Upstream could leave the desktop at the game's
  resolution after the game closed. The restore reused the current display mode with the size patched
  in, and from a GPU-scaled custom resolution that produces a mode Windows rejects (`BadMode`); the
  refresh-rate call that followed then re-applied the wrong mode. Resolution and refresh rate are now a
  single, verified mode change with retries, and every step is logged with its Win32 result.
- **The process watcher can no longer take the app down.** Every failure path in the 500 ms watcher
  loop is caught and logged (throttled), `Process` handles are disposed, and unhandled exceptions are
  written to `AutoActions.crash.log` next to the exe before the process dies.
- **Microphone monitoring, built in.** Windows' "Listen to this device" is a mute and a volume on the
  *Microphone* line of a playback device. ArzActions controls exactly those: a card on the Status page
  and a tray entry for manual use, a *Microphone monitoring* profile action for per-game use, device
  and line pickers in Settings. The state a profile changes on start is put back on close.
- **A redesigned UI with light and dark themes.** Design tokens, a left sidebar, card-based Status
  page, proper hover/pressed/focus/disabled states everywhere. The theme follows Windows by default
  and can be forced in Settings.
- **No upstream updater.** The original app checked Codectory's releases and would have replaced this
  build with theirs; that code is removed. Update by pulling and building.

Settings files from upstream load unchanged.

## Building

Visual Studio 2022 (Community) or the Visual Studio 2022 **Build Tools** with:

- **.NET desktop build tools** including the **.NET Framework 4.8 targeting pack**
- **Desktop development with C++** (MSVC v143, any Windows 10/11 SDK) — only to rebuild the native
  `HDRController.dll` or to produce an x86 build

The solution targets .NET Framework 4.8 / C# 7.3, uses `packages.config`, and has separate `x64` and
`x86` configurations.

```powershell
# from the repo root; MSBuild is not on PATH with Build Tools, so locate it first
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe

& $msbuild Source\AutoActions.sln -t:Restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64
& $msbuild Source\AutoActions.sln -t:Build -m -p:Configuration=Debug -p:Platform=x64
```

Output goes to `Source\Debug_x64\`; run `AutoActions.exe` from there. Notes:

- `Source\Externals\x64\` and `x86\` hold the native `HDRController.dll`, built from
  `Source\HDRController\HDRController.sln` (Release, x64 and x86). The pre-build step copies the
  matching one into the output. Rebuild and re-copy both if you change the C++ code.
- `Source\AutoActions\Controls\AppResources.xaml` is generated on every build from the numbered
  dictionaries next to it — don't edit it. Theme colours live in `Source\AutoActions\Theming\`.
- Internal namespaces, assembly and file names still say `AutoActions`; only the product name changed.
- There are no automated tests. Run the app and read `AutoActions.log` next to the exe: every
  application event, mode change and mic-monitoring operation is logged.

## Diagnostics

`AutoActions.log` (on by default, toggle in Settings) records application events as
`[app] Started|Closed|GotFocus|LostFocus: profile '…', N action(s)` and every display change with
its result code. `AutoActions.crash.log` is written only if the process dies from an unhandled
exception.

## Licence

ArzActions is a modified version of AutoActions by [Codectory](https://github.com/Codectory) and is
distributed under the same licence, the GNU General Public License v3 — see [LICENSE](LICENSE). The
fork is maintained for personal use; if you redistribute builds, the GPL's source and notice
obligations apply to you as well.
