# Work Order — Replace Device Summary "Supported" With Live Controller Presentation

Date: 2026-10-11
Repository: onehoon/SteamAddonforClaw
Baseline: main @ fe6619e912c641f129fe0ca30c1fa57bae283386
Delivery: One focused implementation PR; local Codex implements and tests in software. Device testing after merge belongs to the product owner.

## 1. Objective

Replace the "Supported" pill at the right end of the top **Device identity card** with a compact, truthful read-only summary of the **currently authoritative controller presentation**.

Normal Full1902 operation:
- Xbox360 attached/live: **Xbox 360 · Active**
- Steam Deck attached/live: **Steam Deck · Active**
- MSI/stock controller authority: **MSI Native** (authority/mode label, NOT a claim that the physical device has been freshly tested)

The user should be able to tell which controller the Addon is actually presenting without opening Developer/Settings. Keep the existing manufacturer/model/board/GPU line and the card's footprint. This is NOT a Status page revival, detailed diagnostic dashboard, or controller settings redesign.

## 2. Mandatory architecture and source baseline

Follow the active authority order in:
- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
- docs/work-order/APP_UI_PR_A_NAVIGATION_AND_PAGE_OWNERSHIP_REORGANIZATION_WORK_ORDER.md
- docs/work-order/FULL1902_0903_STATUS_AND_DIAGNOSTIC_LOG_CLEANUP_WORK_ORDER.md

Standalone Full1902 only. Do not reintroduce CTW integration or legacy Steam-routing toggles.

The current code review established these concrete facts:

| Current source | Observed behavior |
| --- | --- |
| src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml | The top card has DeviceManufacturerText, DeviceModelText, DeviceSupportText, DeviceBoardGpuText. The right pill is already present. |
| src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs | RenderDeviceSummary() currently formats snapshot.Hardware.Status as Supported/Unsupported/Compatibility unknown. |
| src/SteamInputAddonforClaw.UI/DeviceSummaryPresentation.cs | Contains the manufacturer alias and compatibility-formatting helpers; existing UI tests cover both. |
| src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs | Single shared RefreshSystemStatusAsync() -> CaptureStatusAsync() -> RenderSystemStatus() -> DeviceContent.RenderDeviceSummary() path; StateInvalidated requests status capture. |
| src/SteamInputAddonforClaw/Status/SystemStatusProvider.cs | Captures hardware/prerequisites/Steam and derives Addon operational status; its fallback Passive result does NOT independently prove that Center M is Enabled. |
| src/SteamInputAddonforClaw/Status/Full1902AddonStatusEvaluator.cs | Returns Ready for Disabled + startup not pending + physical input IsRunning + an ActivePresentation. Current Reason includes the presentation name as TEXT, not as a typed frontend fact. |
| src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs | Owns existing _startupResult, _disabledControllerStartupPending, _physicalOwnership, _presentationOwnership and relevant recovery/suspend lifecycle facts. |
| src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs | ActivePresentation has Xbox360 / SteamDeck / null. The presentation also tracks its suspend-paused state. |
| src/SteamInputAddonforClaw/Frontend/FrontendSnapshotMapper.cs | Maps SystemStatusSnapshot to FrontendStatusSnapshot; currently no typed active-controller-presentation field. |
| src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs | FrontendStatusSnapshot has hardware/Steam/AddonStatus/Reason/RecoverySafe but no active presentation badge field. |
| src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs | Current protocol version = 70. The Main UI uses a versioned named-pipe CaptureStatus response. |
| src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs | Existing NotifyStateInvalidated() and Runtime status/Steam invalidation path can be reused. |
| src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs | RunningAppID/BPM change schedules ReconcileControllerPresentationAsync asynchronously; a frontend invalidation can occur BEFORE it completes, so it does not prove the UI refresh happens AFTER attach/switch. |

Before editing, reconfirm these file paths and any changes newer than the pinned baseline. Preserve the single Runtime controller authority.

## 3. Required UI behavior

Keep the current card structure:

~~~text
[MSI] [Claw model]                     [Xbox 360 · Active]
Board: ... · GPU: ...
~~~

Use the existing right-side pill; do not add a second status card, row, dashboard, periodic timer, or manual Refresh button. Maintain text wrapping and alignment on narrow/scaled Windows layouts. English UI strings consistent with the rest of the product.

Display rules (priority matters):

| Situation and proof | Text |
| --- | --- |
| Hardware definitively unsupported | Unsupported (preserve useful compatibility warning) |
| Hardware compatibility indeterminate | Compatibility unknown or Unavailable; never Active |
| Explicit invalid/partial authority, unsafe recovery, terminal failed ownership or unproven teardown | Needs attention |
| MSI Center M startup roots authoritatively Enabled, stock authority in effect | MSI Native |
| Center M Disabled; initial controller acquisition/attach genuinely pending | Initializing… |
| Center M Disabled; known physical-input loss and existing owned recovery/reacquisition in progress | Reconnecting… |
| Center M Disabled; physical input running, startup no longer pending, actual Xbox360 presentation attached/live and not suspend-paused | Xbox 360 · Active |
| Same proven state with actual SteamDeck presentation | Steam Deck · Active |
| No trustworthy current Runtime/presentation state, read failure or transport disconnected | Unavailable |

Important semantic distinctions:
1. **Supported ≠ connected/healthy.** Hardware compatibility is not an active-controller signal.
2. **Desired output ≠ attached output.** Steam/BPM/RunningAppID cannot produce an Active label by itself. Never show Steam Deck Active until actual Steam Deck presentation is confirmed, or Xbox 360 Active merely because Steam is absent.
3. **Virtual type ≠ physical ownership.** An old non-null presentation kind does not justify Active if current PID1902 DirectInput ownership/input is lost, starting, retiring, recovering, or suspend-paused.
4. **Passive ≠ MSI Native.** In the current SystemStatusProvider, the non-owned fallback may return Passive during a Disabled boot that has not yet acquired the controller. Prove Center M Enabled/stock authority from the existing authoritative authority fact before using the MSI Native label.
5. **MSI Native is an authority label only.** Do not claim the stock physical PID1901 device is connected/usable unless there is already an appropriate proof. Do not initiate a PnP scan for this badge.
6. Use Initializing/Reconnecting only when the existing lifecycle actually demonstrates those conditions. An indefinitely failed or unknown state must not misleadingly remain "Initializing…"/"Reconnecting…". When specific failure diagnosis is not available, prefer Unavailable/Needs attention over false success.
7. Keep the hardware-compatibility unsupported/unknown fallback: "Supported" is redundant in healthy installs, but a genuine unsupported-device warning is still useful.

Do not show physical PID, HidHide status, VIIPER diagnostics, Gyro/LED/Rumble, Steam AppID, or extra owner/teardown details inside this compact card. Those remain in their current Settings/Controller/diagnostic owners.

## 4. Preferred implementation: one typed, read-only projection

Add one **small, typed controller-badge field** to the existing status snapshot pipeline. It represents an observed display result, NOT a second persisted or writable authority.

Suggested minimal shape (names can follow current conventions):

~~~csharp
public enum FrontendControllerBadgeState
{
    Unavailable,
    MsiNative,
    Xbox360Active,
    SteamDeckActive,
    Initializing,
    Reconnecting,
    NeedsAttention
}

public sealed record FrontendStatusSnapshot(/* existing required members */)
{
    public FrontendControllerBadgeState ControllerBadge { get; init; }
        = FrontendControllerBadgeState.Unavailable;
}
~~~

On the Runtime side, use a matching internal enum (or a single narrow mapping if it avoids duplicate abstractions) and derive its value directly from **existing** AddonProcessHost authority/physical/presentation/recovery/suspend facts. Carry that result through SystemStatusProvider -> SystemStatusSnapshot -> FrontendSnapshotMapper.Map -> FrontendStatusSnapshot -> DeviceSummaryPresentation -> DevicePage.RenderDeviceSummary.

A single optional read-only delegate in SystemStatusProvider/AddonRuntimeComposition, analogous to the existing captureFull1902AddonStatus, is acceptable. Keep it a pure projection with an Unavailable fallback if capture throws. The Runtime process remains the only source of truth. Do not build a new manager/service/cache/observer for a UI label.

Example for the **positive Active gate** only (illustrative, not an instruction to bypass existing ownership invariants):

~~~csharp
if (centerMState == FrontendCenterMStartupState.Disabled
    && !disabledStartupPending
    && physicalInputSourceRunning
    && !presentationSuspendPaused)
{
    return actualPresentation switch
    {
        AddonPresentationKind.Xbox360 => ControllerBadgeState.Xbox360Active,
        AddonPresentationKind.SteamDeck => ControllerBadgeState.SteamDeckActive,
        _ => ControllerBadgeState.Unavailable
    };
}
~~~

The implementation must also check existing confirmed blocked/recovery/release conditions before this positive gate as appropriate, and must not substitute a desired presentation for actualPresentation. Prefer reused observations; no additional PnP, HidHide, USB/IP, or VIIPER native calls merely to paint the badge.

In DeviceSummaryPresentation add/test the narrow badge-label mapping, retain FormatManufacturerForDisplay, and remove or adapt only the compatibility helper/tests actually made obsolete. Render in the existing DeviceSupportText location (renaming to DeviceControllerStatusText is fine).

## 5. Frontend transport and refresh correctness

This app uses separate Runtime and Main UI processes. Since FrontendStatusSnapshot is a versioned wire DTO, bump FrontendTransportProtocol.CurrentVersion once (from 70 on the reviewed baseline), document the new version entry in FrontendWire.cs, and update serialization/round-trip fixtures. Do not introduce a new RPC, a second CaptureStatus operation or backward-compatibility translation layer for this pre-release change.

Existing MainWindow.RefreshSystemStatusAsync / RenderSystemStatus remains the **only** Device summary snapshot capture path. DevicePage's feature-settings RefreshAsync() must not add another CaptureStatus call.

Critical current-code lifecycle gap: RunningAppID/BPM invalidation may capture status while the old presentation is still active; after ReconcileControllerPresentationAsync completes, the UI is not currently guaranteed another invalidation. Therefore arrange a **bounded existing StateInvalidated notification AFTER committed presentation attach/switch/retire/failure** so an already-open Device page converges to the actual result. Reuse InProcessAddonFrontendControl.NotifyStateInvalidated (or its existing upstream seam); do not notify just because CaptureStatus read the snapshot. Do not add polling or another generalized event system.

Also ensure the same existing invalidation path refreshes the badge after meaningful initial acquisition completion, physical-session loss, successful/failed owned recovery, and suspend/resume pause-release where the user-visible badge changes. Prefer a small shared call at existing lifecycle completion points; do not add high-frequency input-frame invalidation. Only signal real, meaningful state changes.

Important disconnect case: MainWindow.RefreshSystemStatusAsync currently logs a Runtime-transport failure but **keeps the last rendered snapshot**. A green-looking "Steam Deck · Active" after Runtime disconnect would be false. Clear/override ONLY the controller badge to Unavailable on an actual status transport failure/disconnect; preserve the last device identity and the existing prerequisite/setup-error policy. A successful fresh capture restores the badge. Do not convert transient request errors into controller teardown, reset UI navigation, or trigger extra retries.

## 6. Testing (software only)

Update/add focused tests:

- tests/SteamInputAddonforClaw.UiTests/DeviceSummaryPresentationTests.cs:
  every badge label, manufacturer alias unchanged, Unsupported/unknown fallback.
- tests/SteamInputAddonforClaw.Tests/Full1902AddonStatusEvaluatorTests.cs and/or focused pure projection tests:
  Xbox360 and SteamDeck positive states; Disabled startup pending; physical source stopped; no active kind; suspend paused; blocked terminal recovery; Enabled MSI authority; Partial/unavailable authority. Specifically ensure Passive + Disabled startup pending is NOT MSI Native.
- tests/SteamInputAddonforClaw.Tests/FrontendContractTests.cs:
  typed mapping/defaults and JSON DTO round trip; no reason-string parsing.
- tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs:
  CaptureStatus round-trips the badge across named pipe and rejects a protocol-version mismatch.
- Relevant UI architecture/source tests if existing tests assume DeviceSupportText/FormatDeviceCompatibility.
- Test a completed presentation reconciliation causing a frontend invalidation **after** the final active-kind change, rather than relying only on early RunningAppID/BPM notification.
- Test that Runtime capture failure/disconnect does not leave a stale Active badge rendered.

Run relevant unit/UI-test projects in CI or locally. Unit/source-level tests must not require an MSI Claw, real VIIPER USB attach, physical PID switching, HidHide mutation, resume, or an actual controller. **Real-device checks are the user's role after merge and are not a Codex implementation requirement or PR blocker.**

## 7. Strict non-goals and safety invariants

- No changes to Full1902 Center M Enable/Disable policy, reboot-bound authority, mandatory elevated Runtime/startup task, PID1901/PID1902 switching, PnP recovery policy, HidHide ownership, rollback/fail-close, VIIPER native lifecycle or actual presentation-switch logic.
- No second authority, persisted "ControllerMode" boolean, duplicated state machine, service, periodic status polling, independent Device-page capture, or defensive lock/epoch/barrier solely for theoretical races.
- No expanded Status/Home/Dashboard page, new Controller feature card, Settings relocation, extra developer telemetry, or UI for supported-but-absent speculative hardware.
- Preserve Settings Required Components and existing Controller LED/Vibration/M1/M2 controls, profiles, Overlay, and existing Device-level settings.
- Restrict this PR to narrow read-only projection, frontend wire/mapping, badge render, lifecycle invalidation, and necessary tests.

## 8. Acceptance criteria

1. Supported and healthy Full1902 device shows **Xbox 360 · Active** or **Steam Deck · Active** based on actual attached presentation; brand/model/board/GPU remain unchanged.
2. Opening the Device page fresh renders the real current output. Switching Steam/BPM while Main UI is already open updates the badge after the actual switch commits, including failed/blocked transitions without false Active labels.
3. Center M Enabled uses **MSI Native**, while Disabled-before-acquisition does not incorrectly use that label.
4. Suspend/resume, physical-device loss/re-enumeration and recovery states never display a known-stale Active badge.
5. Runtime disconnect/status capture failure is visibly Unavailable rather than retaining a stale Active badge.
6. The existing status path remains one read-only frontend RPC, normal business/Device behavior remains unaffected, and focused software tests pass.
7. No hardware test is demanded in the implementing PR.

Deliver one focused PR referencing this work order. Explain which existing owner facts are used for every displayed state, what triggers the post-commit UI invalidation, and what software tests ran.
