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

## Related process-privilege architecture

`FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` records the accepted direction for administrator-game compatibility:

- the primary Full1902 Runtime becomes the elevated platform process;
- elevation is required because the medium-integrity Runtime did not receive the relevant low-level WING keyboard sequence from an administrator-elevated foreground game, allowing native Game Bar activation;
- the supported interactive Windows user is itself an administrator; elevation must retain that user's identity;
- Main UI and Overlay are not intentionally de-elevated and may inherit the Runtime token;
- the existing Runtime-owned `WinGSuppressionGuard` remains the one WING / Game Bar suppression owner;
- the existing mandatory startup task must launch the Runtime at the required elevated run level;
- Sleep/Hibernate/Resume, PID1902/HidHide/VIIPER ownership, and Full1902 fail-close policy remain unchanged;
- existing privileged helpers, including the TDP helper, stay unchanged during the first elevation implementation;
- user EXE / PowerShell privilege-boundary cleanup and helper consolidation are explicitly deferred to a later focused design.

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
