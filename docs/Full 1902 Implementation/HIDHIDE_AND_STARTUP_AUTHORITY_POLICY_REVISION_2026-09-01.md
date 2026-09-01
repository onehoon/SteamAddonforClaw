# Full PID1902 Policy Revision — HidHide Deterministic Authority and Mandatory Startup

> **Date:** 2026-09-01  
> **Status:** Current product-policy authority for the topics below  
> **Scope:** Full PID1902 / Addon Controller Mode HidHide ownership, Disabled-boot normalization, Center M Enable cleanup, and mandatory Addon startup-task registration

---

## 1. Why this revision exists

Real MSI Claw Full1902 hardware validation exposed two concrete blockers before a normal user could complete the first `Disable Center M and Restart` transition:

1. the Full1902 persistent HidHide baseline treated the official `HidHideCLI.exe` and `HidHideClient.exe` whitelist registrations as foreign state; and
2. the first creation of the mandatory Addon background Task Scheduler entry failed with `E_ACCESSDENIED` when no Addon task existed yet.

The resulting product behavior was unacceptable:

```text
normal user chooses Addon Controller Mode
→ recoverable existing HidHide/startup state is reported as an error
→ user must understand and manually repair internal Windows/HidHide configuration
→ Addon authority can never be entered
```

The intended product contract is instead:

> **When the user explicitly selects Addon Controller Mode, the Addon owns the effective controller-isolation baseline. Recoverable current Windows/HidHide state must be normalized into that baseline automatically and verified before controller presentation starts.**

The Addon does not need to know which third-party application created historical HidHide entries, and it must not build backup/restore machinery for them.

---

## 2. Precedence over earlier documents

This revision supersedes earlier Full1902 statements that require arbitrary foreign HidHide whitelist/hidden-device state to remain untouched and block Addon authority admission.

In particular, for production Full1902 behavior this revision supersedes conflicting portions of:

- `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
  - §11 where the Disabled baseline was described only as the Addon executable plus the exact PID1902 target;
  - §12 where unsafe/foreign HidHide state was described as a reason to refuse admission and require manual user cleanup;
- `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
  - foreign-HidHide admission/fail-closed wording that assumes the Addon must preserve arbitrary prior configuration;
- `PR2_ADDON_OWNED_HIDHIDE_BASELINE_WORK_ORDER.md`
  - §4/§5/§9 and related tests/acceptance criteria that classify supported readable foreign whitelist/hidden entries as `Conflict` rather than normalization input;
- `PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`
  - transition tests/requirements that preserve arbitrary foreign HidHide state and block Disable/Enable on its presence;
- `PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
  - statements/tests requiring arbitrary foreign HidHide entries to remain `Blocked` after Addon authority has been selected;
- `PR8_OWNED_DIRECTINPUT_SESSION_RECOVERY_WORK_ORDER.md`
  - the statement `Do not remove foreign HidHide state` when reconciling an already-owned Disabled-mode controller.

Those work orders remain useful historical records of the implementation sequence. They are not the current product policy where they conflict with this revision.

This revision does **not** supersede strong physical-identity rules, exact PID1902 target rules, fail-close behavior on real operation failure, or the PR10 rule that a changed owned PID1902 target after physical re-enumeration is not silently migrated.

---

## 3. Authority model remains unchanged

There are still exactly two controller-authority modes:

```text
Center M Enabled
→ MSI / stock controller authority
→ desired physical PID = PID1901

Center M Disabled
→ Addon Runtime controller authority
→ desired physical PID = PID1902
→ Addon Runtime mandatory
→ persistent Addon HidHide controller-isolation authority
```

Steam/BPM still selects only the virtual presentation while the Addon owns the controller:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

This revision does not add a second authority source.

---

## 4. Meaning of exclusive Addon Controller Mode

Addon Controller Mode is not a coexistence mode with HHC, ClawTweaks, DS4Windows, another controller middleware, or manually maintained HidHide controller configuration.

Once the user explicitly chooses Addon Controller Mode:

```text
Addon = controller authority
Addon = effective HidHide controller-isolation authority
```

Therefore the Addon may normalize the current HidHide configuration to the exact state it requires.

It must **not**:

- determine which application originally created each entry;
- maintain per-application HidHide ownership;
- back up arbitrary third-party whitelist/hidden state;
- restore arbitrary third-party state on Center M Enable;
- build a generalized multi-owner/lease/arbitration system.

Other applications are responsible for rebuilding their own state when they later start and become relevant again.

---

## 5. Required official HidHide Applications entries

While Addon Controller Mode is active, the deterministic Applications baseline contains exactly:

```text
verified official HidHideCLI.exe
verified official HidHideClient.exe
current SteamInputAddonforClaw.exe
```

The official HidHide paths must be derived from trusted HidHide package/install-location evidence, using the existing `HidHideTrustedApplicationPathResolver` or the smallest refinement of it.

Do not trust an arbitrary whitelist entry merely because its filename is `HidHideCLI.exe` or `HidHideClient.exe`.

For the currently supported HidHide layout, both root and `x64` candidates may be considered exactly as the existing resolver already does.

### Missing Applications registration is recoverable

This revision is specifically about **HidHide Applications registration**, not missing files on disk.

If the official executable path is resolvable but its Applications registration is absent:

```text
HidHideCLI registration missing
→ AddApplication(canonical HidHideCLI path)

HidHideClient registration missing
→ AddApplication(canonical HidHideClient path)
```

The same applies to a missing Addon Runtime registration.

A user must not be required to open HidHide Configuration Client and re-check these entries manually.

### Actual file/package corruption remains out of scope

This revision does not define automatic repair/reinstallation for:

```text
HidHideCLI.exe file missing from disk
HidHideClient.exe file missing from disk
corrupt/incomplete HidHide package installation
```

If a required canonical official path cannot be resolved under the current supported installation contract, treat the prerequisite as unavailable/fail closed. Package repair may be designed separately if real product evidence requires it.

---

## 6. Deterministic Disabled-mode HidHide baseline

### 6.1 Applications

The desired whitelist is exactly:

```text
Applications =
{
    OfficialHidHideCLI,
    OfficialHidHideClient,
    CurrentAddonRuntime
}
```

Any other Applications entry is normalization input, not a user-facing conflict by itself.

Conceptually:

```text
for each current whitelist entry:
    exact verified official CLI    → keep
    exact verified official Client → keep
    current Addon Runtime          → keep
    anything else                  → remove

missing official CLI    → add
missing official Client → add
missing Addon Runtime   → add
```

This includes stale/unknown entries from controller software the Addon does not identify or support.

If an actual add/remove operation fails or cannot be verified, that operation failure remains fail-closed.

### 6.2 Hidden Devices

The desired hidden-device set depends only on current Addon ownership stage.

Before the exact PID1902 target is known:

```text
HiddenDevices = []
```

After the exact Addon-owned PID1902 primary gamepad collection is proven:

```text
HiddenDevices =
{
    exact Addon-owned PID1902 primary gamepad collection
}
```

All unrelated current hidden-device entries are removed during Disabled-mode normalization.

Do not:

- infer historical ownership;
- preserve arbitrary previous hidden devices merely because another app may once have created them;
- hide all PID1902 children;
- hide PID1901;
- hide VIIPER virtual targets;
- invent VID/PID wildcards.

### 6.3 Global HidHide state

The Disabled baseline remains:

```text
Inverse whitelist = false
Active            = true
```

Every mutation must be followed by readback verification.

---

## 7. Disable transition contract

The reboot-bound Disable transition remains ordered around safety:

```text
User selects Disable and Restart
→ read-only supported-device / transition safety checks
→ ensure and verify mandatory Addon background startup
→ normalize and verify zero-target Disabled HidHide baseline
→ disable and verify Center M startup roots
→ reboot
```

The transition must no longer fail merely because readable/mutable HidHide contains additional whitelist or hidden-device entries.

Instead:

```text
foreign/stale entries present
→ normalize them away
→ verify exact Addon baseline
→ continue
```

Real operation failures still block the transition:

- HidHide unreadable/unavailable;
- official application path cannot be resolved under the supported install contract;
- whitelist/hidden mutation fails;
- inverse/active mutation fails;
- readback does not match the required baseline;
- mandatory startup task cannot be established and verified;
- Center M startup mutation fails or verifies Partial/incorrect.

Do not disable Center M if the mandatory Runtime startup contract has not been proven.

---

## 8. Every Disabled boot must reconcile HidHide before controller activation

Persistent HidHide state from the previous shutdown is not automatically trusted.

A user or another application may have changed HidHide while the Addon was not running.

On every boot where Center M startup roots classify exactly `Disabled`:

```text
1. confirm Center M == Disabled
2. inspect HidHide
3. resolve official HidHideCLI path
4. resolve official HidHideClient path
5. normalize Applications to CLI + Client + Addon only
6. normalize Hidden Devices to the state appropriate for the current ownership stage
7. ensure Inverse=false
8. ensure Active=true
9. re-read and verify the entire baseline
10. only then continue physical ownership / DirectInput / VIIPER presentation
```

The boot question is not:

```text
Who changed HidHide?
```

It is only:

```text
Can the Addon establish and prove the exact isolation state required to exercise its authority now?
```

If not, fail closed before non-neutral controller presentation becomes active.

---

## 9. First Disabled boot remains two-stage

The exact PID1902 primary collection may not exist yet when the Disabled Runtime first starts.

Therefore preserve the two-stage isolation sequence:

```text
Center M Disabled
→ normalize zero-target baseline

Applications:
  HidHideCLI
  HidHideClient
  Addon
Hidden: []
Inverse=false
Active=true

→ verify
→ acquire/reconcile same supported physical MSI Claw to PID1902
→ resolve exact primary PID1902 gamepad collection
→ normalize final baseline

Applications:
  HidHideCLI
  HidHideClient
  Addon
Hidden:
  exact owned PID1902 primary collection
Inverse=false
Active=true

→ verify
→ start/restart DirectInput as appropriate
→ attach/reconcile virtual presentation
```

This preserves isolation-before-input and prevents physical + virtual double input.

---

## 10. Owned recovery uses the same HidHide baseline

PR8/PR9/PR10 recovery must not create a separate HidHide policy.

Before restarting DirectInput after a lost/returned owned physical controller:

```text
strong physical identity proven
→ exact owned target proven
→ Center M still exactly Disabled at the existing mutation boundary
→ normalize/verify the same deterministic Disabled HidHide baseline
→ only then restart DirectInput
```

If another application altered HidHide while the physical controller was absent, that configuration is normalized away.

### PR10 changed-target rule remains unchanged

Do not confuse unrelated foreign hidden entries with the committed owned target identity.

If a real PnP return resolves the same physical MSI Claw to a **different exact PID1902 primary collection** than the committed `_ownedHiddenTarget`:

```text
RecoveredTargetChanged
→ do not silently migrate the committed target in PR10
→ remain neutral
→ record evidence
```

Crash-safe owned-target migration is a separate design question and still requires hardware evidence.

---

## 11. Center M Enable is intentionally asymmetric

`Enable Center M and Restart` releases Addon authority.

The Addon is responsible for removing **its current controller state**, not reconstructing third-party historical state.

Required HidHide release behavior:

```text
remove current Addon Runtime whitelist entry
remove exact Addon-owned hidden target(s) known to the current owner
ensure Inverse=false
ensure Active=false
preserve verified official HidHideCLI registration
preserve verified official HidHideClient registration
```

Do not restore entries removed when Addon Controller Mode was entered.

Do not maintain:

```text
PreviousApplications[]
PreviousHiddenDevices[]
PreviousActiveState
PreviousInverseState
ThirdPartyOwnerMap
```

If another program has independently added its own entries while Addon mode was active, the Addon does not need to identify or reconstruct that program's policy. The other program is responsible for its own startup/reconciliation when it runs.

The Addon must still verify that **its own** authority-release mutations and required global stock-compatible state succeeded before enabling Center M and rebooting.

---

## 12. Foreign-state presence versus actual failure

After this revision, these facts alone are **not** product blockers while entering/reconciling Addon authority:

```text
extra whitelist application exists
extra hidden-device entry exists
official CLI registration is missing
official Client registration is missing
Addon registration is missing
Active=false
Inverse=true, when the existing supported mutation primitive can normalize it safely
```

They are states to normalize.

These remain real blockers:

```text
HidHide cannot be read/configured
required canonical official application path cannot be resolved under supported install assumptions
required mutation fails
post-mutation readback is incorrect/unavailable
physical identity is missing/ambiguous/different at a mutation boundary
Center M authority is not the required state
mandatory Runtime startup cannot be established/verified
```

The distinction is:

> **Existing recoverable configuration drift is not an error. Inability to establish and prove the required baseline is an error.**

---

## 13. Mandatory Addon background startup — first registration

Center M Disabled is a durable authority mode, so the Addon background Runtime startup task is mandatory.

A clean installation may have no existing Addon Task Scheduler entry.

The normal product flow must therefore support:

```text
required Addon startup task missing
→ create it
→ read it back
→ verify exact required contract
→ only then allow Center M Disable
```

A first-use `E_ACCESSDENIED` from normal Runtime registration must not permanently strand the user.

---

## 14. Startup-task verification and mutation policy

Keep one startup-registration owner. Do not add another persisted controller-authority setting.

### Existing compliant task

If an existing Addon-owned task already matches the required contract:

```text
→ success by read-only verification
→ do not rewrite the task
→ no elevation solely for idempotent verification
```

Required contract includes at minimum:

```text
Task name   = Steam Input Addon for Claw
Enabled     = true
Executable  = stable current SteamInputAddonforClaw.exe
Arguments   = --background
Trigger     = current-user interactive logon
Logon type  = InteractiveToken
Run level   = least privilege / non-elevated Runtime
```

### Missing or drifted task

If the task is missing or materially drifted and normal creation/repair is denied:

```text
→ use one bounded elevated write path
→ create/repair only the fixed Addon-owned startup task
→ elevated helper exits
→ normal Runtime reads task back independently
→ success only if exact contract verifies
```

Prefer reusing the repository's existing bounded `runas` + named-pipe helper pattern. Extending the existing startup-configuration helper with one tightly constrained `EnsureAddonStartupTask` operation is acceptable if it is the smallest implementation.

Do not expose a generic administrator Task Scheduler API such as `CreateAnyTask(...)`.

Do not add:

- a permanent elevated Runtime;
- a Windows service solely for this;
- a long-lived privileged broker;
- a Task Scheduler manager hierarchy;
- a second startup authority database.

If UAC is cancelled or the repair/readback fails, leave Center M Enabled and report the transition failure.

---

## 15. Startup-task repair is idempotent

After the first successful creation:

```text
next Runtime start
→ task exists and matches
→ read-only verification succeeds
→ no RegisterTaskDefinition churn
→ no startup-task UAC
```

Repair is required only when the task is missing or materially drifted.

This is both simpler and avoids rewriting an already-valid system object on every startup.

---

## 16. No third-party backup/reconstruction

The following design is explicitly rejected:

```text
enter Addon mode
→ snapshot every HidHide application/hidden entry and global flag
→ run Addon
→ Enable Center M
→ reconstruct the old snapshot
```

Reasons:

- the Addon does not know which process still owns or needs historical entries;
- the captured configuration may already be stale;
- restoring it could resurrect an old or incompatible controller stack;
- it introduces durable ownership metadata, rollback state, and recovery obligations with no supported product requirement;
- it conflicts with the product's exclusive single-controller-authority model.

The desired model is simply:

```text
Addon authority active
→ converge current system to Addon baseline

Addon authority released
→ remove Addon-owned state / disable Addon isolation
→ other applications manage themselves when they run
```

---

## 17. Required implementation/tests for the policy revision

At minimum, current Full1902 implementation must prove:

### Disabled normalization

- official CLI + Client + Addon already registered → idempotent success;
- CLI registration missing → added and verified;
- Client registration missing → added and verified;
- Addon registration missing → added and verified;
- arbitrary extra application → removed and verified;
- arbitrary extra hidden device → removed and verified;
- zero-target baseline → exactly zero hidden targets;
- known-target baseline → exactly requested Addon-owned target(s);
- Active=false → Active=true;
- supported Inverse=true state → Inverse=false;
- mutation/readback failure → fail closed.

### Disabled boot

- every exact `Center M == Disabled` startup re-normalizes before physical/virtual activation;
- configuration drift introduced between runs is corrected;
- failed HidHide verification prevents DirectInput/presentation activation.

### Enable cleanup

- Addon whitelist removed;
- Addon-owned target removed;
- official CLI/Client registrations preserved;
- Active=false and Inverse=false verified;
- no third-party historical restore logic exists.

### Mandatory startup

- existing compliant task verifies without write/elevation;
- missing task can be created through bounded elevation and then verified non-elevated;
- drifted task can be repaired and verified;
- helper/UAC/readback failure prevents Center M Disable;
- second startup after successful creation performs no unnecessary rewrite.

Do not add artificial timing/race machinery beyond realistic supported lifecycle needs.

---

## 18. Final invariant

The current product invariant is:

> **Center M Disabled means the Addon owns controller authority and the effective HidHide controller-isolation baseline. At every Disabled boot/recovery boundary, the Addon converges readable/mutable HidHide state to exactly the official HidHide CLI + Client + current Addon whitelist, the currently valid exact Addon-owned hidden target set, `Inverse=false`, and `Active=true`, verifies it, and only then exposes live controller input. Historical third-party HidHide state is neither preserved nor reconstructed. A mandatory Addon startup task is likewise created/repaired and verified automatically when required, rather than making normal users repair Task Scheduler by hand.**
