# Work Order — PR-A: Mandatory Startup Task Repair, Device Status, and Manual Recovery

> Date: 2026-10-17
> Repository: `onehoon/SteamAddonforClaw`
> Reviewed base: `main` @ `30b029df7e687c8ab099b48073f49cce8e186bd6` (PR #824 squash-merged)
> Finding: **Audit 09-001**, with the Audit 03-001 interface explicitly **reserved for PR-B**
> Product: standalone Full PID1902, **one Windows administrator user / one interactive session**
> Implementation: local Codex; device verification: user **after merge**
> Delivery: **one PR with two independently reviewable commits**

## 1. Goal and locked scope

Make the existing **mandatory Addon-owned Windows Task Scheduler logon task** reliably observable and repairable, while keeping the current Runtime alive when repair fails.

This PR must:

1. Distinguish **task-read failure** from **successfully verified task absence** in enabled startup synchronization.
2. Preserve the existing behavior of automatically ensuring/repairing the task once during normal elevated Runtime composition.
3. Retain the actual startup verification/repair outcome in the **existing** `StartupSettingsCoordinator` authority so it can be exposed after composition.
4. Show a compact **Auto start** status immediately **below the existing controller-presentation badge**, on the **right side of the first Device summary card**.
5. Only on a verification/repair problem, show a small **Repair** button beside that status. Clicking it reruns the existing `EnsureStartupRegistration()` through an authorized Runtime frontend RPC; report verified success or the actual failure without restarting the application.
6. Preserve every existing Full1902 stock/Addon authority, shutdown, uninstall, recovery, and status contract.

**This is not PR-B (03-001).** Do not configure Task Scheduler `RestartCount`/`RestartInterval`, change Runtime exit codes, insert a watchdog, or claim any unexpected Runtime process-death recovery guarantee.

### Commits

| Commit | Work | Suggested subject |
|---|---|---|
| **1** | 09-001: correct startup-task read/absence distinction, return typed outcome, retain the last verified operation result in existing settings coordinator; focused Core tests | `fix(startup): distinguish task read errors and retain registration result` |
| **2** | Existing status snapshot + one manual repair RPC + Device first-card UI + en-US/ko-KR + transport/UI tests | `feat(device): expose startup-task status and repair action` |

No other changes, no separate PR, and no new general-purpose service/state manager.

## 2. Required architectural documents and evidence

Read all active documents in the current precedence order:

- `docs/Full 1902 Implementation/README.md` (active document authority).
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md` (mandatory task ownership, logon, registration and proof).
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` (durable PID1902/HidHide lifetime).
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` (the required **Highest** task RunLevel).
- Relevant prior work orders in `docs/work-order/` for safe-uninstall task absence proof, startup-task idempotent repair, Device summary badge, and UI localization. Historical conflicts do not override current architecture.

Audit sources are the user's `Addon/Audit/09_INSTALL_MAINTENANCE_AUDIT.md` and `03_RUNTIME_LIFECYCLE_AUDIT.md`.

### Confirmed source paths on reviewed main

| Path | Relevant current behavior |
|---|---|
| `src/SteamInputAddonforClaw/Install/StartupRegistration.cs` | `WindowsTaskSchedulerStartupManager.Synchronize(true)` calls `SafeRead()`, which returns `null` on a Task Scheduler exception; that incorrectly triggers registration as if the task were missing. `RemoveOwnedTask()` already uses `TryRead(out ...)` to retain the missing-vs-failure distinction. Successful registration uses one write with bounded readback; compliant task uses zero writes. |
| `src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs` | `EnsureStartupRegistration()` delegates directly to `IWindowsStartupManager.Synchronize(true)`. There is no retained last-attempt result. |
| `src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs` | The High Runtime calls `startupSettings.EnsureStartupRegistration()` once during normal composition and logs its result, but discards it. Uninstall-preparation-only composition intentionally skips this ensure. |
| `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` | Creates `InProcessAddonFrontendControl` with the **same** `composition.StartupSettings` instance. Do not create a second scheduler or repair owner. |
| `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs` | Already holds `_settings`, has `CaptureStatusAsync()`, `StateInvalidated`, and the single Runtime mutation endpoint. |
| `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs` | `FrontendStatusSnapshot` currently has an additive `ControllerBadge`; `IAddonFrontendControl` owns status/RPC contracts. |
| `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs` / `NamedPipeAddonFrontendClient.cs` / `NamedPipeAddonFrontendServer.cs` | Current Main UI frontend protocol is **v85**; the new status member and one RPC require a version advance to **v86** and complete transport coverage. Overlay transport is separate and must not change. |
| `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs` | `RefreshSystemStatusAsync()` calls `CaptureStatusAsync()`; `RenderSystemStatus()` already forwards its snapshot to `DeviceContent.RenderDeviceSummary(snapshot)`. |
| `src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml[.cs]` | The **first `DeviceSummaryCard`** has a right-aligned `DeviceSupportText` badge, and `DeviceBoardGpuText` beneath the title. There is no startup-task UI. |
| `src/SteamInputAddonforClaw.UI/DeviceSummaryPresentation.cs` | Sole presenter for existing controller badge and manufacturer formatting; keep those rules unchanged. |
| `src/SteamInputAddonforClaw.UI/Strings/{en-US,ko-KR}/Resources.resw` | Two-language resources with existing `x:Uid` and `MainUiStrings.Get` patterns. |

The older 2026-10-14 localization work order restricted changes to `DeviceSummaryCard` **for that past PR only**. This new user-approved work specifically allows a **small right-side addition**, never a card redesign.

## 3. Commit 1 — Make startup task ownership/repair results trustworthy

### 3.1 Enabled synchronization: do not equate read failure with missing task

Currently:

```csharp
var current = SafeRead();
if (current is not null && IsCompliant(current, configuration))
    return StartupRegistrationResult.Enabled();
// current == null could mean missing OR failed COM read; both currently Register.
```

Use the **existing `TryRead` semantics** (already used by task removal), rather than adding a new COM service wrapper. Conceptual correction:

```csharp
if (!TryRead(out var current))
{
    // Preserve explicit failure. Never claim the task is missing.
    // Do not Register() without a trustworthy read result.
    return StartupRegistrationResult.ReadFailed(); // illustrative name
}

if (current is not null && IsCompliant(current, configuration))
    return StartupRegistrationResult.Enabled();

// Only an actually absent or materially noncompliant task reaches Register().
```

Preserve and prove:

- An exactly compliant task: successful read, zero writes, zero unexpected UAC.
- Confirmed absent task: register **once**, bounded readback, report success only when compliant.
- Confirmed drifted task: register **once**, bounded readback.
- Failed read: no `Register`, no `Delete`, explicit error for the UI.
- Failed register/access denied: error; no secondary elevation, no retry storm.
- Failed readback: error, even if `Register` reported success.
- Missing stable Velopack EXE: explicit non-ready/repair-failed reason; do not register a task for a guessed path.
- Task deletion/uninstall: preserve existing `TryRead` proof, absence verification and fail-closed behavior.
- Keep exact action `--background`, task name, current-user logon trigger, same interactive user, `Highest`, battery behavior, priority and `PT0S` time limit.
- The legacy Run-key cleanup contract is unchanged.

### 3.2 Return a narrow, actionable result

`StartupRegistrationResult` is currently `(bool Success, string Message)`. Add **only the minimum typed failure classification** required for read/repair/UI behavior; e.g.:

```csharp
internal enum StartupTaskFailureReason
{
    None,
    ReadFailed,
    StableExecutableMissing,
    RegistrationFailed,
    ReadbackUnverified
}

// Illustrative shape only: adapt visibility/constructor defaults to existing tests.
public sealed record StartupRegistrationResult(
    bool Success,
    string Message,
    StartupTaskFailureReason Reason = StartupTaskFailureReason.None);
```

If this shape conflicts with public/internal visibility, use the smallest compile-safe equivalent. Avoid freeform exception text over the wire, extensive HRESULT taxonomies, or an independent health state machine. Preserve existing callers' `Success` and `Message` usage (notably official `CenterMRebootAuthorityTransition` and safe uninstall).

### 3.3 Retain latest outcome in the existing coordinator

Use `StartupSettingsCoordinator` as the **single existing owner** for the last mandatory-task ensure result. Normal composition must call its ordinary ensure method, and the result must remain readable to its already-wired frontend control.

Minimal conceptual shape:

```csharp
public StartupRegistrationResult? LatestStartupRegistration { get; private set; }

public StartupRegistrationResult EnsureStartupRegistration()
{
    var result = _startupManager.Synchronize(true);
    LatestStartupRegistration = result;
    return result;
}
```

A `null` value here means **not verified**, never `Healthy`. Handle an actually thrown external scheduler error at the same owner boundary so a running Runtime can report `Unknown/Failed` rather than unexpectedly exiting merely because task verification threw. Keep it narrow and logged; never fabricate `Success=true`.

The existing Center M Disable-and-Restart call to `EnsureStartupRegistration()` should keep working and continue requiring positive registration proof. Preserve any successful/failed subsequent ensure result on this same coordinator. The uninstall removal path remains a **separate action**: never translate a successfully deleted uninstall task into “Auto start healthy.”

### 3.4 Tests — commit 1

Extend `tests/SteamInputAddonforClaw.Tests/WindowsTaskSchedulerStartupManagerTests.cs`, `StartupRegistrationOwnershipTests.cs`, and narrowly relevant startup/coordinator tests:

- `Synchronize(true)` read exception -> result not successful, reason `ReadFailed`, zero writes, no deletion.
- `Synchronize(true)` genuinely absent -> one write and positive readback.
- Current task compliant -> no rewrite.
- Drifted action/user/RunLevel/battery/time limit -> one repair write, unchanged exact task policy.
- Registration access denied and readback never compliant -> non-ready result, no false green.
- Startup composition retains actual positive/negative last result; Runtime startup continues on failure.
- Uninstall preparation does not start automatic task registration, change controller authority or report a fictitious healthy state.
- Existing Center M Disabled admission / stock-safe uninstall tests still pass.

## 4. Commit 2 — Device summary status, recovery button and IPC

### 4.1 Frontend status model (narrow and typed)

Extend **existing** `FrontendStatusSnapshot` with a small optional/additive task-status member; leave `ControllerBadge` and all existing required constructor fields intact.

Suggested contract:

```csharp
public enum FrontendStartupTaskState
{
    Unknown,
    Verified,
    NeedsAttention
}

public enum FrontendStartupTaskIssue
{
    None,
    ReadFailed,
    StableExecutableMissing,
    RegistrationFailed,
    ReadbackUnverified
}

public sealed record FrontendStartupTaskSnapshot(
    FrontendStartupTaskState State,
    FrontendStartupTaskIssue Issue)
{
    public static readonly FrontendStartupTaskSnapshot Unknown =
        new(FrontendStartupTaskState.Unknown, FrontendStartupTaskIssue.None);
}

// Add to existing FrontendStatusSnapshot as an init-only member:
// public FrontendStartupTaskSnapshot StartupTask { get; init; } = FrontendStartupTaskSnapshot.Unknown;
```

These names are illustrative, but the semantics are binding:

- **Verified** = successful *positive* confirmation of the mandatory logon task's complete current PR-A contract.
- **NeedsAttention** = the most recent ensure/repair definitely failed, including a read error that prevented proof.
- **Unknown** = no trustworthy verification has been attempted/available (e.g. Runtime/protocol unavailable); never use success-looking status.
- Failure reason is a typed code from the **Runtime result**, not generated by the UI from exception strings.
- Never claim the Runtime crash-restart policy has been verified; that belongs to PR-B.

The normal status RPC must project the cached `StartupSettingsCoordinator.LatestStartupRegistration` (or narrow equivalent) through `InProcessAddonFrontendControl.CaptureStatusAsync()`, alongside the existing `FrontendSnapshotMapper.Map/ApplySetup` pipeline. **Status capture is read-only and must NOT call `EnsureStartupRegistration()`, register tasks, repair tasks, or turn into a Task Scheduler polling loop.** It is a **last verified/attempted** status, not a claim that no external actor can ever change Task Scheduler after the capture.

### 4.2 One user-invoked repair RPC

Add a single no-payload Main UI frontend method e.g. `RepairStartupTaskAsync(CancellationToken)` returning the same typed `FrontendStartupTaskSnapshot` (or a small result that contains it).

Runtime implementation:

1. Respect `ThrowIfShuttingDown()` and the existing transport/host shutdown patterns.
2. Invoke the **same existing** `_settings.EnsureStartupRegistration()` as normal startup/Center M authority admission; do not instantiate a scheduler or call COM directly from UI.
3. Keep the bounded one-write/readback semantics. Run a synchronous Task Scheduler operation off the UI thread; avoid blocking the native Runtime message-loop owner or synchronous UI click handler.
4. Return **Verified only after positive readback**, including for a no-write compliant task.
5. On a failed result, return **NeedsAttention** with a meaningful typed reason, retaining the last failure. Do not conceal errors or convert them to “no task.”
6. On an actual thrown exception, log and retain an unsuccessful/unknown result. The existing Runtime and controller presentation must remain operational.
7. After the operation completes, expose the updated status via the RPC result **and** the ordinary status snapshot. Send one existing `StateInvalidated` notification if needed for other rendered surfaces; do not create a new event bus.
8. No “Disable Auto Start” mutation, no repair from Overlay or Settings, no UAC broker, no Task Scheduler COM in the WinUI process.
9. Prevent duplicate button clicks with a single UI in-flight flag. Do not add timer retries, locks, epochs or queues for theoretical interleavings.

Update `IAddonFrontendControl`, `FrontendRpcMethod`, named-pipe server/client and existing RPC test fake; advance `FrontendTransportProtocol.CurrentVersion` **85 → 86** (the actual main value as of review; re-check before implementation). Preserve the **separate Overlay protocol version**.

### 4.3 Exact Device first-card UI contract

In `src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml`, keep the existing:

- `DeviceSummaryCard` outer Border, padding, colors, dimensions and placement.
- Manufacturer, model, `DeviceBoardGpuText` (including Board/GPU formatting).
- Right-side `DeviceSupportText` badge, which already shows `MSI Native`, `Steam Deck · Active`, `Xbox 360 · Active`, `DualSense Edge · Active`, `Initializing…`, etc. **Its semantics and layout inside its badge do not change.**

In **the same Grid.Column=2** replace only the badge's immediate placement with a small right-aligned vertical `StackPanel` (or equivalent), containing:

```text
                          ┌──────────────────────┐
MSI   Claw 8 AI+          │ Steam Deck · Active  │
Board: ... · GPU: ...     └──────────────────────┘
                           ✓ Auto start · OK
```

For a failed/unverified result:

```text
                          ┌──────────────────────┐
MSI   Claw 8 AI+          │ Steam Deck · Active  │
Board: ... · GPU: ...     └──────────────────────┘
                           ⚠ Auto start · Error   [Repair]
```

**No new Device/Settings card, no separate status dashboard, no read-only start-toggle, no settings persistence, no tooltip framework.** A simple native `ToolTipService.ToolTip` on the status line or icon can show a localized, high-level problem explanation (e.g. task read failed / registration failed / verification failed). Do not expose HRESULTs or stack traces on the summary.

Suggested fixed labels:

| State | en-US | ko-KR | Repair button |
|---|---|---|---|
| Verified | `Auto start · OK` | `자동 시작 · 정상` | Hidden |
| NeedsAttention | `Auto start · Error` | `자동 시작 · 오류` | Visible |
| Unknown | `Auto start · Unavailable` | `자동 시작 · 확인 불가` | Visible only if Runtime RPC is available and user can retry |
| In-flight | `Auto start · Repairing…` | `자동 시작 · 복구 중…` | Disabled while executing |

- A repair attempt must not change `DeviceSupportText`, controller badge, hardware identity, GPU/Board details or hardware status.
- Repair button appears only for `NeedsAttention`, or a retryable `Unknown` within a live responsive Runtime. No button for `Verified`.
- Keep compact right column alignment with narrow window sizes, DPI 150%, and text expansion: use wrapping/adaptive sizing rather than a fixed width or global card redesign.
- Use WinUI theme resources and existing Main UI localization, not hardcoded English or new custom UI primitives.
- The existing `MainWindow.RenderSystemStatus()` should continue driving normal Device summary status. On repair return, render the returned task status immediately (or trigger the existing status refresh); do not wait for another navigation.
- When Runtime transport disconnects, **do not leave a stale green indicator**. Extend the existing `DeviceSummaryPresentation.WithUnavailableControllerBadge(...)` or equivalent narrow MainWindow transport-failure projection so both the controller badge and this new task-status projection become unavailable and the Repair button is disabled.
- If another system status refresh comes back while a repair request is in flight, prefer the returned verified/failed repair result when completing the UI operation. Keep this as a narrow UI busy rule, not a synchronization subsystem.
- The nearby separate `MSI Center M` authority card stays unchanged.

### 4.4 Localization

Use `Strings/en-US/Resources.resw` and `Strings/ko-KR/Resources.resw` with matching, nonempty keys for:

- Auto-start healthy / needs attention / unknown / repairing.
- Repair button.
- The supported error reasons and retry indication.
- Optional native tooltip short explanations.

Use `MainUiStrings.Get` for dynamic status and error text; use `x:Uid` for static Repair button labels if suitable. No raw Runtime English `StartupRegistrationResult.Message` in the localized UI. Keep device manufacturer/controller badge text, technical task identity, and existing resource keys untouched.

**Published PRI gate is mandatory:** PR #824 fixed `scripts/verify-ui-localization-pri.ps1` to recognize `*.Message` resources. If this work adds further `x:Uid` properties, ensure real published PRI verification continues to pass, not just the source RESW XML tests.

### 4.5 Focused tests — commit 2

Tests must prove the complete operator path without real Task Scheduler or MSI Claw hardware:

- Existing startup-time result (success/failure/read error) projects correctly through `CaptureStatusAsync()` without extra task-store reads or writes.
- New repair RPC calls the existing coordinator **exactly once per click**, and its verified/failed result matches later status captures.
- Positive result requires true compliance/readback; failing/fake scheduler never produces green.
- Frontend named-pipe round-trip, result/enum serialization, protocol 86 handshake and old-version rejection; do not modify the Overlay wire protocol.
- Repair during known Runtime shutdown returns unavailable/error without a task write.
- Device layout retains exactly one controller badge and one first-card Auto-start line, with Repair button inside the right-side column; no new Settings card.
- UI success/failed/unknown/in-flight presentation, button availability, and immediate result rendering.
- Transport disconnect changes cached green to unavailable and prevents repair click, preserving other captured card values.
- en-US/ko-KR key parity and published PRI asset verification.

Suggested existing test files:

- `tests/SteamInputAddonforClaw.Tests/WindowsTaskSchedulerStartupManagerTests.cs`
- `tests/SteamInputAddonforClaw.Tests/StartupRegistrationOwnershipTests.cs`
- `tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs`
- `tests/SteamInputAddonforClaw.Tests/FrontendContractTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/DeviceSummaryPresentationTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs`

Prefer existing fixtures/helpers over introducing new production-only test abstractions.

## 5. Important lifecycle and safety invariants

1. **Center M Disabled**: PID1902/HidHide remain durable Addon-owned authority, even if the mandatory startup task cannot be repaired now. Never silently switch PID1901, clear HidHide, detach VIIPER, or stop an already-running healthy Runtime solely for this diagnostic.
2. **Center M Enabled**: MSI stock authority is independent of this status. The auto-start status must not masquerade as a controller ownership badge.
3. **Disable Center M and Restart**: existing strict mandatory-task verification remains a blocker for entering Addon authority. No relaxed success criteria, no bypass via Repair.
4. **Enable Center M and Restart**: existing physical cleanup, PID1901 proof, HidHide release and reboot-bound semantics unchanged.
5. **Uninstall**: existing task removal and positive absence verification remain strict; UI repair must not recreate the task during uninstall preparation.
6. **Restart / Crash / Sleep / Hibernate / Resume / PnP**: no new controller lifecycle participant, new suspend hook, new startup loop or second state authority; current behavior preserved.
7. **Single interactive admin user**: no multi-session, RDP, Fast User Switching, cross-user task repair or impersonation support.
8. The Addon already **automatically attempts task verification/repair once at startup**. The visible Repair button is a bounded **user-initiated retry** after an error, not an additional automatic registration path.
9. No extra privileged worker: the existing Runtime is already elevated to High with same-user UAC semantics.
10. **Do not modify** Task Scheduler restart-on-failure settings or Runtime process exit codes here. That is separately tracked as **PR-B / Audit 03-001**, and the simple auto-start label does not promise crash recovery.

## 6. Exact validation and handoff

Run as appropriate:

```powershell
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-publish-assets.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-layout.ps1 -Version 0.1.0 -Configuration Release -PublishDirectory artifacts/publish -NoRestore
git diff --check
```

Respect current repository restore/publish prerequisites; do not mistake a local missing Windows SDK or a preexisting environment issue for a product defect. Actual published Main UI PRI verification is part of the publish gate.

Deliver one PR with the two commits, affected-file summary, regression test results, and a clear **PR-B out-of-scope** statement.

**Real device, Windows Task Scheduler mutation on the user's device, actual reboot/crash recovery, live UAC, physical PID1902/1901, HidHide and VIIPER testing are performed by the user after merge, not by local Codex, and may not be demanded as a PR-review or merge blocker.**

## 7. Final acceptance checklist

- [ ] Enabled-sync task read failure cannot be mistaken for absence; no write on read error.
- [ ] Compliant task is not rewritten; missing/drifted task gets exactly one bounded verified repair.
- [ ] Failed mandatory task establishment stays an observable error while Runtime remains alive.
- [ ] Original Coordinator instance retains the last attempt result; startup and user Repair share its exact owner.
- [ ] Normal status captures perform **zero Task Scheduler writes**.
- [ ] Device first-card right side retains the original controller badge and adds a concise second auto-start line.
- [ ] Repair is visible on error, operates once on user click, and updates the status promptly.
- [ ] Disconnect/unknown cannot appear healthy or enable an unusable Repair action.
- [ ] No duplicate card, new recovery service, or polling/supervisor subsystem.
- [ ] en-US/ko-KR, frontend protocol/serialization, Core/UI tests and published PRI verification pass.
- [ ] Center M authority, safe uninstall, PID1902/HidHide, VIIPER and user-owned hardware validation policy are unchanged.
