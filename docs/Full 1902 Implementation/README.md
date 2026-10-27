# Full PID1902 Implementation — Document Authority

Read the Full1902 references linked below together.

## Current authority order

For controller ownership work, use the following precedence when statements conflict:

1. [HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md](https://drive.google.com/file/d/1qvnZlW-Wt82CtaZ3hWrqZjZK42Ona1kZ/view?usp=drivesdk) for:
   - HidHide Applications/Hidden Devices normalization while Addon authority is active;
   - required official `HidHideCLI.exe` / `HidHideClient.exe` registrations;
   - Disabled-boot HidHide reconciliation;
   - Center M Enable HidHide cleanup semantics;
   - mandatory Addon startup-task identity, trigger, and first-create/repair semantics. The task's required RunLevel is governed by item 4.
2. [REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md](https://drive.google.com/file/d/1OAxmzAQKWTsH6dokWJoT4e3YKotAZlSc/view?usp=drivesdk) for the reboot-bound authority/lifecycle design except where item 1 explicitly revises its older foreign-HidHide policy.
3. [FULL_1902_IMPLEMENTATION_ARCHITECTURE.md](https://drive.google.com/file/d/1hxIz9ykzKXaTdzqvZk__9abaF-gCffAk/view?usp=drivesdk) for the overall Full1902 controller architecture except where item 1 explicitly revises its older foreign-HidHide policy.
4. [FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md](https://drive.google.com/file/d/1JZws3Tt5GMbnlkXJm_X7NYqlB3hOpE79/view?usp=drivesdk) for the Full1902 process-privilege model and startup-task RunLevel. It supersedes conflicting least-privilege / non-elevated Runtime and RunLevel statements in items 1–3. It does not change their controller-authority, task identity, trigger, or repair/readback semantics.
5. Git history preserves historical implementation work orders; the reference documents above define the current authority for new work.

## A2VM automatic rumble lifecycle — disabled by default

A2VM 7/8 retain the existing first-unambiguous-control-HID startup topology admission. The optional rumble-specific lifecycle is separate and disabled in the production default by one internal policy. When startup finds PID1902, normal ownership keeps PID1902 and performs the usual GamepadMode, DirectInput first-valid-input, identity, and HidHide proofs. When startup finds PID1901, normal ownership performs the ordinary strict, verified PID1901-to-PID1902 transition before those same final proofs. Neither path claims the boot marker or performs the optional PID1902 → PID1901 → PID1902 boot cycle or the A2VM initial fast-write shortcut.

The A2VM-only `WM_ENDSESSION(TRUE)` PID1901 preparation callback is also not registered in the production default. Ordinary Runtime/OS cleanup and persistent Addon authority remain unchanged; no special session-end mode write is issued. The existing automatic startup and session-end implementations, marker, and tests remain in the source for internal opt-in coverage but are dormant in production. The user-triggered **Controller → Vibration → Restore** operation remains independent and available when its existing healthy-ownership and lifecycle admission succeeds. None of these policies releases Center M authority, changes persistent HidHide, or weakens normal recovery and fail-close proofs.

## Related process-privilege architecture

[FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md](https://drive.google.com/file/d/1JZws3Tt5GMbnlkXJm_X7NYqlB3hOpE79/view?usp=drivesdk) is the active process-privilege authority for administrator-game compatibility.

Current implemented model:

- the primary Full1902 Runtime is the elevated platform process;
- the packaged main EXE remains `asInvoker` for Velopack/bootstrap compatibility, then the normal application path performs one same-user `runas` elevation before `SingleInstanceGate` or controller ownership;
- the supported interactive Windows user is itself an administrator; over-the-shoulder elevation into a different administrator identity is unsupported and must fail closed before Runtime/controller ownership;
- the mandatory startup task launches the Runtime at Highest run level;
- Main UI and Overlay inherit the Runtime token and remain frontend-only;
- the existing Runtime-owned `WinGSuppressionGuard` remains the one WING / Game Bar suppression owner;
- TDP and Center M helpers are retained as separate fault-containment workers, but now use `asInvoker` manifests and inherit the High Runtime token instead of requesting their own elevation;
- FSE registration, prerequisite setup, Windows App Runtime setup, and other retained workers likewise inherit Runtime High where applicable;
- feature-local self-elevation for startup-task repair/removal and SafeUninstall prerequisite cleanup has been removed;
- Sleep/Hibernate/Resume, PID1902/HidHide/VIIPER ownership, and Full1902 fail-close policy remain unchanged.

Remaining process-privilege follow-up is limited to the user-launched external-process boundary (for example front-button/shortcut executable and PowerShell actions) and final hardware validation of the elevated WING/power lifecycle. TDP/Center M helper keep-vs-inline is no longer an open design question: both helpers are intentionally retained for practical operation-failure containment.

This is a process-privilege decision, not a new controller authority.

## Related independent Device-feature architecture

[EX_FIRST_FAN_CONTROL_ARCHITECTURE_2026-09-10.md](https://drive.google.com/file/d/1Asikfgxprm40sVJ66LyzxIw0kw9_0H1c/view?usp=drivesdk) defines the EX-first Device/Cooling fan-control architecture, including MSI/CTW/RE evidence, target-temperature control, presets, calibration, lifecycle, fail-safe behavior, profile integration, and UI direction.

It does **not** modify the controller-ownership authority order above and must not introduce a new controller authority.

## Related independent XBOX game/profile architecture

[XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md](https://drive.google.com/file/d/1qMsur1Zrg8IhEwcfK6fvkAFZOSYAiBXI/view?usp=drivesdk) defines the XBOX/Game Pass catalog, event-driven game identity, XBOX-specific profile persistence/UI, per-game M1/M2 policy, Overlay projection, and front-button Xbox app action.

It is independent of the controller-ownership authority order above and does **not** change:

- PID1902 physical ownership;
- HidHide authority;
- VIIPER ownership/teardown;
- Steam/BPM Xbox360 ↔ SteamDeck presentation policy.

For Main App navigation, its 2026-10-05 Steam/XBOX page split takes precedence over the older single-Profile terminology in [APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md](https://drive.google.com/file/d/1kSCY14gOLVatysn90j3vlkRxBB-vXDyX/view?usp=drivesdk).

## Important 2026-09-01 correction

Older Full1902 documents used a conservative admission rule:

```text
foreign HidHide whitelist/hidden state
→ Conflict
→ refuse Addon Controller Mode
```

That is no longer the current product contract for readable/mutable HidHide configuration.

Current contract:

```text
Center M Disabled / Addon authority
→ Addon normalizes HidHide to its deterministic baseline
→ verify by readback
→ only then allow live controller input/presentation
```

Required Disabled-mode Applications baseline:

```text
verified HidHideCLI.exe
verified HidHideClient.exe
current SteamInputAddonforClaw.exe
```

All other Applications entries are removed while establishing/reconciling Addon authority. Unrelated Hidden Devices entries are also removed; only the exact currently-owned PID1902 primary collection is retained when known.

The Addon does not back up or reconstruct third-party HidHide configuration.

On `Enable Center M and Restart`, the Addon releases/removes its own current controller state, preserves the two official HidHide application registrations, sets the required global release baseline, and does not attempt to restore historical third-party entries.

See the policy revision document for the exact contract and PR10 implementation addendum.
