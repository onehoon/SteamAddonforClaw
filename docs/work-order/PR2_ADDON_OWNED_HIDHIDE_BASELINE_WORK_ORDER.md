# Work Order — PR2: Addon-Owned HidHide Baseline Foundation

## Status

Implementation work order for the second small code PR in the **Full PID1902 Implementation** track.

This PR is **foundation only**.

It establishes a persistent, deterministic HidHide configuration primitive for the future **Addon Controller Mode** but does **not** yet connect that primitive to MSI Center M Enable/Disable, Windows reboot, PID1902 acquisition, DirectInput, VIIPER presentation, Steam/BPM policy, or runtime controller recovery.

Before implementation, read and treat the following as current design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR1_PERSISTENT_DUAL_VIIPER_DEVICES_WORK_ORDER.md`
- current `main` implementation of:
  - `HidHideContracts.cs`
  - `HidHideDriverClient.cs`
  - `MsiClawHidHideBaselineStage.cs`
  - `MsiClawPhysicalIsolationStage.cs`
  - `StartupHidHideRecoveryCleaner.cs`
  - existing HidHide tests

The project is pre-release. Existing Steam-route-scoped HidHide semantics are **not** a compatibility requirement if they conflict with the new controller-authority architecture.

However, this PR must remain deliberately small. Do not perform the broader routing cleanup here.

---

## 1. Goal

Introduce one narrow Addon-owned HidHide baseline component that can deterministically represent and apply the future persistent controller-isolation configuration used while MSI Center M is Disabled.

Conceptual desired Disabled-mode baseline:

```text
HidHide installed / readable
Inverse whitelist = false
HidHide Active    = true
Addon executable  = whitelisted
Known Addon-owned PID1902 primary gamepad collection(s) = hidden
```

The baseline must be **persistent configuration**, not a Steam-session lease.

Its lifetime is intended to become:

```text
Center M Disabled mode
    ↓
HidHide Addon baseline persists
    ↓
Addon exit / restart / Windows reboot do not automatically remove it
    ↓
Center M Enable or uninstall later removes/restores the Addon controller baseline
```

PR2 does not yet decide *when* this mode becomes active.

It only provides the deterministic primitive that later PRs will call.

---

## 2. Product architecture context

The target product contract is:

```text
Center M Enabled
    → MSI / stock controller authority
    → Addon controller stack passive

Center M Disabled
    → Addon controller authority
    → persistent PID1902 / DirectInput while Addon is running
    → HidHide controller isolation owned by Addon
    → one persistent VIIPER runtime
    → X360 or Steam Deck presentation selected independently from physical ownership
```

The important architectural rule for this PR is:

> **HidHide is part of the Addon controller configuration, not a temporary Steam-route resource.**

Do not design PR2 around `RoutingSessionId`, Steam session start/end, presentation state, or `ExternalNativeTakeover`.

---

## 3. Why a new narrow baseline primitive is needed

Current main contains HidHide logic built for the previous Steam-route model.

### `MsiClawHidHideBaselineStage`

It normalizes HidHide before a route by manipulating global HidHide configuration and then reports a non-restoring routing baseline.

### `MsiClawPhysicalIsolationStage`

It is route/recovery-session oriented:

- requires a current physical DirectInput identity;
- journals hidden-device additions;
- may temporarily enable HidHide Active;
- treats those mutations as session-owned;
- rolls them back when the routing session ends.

That lifecycle is intentionally different from the new Full PID1902 design.

Do **not** try to make PR2 work by adding another mode flag throughout those old stages.

Preferred direction:

```text
old route-scoped stages remain temporarily for old orchestration

new small persistent HidHide baseline primitive
    → future reboot-bound controller authority flow uses this
```

Later cleanup PRs may remove obsolete route-scoped code after the new controller owner is proven.

---

## 4. PR2 design principle: one deterministic Addon baseline

When this primitive is asked to apply the Addon-owned baseline, the result should be deterministic.

The component should answer three simple questions:

```text
1. Is HidHide safe/readable enough to use?
2. Does current configuration match the Addon-owned baseline?
3. Can we apply or clear that baseline and verify the result?
```

Do not introduce a generalized HidHide ownership framework.

Do not introduce:

- HidHide authority manager;
- multi-owner lease manager;
- epoch/barrier state;
- global controller-environment state machine;
- generic policy engine;
- persisted transaction journal for this foundation PR.

A small focused type is preferred.

Conceptual API shape only — exact names may follow repository conventions:

```csharp
InspectAddonBaseline(...)
ApplyAddonBaseline(...)
ClearAddonBaseline(...)
```

or equivalent.

The implementation should remain directly testable with `IHidHideClient` or a similarly small fakeable dependency.

---

## 5. Baseline inputs

PR2 must not discover or switch PID1902 itself.

The baseline primitive should accept the exact inputs it needs from callers.

At minimum:

```text
Addon executable path
Optional known exact PID1902 hidden-device entry / entries
```

The caller may provide zero known PID1902 targets.

This is required for the first Center M Disable flow, where HidHide can be prepared before reboot even if an exact PID1902 collection identity has not yet been safely established.

Valid foundation state:

```text
Inverse = false
Active = true
Addon whitelisted
Known PID1902 target count = 0
```

Later boot acquisition will resolve the exact PID1902 primary collection and call the same baseline primitive again with that exact target.

Do not invent a broad `VID_0DB0&PID_1902` wildcard target.

Do not hide an ambiguous physical device.

---

## 6. Exact hidden-device policy

When one or more exact PID1902 hidden-device entries are supplied, the Addon baseline should contain exactly the required Addon controller target(s) according to the design contract.

For current supported MSI Claw controller work, prefer the exact **primary DirectInput gamepad collection** identity already used by existing MSI Claw physical-isolation code.

Do not broaden the target to:

- every `PID_1902` child;
- every MSI HID device;
- every game controller on the machine;
- PID1901;
- virtual VIIPER controllers.

PR2 does not need to resolve the target itself.

Target resolution belongs to the later PID1902 / DirectInput boot-acquisition PR.

---

## 7. Addon Controller Mode is exclusive — but admission and ownership are separate

The product direction is that Addon Controller Mode does not support coexistence with HHC, ClawTweaks, or another controller manager.

However, PR2 should not become a generic process detector or controller-software scanner.

The HidHide primitive only needs to classify whether the current HidHide configuration is safe to take into the deterministic Addon baseline.

### Required fail-closed conditions

At minimum, do not blindly mutate when:

- HidHide is not installed;
- HidHide configuration cannot be read;
- access is denied;
- raw application whitelist entries cannot be resolved safely;
- inverse-whitelist state cannot be made known-safe;
- another unsupported/foreign HidHide configuration would make deterministic Addon ownership unsafe.

The higher-level known-controller-software admission gate for HHC/ClawTweaks may be implemented in a later transition PR.

PR2 should expose enough reason information for a future caller to refuse the mode transition cleanly.

---

## 8. Foreign HidHide configuration policy

For the Full PID1902 product mode, the Addon is the primary controller authority.

Do not build runtime coexistence behavior that tries to preserve arbitrary foreign controller middleware configuration while the Addon owns the MSI Claw.

At the same time, PR2 should not silently destroy unknown foreign HidHide state merely because it can.

Use a simple admission rule:

```text
Before Addon Controller Mode is committed:
    if current HidHide configuration contains unsupported foreign ownership/state
    → report Conflict / Unsafe
    → caller must not enter Addon Controller Mode
```

Once the future reboot-bound transition has admitted the machine into Addon Controller Mode, later calls may reconcile to the deterministic Addon baseline.

For PR2 tests, define clearly what counts as acceptable baseline input vs a foreign/conflicting state.

Examples of likely conflict evidence:

- foreign application whitelist entries not explicitly accepted by the Addon controller baseline;
- foreign hidden-device entries unrelated to the exact Addon controller target(s);
- unresolved raw whitelist entries;
- inverse-whitelist mode when PR2 cannot safely normalize it.

Do not add a multi-owner preservation map.

---

## 9. Whitelist policy

The future Addon-owned baseline must include the exact executable(s) that legitimately need to see the hidden physical PID1902 controller.

For PR2, keep the required whitelist intentionally narrow.

At minimum:

```text
current Addon executable path
```

If current architecture proves that another **Addon-owned** executable must directly access the hidden physical controller, include it only with a concrete current requirement.

Do not automatically carry forward unrelated trusted-official applications merely because the old routing baseline did so.

Do not preserve a foreign whitelist entry solely for compatibility with another controller manager.

Normalize/canonicalize paths using existing HidHide path conversion behavior and existing repository conventions.

Any unresolved raw whitelist entry must remain a fail-closed condition rather than being silently dropped and overwritten.

---

## 10. Inverse-whitelist policy

Desired Addon baseline:

```text
IsInverseWhitelist = false
```

Current `IHidHideClient` can inspect inverse mode but, at the time this work order was written, exposes mutations for:

- application whitelist;
- hidden-device list;
- Active state;

and does not expose a dedicated inverse-mode setter.

Before editing, verify current main and the existing HidHide driver control contract.

If the existing HidHide driver/API already supports safely setting inverse mode through the same control device:

- add the **smallest typed mutation primitive necessary**;
- perform readback verification;
- keep it inside the existing HidHide client boundary;
- add focused native-client tests.

If current supported API cannot safely normalize inverse mode in this PR:

```text
inverse mode detected
→ baseline admission fails closed
→ no destructive mutation
```

Do not guess IOCTL behavior.

Do not shell out to a second HidHide CLI just to bypass the existing driver client unless there is no supported in-process path and the design is explicitly revised.

---

## 11. Active-state policy

Desired Addon baseline:

```text
HidHide Active = true
```

`ApplyAddonBaseline` must ensure and verify Active=true.

This is persistent configuration.

Do not treat Active=true as a temporary lease that must be restored when the current method/runtime ends.

`ClearAddonBaseline` should establish the future clean stock/Enabled-mode baseline defined by this PR, but it must not be invoked automatically on ordinary Addon process disposal.

PR2 itself does not wire `ClearAddonBaseline` to shutdown, Enable, or uninstall.

---

## 12. Persistent semantics

This PR must explicitly break from the old route-session assumption.

Applying the baseline must **not** create semantics like:

```text
Apply
→ process exits
→ rollback automatically
```

Expected behavior:

```text
Apply Addon baseline
→ configuration remains in HidHide
→ caller/process can end
→ Windows can reboot
→ configuration is still the desired persistent baseline
```

Likewise, tests must not assert automatic rollback at `Dispose()`.

Do not reuse `RoutingRecoverySessionId` as the authority for this configuration.

Do not record the persistent baseline as a stale route mutation that startup cleanup should immediately remove.

---

## 13. Relationship with the existing startup recovery cleaner

Current startup recovery logic was designed to remove proven stale HidHide mutations from an old routing session.

PR2 must **not** broadly rewrite `StartupHidHideRecoveryCleaner` yet.

But the new persistent baseline must be architected so it is not inherently represented as an old transient route journal entry.

In other words:

```text
Persistent Addon Controller Mode baseline
!=
Transient routing-session recovery mutation
```

If a minimal test or comment is needed to prevent PR2 from accidentally writing persistent baseline mutations into the existing transient recovery journal, add it.

Do not perform the full startup-recovery semantic migration in this PR.

That belongs to later Disabled-boot/recovery work.

---

## 14. Apply operation ordering

Keep ordering explicit and easy to review.

A reasonable high-level apply sequence is:

```text
1. Inspect current HidHide configuration
2. Validate configuration is readable and admission-safe
3. Validate/normalize inverse mode to false, or fail closed if unsupported
4. Establish exact Addon whitelist baseline
5. Establish supplied exact hidden-device target baseline
6. Set Active = true
7. Re-inspect
8. Verify complete desired baseline
9. Return Success only after readback matches
```

Exact ordering may change if the current HidHide driver contract has a stronger safety requirement, but explain any deviation in code/tests.

Do not report success based only on mutation method return values.

Readback verification is required.

---

## 15. Clear operation ordering

PR2 should provide the narrow inverse primitive that the future Center M Enable transition can call.

Conceptual Enabled/stock cleanup target:

```text
Addon-owned hidden PID1902 target(s) removed
Addon controller whitelist entry removed if it exists only for controller ownership
HidHide Active returned to the defined clean baseline
Inverse mode in known-safe normal mode
```

Because PR2 is a foundation and no actual reboot-bound authority transition is wired yet, keep cleanup deterministic and testable.

Do not attempt to restore arbitrary pre-existing foreign controller-manager state.

The future product contract is not “restore whatever happened to be there.”

It is “leave Addon Controller Mode and return to the supported stock controller environment.”

If foreign/conflicting configuration appears during clear and prevents a safe deterministic result, fail closed and report the reason.

---

## 16. Result contract

Use a small explicit result model rather than exceptions leaking into callers for normal known failure classifications.

Conceptually useful outcomes:

```text
Success
AlreadyCompliant
Conflict / Unsafe
Unavailable
MutationFailed
VerificationFailed
```

Do not create a large error taxonomy.

Include a stable reason string/enum sufficient for:

- tests;
- logs;
- future UI error reporting;
- future reboot-transition admission decisions.

Unexpected exceptions from the HidHide client should be converted into fail-closed results and logged through existing diagnostics conventions.

---

## 17. Idempotence

The baseline operations must be idempotent.

Examples:

```text
Apply when already compliant
→ no destructive churn
→ Success / AlreadyCompliant

Apply same exact target twice
→ one target remains

Clear when already clear
→ Success / AlreadyCompliant
```

Do not remove/re-add entries merely to prove ownership if current state already matches the desired deterministic baseline.

This matters because future boot reconciliation may call the same primitive repeatedly.

---

## 18. No PID1902 discovery in PR2

PR2 must not add MSI Claw PnP scanning simply to populate hidden-device entries.

The API must work with:

```text
knownTargets = []
```

and:

```text
knownTargets = [exactPrimaryPid1902Collection]
```

Later PR5 will own:

```text
PID1901 → PID1902
PnP settle
DirectInput acquire
exact primary collection resolve
→ call HidHide baseline reconcile with exact target
```

Keep those responsibilities separate.

---

## 19. Logging

Use existing HidHide / controller diagnostics categories.

Useful bounded events include conceptually:

```text
AddonHidHideBaseline inspection completed
AddonHidHideBaseline already compliant
AddonHidHideBaseline apply started
AddonHidHideBaseline applied and verified
AddonHidHideBaseline conflict detected
AddonHidHideBaseline verification failed
AddonHidHideBaseline cleared and verified
```

Useful fields:

```text
Active
Inverse
WhitelistCount
HiddenTargetCount
RequestedTargetCount
Result
Reason
```

Do not log on a timer.

Do not add per-input/per-frame logging.

---

## 20. In scope

Implement only the following:

- a focused Addon-owned persistent HidHide baseline primitive;
- deterministic inspection of whether current HidHide state is compatible with the Addon baseline;
- persistent apply of the Addon baseline;
- deterministic clear/stock cleanup primitive for future Enable transition use;
- exact Addon executable whitelist handling;
- optional exact supplied PID1902 hidden target handling;
- HidHide Active=true handling for Addon baseline;
- normal/non-inverse baseline handling;
- minimal inverse-mode mutation support only if the existing verified HidHide control contract safely supports it; otherwise fail closed;
- readback verification after mutations;
- idempotent behavior;
- clear failure/result reasons;
- focused unit tests;
- minimal `HidHideDriverClient` extension/tests only where required by the baseline contract.

---

## 21. Explicitly out of scope

Do **not** implement any of the following in PR2:

### Controller physical ownership

- PID1901 → PID1902 switching;
- PID1902 → PID1901 switching;
- native MSI controller mode mutation;
- DirectInput acquire/read/publisher;
- physical device PnP stabilization;
- exact PID1902 target discovery.

### MSI Center M transition

- enabling/disabling Center M scheduled tasks;
- service StartType changes;
- Center M process kill/quiesce;
- Center M UI changes;
- `Disable and Restart` / `Enable and Restart` flow;
- Windows reboot requests.

### VIIPER / presentation

- Xbox360 attach;
- Steam Deck attach;
- publisher start/stop;
- X360 ↔ Deck switching;
- Steam game detection changes;
- BPM selection changes.

### Controller features

- rumble;
- gyro;
- WING/OEM1 mapping;
- M1/M2 policy;
- controller profiles.

### Runtime recovery

- sleep/resume recovery;
- PID1902 → PID1901 reclaim;
- physical device loss/re-arrival;
- DirectInput fault recovery;
- Center M resurrection recovery;
- full crash-journal migration.

### Legacy cleanup

- removing old routing coordinator/state machines;
- deleting `MsiClawPhysicalIsolationStage`;
- deleting `MsiClawHidHideBaselineStage`;
- rewriting all old HidHide tests;
- broad routing refactor.

Those are later PRs.

---

## 22. Expected code size / reviewability

Target roughly **100–400 LOC of production + focused test changes where practical**.

This is a guideline, not a hard numeric requirement, but if the PR starts growing because it is also modifying PID routing, Center M, reboot UX, Steam policy, or recovery orchestration, stop and split it.

The PR should be understandable as:

> “Introduce and prove the persistent Addon HidHide baseline primitive.”

Nothing more.

---

## 23. Suggested implementation location

Prefer keeping the new primitive under the existing HidHide/controller domain rather than routing policy.

Possible naming examples only:

```text
HidHide/AddonControllerHidHideBaseline.cs
HidHide/PersistentHidHideBaseline.cs
```

Do not create a new top-level `Managers` layer.

Do not make the type depend on Steam routing abstractions.

It should depend primarily on:

```text
IHidHideClient
Addon executable path / requested exact target set
```

plus only minimal diagnostics helpers.

---

## 24. Test requirements

Add focused deterministic tests.

At minimum cover:

### Inspection / admission

- HidHide not installed → Unavailable/fail closed;
- access/configuration unavailable → fail closed;
- unresolved raw whitelist entry → conflict/fail closed;
- inverse mode unsupported/unfixable → fail closed;
- compliant baseline → reported compliant;
- foreign whitelist entry → conflict according to defined Addon authority policy;
- foreign hidden-device entry → conflict according to defined Addon authority policy.

### Apply with no known PID1902 target

Input:

```text
Addon path known
requested hidden targets = []
```

Verify:

```text
Addon whitelist present
Active = true
Inverse = false / verified safe
no fabricated PID1902 target
complete readback success
```

### Apply with exact PID1902 target

Verify:

```text
exact target added once
no broad wildcard target
Addon whitelist present
Active = true
final inspection matches desired baseline
```

### Idempotence

- apply twice;
- apply when already compliant;
- clear twice;
- exact hidden target already present.

### Mutation failures

For each mutation used by the implementation, verify the operation fails closed when:

- mutation reports false;
- mutation applies but readback does not match;
- post-mutation inspection becomes unavailable;
- unexpected foreign state appears in verification.

Do not add timing/race tests for instruction-level interleavings that the product does not need to support.

### Clear baseline

Verify the future stock-cleanup primitive:

```text
Addon hidden targets removed
Addon controller whitelist removed as defined
Active matches defined clean baseline
readback verified
```

and fails closed when deterministic cleanup cannot be verified.

---

## 25. Existing tests / compatibility

Keep existing tests passing unless they encode behavior directly incompatible with the new foundation and the changed code path truly replaces that contract.

PR2 should not require a broad rewrite of route-scoped isolation tests because those old stages remain temporarily in the repository.

Do not add compatibility code solely to preserve unreleased architecture.

If a test fails because the new baseline is intentionally independent from routing sessions, update only the tests that directly exercise the changed/new primitive.

---

## 26. Failure policy

The foundation must fail closed.

If the desired baseline cannot be proven:

```text
Do not report compliant.
Do not let the future caller assume controller isolation is safe.
Return a clear failure/conflict reason.
```

A failed apply must never be interpreted by future callers as permission to attach a virtual controller.

PR2 itself does not attach anything, but its result contract must make this future safety boundary explicit.

Do not add automatic retries unless a current HidHide operation has an established, realistic transient failure that already requires them.

Do not add theoretical race-defense machinery.

---

## 27. Acceptance criteria

PR2 is complete only when all of the following are true:

1. A small production primitive exists for inspecting, applying, and clearing the persistent Addon HidHide baseline.
2. It is independent from Steam routing sessions.
3. It does not use `RoutingRecoverySessionId` as the persistent ownership authority.
4. It accepts zero or more exact hidden PID1902 target entries from its caller.
5. It never invents a broad PID1902 wildcard target.
6. It ensures the Addon executable whitelist entry required by the baseline.
7. It ensures HidHide Active=true for the Addon baseline.
8. It requires/normalizes non-inverse mode only through a verified supported HidHide control path; otherwise it fails closed.
9. It performs readback verification before returning success.
10. It is idempotent.
11. Unsupported foreign/conflicting HidHide state is rejected rather than turned into runtime coexistence logic.
12. It does not perform PID switching, DirectInput acquisition, Center M mutation, reboot, VIIPER attach, or Steam/BPM work.
13. Existing old routing stages are not broadly refactored/deleted in this PR.
14. Focused tests cover happy path, conflict, failure, verification, zero-target first-boot preparation, exact-target application, clear, and idempotence.
15. Full Debug/Release build and test suite remain clean.

---

## 28. Validation commands

Run the repository-standard validation plus focused HidHide tests.

At minimum:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Debug
dotnet test -c Release
```

Also run focused tests for:

- the new persistent baseline primitive;
- `HidHideDriverClient` if its mutation surface changes;
- any existing HidHide contract tests touched by this PR.

Run `git diff --check` before opening the PR.

No MSI Claw hardware validation is required to merge this **foundation-only** PR if the implementation performs no PID switch and does not hide a live physical controller through production wiring.

Hardware validation becomes required when a later PR wires this baseline into the reboot-bound authority transition / Disabled boot path.

---

## 29. PR description requirements

The PR description should state clearly:

- this is **PR2: Addon-Owned HidHide Baseline Foundation**;
- it implements the persistent HidHide configuration primitive only;
- the primitive is not yet production-wired to Center M Disable/Enable;
- no PID1901/PID1902 mutation occurs;
- no DirectInput acquisition occurs;
- no virtual controller is attached;
- no reboot is requested;
- Steam/BPM behavior is unchanged;
- old route-scoped HidHide orchestration remains temporarily untouched;
- full test/build results.

Do not claim that Full PID1902 controller ownership is implemented after this PR.

---

## 30. Final implementation rule

Keep this PR conceptually small:

```text
PR1
Persistent dual VIIPER devices
    ↓
PR2
Persistent Addon-owned HidHide baseline
    ↓
PR3
Reboot-bound Center M authority transition
    ↓
PR4+
Disabled boot admission / PID1902 / DirectInput / presentation
```

The final PR2 invariant is:

> **The repository has one tested, persistent, deterministic HidHide baseline primitive ready for future Addon Controller Mode, but PR2 itself does not yet change controller authority or expose a virtual controller.**
