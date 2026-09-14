# arz-AutoActions-edit

A personal fork of [Codectory/AutoActions](https://github.com/Codectory/AutoActions) — a Windows
tray app that watches for running applications and applies profiles of actions when they start,
close, gain or lose focus: toggle HDR, change resolution / refresh rate / colour depth, switch the
default audio device, or run and kill programs.

Repository: <https://github.com/arz67mangos/arz-AutoActions-edit>

## Why this fork exists

The upstream app could stop applying its "Closed" actions mid-session — the game launched at a
custom resolution and never got restored. The cause is a set of robustness defects in the process
watcher and display code, analysed in [`AUTOACTIONS_PLAN.md`](AUTOACTIONS_PLAN.md). This fork
works through that plan in phases:

| Phase | Status | What |
|---|---|---|
| 1 | done | Process watcher can no longer crash the app; unhandled-exception handlers write `AutoActions.crash.log`; logging on by default; `ChangeDisplaySettingsEx` results logged instead of discarded; native `HDRController.dll` rebuilt from source (upstream's prebuilt one was a stub); upstream auto-update off by default |
| 1b | done | **The restore bug.** Switching back from a GPU-scaled custom resolution failed with `BadMode` because the request inherited the custom mode's scaling value. Resolution + refresh rate are now one verified mode change with retries |
| 2 | planned | Snapshot display state on app start and restore it on close automatically (no hand-written Closed action) |
| 3 | planned | Built-in microphone-monitoring (listen-to-this-device) control, manual and per-profile |
| 4 | planned | UI refresh and a real dark mode |
| 5 | planned | Rebrand and detach from the upstream auto-updater |

Everything upstream does (see its README for the feature list and screenshots) still works the
same way; settings files are compatible.

## Diagnostics

Since phase 1 the app writes `AutoActions.log` next to the exe by default (toggle in Settings). Every
application event is logged as `[app] Started|Closed|GotFocus|LostFocus: profile '…', N action(s)`,
and every display mode change is logged with its Win32 result code. If the process ever dies from an
unhandled exception, `AutoActions.crash.log` next to the exe records the full stack trace.

## Building

Requirements — Visual Studio 2022 (Community) or the Visual Studio 2022 **Build Tools** with:

- **.NET desktop build tools** workload, including the **.NET Framework 4.8 targeting pack**
- **Desktop development with C++** workload (MSVC v143, any Windows 10/11 SDK) — only needed to
  rebuild the native `HDRController.dll` or to produce an x86 build

The solution targets .NET Framework 4.8 and C# 7.3, uses `packages.config` for NuGet, and has
separate `x64` and `x86` configurations (no AnyCPU).

```powershell
# from the repo root; locate MSBuild first if it is not on PATH
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe

msbuild Source\AutoActions.sln -t:Restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64
msbuild Source\AutoActions.sln -t:Build -m -p:Configuration=Debug -p:Platform=x64
```

Output goes to `Source\Debug_x64\`. `RunBuild.bat` builds both Release platforms and zips them into
`Releases\`. Notes:

- `Source\Externals\x64\` and `x86\` hold `HDRController.dll`, the native HDR helper, built from
  `Source\HDRController\HDRController.sln` (Release, x64 and x86). The pre-build step copies the
  matching one into the output. Rebuild and re-copy both if you change the C++ code.
- `Source\AutoActions\Controls\AppResources.xaml` is generated on every build from the numbered
  dictionaries next to it. Don't edit it directly.
- There are no automated tests; verification is running the app and reading `AutoActions.log`.

## Credits and licence

All of the original work is by [Codectory](https://github.com/Codectory) — this fork only patches
and extends it. Licensed under the same terms as upstream; see [LICENSE](LICENSE).
