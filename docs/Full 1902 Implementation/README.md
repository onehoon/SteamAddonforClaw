# Full PID1902 Implementation — Document Authority

Read the Full1902 documents in this folder together.

## Current authority order

For controller ownership work, use the following precedence when statements conflict:

1. `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md` for:
   - HidHide Applications/Hidden Devices normalization while Addon authority is active;
   - required official `HidHideCLI.exe` / `HidHideClient.exe` registrations;
   - Disabled-boot HidHide reconciliation;
   - Center M Enable HidHide cleanup semantics;
   - mandatory Addon startup-task identity, trigger, and first-create/repair semantics. The task's required RunLevel is governed by item 4.
2. `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` for the reboot-bound authority/lifecycle design except where item 1 explicitly revises its older foreign-HidHide policy.
3. `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` for the overall Full1902 controller architecture except where item 1 explicitly revises its older foreign-HidHide policy.
4. `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` for the Full1902 process-privilege model and startup-task RunLevel. It supersedes conflicting least-privilege / non-elevated Runtime and RunLevel statements in items 1–3. It does not change their controller-authority, task identity, trigger, or repair/readback semantics.
5. Historical `docs/work-order/*` files describe the implementation contract at the time each PR was prepared. Later policy revisions and the active work order/addendum take precedence for new implementation work.

## Narrow A2VM boot rumble exception

For A2VM 7/8 only, the Disabled-startup path uses the model identified at startup and the first unambiguous supported control-HID observation; it does not wait for three equal topology snapshots. The once-per-Windows-boot rumble prime remains gated by the existing marker and Center M Disabled authority. When that marker is claimed and startup finds PID1902, the Addon writes once through the unique current PID1902 `0xFFF0/0x0040` endpoint, waits up to 10 seconds for one current PID1901 `0xFFA0/0x0001` endpoint, then writes DirectInput once through that exact fresh instance. No intermediate root, Container-ID, Strong-Identity, or whole-state reproof is performed. If startup finds PID1901, it writes DirectInput once without a separate full transition verification. A successful new DirectInput write is followed by one final readiness window (maximum 12 seconds) that requires PID1901 absence, the exact PID1902 primary gamepad collection, a matching live DirectInput source with its first valid state, and the current compliant HidHide target set before ownership/presentation. This path skips the redundant GamepadMode readback only after that successful DirectInput write; an already-PID1902 startup without a new write retains the existing normalization/readback. Other models, Enabled/stock onboarding, recovery, and resume keep their existing stricter paths. The marker remains boot-scoped and is not controller authority: desired PID1902, persistent HidHide, no ordinary shutdown release, and no repeat of the prime on Runtime restart/resume remain in force. Separately, Windows `WM_ENDSESSION(TRUE)` may request one best-effort PID1901 command only after safe owner retirement; it does not wait for PID1901, release Center M authority, or alter HidHide. User-invoked **Restore Vibration** is an explicit recovery action, not a stock authority release. See the [A2VM initial fast-path work order](../work-order/2026-10-11_A2VM_INITIAL_CONTROLLER_FAST_PATH_HHC_CTW_SIMPLIFICATION_WORK_ORDER.md) and the [2026-10-10 A2VM rumble work order](../work-order/2026-10-10_A2VM_FAMILY_BOOT_RUMBLE_REARM_LED_VIBRATION_AND_RECOVERY_UI_WORK_ORDER.md).

If the single final PID1902/DirectInput readiness window expires while normal PID1902 enumeration is still pending, the existing typed initial-acquisition deferral and Device Arrival recheck may run with the boot marker consumed. It does not repeat either mode write. Ambiguous or incompatible endpoints, PID1901 still present at final admission, invalid DirectInput input, or HidHide/VIIPER failures remain fail-closed; no virtual attach occurs until all final proofs pass.

## Related process-privilege architecture

`FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` is the active process-privilege authority for administrator-game compatibility.

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

`EX_FIRST_FAN_CONTROL_ARCHITECTURE_2026-09-10.md` defines the EX-first Device/Cooling fan-control architecture, including MSI/CTW/RE evidence, target-temperature control, presets, calibration, lifecycle, fail-safe behavior, profile integration, and UI direction.

It does **not** modify the controller-ownership authority order above and must not introduce a new controller authority.

## Related independent XBOX game/profile architecture

docs/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md defines the XBOX/Game Pass catalog, event-driven game identity, XBOX-specific profile persistence/UI, per-game M1/M2 policy, Overlay projection, and front-button Xbox app action.

It is independent of the controller-ownership authority order above and does **not** change:

- PID1902 physical ownership;
- HidHide authority;
- VIIPER ownership/teardown;
- Steam/BPM Xbox360 ↔ SteamDeck presentation policy.

For Main App navigation, its 2026-10-05 Steam/XBOX page split takes precedence over the older single-Profile terminology in docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md.

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
