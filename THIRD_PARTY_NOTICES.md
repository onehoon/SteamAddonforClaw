# Third-Party Notices

This file lists third-party software distributed with, linked by, or referenced during development of Steam Input Addon for Claw. A project listed in the References section does not have its source code incorporated into this project.

## Distributed / Linked Components

The following are the direct NuGet dependencies currently declared by `src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj`.

### Microsoft.WindowsAppSDK

- Version: 2.3.1
- License: Microsoft Software License Terms (the package's `license.txt`)
- Upstream: https://github.com/microsoft/WindowsAppSDK

### Velopack

- Version: 1.2.0
- License: MIT
- Upstream: https://github.com/velopack/velopack

### Vortice.DirectInput

- Version: 3.8.3
- License: MIT
- Upstream: https://github.com/amerkoleci/Vortice.Windows

## Planned Components

### VIIPER

- Project: VIIPER
- Upstream: https://github.com/Valkirie/VIIPER
- Original project: https://github.com/Alia5/VIIPER
- Copyright: Peter R. (as identified by the upstream project)
- License: GPL-3.0-or-later

VIIPER is intended to provide the virtual USB controller backend. It is not included in the current development build and this repository does not currently distribute `libVIIPER.dll` or `viiper.exe`.

If a modified VIIPER build is distributed by this project, the corresponding modified source will be made available under the applicable GPL terms.

## References

### Handheld Companion

- Upstream: https://github.com/Valkirie/HandheldCompanion
- License: CC-BY-NC-SA-4.0

Used as a technical reference for MSI Claw hardware behavior, controller modes, DirectInput mappings, extra-button identification, and Steam Controller behavior.

No Handheld Companion source code is copied, translated, or ported into this project. Any direct reuse requires a separate license-compatibility review and the applicable copyright and license notices.

### hbashton/DS4Windows

- Upstream: https://github.com/hbashton/DS4Windows
- License: GPL-3.0

Used as a technical reference for VIIPER lifecycle, usbip-win2 interaction, HidHide coordination, controller hotplug, and physical/virtual controller separation patterns.

No DS4Windows source code is currently incorporated. Any direct reuse requires a separate license review and preservation of the applicable copyright and license notices.

### ClawTweaks

ClawTweaks is a compatibility reference only. It is not a runtime dependency: this project does not import its source, depend on private IPC or internals, modify ClawTweaks, or steal or mutate ClawTweaks-owned virtual devices.
