# Full PID1902 Implementation — Document Authority

Read the Full1902 documents in this folder together.

## Current authority order

For controller ownership work, use the following precedence when statements conflict:

1. `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md` for:
   - HidHide Applications/Hidden Devices normalization while Addon authority is active;
   - required official `HidHideCLI.exe` / `HidHideClient.exe` registrations;
   - Disabled-boot HidHide reconciliation;
   - Center M Enable HidHide cleanup semantics;
   - mandatory Addon startup-task first-create/repair semantics.
2. `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` for the reboot-bound authority/lifecycle design except where item 1 explicitly revises its older foreign-HidHide policy.
3. `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` for the overall Full1902 controller architecture except where item 1 explicitly revises its older foreign-HidHide policy.
4. Historical `docs/work-order/*` files describe the implementation contract at the time each PR was prepared. Later policy revisions and the active work order/addendum take precedence for new implementation work.

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
