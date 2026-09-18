# Contributing to arzGUI

## Before you start

Open an issue before a large behavioural or visual change. Keep compatibility with Windows 11,
.NET Framework 4.8, C# 7.3, and the existing `packages.config` projects. Do not edit
`Source/AutoActions/Controls/AppResources.xaml`; the build generates it from the numbered resource
dictionaries.

## Build and verify

Use Visual Studio 2022 or Build Tools with the .NET Framework 4.8 targeting pack:

```powershell
$msbuild = (Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\amd64\MSBuild.exe" | Select-Object -Last 1).FullName
& $msbuild Source\AutoActions.sln -t:Restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64
& $msbuild Source\AutoActions.sln -t:Build -m -p:Configuration=Debug -p:Platform=x64
```

Run `Source\Debug_x64\arzGUI.exe`. Exercise the changed workflow, inspect `arzGUI.log`, and verify
UI changes in both light and dark themes. Hardware-specific display changes should include the
result from the relevant check under `Source/Tools/`.

## Changes and pull requests

Use four-space C# indentation and existing naming conventions. Add new files to their `.csproj`.
Use concise Conventional Commit messages such as `fix: restore display mode after exit`. Pull
requests should explain the behaviour and risk, list manual checks, link related issues, and attach
before/after screenshots for UI work. By contributing, you agree that your contribution is
licensed under GPL-3.0.
