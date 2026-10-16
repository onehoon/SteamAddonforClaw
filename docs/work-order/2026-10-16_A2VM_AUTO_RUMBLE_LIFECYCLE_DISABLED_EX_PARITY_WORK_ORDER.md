# Work Order — Disable Automatic A2VM Rumble Mode Cycling; Use the EX/CG3EM Startup and Session-End Routes

**Project:** SteamAddonforClaw — standalone Full1902  
**Date:** 2026-10-16  
**Implementation owner:** Local Codex  
**Delivery:** One focused PR, production policy disabled by default  
**Scope:** A2VM 7/8 automatic startup rumble cycle and Windows session-end PID1901 preparation entry points only  
**Out of scope:** Removing the dormant implementation, rewriting recovery, changing VIIPER, or altering manual Restore Vibration

## 1. Decision and objective

The owner has chosen **the same normal startup and shutdown routing policy as EX/CG3EM for A2VM 7/8**. Automatic vibration priming is not worth a possible loss of the entire controller during startup.

**Preserve the existing A2VM automatic re-arm and session-end implementation in the repository, but make the production entry gates inactive.** Keep the user-triggered **Controller → Vibration → Restore** behavior fully functional.

This is **not** a request to remove all PID1901 transitions. With Center M Disabled, an actual initial PID1901 controller must still transition to PID1902 through the **ordinary strict EX/CG3EM ownership path**. Only the *optional* PID1902 → PID1901 → PID1902 boot-rumble round trip and the separate A2VM Windows session-end PID1901 preparation are disabled.

Do not introduce a new Settings toggle, runtime feature switch, service, manager, persistent state, reconciliation loop, generalized recovery framework, or user-facing option.

### Target behavior

| Situation | A2VM 7/8 after this PR | EX/CG3EM |
| --- | --- | --- |
| Center M Disabled; startup already PID1902 | Keep PID1902; normal mode/readback, DirectInput, HidHide, VIIPER | Same |
| Center M Disabled; startup PID1901 | Ordinary strict PID1901 → PID1902 transition, then normal proof/attach | Same |
| Runtime restart / update restart | No optional PID round trip | Same |
| Actual Windows session end | No A2VM-only best-effort PID1901 command; retain normal OS/process cleanup | Same |
| Sleep/Hibernate/Resume and device loss | Existing ordinary recovery; no optional rumble cycle | Same |
| Controller → Vibration → Restore | Explicit manual two-leg re-arm remains available when the healthy-owned-session admission passes | Preserve existing model-specific function |

The user accepts that physical vibration might not be primed automatically on every Windows boot and will invoke **Restore** from the Controller menu as needed. Do **not** redefine that button as a general recovery function for a controller that has never been acquired: the existing manual operation requires a healthy, strongly owned PID1902 input session and appropriate presentation/lifecycle admission.

## 2. Evidence and reason for the policy change

The October 16 AV2M logs show a real startup availability regression on A2VM 8:

- **16:35:30**: an optional boot-rumble cycle issued the PID1902 → PID1901 command.
- **16:35:33**: the second command requested PID1902 again.
- **16:35:37.585**: DirectInput Acquire succeeded, but the first reports remained the known invalid initial state.
- **16:35:37.664**: `MsiClawInputSource` stopped after exceeding `MaximumKnownInvalidInitialStates = 16`, with `InitialStateNotReady`.
- The A2VM fast owner returned `FirstValidStateNotObserved`, `InitialAcquisitionRetryReason=None`; the normal host's initial deferred acquisition did not start.
- **17:19:20**: a plain Runtime restart (without re-running the boot cycle) acquired PID1902, proved the first valid DirectInput state, and attached the virtual Xbox360.

This demonstrates a realistic risk introduced by the optional mode round trip. It **does not prove** that all known initial-invalid-state failures are impossible on the ordinary EX route; preserve all existing real input validation and fail-close requirements.

The prior PR #806 Xbox360 idle native-state deduplication occurs **after** physical ownership/presentation setup. Do not make changes to that publisher as part of this task.

## 3. Required source and design review

Before editing, review the checked-out **latest main** (do not implement from stale excerpts alone):

1. `docs/Full 1902 Implementation/README.md` — active documentation precedence and the currently documented A2VM boot exception.
2. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` — persistent Full1902 authority.
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` — §§9, 11, 12, 17, 18, 24.
4. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
5. `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`.
6. `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`.
7. `src/SteamInputAddonforClaw/Lifecycle/NativeTrayHostWindow.cs`.
8. `src/SteamInputAddonforClaw/Prerequisites/BootSession.cs`.
9. `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs` and `MsiClawAddonPresentation.cs` to verify the separate manual Restore route.
10. `tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs`.
11. `tests/SteamInputAddonforClaw.Tests/AddonProcessHostWindowsSessionEndTests.cs`.
12. `tests/SteamInputAddonforClaw.Tests/BootSessionTests.cs`.
13. The relevant historical work orders under `docs/work-order`, especially `2026-10-10_A2VM_FAMILY_BOOT_RUMBLE_REARM_LED_VIBRATION_AND_RECOVERY_UI_WORK_ORDER.md` and `2026-10-11_A2VM_INITIAL_CONTROLLER_FAST_PATH_HHC_CTW_SIMPLIFICATION_WORK_ORDER.md`.

**Precedence:** This new work order supersedes earlier instructions **to automatically invoke** the A2VM 7/8 startup rumble cycle and session-end PID1901 command. It does **not** rescind persistent PID1902/HidHide authority, mandatory first-valid-input proof, safe cleanup, manual Restore, or normal controller recovery. Historical orders should not be deleted.

## 4. Minimal implementation

### A. One production policy; no user setting

Prefer a single small **internal, compile-time production policy** in the existing Runtime host, defaulting to `false`. For example:

```csharp
// AddonProcessHost.cs
private const bool EnableA2vmAutomaticRumbleLifecycle = false;
```

This policy should control **both** optional entry points:

1. A2VM startup-only rumble fast path, via the existing owner's construction/admission.
2. A2VM-only `WM_ENDSESSION(TRUE)` preparation callback.

The exact identifier can follow local conventions. Keep this policy internal to production; it is **not** a persisted setting or a new generalized toggle architecture. Avoid separate independent booleans for boot and shutdown, because the product decision disables the whole optional automatic lifecycle together.

### B. Normal startup follows the EX/CG3EM path

In `MsiClawAddonPhysicalOwnership.AcquireCoreAsync`, gate **both** A2VM-specific startup branches, not just `bootRumbleCycleEnabled`:

1. Do not claim or consult the A2VM boot marker in normal production startup.
2. When initial mode is PID1902, do **not** enter the `bootRumbleCycleEnabled` two-write round trip; fall through to the ordinary PID1902 proof and DirectInput/HidHide path.
3. When initial mode is PID1901, do **not** enter the `a2vmInitialFastPath && initialMode == XInput` single fast-write shortcut; fall through to the **ordinary strict** `_switchMode(DirectInput, initialIdentity, ...)` path, just like EX/CG3EM.
4. Retain the complete existing A2VM-only fast implementation, `AcquireA2vmInitialFastFinalOwnershipAsync`, marker implementation, and supporting helpers for potential future reuse. No renaming or deletion of dormant methods is necessary.
5. Preserve identity matching, `GamepadMode` checks, first-valid-state validation, HidHide readback, physical isolation, and fail-close semantics from the ordinary path.
6. Do not broaden retry conditions, change `MaximumKnownInvalidInitialStates`, or add startup watchdog machinery in this PR.

**Suggested small owner-side seam** (adapt to exact current signatures):

```csharp
// Owner constructor: one internal parameter makes the retained dormant
// implementation explicitly testable without turning it on in production.
bool enableA2vmAutomaticRumbleLifecycle = false

// Store in a readonly field, then in AcquireCoreAsync:
if (_enableA2vmAutomaticRumbleLifecycle
    && IsA2vmBootRumblePrimeModel(_hardwareDeviceModel))
{
    // Preserve the existing boot-marker claim and diagnostic block unchanged.
}

var a2vmInitialFastPath =
    _enableA2vmAutomaticRumbleLifecycle
    && IsA2vmBootRumblePrimeModel(_hardwareDeviceModel);

// Preserve the existing branches below this decision intact.
// With false, PID1902 and PID1901 both take the ordinary EX route.
```

Wire the **single host policy** into the owner constructor in `AddonProcessHost.CreatePhysicalOwnership`:

```csharp
enableA2vmAutomaticRumbleLifecycle: EnableA2vmAutomaticRumbleLifecycle
```

An optional internal constructor parameter is justified **only** to retain exercise of dormant code in existing unit tests. Do not create a new interface, owner, provider, manager, or mutable runtime state for it. If a simpler equivalent directly preserves the old tests and production invariants, use that.

**Important:** Do not modify `IsA2vmBootRumblePrimeModel` to return false for supported models. It is model identity/capability classification, not the product policy. Keep `SupportsManualRumbleRearm` and the manual recovery path untouched.

### C. No A2VM-only Windows session-end PID1901 preparation

In `AddonProcessHost.TryInitializeTray`, stop wiring the optional A2VM preparation callback for the default production policy, while retaining the existing `HandleWindowsSessionEnd`, `PrepareForWindowsSessionEndAsync`, and downstream typed cleanup/write helpers as dormant implementation.

A minimal routing seam is:

```csharp
_trayHostWindow = new NativeTrayHostWindow(
    EnableA2vmAutomaticRumbleLifecycle ? HandleWindowsSessionEnd : null);
```

`NativeTrayHostWindow` already accepts an optional `Action?` callback. When null, its Win32 message procedure naturally falls back to `DefWindowProcW`; do not replace or rewrite the existing window procedure.

Do not execute the A2VM-only session-end preparation and its 2-second bounded special path in default production. Do not replace it with another PID1901 write or with an unconditional `VIIPER.Close` operation.

**Keep the ordinary Runtime/OS termination and restart behavior intact**, including existing normal teardown where invoked, canonical VIIPER ownership/fail-close, PID1902 restoration policy, persistent HidHide baseline, and no accidental stock authority release. A normal Addon exit or restart must never invoke the special session-end method.

Do not change the retained dormant session-end function's safety ordering, deadlines, or typed failure rules merely because its default entry point is disabled.

### D. Manual Restore remains independent

Do not gate or change:

- `ControllerPage.RestoreVibrationButton_Click`.
- `Frontend.RunDeveloperRumbleRearmAsync` RPC and typed result.
- `AddonProcessHost.RunDeveloperRumbleRearmAsync`, `GetDeveloperRumbleRearmAdmissionFailure`, and its coordination with presentation retirement.
- `MsiClawAddonPhysicalOwnership.RunDeveloperRumbleRearmAsync` and actual verified manual PID1902 → PID1901 → PID1902 transition.
- Supported model policy for A2VM 7/8 and CG3EM, LED and vibration-strength settings, physical rumble feedback, or motor tests.

The automatic lifecycle policy must have **no effect** on explicit, user-requested Restore.

## 5. Tests and regression checks

Keep the existing dormant A2VM fast-path and session-end implementation testable. Do **not** delete broad test suites just to make the new default compile.

### New default-policy tests

1. **A2VM 7 and 8, initial PID1902, automatic policy OFF:** no boot-marker call, no fast mode writes, normal GamepadMode/DirectInput/HidHide proof, successful owned result.
2. **A2VM 7 and 8, initial PID1901, automatic policy OFF:** no marker/fast-write shortcut; exactly one ordinary strict PID1901 → PID1902 mode transition with required proof, then physical ownership/attach eligibility.
3. **EX/CG3EM:** retain current behavior; no new A2VM path.
4. **A2VM manual Restore:** remains admitted for healthy owner/presentation and follows the existing verified manual two-leg sequence; nonhealthy/ambiguous owner remains unavailable.
5. **Windows session end:** verify production `NativeTrayHostWindow` receives no optional A2VM preparation callback, whereas the dormant callback/helper ordering remains available in source tests; normal tray icon functionality remains.
6. **Runtime restart, Resume, physical PnP change:** no automatic rumble mode cycling is introduced by any of these lifecycle events. Do not require new timing states or hardware tests.

### Existing dormant-path tests

- The `MsiClawAddonPhysicalOwnershipTests` harness may opt into `enableA2vmAutomaticRumbleLifecycle: true` **internally** for the existing legacy fast-path coverage. This keeps the previously implemented feature checked even while production defaults OFF.
- Update any tests that accidentally assumed the optional feature is **always** enabled in production; preserve targeted behavior tests with explicit opt-in.
- Existing `AddonProcessHostWindowsSessionEndTests` contains a source-level assertion that the callback is unconditionally registered. Update this assertion to verify the new policy-controlled wiring; preserve tests covering the dormant session-end sequence and safeguards.
- `BootSessionTests` should remain: marker code is deliberately preserved even though normal production does not invoke it.

Run relevant automated unit tests and the existing build/test CI. A test that validates an unreachable special path remains useful for future reactivation, but must **not** cause that path to run in the production default.

**No real-hardware verification is a local Codex task or PR merge blocker.** Physical testing, including vibration behavior, is the user's post-merge role.

## 6. Documentation

Update **current authoritative** documentation to reflect the new production default. In particular:

- `docs/Full 1902 Implementation/README.md` — its “Narrow A2VM boot rumble exception” currently describes an active automatic cycle. Make it explicit that the implementation is retained but **disabled in production**.
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` — update startup and Windows session-end sections and any summary statements asserting the special path is active.
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` and any user-facing README or guide which asserts automatic startup/session-end rumble behavior, if applicable.

Leave historical `docs/work-order` records intact. They describe previously implemented features and should not be rewritten as if never built.

Document that startup PID1901 → PID1902 remains mandatory when Center M is Disabled and that user-invoked Restore remains the only permitted intentional A2VM rumble-specific cycle under the new default.

## 7. Exclusions and anti-overengineering rules

- **Do not delete** the A2VM fast-start implementation, deferred-result support specific to that implementation, session-end method bodies, boot marker implementation, or the tests that preserve their contracts.
- Do not remove or weaken real lifecycle recovery (Sleep/Hibernate/Resume, PnP, PID1901 drift reclaim, startup failure fail-close, shutdown, VIIPER/HidHide ownership).
- Do not change controller cadence (125/250 Hz), PR #806 native state deduplication, Xbox360 publisher, or VIIPER.
- Do not add generalized `ControllerRecoveryManager`, special observer, per-session authority, extra locks, epochs, or retry state.
- Do not conflate the dormant A2VM session-end rumble preparation with **normal teardown**; only the special callback is disabled.
- Do not remove or alter the Controller-page vibration configuration, LED, or manual recovery UI.
- Do not require unsupported multi-user/RDP behavior or theoretical instruction-level race protection.
- Keep production changes narrow; preserving legacy unit-test reachability via one small internal constructor seam is acceptable.

## 8. Definition of done

- [ ] Current production on A2VM 7/8 follows the same ordinary Full1902 startup route as EX/CG3EM, for both initial PID1902 and initial PID1901.
- [ ] No optional PID1902 → PID1901 → PID1902 boot-prime occurs in default production, and no boot marker is claimed merely for that disabled feature.
- [ ] A2VM Windows-session-end PID1901 preparation callback is not registered in default production.
- [ ] Existing automatic A2VM feature implementations remain in source and covered by explicit opt-in tests.
- [ ] Manual **Controller → Vibration → Restore** remains unchanged and accessible when the existing health/safety admission allows it.
- [ ] No normal physical ownership, HidHide, VIIPER, presentation, stop/cleanup, or real recovery safety contract regresses.
- [ ] Authoritative documentation reflects the new **disabled-by-default** product policy; historical work orders remain.
- [ ] Relevant automated tests and CI pass; no actual hardware test is required before merge.

**PR shape:** One PR. Commit separation between production routing, tests, and documentation is optional. Keep the scope focused and favor the simplest code that expresses one production default and preserves existing lifecycle safety.
