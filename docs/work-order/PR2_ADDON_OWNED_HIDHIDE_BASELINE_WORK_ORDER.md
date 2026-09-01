# Work Order — PR2: Addon-Owned HidHide Baseline Foundation

## Status

Implementation work order for the second small code PR in the **Full PID1902 Implementation** track.

This PR is **foundation only**.

It establishes the persistent, deterministic HidHide configuration primitive required by future Addon Controller Mode.

It does **not** yet activate controller authority, change MSI Center M, change PID, acquire DirectInput, attach VIIPER devices, enforce Runtime lifetime, or request a reboot.

Before implementation, read the current design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR1_PERSISTENT_DUAL_VIIPER_DEVICES_WORK_ORDER.md`
- current `main` HidHide implementation/tests.

The architecture has now clarified that:

```text
Center M Disabled
→ Addon Runtime is the mandatory controller authority
→ PID1902 is the desired physical state for the entire Disabled-mode lifetime
→ HidHide configuration persists across Runtime restart and Windows restart
→ Windows shutdown/restart does not intentionally restore PID1901
→ PID1901 is restored on explicit authority release such as Enable Center M
```

**PR2 must not implement any of that runtime/PID lifecycle.**

PR2 only provides the persistent HidHide primitive that those later PRs will call.

---

## 1. Goal

Introduce one small Addon-owned HidHide baseline component that can inspect, apply, verify, and clear the deterministic controller-isolation configuration required by future Addon Controller Mode.

Desired Disabled-mode baseline:

```text
HidHide installed/readable
Inverse whitelist = false
HidHide Active    = true
Addon executable  = whitelisted
Known exact Addon-owned PID1902 primary gamepad collection(s) = hidden
```

The configuration is persistent.

It must not behave like an old Steam-route lease that automatically rolls back when a routing session or process ends.

Conceptually:

```text
Apply Disabled baseline
→ configuration remains
→ later Windows/Runtime lifecycle does not automatically remove it

Clear baseline
→ future explicit authority-release flow can return to stock-compatible state
```

PR2 does not decide when either operation is invoked in production.

---

## 2. Architecture constraints

### One persistent baseline, not another authority layer

Do not add:

- `HidHideAuthorityManager`;
- multi-owner lease tracking;
- routing epochs/barriers;
- controller-authority state machines;
- generic policy engines;
- a new recovery journal for persistent configuration.

Prefer one focused type depending primarily on:

```text
IHidHideClient
Addon executable path
zero or more exact requested hidden-device entries
```

Possible API shape only:

```text
Inspect(...)
ApplyDisabledBaseline(...)
ClearAddonBaseline(...)
```

Use repository naming conventions if a simpler name fits better.

### Persistent state is not transient recovery state

Do not represent this baseline through `RoutingRecoverySessionId` or another old Steam-route ownership token.

```text
Persistent Addon Controller HidHide baseline
!=
Transient routing-session mutation
```

Do not broadly rewrite the startup recovery cleaner in PR2. Later startup/recovery PRs will migrate that policy.

---

## 3. Inputs

PR2 must not discover or switch PID1902.

The primitive accepts exact caller-provided targets.

Required inputs:

```text
Addon executable path
optional exact PID1902 hidden-device entries
```

Both are valid:

```text
knownTargets = []
```

and:

```text
knownTargets = [exactPrimaryPid1902GamepadCollection]
```

Zero targets are required for the future first Disable transition, where HidHide can be prepared before the exact PID1902 collection has been observed safely.

Do not fabricate:

- `VID_0DB0&PID_1902` wildcard hiding;
- every PID1902 child;
- every MSI HID device;
- PID1901 hiding;
- virtual VIIPER targets.

Exact target discovery belongs to a later PID1902/DirectInput PR.

---

## 4. Exclusive Addon Controller Mode and foreign configuration

The final product does not support runtime coexistence with HHC, ClawTweaks, or another controller middleware while Addon Controller Mode is active.

PR2 itself must not become a process scanner.

Its responsibility is only to determine whether HidHide configuration is safe to place into the deterministic Addon baseline.

Fail closed when current HidHide state cannot be safely understood or normalized, including as applicable:

- not installed;
- access denied;
- configuration unreadable;
- unresolved raw whitelist entries;
- unsupported inverse-whitelist state;
- unsupported foreign whitelist entries;
- unsupported foreign hidden-device entries.

Do not silently delete unknown foreign controller state simply because the driver allows it.

Use admission semantics:

```text
unsupported foreign state before Addon mode
→ Conflict / Unsafe
→ future caller must refuse the mode transition
```

Do not build a foreign-state preservation map or multi-owner merge policy.

---

## 5. Whitelist policy

Disabled-mode baseline must include the exact executable(s) that legitimately need to read the hidden physical MSI Claw controller.

For PR2, keep this narrow.

At minimum:

```text
current Addon Runtime executable path
```

Only add another Addon-owned executable if current architecture proves it directly requires physical PID1902 access.

Do not carry unrelated old routing whitelist entries forward solely for compatibility.

Unresolved raw whitelist entries must fail closed rather than being silently dropped and overwritten.

---

## 6. Inverse-whitelist policy

Desired baseline:

```text
IsInverseWhitelist = false
```

Before editing, verify the current HidHide control contract.

If current in-process HidHide API safely supports setting inverse mode:

- add only the smallest typed mutation primitive necessary;
- keep it in the existing HidHide client boundary;
- verify by readback;
- add focused client tests.

If the supported API cannot safely normalize inverse mode:

```text
inverse mode detected
→ fail closed
→ do not guess IOCTL behavior
```

Do not shell out to a second CLI merely to bypass the existing driver client unless the architecture is explicitly revised.

---

## 7. Active-state policy

Desired Disabled baseline:

```text
HidHide Active = true
```

`ApplyDisabledBaseline` must ensure and verify it.

Active=true is persistent controller configuration.

It must not be treated as a temporary lease that is reverted when the object or current Runtime is disposed.

PR2 does not wire cleanup to process shutdown.

---

## 8. Apply behavior

Keep ordering explicit and reviewable.

Recommended high-level flow:

```text
1. Inspect current HidHide configuration
2. Verify readable/admission-safe state
3. Require/normalize non-inverse mode
4. Establish exact Addon whitelist baseline
5. Establish supplied exact hidden-device target(s)
6. Set Active = true
7. Re-inspect
8. Verify complete desired baseline
9. Return success only after readback matches
```

Do not rely only on mutation method return values.

Readback verification is required.

Apply must be idempotent.

```text
already compliant
→ no destructive churn
→ Success / AlreadyCompliant
```

---

## 9. Clear behavior

Provide the narrow inverse primitive required by the future explicit stock-authority transition.

Conceptual cleanup target:

```text
Addon-owned PID1902 hidden target(s) removed
Addon controller whitelist entry removed when no longer required
HidHide Active returned to the supported clean Enabled-mode baseline
non-inverse state known/verified
```

Do not attempt to restore arbitrary historical foreign-controller configuration.

The target product contract is deterministic stock-compatible cleanup, not a generic snapshot/restore engine.

PR2 only provides/tests the primitive. It does not call it from Center M Enable, uninstall, or shutdown.

---

## 10. Result contract

Use a small explicit result model for expected conditions.

Useful conceptual outcomes:

```text
Success
AlreadyCompliant
Conflict / Unsafe
Unavailable
MutationFailed
VerificationFailed
```

Do not create a large failure taxonomy.

Expose enough stable reason information for:

- unit tests;
- logs;
- future transition UX;
- future Disabled-boot admission.

Unexpected exceptions should become fail-closed results and use existing logging conventions.

---

## 11. Existing code relationship

Current route-scoped components may remain temporarily:

- `MsiClawHidHideBaselineStage`;
- `MsiClawPhysicalIsolationStage`;
- old routing/recovery tests.

Do not spread a new `persistentMode` boolean through those old stages merely to preserve both architectures.

Preferred migration posture:

```text
old route-scoped implementation remains temporarily
new persistent baseline primitive exists independently
later ownership PRs use the new primitive
later cleanup removes obsolete route semantics
```

The project is unreleased, so no compatibility layer is required once old policy becomes dead.

---

## 12. In scope

Implement only:

- focused persistent Addon HidHide baseline primitive;
- inspect/compliance classification;
- persistent apply;
- deterministic clear primitive;
- exact Addon executable whitelist handling;
- zero or more exact caller-supplied PID1902 hidden targets;
- Active=true Disabled baseline;
- non-inverse baseline handling;
- minimal verified inverse-mode mutation support only if required/safe;
- readback verification;
- idempotence;
- bounded logging/reasons;
- focused unit tests;
- minimal `HidHideDriverClient` extension only when necessary.

Target roughly **100–400 LOC of production changes plus focused tests where practical**.

If the PR grows because it starts changing controller ownership or UI, split it.

---

## 13. Explicitly out of scope

### Runtime authority/lifetime

Do not implement:

- mandatory Addon Runtime startup;
- startup-task policy changes;
- intentional Runtime-exit blocking;
- process supervisor/watchdog;
- crash auto-restart;
- UI/frontend lifetime changes.

Those begin in PR3/later hardening.

### Physical controller

Do not implement:

- PID1901 → PID1902;
- PID1902 → PID1901;
- native mode mutation;
- PnP stabilization;
- DirectInput acquisition;
- exact PID1902 target discovery.

### Center M / reboot

Do not implement:

- Center M task/service mutation;
- Center M process kill/quiesce;
- Disable and Restart / Enable and Restart UX;
- Windows restart requests.

### VIIPER / Steam

Do not implement:

- X360 attach;
- SteamDeck attach;
- publisher start/stop;
- X360 ↔ Deck switching;
- RunningAppID/BPM policy changes.

### Recovery / other features

Do not implement:

- sleep/resume ownership recovery;
- PID drift reclaim;
- PnP re-arrival recovery;
- DirectInput fault recovery;
- Center M resurrection recovery;
- rumble;
- gyro;
- WING/OEM1/M1/M2 policy;
- broad old-routing cleanup.

---

## 14. Test requirements

At minimum cover:

### Inspection

- not installed → unavailable/fail closed;
- access/config unavailable → fail closed;
- unresolved raw whitelist → conflict;
- unsupported inverse mode → conflict/fail closed;
- compliant baseline → compliant;
- unsupported foreign whitelist/hidden state → conflict.

### Apply with zero targets

Verify:

```text
Addon whitelist present
Active = true
non-inverse state verified
no fabricated PID1902 target
```

### Apply with exact target

Verify:

```text
exact target added once
no wildcard/broad target
Addon whitelist present
Active = true
complete readback matches
```

### Idempotence

- apply twice;
- apply already-compliant state;
- exact target already present;
- clear twice.

### Failure/readback

Verify fail closed when:

- mutation reports failure;
- mutation appears successful but readback differs;
- post-mutation inspection becomes unavailable;
- unexpected conflicting state appears.

### Clear

Verify Addon-owned target/whitelist cleanup and the defined clean baseline by readback.

Do not add artificial timing/race tests unrelated to real HidHide behavior.

---

## 15. Acceptance criteria

PR2 is complete when:

1. One small persistent Addon HidHide baseline primitive exists.
2. It is independent from Steam routing sessions.
3. It does not use routing recovery-session identity as persistent authority.
4. It accepts zero or more exact hidden PID1902 targets from callers.
5. It never invents broad PID1902 hiding.
6. It ensures the required Addon whitelist entry.
7. It ensures HidHide Active=true for Disabled baseline.
8. It handles non-inverse mode only through a verified supported path or fails closed.
9. It performs readback verification before success.
10. It is idempotent.
11. Unsupported foreign state is rejected rather than turned into coexistence logic.
12. It does not touch Runtime lifetime, PID, DirectInput, Center M, reboot, VIIPER presentation, or Steam/BPM.
13. Old route-scoped stages are not broadly rewritten/deleted.
14. Focused tests cover compliant/conflict/failure/zero-target/exact-target/clear/idempotence cases.
15. Repository Debug/Release builds and tests are clean.

---

## 16. Validation

Run repository-standard validation, including at minimum:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Debug
dotnet test -c Release
git diff --check
```

No MSI Claw hardware validation is required to merge this **foundation-only** PR if it is not production-wired to mutate/hide the live controller.

Hardware validation begins when later PRs connect this baseline to actual Disabled-mode transition and PID1902 acquisition.

---

## 17. PR description requirements

State clearly:

- **PR2: Addon-Owned HidHide Baseline Foundation**;
- persistent HidHide primitive only;
- not production-wired to Center M Disable/Enable yet;
- no Runtime lifetime enforcement;
- no PID mutation;
- no DirectInput;
- no virtual controller attach;
- no reboot;
- no Steam/BPM behavior change;
- old route-scoped HidHide orchestration remains temporarily;
- build/test results.

Do not claim Full PID1902 ownership is implemented.

---

## 18. Updated small-PR sequence

```text
PR1  Persistent dual VIIPER devices                 [done]
  ↓
PR2  Persistent Addon-owned HidHide baseline        [this work order]
  ↓
PR3  Mandatory Runtime / startup contract
  ↓
PR4  Reboot-bound Center M authority transition
  ↓
PR5  Disabled-boot admission
  ↓
PR6  PID1902 + DirectInput ownership
  ↓
PR7  First presentation attach
  ↓
PR8  Runtime X360 ↔ SteamDeck switching
  ↓
PR9+ Owned-state recovery / crash keepalive / cleanup
```

The final PR2 invariant is:

> **The repository has one tested persistent HidHide baseline primitive ready for Addon Controller Mode, while controller authority and Runtime lifetime remain completely unchanged until later PRs.**
