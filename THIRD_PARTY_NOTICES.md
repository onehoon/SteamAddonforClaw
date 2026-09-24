# Third-Party Notices

This file lists third-party software distributed with, linked by, or referenced during development of Steam Addon for Claw. It is not an exhaustive inventory of all files in a final self-contained distribution; release packaging requires a separate, package-specific license review. A project listed in the References section does not have its source code incorporated into this project.

## Distributed / Linked Components

The following are the direct NuGet dependencies currently declared by `src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj`.

### CommunityToolkit.WinUI.Controls.SettingsControls

- Version: 8.2.250402
- License: MIT
- Upstream: https://github.com/CommunityToolkit/Windows

### Microsoft.WindowsAppSDK

- Version: 2.5.1
- License: Microsoft Software License Terms (the package's `license.txt`)
- Upstream: https://github.com/microsoft/WindowsAppSDK

### System.Management

- Version: 10.0.0
- License: MIT
- Upstream: https://github.com/dotnet/runtime

### Velopack

- Version: 1.2.158
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
- Distribution: The application pins the official upstream release installer version and SHA-256. When explicit prerequisite provisioning is required, the installer is downloaded from the pinned official GitHub release asset, verified before execution, and is not included in the application package.

### VIIPER

- Project: VIIPER
- Canonical source: https://github.com/onehoon/VIIPER/tree/6a30dc60bb11ca25b938e479dc959b8172651f91
- Source baseline: pinned commit `6a30dc60bb11ca25b938e479dc959b8172651f91`
- Lineage: https://github.com/Valkirie/VIIPER -> https://github.com/Alia5/VIIPER
- Copyright: Peter Repukat (as identified by the upstream project)
- License: GPL-3.0
- Distribution: `Dependencies/Viiper/libVIIPER.dll`, built directly from the pinned source; build command, toolchain, and SHA-256 are recorded beside the payload in `PROVENANCE.md`.

The project does not redistribute Handheld Companion's bundled `libVIIPER.dll` and does not claim byte-for-byte identity with that artifact. HHC is a behavior reference only.

### NirCmd

- Version: 2.87 (x64)
- Copyright: Copyright (c) 2003–2024 Nir Sofer
- License: Freeware; redistribution is permitted without charge when the complete distribution package is included without modification
- Upstream: https://www.nirsoft.net/utils/nircmd.html
- Distribution: The complete official x64 ZIP contents are included under `src/SteamInputAddonforClaw/Dependencies/NirCmd/`. The Addon invokes `nircmdc.exe`; upstream archive details, file list, retrieval date, and SHA-256 values are recorded in the adjacent `PROVENANCE.md`.

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

ClawTweaks is a compatibility and technical reference only. It is not a runtime dependency: this project does not import its source, depend on ClawTweaks-specific internal APIs, modify ClawTweaks, or steal or mutate ClawTweaks-owned virtual devices. The projects are independently implemented.

## usbip-win2

- Version: 0.9.8.0
- License: BSD-2-Clause
- Upstream: https://github.com/vadimgrn/usbip-win2
- Distribution: The application pins the official upstream x64 release installer version and SHA-256. When explicit prerequisite provisioning is required, the installer is downloaded from the pinned official GitHub release asset, verified before execution, and is not included in the application package.
