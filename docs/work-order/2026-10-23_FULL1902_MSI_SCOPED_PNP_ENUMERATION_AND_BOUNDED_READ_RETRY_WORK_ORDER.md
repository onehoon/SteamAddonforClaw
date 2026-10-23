# Work Order — Scope Full1902 MSI PnP Enumeration and Retry Transient Native-State Reads

**Date:** 2026-10-23  
**Repository:** `onehoon/SteamAddonforClaw`  
**Suggested delivery:** One focused code PR  
**Priority:** High — confirmed controller-unavailable production failure  
**Product:** Standalone Full 1902 MSI Claw Addon (CG3EM/EX and supported A2VM). No integration with other controller-management applications is in scope.  
**Implementer:** Local Codex. Automated/code checks are Codex's responsibility; physical-device verification is the user's separate post-merge role and is **not** a required implementation or merge gate.

## 1. Goal

Remove unnecessary *deep global* Windows PnP property enumeration from the **ordinary startup-admission and ownership-critical** MSI Claw path: hardware compatibility admission, topology readiness, native-state capture, mode/control HID resolution, exact DirectInput PnP identity lookup, and HidHide target-set resolution. Optional post-ownership settings features and one-shot global diagnostics are explicitly outside this initial optimization. Use a single lightweight present-instance discovery pass to select only the known MSI Claw controller VID/PIDs, then hydrate those nodes and the ancestors actually needed for physical identity. At the same time, let the existing bounded native-state settle retry a narrowly identifiable SetupAPI/PnP **read/enumeration failure**, instead of immediately abandoning ownership on the first transient exception.

Do **not** remove strong physical identity verification, duplicate/mixed-topology detection, required control-HID readiness, existing rollback/fail-close policy, or existing lifecycle behavior.

## 2. Confirmed incident and code cause

From Google Drive `Addon/Log/1023`, on the 2026-10-23 `0.1.414.0` runtime:

- **09:56:10–09:56:11:** three stable topology polls, `DeviceCount=321`.
- **09:56:12:** persistent HidHide baseline active and compliant (`Active=True`, three hidden target devices).
- **09:56:14.222:** `[ERROR] [NativeState] MSI Claw PnP enumeration failed.` at `WindowsControllerDeviceEnumerator.GetRegistryMultiString`: `InvalidOperationException: Unable to read PnP property 1.`
- **09:56:14.686:** initial physical ownership failed, `InitialNativeState:Failed:Unable to read PnP property 1.`, `ModeWriteIssued=False`, and `InitialAcquisitionRetryReason=None`.
- **09:56:14.721:** VIIPER presentation released; persistent HidHide remained active, leaving neither usable physical input nor a virtual gamepad.
- **09:58 restart:** the same runtime recovered normally, with one strong PID1902 physical root, DirectInput acquisition, HidHide verification, and Xbox 360 virtual attach.

`property 1` is `SPDRP_HARDWAREID`. The log does **not** record the failing PnP instance or `GetLastWin32Error()`, so it does not prove the failing device was the Claw or establish the exact Windows error.

Relevant current `main` call chain:

```text
ControllerTopologyWaiter.CreateRelevantTopologySnapshot
  -> IControllerDeviceEnumerator.EnumeratePresentDevices() [all-class deep scan]

MsiClawAddonPhysicalOwnership.AcquireCoreAsync
  -> MsiClawNativeStateManager.CaptureStableCurrentSnapshotAsync
     -> CaptureSnapshot()
        -> EnumeratePresentDevices() [all-class deep scan]
           -> SPDRP_HARDWAREID/COMPATIBLEIDS/etc. for every present node
              -> unrelated node can throw
        -> catch Exception -> NativeStateCaptureStatus.Failed
     -> Failed has no existing settle/retry branch
  -> initial ownership aborts
  -> host releases virtual presentation; no typed arrival deferral
```

This is a **real** lifecycle reliability bug, not a request to defend against speculative instruction-level interleavings.

## 3. Files and existing facilities to inspect/reuse

1. `src/SteamInputAddonforClaw/Controllers/Detection/WindowsControllerDeviceEnumerator.cs`
   - The no-argument `EnumeratePresentDevices()` uses `DIGCF_PRESENT | DIGCF_ALLCLASSES` and reads detailed properties **for each** present PnP device.
   - The existing `EnumeratePresentDevices(ushort vendorId, ushort productId)`, `ReadLightweightDevices`, `Hydrate`, `AncestorIds`, `GetRegistryProperty` already provide a useful narrow-enumeration foundation.
   - Its narrow implementation still walks *instance IDs* globally but hydrates only the target and ancestors; that is acceptable. Avoid promising zero global SetupAPI iteration.
2. `src/SteamInputAddonforClaw/Controllers/Detection/IControllerDeviceEnumerator.cs`
   - Preserve fake/test-enumerator usability; its existing default VID/PID implementation delegates to the broad interface.
3. `src/SteamInputAddonforClaw/Startup/ControllerTopologyWaiter.cs`
   - Both regular three-snapshot topology admission and `WaitForFirstUnambiguousControlHidAsync` use the deep global enumeration today.
4. `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeStateManager.cs`
   - `CaptureSnapshot` and `HasMixedNativeModeTopology` use broad enumeration.
   - `CaptureStableCurrentSnapshotAsync` already has a shared **5-second / 500-ms** bounded settle for permitted `DeviceNotFound` and mixed PID1901/1902 states. Reuse it.
5. `src/SteamInputAddonforClaw/Devices/Abstractions/INativeControllerStateManager.cs`
   - `NativeStateCaptureResult` currently has `Status/Snapshot/Reason`; consider the minimum explicit retryability signal if needed. Do not infer retryability by parsing English exception messages.
6. `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs` and `MsiClawGamepadModeClient.cs`
   - Source/command-HID resolution still performs broad enumerations in the normal initial mode-transition/readback path. Switch the **Claw-only** required lookups to the same narrowed topology to prevent an immediately subsequent unrelated-node read from defeating the initial fix. Preserve their existing per-operation verification and timing budgets.
7. `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeContracts.cs`, `MsiClawHardware.cs`, `MsiClawInternalControllerMatcher.cs`
   - Preserve known VID `0x0DB0`, PIDs `1901/1902/1903`, physical-root resolution, container/parent proof, control-HID Usage/Collection checks, and the exact MSI logical-group rules.
8. `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`, `Hosting/AddonProcessHost.cs`
   - Confirm the corrected observation reaches existing ownership without inventing a second retry owner, global watchdog, or unnecessary restart cycle.

9. `src/SteamInputAddonforClaw/Devices/WindowsDeviceProbeContextFactory.cs` and `src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs`
   - The initial hardware compatibility gate calls no-argument `EnumeratePresentDevices()` in `WindowsDeviceProbeContextFactory.Capture()`. A read exception makes hardware assessment `Indeterminate`.
   - **Important existing behavior:** `StartupCoordinator.EvaluateHardwareCompatibilityWithStabilizationAsync` already retries `Indeterminate` for **up to 5 seconds at 500-ms intervals**. Do not build a duplicate hardware-probe retry system; eliminate the unnecessary deep global enumeration and preserve this existing stabilization.
   - Current standalone production MSI adapter `Probe` needs only known MSI VID/PID presence; model identification uses the exact BaseBoard product. Do not drop model verification.
10. `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` and `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
   - In `CreatePhysicalOwnership`, `instanceId => controllerDevices.EnumeratePresentDevices().FirstOrDefault(...)` performs an unnecessary deep global scan despite the existing `FindPresentDevice(instanceId)` exact-instance path.
   - The `() => controllerDevices.EnumeratePresentDevices()` delegate feeds `MsiClawHardware.ResolveOwnedPid1902HidHideTargets(pnpDevice, _enumeratePnpDevices())` after DirectInput first-valid-state. An unrelated PnP property failure here stops ownership, which can still leave HidHide hiding physical input without virtual output.
   - Retain exact-primary and auxiliary control/consumer identity, Usage, same-physical-root validation when switching this delegate to the MSI-scoped snapshot. No global scan is needed to establish this target set.

**Architecture contracts:** `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` and `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`. Compare the earlier `docs/work-order/2026-10-10_A2VM_BOOT_RUMBLE_INTERMEDIATE_PID1901_SETTLE_SIMPLIFICATION_WORK_ORDER.md` for a *similar error signature*, not as permission to carry over its A2VM-only boot-prime assumptions.

## 4. Implementation requirements

### 4.1 One Claw-scoped, single-pass enumeration path

Extend/reuse the **existing** `IControllerDeviceEnumerator` / `WindowsControllerDeviceEnumerator` with the smallest appropriate API for **one VID and multiple known PIDs**, e.g.:

```csharp
// Illustrative signature, not a mandatory new abstraction:
IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(
    ushort vendorId,
    IReadOnlyCollection<ushort> productIds);
```

Its Windows implementation should:

1. Make **one** lightweight pass over present PnP instance IDs; select MSI `VID_0DB0` and `PID_1901/1902/1903` candidates.
2. Read detailed SetupAPI properties only for those selected candidates and the ancestry required by existing identity/Container/root matching.
3. Return all relevant present MSI child collections (gamepad, vendor control HID, auxiliary control/consumer HID), plus needed ancestor descriptors. Do not silently discard the supported vendor-defined control UsagePage nodes.
4. Preserve `MsiClawPhysicalIdentity.ResolvePhysicalRoot`, logical grouping, strong-identity checks, and the detection of **multiple physical roots** or **mixed-mode nodes**. Distinct physical devices must remain distinct; never pick the first matching candidate.
5. Make the interface default implementation compatible with existing fake enumerators/tests. Production Windows must use the genuinely scoped implementation; a default interface fallback to the old broad scan is not an acceptable production fix.
6. Keep the existing no-argument global scanner intact for **genuinely global diagnostic/inventory** consumers. This PR is not a wholesale rewrite of device discovery.
7. Avoid calling the existing single-PID overload **three times** per snapshot; that would repeatedly walk all present devices. Share its lightweight parsing/hydration logic for one pass instead.
8. Do not assume all ancestors themselves have MSI VID/PID. Preserve them as identity evidence **without automatically promoting an ancestry-only node to an MSI candidate**. However, an MSI `USB\\VID_0DB0&PID_190x` physical root can also be an independently discovered **real matching target** in the existing broad-scan semantics. Do **not** blanket-exclude matching roots from candidate grouping merely because they are ancestors of HID children. Preserve current grouping/selected-identity behavior by distinguishing discovery matches from ancestry-only additions, rather than by a rule that suppresses every ancestor.

**Cover the remaining ownership-critical callers rather than just native-state capture:**

9. `WindowsDeviceProbeContextFactory.Capture()`: build the same `DeviceProbeContext` from scoped MSI candidates (plus required identity context); preserve manufacturer/baseboard checks and existing hardware-probe stabilization.
10. `AddonProcessHost.CreatePhysicalOwnership()`: replace the exact-instance lookup delegate with the already available `FindPresentDevice(instanceId)` (or a demonstrably equivalent exact targeted lookup). Replace the `_enumeratePnpDevices` delegate with one scoped MSI snapshot sufficient for all three PID1902 HidHide collections and physical-root checks. Keep `EnumeratePresentDevices(vendorId, productId)` and `IsPresent` behavior intact.
11. `MsiClawAddonPhysicalOwnership`: verify the auxiliary target-set and subsequent ownership success/failure use the new delegate correctly; do not silently omit the primary or change HidHide readback/reconcile policy.

**Candidate invariant:** test the same present MSI physical root/child hierarchy against old broad-scan and new scoped-scan results. An *ancestry-only* node must not create a new logical controller, an extra product mode, or change the selected snapshot. A root independently matched by MSI VID/PID is legitimate evidence and may still be included as in existing behavior. Do not add persistent candidate-role state or a new identity authority solely to express this distinction.

Update `ControllerTopologyWaiter` (both readiness variants), `MsiClawNativeStateManager.CaptureSnapshot/HasMixedNativeModeTopology`, and the Claw-only mode source/firmware command-HID resolution noted above.

For the ordinary EX/CG3EM startup admission, retain **three matching controller-relevant snapshots** and the requirement for the correct PID1901 or PID1902 control HID. Preserve existing A2VM-specific first-unambiguous-control-HID behavior. Do not introduce an unrelated change to startup timing policy.

### 4.2 Bounded retry for actual PnP read/enumeration exceptions

A failure in native PnP enumeration or registry-property retrieval is not the same as a conclusively wrong physical identity. The startup sequence has **three existing bounded observation phases**. Preserve their distinct responsibilities and budgets rather than layering an additional retry framework:

| Observation phase | Existing limit/cadence | This PR |
|---|---|---|
| Hardware compatibility: `StartupCoordinator.EvaluateHardwareCompatibilityWithStabilizationAsync` | Up to 5 s / 500 ms for `Indeterminate` | Retain current retries; avoid global detailed PnP reads in `WindowsDeviceProbeContextFactory` |
| Topology readiness: `ControllerTopologyWaiter.WaitUntilStableAsync` / `WaitForFirstUnambiguousControlHidAsync` | Up to 5 s / 350 ms | Continue the **same** wait on specifically typed transient SetupAPI/PnP read/enumeration exceptions, instead of returning `Indeterminate` on the first such exception; reset the three-snapshot stable counter after a failed poll |
| Native state: `MsiClawNativeStateManager.CaptureStableCurrentSnapshotAsync` | Up to 5 s / 500 ms | Add strictly classified PnP read failures to the existing settle reasons; retain existing `DeviceNotFound`/mixed-mode policy |

Both topology readiness variants currently have an outer `catch(Exception)` that exits their polling loop immediately. Change only the *known native PnP read-fault* path to re-observe inside the original deadline, retaining the last failure for terminal diagnostics. The A2VM first-unambiguous-control-HID path must still accept the first truly verified unique endpoint; EX/CG3EM still requires three equal stable MSI-relevant observations. An unrelated programming exception must continue to fail, not be concealed as a transient PnP event.

At native-state capture, reuse `CaptureStableCurrentSnapshotAsync`'s current deadline and poll cadence:

- Allow re-observation of a **known PnP/SetupAPI enumeration or property-read exception** while the 5-second settle budget remains.
- Distinguish the retryable PnP-origin failure from unrelated application exceptions. A minimal explicit typed error/flag is acceptable; avoid string-prefix parsing, a generalized retry framework, a new background worker, or duplicate retry budgets.
- Let genuine `DeviceNotFound` and mixed PID1901/1902 states continue under their **existing** policy; do not convert all `Indeterminate` results into retryable successes.
- **Do not retry or weaken** strong-identity mismatch, multiple distinct logical controllers, unsupported PID/mode, ambiguous source/target control HID, failed native mode write, HidHide readback failure, or DirectInput/VIIPER ownership failure under this PnP-read policy.
- A successful fresh re-observation runs the **unchanged** ownership pipeline. If the bounded period ends, return `Failed` and remain fail-closed.
- Ensure cancellation exits promptly; do not swallow `OperationCanceledException`.
- Do **not** issue/reissue PID1901↔1902 writes, repeat a successful mode-write sequence, mutate HidHide, or attach VIIPER **because of a retry**. Retry only the still-passive *read/observation* operation before its existing mutation point.
- Preserve stock Enabled baseline/restoration and Disabled-mode behavior. The same bounded passive observation can be reused by current callers; do not require a new authority state.

One minimal coding approach (Codex may choose a simpler equivalent):

```csharp
// Preserve Failed as the failure status and carry a separate explicit hint.
// For example, an init-only property on the existing result record:
public bool RetryablePnPReadFailure { get; init; }

// In the existing stable-snapshot loop:
var readFault = current.Status == NativeStateCaptureStatus.Failed
                && current.RetryablePnPReadFailure;
if (!missingDevice && !mixedTopology && !readFault)
    return current;
// Existing 500 ms / 5 s settle applies, with no write or attach.
```

Only set that hint for actual OS PnP calls that threw; *not* for general `Exception` or final identity classification. Keep normal callers and existing tests compatible.

### 4.3 Actionable native error details and sensible log severity

`GetRegistryProperty` currently throws only `"Unable to read PnP property {property}."`, losing `Marshal.GetLastWin32Error()` and which devnode failed. Correct that at the **native-read error boundary**, with at least:

- operation / property name and numeric property ID (for example `SPDRP_HARDWAREID=1`);
- `Win32Error` numeric code (and `RequiredSize` where useful);
- failing PnP `InstanceId` when retrievable; otherwise `DevInst` and `InstanceIdUnavailable`. Do not let diagnostic extraction throw a second exception or hide the original one.
- Apply the same native-error preservation to `SetupDiGetClassDevsW`, `SetupDiEnumDeviceInfo`, and `SetupDiGetDeviceInstanceIdW` failures, not only `SetupDiGetDeviceRegistryPropertyW`. Record the exact operation and Win32 code. Preserve the original exception/operation metadata through callers so retry classification does not depend on English error text.
- An intentionally minimal typed native-enumeration/read failure is acceptable; do not classify **every** `InvalidOperationException` or arbitrary code exception as a transient OS error. A genuine read failure can be retried until the existing bounded deadline, even when its ultimate underlying cause is not proven transient; exhausted attempts must still fail closed.

Preserve current optional-property semantics (`ERROR_INVALID_DATA=13`, `ERROR_NOT_FOUND=1168`) rather than treating those as crashes. If the API reports a required buffer larger than the existing fixed 16-KiB buffer, handle the ordinary insufficient-buffer case appropriately rather than repeatedly retrying an identically undersized request. No generalized PnP property-reader redesign.

**Required logging policy:**

| Event | Level | Rationale |
|---|---|---|
| Per-attempt retryable PnP read failure while still within the existing bounded settle | `Debug` | Avoid stack-trace/Error spam for transient, self-recovered observations |
| Recovery after one or more failed reads | **one** `Info` summary | Include `Event`, `Attempts`, `ElapsedMs`, cause / error code if available |
| Exhausted/terminal PnP read failure preventing **Disabled-mode** Full1902 startup ownership/admission with persistent HidHide | **one** `Error` at the authoritative terminal failure boundary | User can be left without usable controller input; avoid duplicating the same Error at each intermediate layer |
| Ambiguous/wrong identity or other policy-rejected state | Preserve existing warning/fail-close diagnostics | Not the same as transient SetupAPI read failure |

A recovered exception must **not** log `Error` on each `CaptureSnapshot` attempt and then still log a successful result. Likewise do not hide a final controller-unavailable outcome as `Info`. **No new `Critical` logger or event severity is required.** Keep existing stable diagnostic event names unless an added distinct event is needed; preserve actionable one-line summary.

### 4.4 No new authority or lifecycle framework

- One Windows user / one interactive session. No RDP, multi-session, or Fast User Switching compatibility additions.
- Preserve the existing owner/gate, typed PnP arrival behavior where applicable, HidHide baseline, VIIPER attach/teardown and stock restoration.
- Real loss, PnP re-enumeration, Restart/Crash/Shutdown, Sleep/Hibernate/Resume, and PID1901↔1902 transitions remain supported.
- No new poller, watchdog, dedicated retry manager, epoch, lock, service, UI state, settings toggle, or broad automatic recovery loop.
- Do not remove the intentional **global diagnostic enumeration** just to reduce scan counts in logs.

## 5. Automated tests / verification (Codex responsibility)

Add or adjust focused unit tests, preferably within the existing tests for `MsiClawNativeStateManager`, `ControllerTopologyWaiter`, `MsiClawModeController`, and the enumerator helper logic.

Required coverage:

1. **Scoped selection:** one global lightweight ID pass retains all MSI PID1901/1902/1903 relevant children and needed ancestry; ignores unrelated devices for property hydration.
2. **Original reproduction:** an unrelated PnP node has a failing `SPDRP_HARDWAREID` read, while the MSI controller is healthy: the Claw-only snapshot remains successful, with correct physical identity, without touching that unrelated node's registry properties.
3. **True transient MSI read failure:** first targeted SetupAPI/property read fails, next succeeds: bounded settle returns `Success`, with no premature mode write, HidHide mutation, or VIIPER attach.
4. **Persistent targeted PnP failure:** retries stop by deadline and return `Failed`; no blind mode command, no early virtual attach; terminal diagnostics retain useful Win32 code/identity context.
5. **Non-PnP exception:** unrelated programming/application exception stays non-retryable. Cancellation propagates.
6. **Safety:** two distinct physical MSI roots, mixed mode, wrong/weak physical identity, missing/ambiguous control-HID usage/collections retain their existing outcomes; PnP retry must not turn them into an accepted controller.
7. **Admission:** CG3EM/EX three-snapshot stable readiness and A2VM-specific first-ready admission remain unchanged, including external controller connect/disconnect not affecting MSI-only stability.
8. **Logging:** recovered transient errors emit at most one `Info` recovery summary and no per-attempt `Error`; terminal failure produces one useful `Error`, not unbounded repeated stack traces.
9. **Consumer compatibility:** no regression for callers intentionally using global inventory or the existing single-PID and exact-instance lookup methods.
10. **Hardware compatibility gate:** a failing non-MSI node's hardware property does not prevent known MSI support/model detection. Confirm existing 5 s / 500 ms `Indeterminate` stabilization is preserved, not replaced by another retry system.
11. **Two topology admission variants:** a typed PnP read exception on the first attempt followed by healthy Claw topology can succeed within the existing 5-second wait. EX/CG3EM needs its normal three equal samples after the failed observation; A2VM needs its one verified unique control HID. Persistent error remains passive after deadline; unrelated application exceptions do not become success.
12. **Post-DirectInput HidHide target-set:** demonstrate a healthy exact primary/auxiliary PID1902 trio can be resolved without hydrating unrelated PnP nodes, and that exact-target lookup does not invoke global `EnumeratePresentDevices()`. When the scoped target-set is unresolved/unsafe, first-valid-input does **not** bypass HidHide fail-close.
13. **Candidate versus ancestor:** test an independently discovered MSI USB root that is also a child node's ancestor, and an ancestry-only extra node (including misleading VID/PID data in its hydrated properties). Preserve the old selected mode, logical grouping and strong identity; never turn an ancestry-only addition into a second logical candidate.
14. **Native failure-site diagnostics:** test property, device-enumeration, and Instance ID query failures with operation/error code preserved and typed retryability; do not require device hardware or a Windows PnP fault in CI.

Run the repository's normal build and relevant automated tests, and report commands/results in the implementation PR. **Do not make physical handheld testing a Codex deliverable or PR blocker.**

## 6. Acceptance criteria

- The 2026-10-23 `SPDRP_HARDWAREID` global-read incident no longer prevents Full1902 startup when the MSI controller's own relevant topology is healthy, including hardware admission, both topology waiter variants, NativeState/command lookup, exact DirectInput identity and post-acquire HidHide target-set resolution.
- A transient MSI-target property/read error self-recovers within the existing 5-second bound, without an app restart or any speculative controller write.
- A persistent or non-recoverable error fails closed with a clear terminal `Error` and actionable native details, not a misleading success.
- Read-only re-observation cannot accidentally consume ownership or attach virtual output.
- Existing PID1901↔1902 safety, HidHide persistent ownership, VIIPER teardown, power/restart/loss lifecycle and stock mode restoration remain intact.
- The implementation reuses current enumerator/settle/owner structure. Simplicity and production-reachable behavior are preferred over theoretical-race hardening.

## 7. Explicit non-goals

- Hardware/BIOS/driver blame or speculation based on the unknown original Win32 error.
- Changing the strict native-mode and GamepadMode hardware contracts.
- Refactoring unrelated diagnostic scanners, LED readers, vibration modules, updater inventory, or global environment discovery in this PR. The current scope is **all ownership-critical startup reads**, not every optional post-ownership device-property query. If a separate optional settings consumer uses a global scan, it does not justify expanding this reliability fix unless its failure actually blocks ownership.
- New physical-device validation procedures, UI controls, or persistent recovery flags.
- New controller-manager integrations.
