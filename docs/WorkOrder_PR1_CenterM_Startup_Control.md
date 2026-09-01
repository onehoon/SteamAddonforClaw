# Work Order — PR1: MSI Center M Startup Enable / Disable Control

## Goal

Add the first piece of the new controller-platform direction: allow Steam Addon for Claw to enable
or disable MSI Center M startup from the Device page.

This PR is intentionally narrow. It must only control these three MSI Center M startup roots:

1. Scheduled Task: `MSI_Center_M_Server`
2. Scheduled Task: `MSI_Center_M_Updater`
3. Windows Service: `MSI Foundation Service`

Changing the state does **not** immediately stop or start the currently running MSI Center M runtime.
A Windows restart is required for the requested state to become the clean runtime baseline.

## 1. Product behavior

**Disable** sets both Scheduled Tasks `Enabled = false` and the Foundation Service
`StartupType = Disabled`, then reports *restart required*. It must NOT call `task.Stop()`, kill MSI
Center M processes, stop the running Foundation Service, or alter controller PID / HidHide / VIIPER /
routing state. The running Center M session may continue until Windows shuts down — intentional. The
clean ownership boundary begins after reboot.

**Enable** sets both Scheduled Tasks `Enabled = true` and the Foundation Service
`StartupType = Automatic`, then reports *restart required*. It must NOT call `task.Run()`,
`service.Start()`, or manually launch MSI Center M / its Launcher. Windows/MSI perform their normal
startup sequence after reboot.

## 2. Why this PR does not stop processes

HHC and ClawTweaks tear down immediately because their switch takes effect during the current Windows
session. This PR's contract is different: change startup configuration → request restart → Windows
lifecycle boundary → new Center M state. No immediate teardown is required, and this avoids a
controller-mode transition mid-session while the new PID1902/X360 architecture does not exist yet.

## 3. Center M card placement

> **Later change (PR #442):** the card was moved to the **top of the Controller tab** (above *Steam
> Input Routing*). Enabling/disabling MSI Center M switches controller authority (PID1902 ↔ stock),
> which is a controller concern rather than a power/TDP one. The rest of this section describes the
> original PR1 placement; the card contents, behaviour, and contract are unchanged by the move.

Add the card at the top of the existing Device page, **before TDP Control**. The Device page already
owns physical-device/system features (TDP Control, CPU Boost, Windows Power Mode); Center M ownership
belongs in the same area. Suggested layout:

```
MSI Center M
Control whether MSI Center M starts with Windows.
Status: Enabled
[ Enable ] [ Disable ]
```

After a successful change also show `Restart Windows to apply this change.` Use the existing
WinUI/CommunityToolkit visual language. Do not add a new navigation tab.

## 4. Do not use an inverted toggle

Avoid `Disable MSI Center M [On]` (toggle-on == Center-M-off is confusing). Prefer explicit
`Status: Enabled` + `[ Enable ] [ Disable ]`. The button representing the already-active
configuration may be disabled. This also represents partial/inconsistent state cleanly.

## 5. Runtime authority

The UI must not own Windows Task Scheduler / Service state:

```
DevicePage → IAddonFrontendControl → Runtime → CenterMStartupHelper → Task Scheduler / SCM
```

DevicePage only requests a snapshot, renders it, requests Enable/Disable, and renders the returned
authoritative result. Do not persist a parallel `CenterMEnabled` boolean in UI settings — the real
Windows state is the source of truth. Do not introduce a generalized hardware-setting manager. A
narrow Center M-specific runtime component is sufficient.

## 6. Frontend contract

`FrontendCenterMStartupState { Enabled, Disabled, Partial, Unavailable }` and a narrow
`FrontendCenterMStartupSnapshot(State, ServerTaskEnabled, UpdaterTaskEnabled, FoundationServiceEnabled,
FailureMessage)`. Mutation result: see **Addendum E** for the authoritative 4-value outcome enum. Add
only `CaptureCenterMStartupAsync` and `SetCenterMStartupEnabledAsync(bool)` to `IAddonFrontendControl`.
Do not build a generic service/task administration contract.

## 7. State detection

Read all three actual Windows states. Report **Enabled** only when both tasks are enabled AND the
service startup type != Disabled (`Automatic` is the enabled target). Report **Disabled** only when
both tasks are disabled AND the service startup type == Disabled. Any mixed state is **Partial** and
must be surfaced (`Status: Needs attention` — "choose Enable or Disable to repair it"), never
auto-repaired by a state machine. Do not use whether the service is currently *Running* as the
persisted startup-state authority (see **Addendum F**).

## 8. Mutation behavior

Treat one user action as one logical operation over the three targets: write all three, then read
all three back. Success requires the read-back to produce the exact requested state — never report
success merely because the setter calls returned without exceptions.

## 9. Failure policy

If one operation fails: do not pretend success; capture the actual resulting three-part state; return
`Partial` if appropriate; surface a concise error; allow retry. No rollback machinery in this PR — an
explicit retry is sufficient.

## 10. Elevation

Changing Scheduled Task enable state and service startup type requires elevation. See **Addendum A**:
a dedicated `SteamInputAddonforClaw.CenterMStartupHelper` mirrors the `TdpHelper` packaging pattern
(`requireAdministrator` manifest, `runas`, short-lived, named-pipe request/result). A cancelled UAC
prompt returns a normal cancel result to the UI. No persistent privileged service.

## 11. Restart UX

See **Addendum D**: PR1 shows the informational message `Restart Windows to apply this change.` only.
No `Restart now` action, no reboot API/command/IPC, no automatic reboot. The user restarts Windows
manually.

## 12. Important lifecycle contract

A successful `Disabled` configuration does NOT mean Center M is already absent from the current
session. `startup config = Disabled` while running Center M processes still exist is expected. No
production code in this PR may assume `CenterMStartupState.Disabled == Center M currently not running`.

## 13. Do NOT modify MSI Center M processes

Out of scope: `Process.Kill`, terminating `MSI Center M.exe` / `MSI_Center_M_Server*` /
`MSI_Center_M_Launcher` / `mongMode` / `MCMOSDInfo` / `Gamebar_Widget`. No watchdog in PR1.

## 14. MSI Quick Settings / Game Bar is OUT OF SCOPE

Do not touch `9426MICRO-STARINTERNATION.MSIQuickSettings`, `OEMGameBarWidget`, `Gamebar_Widget`, or
Xbox Game Bar. PR1 is only the three startup roots.

## 15. Controller stack is OUT OF SCOPE

Do not change PID1901/PID1902, native-mode transition, DirectInput acquisition, HidHide, USBIP,
VIIPER, virtual controllers, Steam/BPM detection, routing eligibility/reconciliation, rollback, WING,
OEM1 mapping, rumble, or controller recovery.

## 16. Do not remove old Center M coexistence code yet

Do not delete existing Center M suppression / routing-safety / native-takeover / recovery logic.
Cleanup belongs in later PRs after the new architecture is hardware-proven.

## 17. Logging

Concise logs only: snapshot state, enable/disable requested, individual target failure, final
read-back state, restart required. No continuous Center M polling in this PR.

## 18. Tests

State classification (`true/true/Automatic → Enabled`, `false/false/Disabled → Disabled`,
`false/true/Disabled → Partial`, `true/true/Disabled → Partial`). Disable/Enable mutation:
the component only ever issues one `SetEnabled` over the three targets and has no `Stop()`/`Start()`/
`Process.Kill` path at all. Read-back: a write that verifies to a mixed state is not a success.
Cancelled: a cancelled elevation returns `Cancelled` with the last real snapshot, never a fabricated
requested state. UI: Device page renders Enabled/Disabled/Partial, disables the already-selected
action, shows restart-required after success and failure info on failure. Architecture: UI →
frontend contract → Runtime; no direct Task Scheduler / ServiceController ownership in the UI project.

## 19. Manual hardware validation

On a supported MSI Claw: confirm the baseline (`MSI_Center_M_Server` / `MSI_Center_M_Updater` enabled,
`MSI Foundation Service` Automatic). Device page shows `Status: Enabled`. Press **Disable**; before
reboot confirm both tasks disabled + service startup `Disabled` (existing processes/service may keep
running); UI shows `Status: Disabled` + restart required. Reboot; confirm none of the three
auto-start and the Addon still starts. Press **Enable**; confirm both tasks enabled + service
`Automatic` (do not expect immediate start); reboot; confirm Center M returns via its normal path.
Do NOT evaluate PID1902/X360 behavior in this PR.

## 20. Acceptance criteria

Device page has an MSI Center M card; user can explicitly Enable/Disable Center M startup; only the
two Scheduled Tasks and the Foundation Service startup type are mutated; Disable does not stop running
tasks/service/processes; Enable does not manually start them; actual Windows state is the authority;
mixed state is visible as Partial; every mutation performs read-back verification; successful mutation
tells the user a restart is required; no MSI Quick Settings / Game Bar / controller / routing changes;
no watchdog; existing controller lifecycle tests remain green; new Center M startup-control tests
pass; full test suite passes; Debug and Release builds are clean.

## 21. Expected next PR (do not implement)

PR2: Windows startup → verify Center M Disabled baseline → establish physical PID1902 → HidHide
ownership → persistent virtual Xbox 360 baseline. PR1 must remain a clean, independently testable
prerequisite.

---

# PR1 Addendum — Resolved Implementation Decisions

These decisions are fixed for PR1. Do not choose alternative implementations unless the existing code
makes the specified path technically impossible.

## A. Elevation: use a dedicated `CenterMStartupHelper`

The earlier instruction to extend `SteamInputAddonforClaw.CenterMHelper` is **withdrawn** — code
inspection confirmed `CenterMHelper.exe` is not an elevation helper; it is an intentionally inert
OEM1 native-launch-suppression decoy staged/renamed as `MSI Center M.exe`. Do not modify that binary.

Create one narrow privileged helper `SteamInputAddonforClaw.CenterMStartupHelper` whose only
responsibility is: enable/disable `MSI_Center_M_Server`, enable/disable `MSI_Center_M_Updater`, set
`MSI Foundation Service` startup type to `Automatic`/`Disabled`. Use `TdpHelper` as the repository
pattern for the `requireAdministrator` manifest, `Verb="runas"` launch, short-lived lifetime,
named-pipe request/result, and UAC cancellation handling. Do not reuse `TdpHelper` itself and do not
add Center M startup verbs to it. Boundary:

```
DevicePage → IAddonFrontendControl → Runtime Center M startup component → CenterMStartupHelperClient
  → runas → CenterMStartupHelper → Task Scheduler / SCM
```

Keep it minimal: no generic service/task-management APIs, no persistent background behavior, no
process monitoring/termination, no controller-suppression or decoy behavior, no Game Bar management,
no PID/HidHide/VIIPER operations. `CenterMHelper` stays an inert decoy; `TdpHelper` stays dedicated to
hardware control. A new helper project is explicitly allowed here because forcing this into either
existing helper would increase coupling rather than reduce complexity.

## B. QAM exposure: explicitly out of scope

PR1 is WinUI DevicePage only. Do not expose Center M startup Enable/Disable through QAM, QamHost
commands, Steam QAM cards, or controller shortcuts. If extending `IAddonFrontendControl` requires
compile-time changes to `QamFrontendBridge` or another frontend implementation, implement only the
minimum contract plumbing to keep the solution compiling. Center M startup control is a
reboot-requiring administrative setting and stays on the full Device page.

## C. Existing UI architecture tests may be updated

> **Later change (PR #442):** the card now lives at the top of the **Controller** page, so the
> `UiArchitectureTests` guard asserts `CenterMStartupCard` precedes `SteamInputRoutingExpander` in
> `ControllerPage.xaml` (and is absent from `DevicePage.xaml`), and
> `CenterMStartupPresentationTests` reference `ControllerPage.CenterMStartupPresentation`.

Adding the MSI Center M card above TDP Control legitimately changes DevicePage structure. Updates to
`UiArchitectureTests` ordering/string assertions are allowed when required by the new card. Preserve
the purpose of those tests — do not remove architecture assertions or weaken ownership boundaries to
make tests pass. "Existing tests remain green" means the suite passes against the new intended
architecture, not that test source files are byte-for-byte unchanged.

## D. Restart UI: informational only in PR1

No `Restart now` action in PR1. After a successful Enable/Disable, show an InfoBar / equivalent:
`Restart Windows to apply this change.` No shutdown/reboot APIs, no privileged reboot command, no
restart IPC, no automatic reboot. A Restart button may be considered separately later.

## E. Clarify `Unavailable`

`FrontendCenterMStartupState.Unavailable` describes inability to provide a meaningful startup
snapshot: the feature is not applicable to the detected hardware; the startup components cannot be
identified; or Task Scheduler / SCM state could not be read reliably. Do not convert the snapshot to
`Unavailable` merely because a mutation helper failed to start or UAC was cancelled — those are
mutation outcomes:

```
FrontendCenterMStartupMutationOutcome { Succeeded, Cancelled, Failed, Unavailable }
```

* `Succeeded` — requested configuration written and exact read-back verified.
* `Cancelled` — the user cancelled elevation before the mutation completed.
* `Failed` — the mutation was attempted but could not produce the requested verified configuration.
* `Unavailable` — the Center M startup feature itself cannot currently be operated.

Whenever possible, return the latest actual snapshot alongside `Cancelled` or `Failed`. Never
fabricate the requested state after a failed/cancelled privileged operation.

Tests must pin: a cancelled UAC returns the last real snapshot, not the requested state.

## F. Service state authority

Use the configured service startup type as the authority — never `Running`/`Stopped`. Targets:
`Enable → MSI Foundation Service StartType = Automatic`, `Disable → StartType = Disabled`. Read back
the configured start type after mutation before reporting success. PR1 intentionally leaves the
current service/process session untouched.
