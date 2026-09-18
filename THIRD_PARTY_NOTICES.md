# Third-party notices

arzGUI distributes the following independent components. Their source and licence pages are linked
below; unmodified licence texts are included in release archives under `ThirdPartyLicenses/`.

| Component | Version | Licence and copyright |
|---|---:|---|
| [AutoActions](https://github.com/Codectory/AutoActions) | upstream source | GPL-3.0; Codectory and contributors |
| [AudioSwitcher](https://github.com/xenolightning/AudioSwitcher) | vendored source | Ms-PL; © Xenolightning |
| [CCDWrapper](https://github.com/regueiro/CCDWrapper) | 1.0.1 | MIT; © 2013–2014 Erti-Chris Eelmaa, Santiago Regueiro |
| [CoreAudio](https://www.nuget.org/packages/CoreAudio/1.40.0) | 1.40.0 | MIT; © 2017 Xavier Flix |
| [Hardcodet.NotifyIcon.Wpf](https://www.nuget.org/packages/Hardcodet.NotifyIcon.Wpf/1.1.0) | 1.1.0 | Code Project Open License 1.02 |
| [Microsoft.Xaml.Behaviors.Wpf](https://github.com/microsoft/XamlBehaviorsWpf) | 1.1.39 | MIT; © Microsoft Corporation |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | 13.0.1 | MIT; © 2007 James Newton-King |
| [NvAPIWrapper](https://github.com/falahati/NvAPIWrapper) | 0.8.1.101 | LGPL-3.0; © 2017–2020 Soroush Falahati |
| Microsoft .NET compatibility libraries | package versions in `packages.config` | MIT; © .NET Foundation and contributors |

The AudioSwitcher device-switching wrappers are vendored as source under
`Source/AutoActions.Audio/AudioApi/` and `AudioApi.CoreAudio/`, carried over from upstream
AutoActions. They are covered by the Microsoft Public License, whose compatibility with the
GPL is disputed; replacing them with an MIT-licensed equivalent is tracked as open work.

NvAPIWrapper is dynamically linked as `NvAPIWrapper.dll`. It is unmodified and replaceable with a
compatible build. Its LGPL-3.0 source is available from the linked upstream repository. The arzGUI
source and build instructions provide the corresponding application code needed to relink it.

Windows, NVIDIA, and other product names are trademarks of their respective owners. Their mention
describes compatibility only and does not imply endorsement.
