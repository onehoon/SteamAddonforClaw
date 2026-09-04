# Work Order — Full1902 Cleanup J: Retire Legacy Vibration Feedback Transport and Historical Routing Naming Tail

## Status

Focused final legacy-tail cleanup after PR #484 / Cleanup I.

Code-review baseline used for this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     c4c01ef8c9ad96e83fdaf708f9084c840a79d26a
latest merged production PR: #484 — Full1902 Cleanup I
```

Read these first and use the authority order in the Full1902 README:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/FULL1902_CLEANUP_I_DISCONNECT_DEVELOPER_SYNTHETIC_STEAM_PRODUCTION_WIRING_WORK_ORDER.md`

This cleanup follows the project rule:

> Remove production legacy architecture that no longer has a real owner. Preserve useful low-level hardware primitives and Developer UI shells when they are expected to be redesigned/reconnected later.

---

# 1. Goal

Cleanup J closes two small but still visible routing-era tails:

1. the old vibration-feedback transport / authority / session RPC stack that no longer has a production Full1902 owner;
2. the historical `RoutingTransition` termination-block reason whose current meaning is actually a controller-authority startup transition.

The target is **not** to implement Full1902 rumble in this PR.

The target end state is:

```text
Full1902 production controller runtime
    -> no FeedbackAuthority
    -> no SteamDeckRumbleFeedbackBridge
    -> no vibration-test RPC/session writer
    -> no routing-era vibration ownership semantics

Developer Vibration Test page
    -> remains in the UI
    -> remains visibly unavailable/disabled
    -> no Runtime RPC/session side effects while unavailable

Future rumble primitives
    -> MSI physical rumble transport/packet builder retained
    -> pure Steam Deck feedback decoder retained
    -> future Full1902 integration may reconnect them through the current presentation owner

User termination safety
    -> same gate / same behavior
    -> `RoutingTransition` renamed to current controller-authority terminology only
```

Do not add a replacement feedback manager, authority service, lease system, synthetic session, or new state machine.

---

# 2. Why this cleanup is justified now

Cleanup A removed the routing Runtime that used to own the developer vibration transport. Cleanup I removed the remaining Developer synthetic Steam session from production.

On current `main`, the Vibration Test page already says:

```text
The developer vibration test is unavailable in this build.
```

and all three action buttons are disabled.

However, the page still performs production frontend work:

```text
Activate()
-> subscribe StateInvalidated
-> OpenVibrationTestSessionAsync()
-> GetBootstrapAsync()

DeactivateAsync()
-> CloseVibrationTestSessionAsync()
```

The Runtime/frontend still owns:

```text
_vibrationSessionGate
_vibrationSession
VibrationTestSessionWriter
RunVibrationTestAsync
OpenVibrationTestSessionAsync
CloseVibrationTestSessionAsync
MapVibrationTestOutcome
vibration session shutdown cleanup
```

and the named-pipe protocol still exposes:

```text
RunVibrationTest
OpenVibrationTestSession
CloseVibrationTestSession
RunVibrationTestRequest
FrontendVibrationTestCommand
FrontendVibrationTestResult
```

This has no production controller behavior anymore. It is only legacy diagnostic plumbing around a disabled UI.

Separately, `FeedbackAuthority` and `SteamDeckRumbleFeedbackBridge` remain in the production assembly but have no production construction site. Current references are their own implementation and tests.

The handoff explicitly identifies these as cleanup targets when no real production owner remains.

---

# 3. Product decision: keep the Vibration Test UI shell

Do **not** delete the Vibration Test page from the Developer UI in Cleanup J.

Preserve:

```text
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs
MainWindow navigation to VibrationTestPage
Developer menu entry that opens the page
```

The page remains a placeholder for a future Full1902 rumble implementation.

After this cleanup it should remain visibly unavailable:

```text
Rumble button = disabled
Haptic button = disabled
Stop button   = disabled
Status        = "The developer vibration test is unavailable in this build."
```

But because the feature is unavailable, entering/leaving the page must no longer open/close a Runtime vibration session, subscribe to unrelated Runtime invalidations, or send vibration RPCs.

Preferred minimal shape:

```csharp
internal void Activate()
{
    RumbleButton.IsEnabled = false;
    HapticButton.IsEnabled = false;
    StopButton.IsEnabled = false;
    StatusText.Text = "The developer vibration test is unavailable in this build.";
}

internal void Deactivate()
{
}
```

The exact method shape may follow existing MainWindow expectations. Do not redesign navigation.

If `DeactivateAsync()` must remain because MainWindow currently awaits it, it may be reduced to a completed task rather than preserving a dead RPC solely for that call shape.

Do not create a fake local vibration session to replace the removed Runtime session.

---

# 4. Remove the vibration-test frontend contract

The frontend vibration transport is a real wire-contract surface and is no longer useful while the page is intentionally disconnected.

Remove the complete reference closure for:

```text
FrontendVibrationTestCommand
FrontendVibrationTestResult
IAddonFrontendControl.RunVibrationTestAsync(...)
IAddonFrontendControl.OpenVibrationTestSessionAsync(...)
IAddonFrontendControl.CloseVibrationTestSessionAsync(...)
```

From:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
```

Do not leave default-interface no-op implementations merely for compatibility. The app is pre-release and the feature is intentionally unavailable.

Future Full1902 rumble work should introduce the smallest contract appropriate to the new design rather than inheriting this routing-era contract accidentally.

---

# 5. Remove the named-pipe vibration RPCs

Update:

```text
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
```

Remove:

```text
FrontendRpcMethod.RunVibrationTest
FrontendRpcMethod.OpenVibrationTestSession
FrontendRpcMethod.CloseVibrationTestSession
RunVibrationTestRequest
client methods for the three RPCs
server dispatch for the three RPCs
any per-connection/session cleanup that exists only for vibration-session lifecycle
```

Fresh-reference-close the server before editing. If connection teardown tracks an open vibration session or calls `CloseVibrationTestSessionAsync`, remove that branch as part of the same closure.

Do not touch unrelated Developer RPCs:

```text
SetDeveloperTestMode
GenerateEnvironmentReport
Claw sensor probe
Fan probe
other current Developer tooling
```

---

# 6. Frontend protocol bump is required

This cleanup removes three named RPC methods and their payload/result contracts.

Therefore bump:

```text
FrontendTransportProtocol.CurrentVersion
22 -> 23
```

Add a concise version-history comment to `FrontendWire.cs`, for example:

```text
Version 23: Full1902 Cleanup J removes the disconnected legacy Vibration Test RPC/session contract
(RunVibrationTest, OpenVibrationTestSession, CloseVibrationTestSession and their DTOs).
A v22 peer could otherwise connect and attempt methods the Runtime no longer implements, so fail the
handshake up front.
```

Update only tests/fixtures that intentionally assert the current frontend protocol version.

Do not change:

```text
OverlayTransportProtocol
QAM policy beyond its use of the shared Frontend protocol
```

---

# 7. Remove Runtime/frontend vibration-session plumbing

File:

```text
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

Remove the complete vibration-session-only reference closure, expected to include:

```text
_vibrationSessionGate
_vibrationSession
RunVibrationTestAsync(...)
OpenVibrationTestSessionAsync(...)
CloseVibrationTestSessionAsync(...)
GetOrOpenVibrationSession(...)
WriteVibrationSessionIfCurrent(...)
VibrationTestOpcode(...)
MapVibrationTestOutcome(...)
shutdown-time vibration-session flush/close code
comments describing routing-era delayed STOP / production vibration transport
```

Fresh-grep every helper before deleting it.

Do not disturb unrelated diagnostic sessions:

```text
ClawSensorProbeSession
FanProbeSession
Environment Discovery
DeveloperTestModeState
```

Do not make `InProcessAddonFrontendControl` responsible for future rumble ownership in this PR.

---

# 8. Delete the legacy feedback authority

Delete:

```text
src/SteamInputAddonforClaw/Feedback/FeedbackAuthority.cs
```

This removes:

```text
FeedbackAuthority
FeedbackAuthorityToken
FeedbackAuthorityLease
Acquire / Revoke / RevokeAndDrain
lease draining / generation arbitration
```

Current Full1902 has one controller/presentation owner. There is no production feedback owner selecting among multiple routing/session feedback sources.

Do not replace this with:

```text
Full1902FeedbackAuthority
RumbleAuthorityManager
FeedbackLeaseManager
FeedbackEpoch
new lock/generation abstraction
```

If future rumble requires safe callback teardown, design it against the actual canonical VIIPER presentation lifetime and real callback ownership at that time.

---

# 9. Delete the legacy Steam Deck feedback bridge

Delete:

```text
src/SteamInputAddonforClaw/Feedback/SteamDeckRumbleFeedbackBridge.cs
```

This includes the bridge-specific developer-test outcome / delayed STOP / authority-callback logic, including types that have no independent caller after the frontend vibration RPC is removed.

Expected removal includes, subject to fresh closure:

```text
SteamDeckRumbleFeedbackBridge
DeveloperVibrationTestOutcome
bridge-specific authority/drop sequencing
bridge-specific delayed Developer STOP lifecycle
bridge-specific callback logging that exists only around the deleted bridge
```

Do not preserve the bridge merely because tests exercise its theoretical revoke/lease races. It has no current production construction site.

This is exactly the type of disconnected architecture that should not dictate production complexity.

---

# 10. Preserve useful future rumble primitives

Do **not** delete the low-level MSI hardware implementation merely because no Full1902 rumble owner is connected yet.

Preserve:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumbleSink.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumblePacketBuilder.cs
```

and their supporting contracts/primitives, including where still required:

```text
TwoMotorRumble
IPhysicalRumbleSink
PhysicalRumbleWriteResult
PhysicalRumbleWriteStatus
IMsiClawRumbleTransport
IMsiClawRumbleEndpointResolver
MSI endpoint/identity validation used by MsiClawRumbleSink
```

Preserve their focused hardware tests.

These are RE-backed device primitives and are likely reusable by the future Controller vibration-strength / Full1902 feedback feature.

They do not create an authority model by themselves.

---

# 11. Preserve the pure Steam Deck feedback decoder

Keep:

```text
src/SteamInputAddonforClaw/Feedback/SteamDeckRumbleDecoder.cs
```

and its pure decoder tests.

Reason:

- it is a stateless packet-decoding primitive;
- it does not own Runtime lifecycle, authority, leases, VIIPER, HidHide, or controller state;
- a future Full1902 SteamDeck feedback path can reuse it without reviving the old bridge architecture.

If removing `SteamDeckRumbleFeedbackBridge` exposes bridge-only decoder DTO fields that are truly unused and not part of the pure decoder contract, they may be simplified only when mechanically obvious and covered by existing decoder tests.

Do not redesign the decoder in Cleanup J.

---

# 12. Remove the vibration test session writer

Delete:

```text
src/SteamInputAddonforClaw/Feedback/VibrationTestSessionWriter.cs
```

once the frontend session RPC and `InProcessAddonFrontendControl` session fields are removed.

The current writer exists solely to create a dedicated log file for a Developer Vibration Test that is already unavailable.

Do not retain a production session/log owner for a disabled UI placeholder.

General application logging remains unchanged.

---

# 13. UI shutdown/navigation cleanup

Fresh-reference-close vibration session calls in:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs
```

Current `MainWindow.CloseVibrationTestForUiShutdownAsync()` performs an extra frontend close call after page deactivation.

After the RPC is removed:

```text
no OpenVibrationTestSessionAsync
no CloseVibrationTestSessionAsync
no RunVibrationTestAsync
```

should remain in UI code.

Keep the navigation/page lifecycle methods needed by the existing UI shell, but make them local/no-op for the unavailable feature.

Do not remove the page from `MainWindow.xaml` in this PR.

---

# 14. Rename the historical termination reason

File:

```text
src/SteamInputAddonforClaw/Lifecycle/UserTerminationGuard.cs
```

Current enum value:

```csharp
UserTerminationBlockReason.RoutingTransition
```

is no longer describing Steam routing.

Its only current production meaning is:

```text
Center M Disabled startup
-> physical PID1902 acquisition + Win+G suppression arm is still committing
-> Enable-and-Restart must not race that controller-authority transition
```

Rename it to:

```csharp
UserTerminationBlockReason.ControllerAuthorityTransition
```

or an equivalently precise current-authority name if fresh code context shows a better existing convention.

Preferred exact rename for consistency:

```text
RoutingTransition -> ControllerAuthorityTransition
```

Update:

```text
AddonProcessHost current use
UserTerminationGuardTests
CenterMRebootAuthorityTransitionTests
other non-historical source/test references
```

Do **not** change the behavior of the gate.

This is a naming cleanup only:

```text
_disabledControllerStartupPending != 0
-> termination / authority-release transition remains blocked
```

Do not remove `_disabledControllerStartupPending` or weaken the real startup-commit safety boundary.

Do not rewrite historical work-order documents solely to rename the old term.

---

# 15. Test cleanup and required coverage

## 15.1 Delete tests that exist only for removed legacy feedback authority/bridge behavior

Fresh-close and remove/rewrite tests whose sole subject is:

```text
FeedbackAuthority generation/lease/revoke semantics
SteamDeckRumbleFeedbackBridge authority-drop behavior
Developer delayed STOP behavior owned by the deleted bridge
bridge RX diagnostic logging
vibration-session RPC/session-writer lifecycle
```

Known candidates include:

```text
tests/SteamInputAddonforClaw.Tests/DeveloperVibrationTestTests.cs
tests/SteamInputAddonforClaw.Tests/SteamDeckFeedbackRxDiagnosticsTests.cs
tests/SteamInputAddonforClaw.Tests/VibrationTestSessionLifecycleTests.cs
tests/SteamInputAddonforClaw.Tests/VibrationTestRpcContractTests.cs
bridge/authority portions of RumbleV1Tests.cs
```

Do not mechanically delete useful decoder tests from `RumbleV1Tests.cs`. Split or trim the file if necessary so pure decoder coverage remains.

## 15.2 Preserve MSI physical rumble tests

Keep:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawRumbleTests.cs
```

including endpoint validation, packet construction, physical-byte conversion, write-result/failure behavior, and disposal semantics that belong to `MsiClawRumbleSink`.

## 15.3 Required UI boundary test

Add/update a focused test proving the Developer Vibration Test page remains present but has no frontend vibration contract dependency.

Acceptable repository-style assertion:

```text
VibrationTestPage remains in MainWindow / Developer navigation
AND
VibrationTestPage.xaml.cs contains no:
  RunVibrationTestAsync
  OpenVibrationTestSessionAsync
  CloseVibrationTestSessionAsync
```

Also preserve the disabled/unavailable UX assertion where practical.

Do not create a new UI testing framework.

## 15.4 Required transport contract test

Update transport tests to prove:

```text
FrontendTransportProtocol.CurrentVersion == 23
FrontendRpcMethod does not contain:
  RunVibrationTest
  OpenVibrationTestSession
  CloseVibrationTestSession
```

Remove fake frontend methods/state that existed only to exercise these deleted calls.

## 15.5 Required termination naming test

Update existing termination tests to assert the same behavior with:

```text
ControllerAuthorityTransition
```

No behavioral expectation should change.

---

# 16. Explicit out of scope

Do **not** implement or redesign any of the following in Cleanup J:

```text
actual Full1902 rumble feedback
VIIPER SteamDeck output callback wiring
X360 rumble callback wiring
vibration-strength user setting
per-game vibration profiles
motor gain/scaling policy
haptic-vs-rumble product policy
new Developer vibration test transport
new vibration diagnostic session format
new rumble authority/lease manager

Developer Test Mode removal
Developer page removal
Environment Discovery removal
Claw Sensor Probe removal
Fan Probe removal

PID1901/PID1902 controller authority changes
HidHide changes
DirectInput changes
VIIPER presentation ownership changes
front-button changes
QAM behavior changes
power/resume changes
PnP recovery changes
uninstall changes
profile behavior changes
```

Future vibration-strength work should start from the retained MSI physical primitives + current Full1902 presentation owner, not from the deleted routing-era bridge.

---

# 17. Lifecycle invariants that must remain unchanged

Cleanup J is almost entirely dead-contract/dead-source cleanup plus one enum rename.

Preserve all real supported lifecycle behavior:

```text
Sleep / Hibernate / Resume
Restart / Crash / Shutdown
physical device loss / PnP re-enumeration
PID1901 <-> PID1902 authority restoration/reclaim
HidHide deterministic normalization / release
VIIPER neutral / detach / teardown / PendingCleanup behavior
controlled Runtime restart while Center M Disabled
Enable Center M and Restart safety
actual operation failure fail-close
```

Especially preserve:

```text
_disabledControllerStartupPending
-> blocks Center M authority release while Disabled-mode startup ownership is committing
```

and:

```text
PowerResumeObserved
-> remains delivered for Addon-authority resume lifecycle
```

No feedback/vibration cleanup should touch these paths.

---

# 18. Overengineering guard

Do not replace deleted code with another abstraction merely to keep an unavailable Developer feature internally "complete".

Specifically, do not add:

```text
FeedbackManager
RumbleManager
VibrationSessionService
DeveloperVibrationAdapter
FeedbackAuthorityV2
RumbleEpoch
FeedbackLease
callback broker
new background worker/timer
```

The page is allowed to be a static unavailable placeholder until a real Full1902 rumble feature is designed.

Preserving useful stateless/hardware primitives is enough.

---

# 19. Expected source result

After Cleanup J, production code should have zero references to:

```text
FeedbackAuthority
FeedbackAuthorityToken
FeedbackAuthorityLease
SteamDeckRumbleFeedbackBridge
DeveloperVibrationTestOutcome
VibrationTestSessionWriter
FrontendVibrationTestCommand
FrontendVibrationTestResult
FrontendRpcMethod.RunVibrationTest
FrontendRpcMethod.OpenVibrationTestSession
FrontendRpcMethod.CloseVibrationTestSession
RunVibrationTestRequest
UserTerminationBlockReason.RoutingTransition
```

Expected retained source includes:

```text
VibrationTestPage.xaml
VibrationTestPage.xaml.cs (static unavailable shell)
SteamDeckRumbleDecoder
TwoMotorRumble
IPhysicalRumbleSink
PhysicalRumbleWriteResult / Status
MsiClawRumbleSink
MsiClawRumblePacketBuilder
MSI rumble endpoint/transport primitives
ControllerAuthorityTransition termination reason
```

Historical docs may still contain old names/architecture descriptions. Do not rewrite history unless a current-authority document incorrectly states present behavior.

---

# 20. Validation

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release --no-build
```

Requirements:

```text
0 build errors
0 warnings introduced by Cleanup J
full Release test suite PASS
```

Fresh source search must prove the removed production symbols are gone from current source except historical docs where intentionally preserved.

Verify:

```text
Developer Vibration Test page still exists
buttons remain disabled
page shows unavailable state
opening/leaving page does not issue vibration RPC/session work
Frontend protocol is v23
actual MSI rumble low-level tests still pass
Steam Deck pure decoder tests still pass
ControllerAuthorityTransition gate behaves exactly like the old RoutingTransition gate
```

No manual hardware validation is required for the deleted vibration path because Cleanup J must not activate any rumble behavior.

If implementation unexpectedly changes physical rumble output, VIIPER feedback, controller ownership, or resume behavior, that is scope expansion and must be treated as a bug rather than justified as cleanup.

---

# 21. PR title

Preferred:

```text
Full1902 Cleanup J: retire legacy vibration feedback transport and routing naming tail
```

---

# 22. Completion checklist

- [ ] `FeedbackAuthority.cs` deleted.
- [ ] `SteamDeckRumbleFeedbackBridge.cs` deleted.
- [ ] bridge-only `DeveloperVibrationTestOutcome` removed.
- [ ] vibration test Runtime session fields/helpers removed from `InProcessAddonFrontendControl`.
- [ ] vibration frontend DTOs/methods removed.
- [ ] three vibration named-pipe RPCs removed.
- [ ] vibration session writer deleted.
- [ ] frontend protocol bumped `22 -> 23` with version-history comment.
- [ ] Vibration Test page retained in Developer UI.
- [ ] page remains disabled/unavailable.
- [ ] page no longer opens/closes Runtime vibration sessions or sends vibration commands.
- [ ] MainWindow shutdown no longer calls deleted vibration RPCs.
- [ ] `SteamDeckRumbleDecoder` retained.
- [ ] `MsiClawRumbleSink` / packet builder / physical rumble primitives retained.
- [ ] decoder + MSI physical rumble tests retained and passing.
- [ ] bridge/authority/session-RPC-only tests removed or rewritten.
- [ ] `RoutingTransition` renamed to `ControllerAuthorityTransition`.
- [ ] controller-authority startup transition gate behavior unchanged.
- [ ] no new feedback/rumble authority abstraction introduced.
- [ ] no Full1902 controller/power/PnP/HidHide/VIIPER behavior changed.
- [ ] Debug build passes.
- [ ] Release build passes.
- [ ] full Release test suite passes.
