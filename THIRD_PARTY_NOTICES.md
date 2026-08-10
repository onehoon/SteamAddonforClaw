# Third-Party Notices

This file lists third-party software distributed with, linked by, or referenced during development of Steam Input Addon for Claw. It is not an exhaustive inventory of all files in a final self-contained distribution; release packaging requires a separate, package-specific license review. A project listed in the References section does not have its source code incorporated into this project.

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

### HidHide

- Version: 1.5.230
- License: MIT
- Upstream: https://github.com/nefarius/HidHide
- Distribution: Official unmodified `HidHide_1.5.230_x64.exe` installer bundled for explicit first-time provisioning.

### VIIPER

- Project: VIIPER
- Canonical source: https://github.com/onehoon/VIIPER/tree/steam-input-addon-baseline-1
- Source archive: https://github.com/onehoon/VIIPER/archive/refs/tags/steam-input-addon-baseline-1.tar.gz
- Source baseline: `steam-input-addon-baseline-1` -> `209c882009caea4f3baf322b9b6020c1a921feed`
- Lineage: https://github.com/Valkirie/VIIPER -> https://github.com/Alia5/VIIPER
- Copyright: Peter Repukat (as identified by the upstream project)
- License: GPL-3.0
- Distribution: `Dependencies/Viiper/libVIIPER.dll`, built directly from the pinned source; build command, toolchain, and SHA-256 are recorded beside the payload in `PROVENANCE.md`.

The project does not redistribute Handheld Companion's bundled `libVIIPER.dll` and does not claim byte-for-byte identity with that artifact. HHC is a behavior reference only.

## References

### Handheld Companion

- Upstream: https://github.com/Valkirie/HandheldCompanion
- License: CC-BY-NC-SA-4.0

Used as a technical reference for MSI Claw hardware behavior, controller modes, DirectInput mappings, extra-button identification, and Steam Controller behavior.

No Handheld Companion source code is copied, translated, or ported into this project. Any direct reuse requires a separate license-compatibility review and the applicable copyright and license notices.

### hbashton/DS4Windows

- Upstream: https://github.com/hbashton/DS4Windows
- License: GPL-3.0-or-later

Used as a technical reference for VIIPER lifecycle, usbip-win2 interaction, HidHide coordination, controller hotplug, and physical/virtual controller separation patterns.

No DS4Windows source code is currently incorporated. Any direct reuse requires a separate license review and preservation of the applicable copyright and license notices.

### ClawTweaks

- Upstream: https://github.com/enterTheVoidCode/ClawTweaks
- Copyright: 2025 enterTheVoidCode and ClawTweaks contributors
- License: GNU AGPL v3

ClawTweaks is a compatibility reference only. It is not a runtime dependency: this project does not import its source, depend on private IPC or internals, modify ClawTweaks, or steal or mutate ClawTweaks-owned virtual devices.

## usbip-win2

- Version: 0.9.7.7
- License: BSD-2-Clause
- Upstream: https://github.com/vadimgrn/usbip-win2
- Distribution: official unmodified x64 GitHub release installer
