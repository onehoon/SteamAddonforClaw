# Work Order — Windows 11 Tweaks Tab

> **Date:** 2026-10-13  
> **Product:** Steam Addon for Claw, standalone Full1902 application  
> **Scope:** A new Main UI Tweaks tab with exactly five Windows 11 settings  
> **Delivery:** One focused PR, targeting approximately 500 changed lines where practical  
> **Hardware validation:** Performed by the user after merge; it is not a PR or merge gate

## Goal

Add a dedicated **Tweaks** tab to the existing Main UI. It exposes five individually switchable Windows settings and always derives the displayed state from Windows:

1. **Memory Integrity**
2. **Virtual Machine Platform**
3. **Instant Context Menus** — disable Windows menu animation; this is not a menu-delay change.
4. **Classic Context Menu**
5. **Disable Password on Wake**

Each switch writes its defined ON or OFF configuration immediately, then reads Windows back. There is no separate Apply button, automatic restart, per-toggle restart popup, or per-toggle restart banner. When Windows needs a restart or Explorer restart before a requested state is fully effective, show that status inline with the affected setting.

The page is deliberately a small set of Windows controls. Do not add other tweak examples, a general-purpose registry editor, or a speculative settings framework.

## Required reading and authority

Read these documents before implementation:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md` and its current overrides
- `docs/work-order/MAIN_UI_OVERLAY_TAB_CLAWHUD_SETTINGS_REDESIGN_WORK_ORDER_2026-09-22.md`
- `docs/work-order/2026-10-12_MAIN_APP_UI_SYSTEM_LIGHT_DARK_THEME_WORK_ORDER.md`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml` and `MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml` and `SettingsPage.xaml.cs`
- The current Runtime/frontend contract and its tests

The Full1902 elevated Runtime remains the privileged platform owner. The Main UI is a frontend and must use the existing frontend-to-Runtime request path. Do not add CTW integration, a Windows service, a second elevated process, or a new controller authority. Do not alter controller, HidHide, VIIPER, Steam/BPM, or device-control behavior.

## Product and UI scope

Add one top-level **Tweaks** navigation item and its page to the existing Main UI navigation. Insert **Tweaks immediately above How to Use**. Keep the existing Device default selection and preserve the relative order and behavior of every existing item. Do not place the page inside Settings, Quick Settings Overlay, or the in-game Overlay.

The page contains exactly the five items listed above. Each row has a concise description, a switch, and a compact state/detail label. Match existing WinUI SettingsCard spacing and Fluent styling. Do not add decorative assets, a new visual-design system, or an advanced editor.

The page must:

- Load a fresh OS snapshot when opened and after a mutation completes.
- Show a state read from Windows, never a guessed default.
- Disable the affected switch while its request is running or when the setting is unsupported, managed, or indeterminate.
- Avoid optimistic state changes. A failed write/readback leaves the displayed value at the last verified OS state and shows a concise inline error.
- Keep each tweak independent. A failure or pending restart for one row must not block the other rows.
- Avoid polling. Refresh on page entry, after a write, and when the existing Runtime/frontend lifecycle reports a relevant state refresh.
- Avoid using the UI as the owner of Windows state or OS mutation logic.

Use the existing MainWindow-owned navigation/page lifetime and existing typed frontend transport conventions. Add the smallest typed request/snapshot contract that fits those conventions. Keep one clear Runtime-side owner for these five operations; do not build a generic registry framework, provider plugin system, or multiple overlapping managers.

## First-entry warning

The first time the user opens Tweaks on each **local calendar day**, show one consolidated warning before presenting the controls. Persist the last acknowledged local date through the existing application settings/data persistence path. Do not add a second data root or a new persistence subsystem.

The warning must cover:

- Some changes need Windows restart or Explorer restart to become fully effective. The app will not restart either automatically.
- Memory Integrity strengthens kernel protection; disabling it reduces protection. Enabling it can make incompatible drivers or applications malfunction and, rarely, prevent normal boot.
- Virtual Machine Platform can be required by virtualization-dependent software; disabling it can make that software stop working.
- Disabling password/sign-in on wake allows someone with physical access to resume to the desktop without entering credentials.
- These switches are reversible where Windows and policy permit; policy or firmware locks can prevent a requested change.

Show the warning only once per local date, not once per page navigation or toggle. Record acknowledgement only after the dialog has actually been shown and accepted. If the date changes while the app remains open, the next Tweaks entry may show that day's warning. Do not make the warning a restart prompt or add a separate confirmation dialog to every toggle.

## State and mutation contract

Represent each setting with a small explicit state sufficient to distinguish:

- On / Off (verified effective state)
- Enable pending restart
- Disable pending restart
- Shell/Explorer restart pending, where applicable
- Unsupported / unavailable
- Managed or locked by Windows policy/firmware
- Unknown / read or write failed

Do not collapse unknown, pending, unsupported, or policy-managed into Off. When the OS exposes configured and currently running states separately, report both sufficiently to avoid claiming a pending change is already active. Use inline text such as “Enabled — restart required” or “Managed by Windows” instead of a banner or popup.

For each user switch action:

1. Validate that the setting is supported, writable, and not policy/firmware locked using current OS state.
2. Write only the value(s) owned by that tweak and the requested ON/OFF state.
3. Do not auto-restart Windows, sign out, restart Explorer, or launch another settings app.
4. Read state back from Windows and render the verified result, including pending status.
5. On any partial failure, read state again and report actual state plus the error. Do not report success based only on process exit code or a registry write return.

For a control that requires restart to become effective, the requested configuration may be updated immediately while its runtime/effective state remains pending. A later page refresh or app reopen must continue to report the OS state accurately.

## Exact five tweak definitions

### 1. Memory Integrity

- **Read actual state:** Query the existing Windows Device Guard/VBS state through `Win32_DeviceGuard` in `root\\Microsoft\\Windows\\DeviceGuard`. Use `SecurityServicesConfigured` to identify configured Memory Integrity (service ID 2) and `SecurityServicesRunning` to identify currently running Memory Integrity (ID 2). Do not infer that it is running solely because a registry value is set.
- **Enable:** Use the documented non-UEFI-lock configuration for Memory Integrity. Set only the Memory Integrity configuration owned by this switch. Do not set UEFI lock, mandatory VBS boot, Secure Boot requirements, Credential Guard, or other Device Guard settings.
- **Disable:** Disable only Memory Integrity configuration owned by this switch. Do not turn off VBS, Credential Guard, or other security services as a side effect.
- **Pending:** If the configured and running states disagree after a write, show the requested state as pending restart. If configuration indicates enabled but it is not running because hardware or boot requirements are unavailable, show unavailable/unsupported or an explanatory failure rather than “On.”
- **Managed/locked:** If Group Policy, MDM, App Control, a UEFI lock, or another effective Windows policy owns or locks the state, do not write through it. Disable the switch and explain that Windows manages it. Preserve existing policy and lock values.
- **Safety:** Never set `Locked=1` or `Mandatory=1`. Enabling without UEFI lock is the supported app-owned behavior. The daily warning is the security and compatibility notice; do not add repeated confirmation popups.

Microsoft documents the supported configuration choices, the driver compatibility risk, and the distinction between configured and running state in [Enable memory integrity](https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity).

### 2. Virtual Machine Platform

- **Feature identity:** Windows optional feature name `VirtualMachinePlatform`.
- **Read actual state:** Query Windows Optional Features/DISM and distinguish `Enabled`, `Disabled`, `EnablePending`, `DisablePending`, payload unavailable, and query failure.
- **Enable/disable:** Request the corresponding optional-feature change without allowing DISM/Windows to reboot automatically. Do not enable WSL or any other optional feature as a side effect.
- **Pending:** Preserve and show DISM's enable/disable pending state. A successful command with pending state is not the same as a currently enabled/disabled feature.
- **Managed/unavailable:** If servicing policy blocks the operation, the payload is unavailable, or DISM cannot determine state, show the reason and leave the switch disabled until a fresh state read succeeds.
- **Scope:** Do not silently download or install unrelated components. Do not use direct registry edits as a substitute for the Windows feature-management API.

Microsoft documents Windows feature servicing and its pending states in [Enable or Disable Windows Features Using DISM](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/enable-or-disable-windows-features-using-dism?view=windows-11).

### 3. Instant Context Menus

- **Meaning:** Turn Windows menu animation off so menus appear without the animation. This is specifically the animation setting, not the delay before a menu opens.
- **Read actual state:** Use `SystemParametersInfo(SPI_GETMENUANIMATION)`.
- **Enable/disable:** Use `SystemParametersInfo(SPI_SETMENUANIMATION)` with the requested boolean and the normal persistence/change-notification flags. Re-read with `SPI_GETMENUANIMATION` before reporting success.
- **ON:** Menu animation disabled.
- **OFF:** Menu animation enabled.
- Do not read, write, or tune `MenuShowDelay`; do not alter tooltip, combo-box, window, or general UI animation settings.

Microsoft documents the menu-animation getter/setter in [SystemParametersInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow).

### 4. Classic Context Menu

- **Meaning:** Use the Windows 11 classic context menu for the current Windows user.
- **State mapping:** The enabled state is the presence of the exact `InprocServer32` key under `HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}`. Read the exact key/value without treating unrelated shell registrations as this tweak.
- **ON:** Create the exact per-user key and its default string value as an empty string.
- **OFF:** Remove only the exact key created/owned by this tweak. Do not delete its parent CLSID or other shell/COM registrations.
- **Pending:** Do not restart Explorer automatically. After write/readback, show “Explorer restart required” until the existing shell applies the change. No restart banner or popup.
- **Policy/error:** If Windows policy prevents the change, or readback fails, show managed/unknown rather than claiming success.
- Do not add an “old context menu delay” or any other registry sample.

### 5. Disable Password on Wake

- **Meaning:** When ON, request that Windows return from Sleep/Hibernate to the desktop without requiring sign-in. This weakens physical-access protection.
- **Read actual state:** Read the active power scheme's `CONSOLELOCK` (`0e796bdb-100d-47d6-a2d5-f7d2daa51f51`) values for both AC and DC. Show ON only when both are configured to not require a password; show mixed/unknown if they differ or cannot be read.
- **Enable/disable:** Write `CONSOLELOCK=0` for both AC and DC when enabling “Disable Password on Wake”; write `CONSOLELOCK=1` for both when disabling the tweak. Re-read both values. Preserve all other active power scheme settings.
- **Managed:** If effective Windows policy owns the wake sign-in setting or prevents the write, disable the switch and identify it as managed. Do not fight policy by repeatedly rewriting the value.
- **Scope:** Do not disable the lock screen globally, change account passwords/PINs, alter screensaver sign-in, or turn off sign-in after display timeout. Do not trigger a restart or sleep cycle.

Microsoft identifies this power setting as `CONSOLELOCK`, including its values and resume behavior, in [Prompt for password on resume](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/no-subgroup-settings-prompt-for-password-on-resume).

## Model applicability

The tweak definitions/catalog must support an explicit applicability value so model-specific registry tweaks can be filtered using the product's existing model-identification source.

- A model-independent entry is visible on supported Windows 11 devices.
- An entry marked **A2VM-only** is visible only when the existing model identity positively identifies a supported A2VM model.
- Unknown, ambiguous, or unsupported model identity must never expose an A2VM-only entry. Fail closed for that entry; do not guess from a marketing name or registry data.
- Keep applicability metadata in the existing small typed catalog/definition for each of the five entries. Do not add extra example rows, placeholder tweaks, or a general-purpose catalog editor.
- If none of the five agreed definitions is tagged A2VM-only, the filtering path still needs a small deterministic test that proves an A2VM-only definition would be hidden on unknown/unsupported models. Do not label a generally applicable setting A2VM-only merely to populate a test case.

## Errors, policy, and lifecycle

- Keep OS access and mutations in the existing elevated Runtime process and its existing lifecycle. Main UI closure or navigation must not change OS settings.
- Use bounded, cancellable async operations where the existing frontend contract requires it; keep the UI responsive while DISM/WMI/registry operations run.
- Serialize mutations per setting through the existing Runtime request handling. Do not introduce global locks, epochs, retry loops, polling, or a generalized transaction engine.
- Never retry a failed registry/DISM/power setting write in a loop. A user may retry by operating the switch again after the state is refreshed.
- Treat OS read failures, access denied, missing WMI/DISM capability, conflicting policy, and partial AC/DC writes as real errors. Re-read actual state and report it plainly.
- Do not log credentials or unrelated registry contents. Logs should identify the tweak, operation, outcome/state, and sanitized OS error details only.
- The supported app remains one interactive Windows user/session. Do not add RDP, Fast User Switching, or multi-session handling.

## Deterministic verification

Add focused tests for the state mapping and Runtime/frontend contract using fakes or pure state-mapping helpers. At minimum verify:

- Exactly five entries exist and their order/names match the approved list.
- Navigation places Tweaks immediately above How to Use without changing the existing default page or the relative order/behavior of other navigation items.
- Every displayed switch is initialized from an OS snapshot; unknown/pending/managed are never mapped to Off.
- ON and OFF map to the exact values and only the owned key/feature/power settings are changed.
- The context-menu animation toggle uses `SPI_GETMENUANIMATION` / `SPI_SETMENUANIMATION`, and there is no `MenuShowDelay` mutation.
- Classic context menu removal deletes only the exact per-user tweak key.
- Power wake-sign-in reads and writes both AC and DC values, and mixed state is not presented as verified ON/OFF.
- Memory Integrity distinguishes configured from running, does not set a UEFI lock, and reports pending/managed/unavailable states.
- Virtual Machine Platform distinguishes DISM pending/payload-unavailable/error from enabled/disabled.
- A2VM-only definitions are hidden for unknown and unsupported models, with no extra catalog entry required.
- The daily warning is shown once per local date and at most once after acknowledgement; date boundaries use local calendar date.
- Write failure/partial failure re-reads state and never displays an unverified success.
- No code path auto-restarts Windows or Explorer, no per-toggle restart modal/banner is introduced, and no separate Apply action exists.

Run the focused automated tests and the repository's standard build/test checks relevant to changed projects. Hardware validation on an MSI Claw is owned by the user after merge; lack of access to hardware must not block review or merge.

## Out of scope

- Any tweak beyond the exact five listed above.
- CPU/GPU/performance presets, debloating, services, startup apps, privacy settings, power plans, or generic registry editing.
- Menu-open delay changes or `MenuShowDelay`.
- Automatic restart, scheduled restart, automatic Explorer restart, sign-out, or per-toggle restart prompts/banners.
- Password/PIN changes, global lock-screen removal, account/security policy bypass, or credential storage.
- CTW integration or changes to the controller/Full1902 authority model.
- Hardware test automation or hardware validation as a PR/merge gate.

## Acceptance criteria

- The Main UI has a dedicated Tweaks tab immediately above How to Use, with exactly the five approved entries and a consolidated once-per-local-day warning.
- Each switch reads current Windows state, immediately writes its defined ON/OFF values, reads back, and clearly distinguishes effective, pending, managed, unsupported, and error states.
- There is no automatic restart, per-toggle restart popup/banner, or Apply button.
- Unsupported or unknown models never see any A2VM-only registry tweak.
- No controller ownership or existing UI navigation behavior regresses.
- Automated/deterministic checks and standard checks pass. Hardware validation remains post-merge user work, not a blocker.

## Suggested PR shape

Deliver as one focused PR if the implementation remains near the repository's approximately 500-line review target. Keep the work to the new page/navigation, the smallest Runtime/frontend contract and implementation, focused tests, and no unrelated cleanup. If measured scope materially exceeds that target, split only at a clean feature boundary while keeping this work order's five-entry behavior and state contract intact.
