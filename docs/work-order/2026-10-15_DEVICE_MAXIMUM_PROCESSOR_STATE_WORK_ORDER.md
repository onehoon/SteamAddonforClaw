# Work Order — Device Maximum Processor State (99% / 100%)

**Date:** 2026-10-15  
**Repository:** `onehoon/SteamAddonforClaw`  
**Owner:** Local Codex (implementation), product owner (post-merge hardware verification)  
**Baseline:** Latest `main` at implementation time; reread HEAD before editing.  
**Delivery:** One focused feature PR. This file is a work order only; do not implement in the work-order commit.  
**Product:** Standalone Full PID1902, Windows 11, one Windows user and one interactive session.

## 0. Locked product decisions

1. Add an **independent SettingsExpander to the Main App Device tab**, immediately **below CPU Boost** and above Windows Power Mode.
2. The setting is **Maximum Processor State** in `en-US` and **최대 프로세서 상태** in `ko-KR`, with these exact strings in the respective **Main UI RESW** files.
3. Render the power-source child headers as **AC** and **DC** in **both** languages, matching the existing Device TDP / CPU Boost / Power Mode conventions. Do not use "Plugged in" / "On battery" for the new controls.
4. Only **99% and 100%** are selectable, with **one dropdown each for AC and DC**. No slider, arbitrary percentages, or CPU utilization percentage control.
5. Provide a separate **Enabled / Override** toggle for this setting, independent of CPU Boost, TDP, and Windows Power Mode. Default state for **new users** is **OFF**; first launch must never overwrite their existing Windows maximum-processor settings.
6. Use **Segoe Fluent Icons `Dial4`, U+F149**, via the existing WinUI 3 `FontIcon` control, e.g. `<FontIcon Glyph="&#xF149;" />`. There is no new font asset, bitmap, or dependency. The glyph is not used by the current Device / Steam / Xbox / Controller page icon lists inspected for this work order.
7. **Main App Device only:** NO new Overlay / Quick Settings row or mutation, NO QAM feature, NO Steam game profile setting, NO Xbox game profile setting, and no placeholder architecture for those future integrations.
8. No CTW integration. Never touch PID1901/PID1902, physical controller ownership, HidHide, VIIPER, USB/IP, routing, or controller Sleep/Hibernate/Resume logic in this feature.
9. Runtime is the only Windows power-policy owner; Main UI remains a frontend. Follow the current elevated Runtime / named-pipe frontend architecture.
10. Physical handheld validation belongs to the **user after merge**. Do not require hardware testing by local Codex, and do not gate PR approval or merge on hardware evidence.

## 1. Required reading / authority

Read the applicable Full1902 documentation before implementation; use newer policy over conflicting historical text:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/2026-10-14_MAIN_APP_DEVICE_TAB_RESW_PR2_WORK_ORDER.md` for the existing RESW/PRI conventions (its localization-only scope does not constrain this new feature).
- `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md` and `docs/shared-frontend/SF_V2_01_DEVICE_QUICK_SETTINGS_SHARED_AGGREGATE_WORK_ORDER.md` for shared Device capture behavior; **do not add this feature to the Overlay projection**.

Relevant current source files (inspect again at HEAD):

- `src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml` and `DevicePage.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw`
- `src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw`
- `src/SteamInputAddonforClaw.UI/MainUiStrings.cs`
- `src/SteamInputAddonforClaw/Profiles/DeviceSettings.cs`
- `src/SteamInputAddonforClaw/Profiles/ProfileStore.cs`
- `src/SteamInputAddonforClaw/Profiles/Performance/CpuBoostRuntime.cs`
- `src/SteamInputAddonforClaw/Profiles/Performance/WindowsCpuBoostPowerPolicy.cs`
- `src/SteamInputAddonforClaw/Profiles/Performance/ICpuBoostPowerPolicy.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
- Existing runtime/frontend/transport/RESW tests and `scripts/verify-ui-localization-pri.ps1`.

Microsoft references:

- Maximum processor performance state / `PROCTHROTTLEMAX`: https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-maxperformance
- Independent Boost mode / `PERFBOOSTMODE`: https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode
- Segoe Fluent Icons `Dial4` (`F149`): https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font

## 2. Verified baseline and narrow implementation direction

The current Device page has this order:

```text
MSI Center M
TDP Control
CPU Boost
Windows Power Mode
Battery Charge Limit
```

Insert only the new expander between CPU Boost and Windows Power Mode. Existing entries use `ctcontrols:SettingsExpander`, a `ToggleSwitch` in the expander header, and `SettingsCard` child rows for AC and DC. Reuse that layout.

The existing `DevicePage.RefreshAsync()` calls a single `CaptureDeviceQuickSettingsAsync()` and renders its CPU Boost, TDP, Power Mode, and Battery Charge Limit children. `FrontendDeviceQuickSettingsSnapshot` is read-only and already used by the Overlay to build its **existing** rows. Add this feature as an additional read-only child snapshot **without adding new Overlay rows, mutations, or navigation**. Retain isolation of per-child capture failures; one failed new-policy read must not hide or fail the four existing children.

`AddonProcessHost` constructs one `CpuBoostRuntime` with `ProfileStore` / `ProfileMutationGate`, passes that same owner to `InProcessAddonFrontendControl`, reconciles it at startup, and also reconciles CPU Boost after resume. Follow this *existing owner/wiring pattern*, but the new maximum-state Runtime must be **Device-only**, never resolved against per-game profiles. Do not add a generic power-setting framework or extra controller-facing lifecycle managers.

The current Main UI RESW values for Device TDP, CPU Boost and Power Mode card headers are already exactly `AC` / `DC` in both `en-US` and `ko-KR`. **Keep them unchanged.** New feature strings are additive.

## 3. Windows power policy / exact setting

Use the Windows **maximum processor performance state** power-policy value:

```text
Subgroup:  SUB_PROCESSOR
GUID:      54533251-82be-4824-96c1-47b60b740d00

Setting:   PROCTHROTTLEMAX
GUID:      bc5038f7-23e0-4960-96da-33abaf5935ec

Allowed by this product: 99, 100 (percentage)
```

These values are percentages of a *processor performance state*, **not** CPU utilization caps. This setting is independent of `PERFBOOSTMODE`; 99% must **not** be described as an unconditional Turbo Boost-disable guarantee or as equivalent to CPU Boost Disabled.

Use existing `PowrProf.dll` interop conventions (not launching `powercfg.exe`, PowerShell, WMI, registry hacks, or changing the power setting's visibility attributes):

- `PowerGetActiveScheme`; free its returned allocation via `LocalFree`.
- `PowerReadACValueIndex` / `PowerReadDCValueIndex` for the *actual current* AC and DC raw percentages, including values outside 99/100.
- `PowerWriteACValueIndex` / `PowerWriteDCValueIndex` only for the requested side(s).
- `PowerSetActiveScheme` for the same active scheme **after a successful requested write** to make changes effective (same pattern as `WindowsCpuBoostPowerPolicy`); a failed activation is an **apply failure**, not a success.
- Check and report each Win32 result; do not silently normalize failed/unknown current reads to 100%, and never write a value outside 99 or 100 from an untrusted IPC request.
- Keep AC-only updates from writing DC and DC-only updates from writing AC. When turning the feature ON or doing startup/resume reconciliation, apply the configured complete AC/DC pair. Partial success must be reported as partial/failed as appropriate; do not announce success when a requested write or activation failed.
- Use **only `PROCTHROTTLEMAX`** for this bounded feature. Microsoft also documents `PROCTHROTTLEMAX1` for processor performance class 1; do **not** silently mutate that separate Windows setting or invent P-core/E-core UI or behavior. Actual Claw performance effects belong to user hardware verification.

Windows might already be configured to **85%, 90%, or any 0–100% raw setting**. Reading this must remain possible; it does not expand the new UI dropdown or imply permission to rewrite the value.

## 4. Device persistence and first-run policy

Add an **additive, typed, Device-only** section under `DeviceSettings.Performance` (suggested member `MaximumProcessorState`) using the current `ProfileStore` JSON serialization/extension-data contract. Do not change the overall schema version merely for an optional additive field unless current validation/serializer code actually requires it.

Recommended storage shape:

```json
{
  "device": {
    "performance": {
      "maximumProcessorState": {
        "enabled": false,
        "ac": 100,
        "dc": 100
      }
    }
  }
}
```

Storage example, **not** a requirement to write this block automatically at startup. Keep a missing section legal and semantically equivalent to **Disabled with provisional 100% / 100% choices**; do not persist or apply anything merely to construct the Device view.

**Required first-run behavior:**

1. Missing / legacy `MaximumProcessorState` section → **OFF**, show dropdown choices `100%` and `100%` as the default *prospective selection*, **but perform zero writes** to Windows and no forced 99/100 normalization of its current values. Opening the Device tab / capturing the snapshot also performs zero persistence and power-policy writes.
2. Explicit user enables the feature for the first time → persist an enabled complete 100/100 desired pair and attempt to apply both. This action is an **explicit request**, not a silent first-launch write. If the user wants 99%, they can change the relevant dropdown after enabling.
3. While enabled, setting AC or DC to 99/100 persists the new desired value and applies only the selected side.
4. Explicit user disables the feature → persist `enabled=false`, **perform no Windows writes and no restoration**. The current Windows settings remain as last applied or independently changed, consistent with the existing CPU Boost Enabled-toggle policy.
5. Re-enable → apply the saved complete AC/DC pair, not an ad-hoc Windows-derived replacement.
6. Runtime startup, applicable resume recovery and Device-only reconcile → apply only when `enabled=true`; otherwise **leave Windows untouched**. This feature stays global during Steam/Xbox game launch/exit and does not inherit, shadow, or modify any per-game profile.
7. Persistence failure → do not write Windows. Windows write/activation failure after a successful save → keep the desired values durable, report `ApplyFailed`, and allow the existing normal future reconcile path to retry; no rollback to the prior value.
8. Avoid introducing extra timers, permanent polling, profile authorities, epochs, or a broad generic power-management abstraction. Reuse `ProfileMutationGate` so updates to `profiles.json` do not clobber independent CPU Boost/TDP/Power Mode data.

**Important:** The new Enabled toggle controls whether the **Addon applies/manages the maximum-state policy**, NOT whether Windows' maximum-state limit is active. Switching OFF **does not reset it to 100%**. The RESW description must not claim that OFF removes or reverts the limit.

If a persisted value is malformed, e.g. `ac=50`, `dc=150` or missing one side, do not apply it or silently coerce it to 99/100. Handle consistently with the existing safe-load contract: preserve unrelated profile data, surface an unavailable/invalid state and prevent unsafe writes. Do not reject an entire otherwise valid legacy profile merely because the *new optional field is absent*.

## 5. UI details

```text
CPU Boost                      [existing]
Maximum Processor State        [new, Dial4 F149]     [toggle: OFF initially]
   AC                                                [100% v]
   DC                                                [100% v]
Windows Power Mode             [existing]
```

Use `ctcontrols:SettingsExpander` with `x:Name="MaximumProcessorStateExpander"`, `IsExpanded="False"` initially, a header `ToggleSwitch` (same visual conventions as CPU Boost), and two `SettingsCard` child rows containing `ComboBox` options **100% and 99%** in that order. Prefer normal combo items and existing XAML conventions; no slider, extra "apply" button, extra `ContentDialog`, custom templating or icon packages.

Example icon:

```xml
<ctcontrols:SettingsExpander.HeaderIcon>
    <FontIcon Glyph="&#xF149;" />
</ctcontrols:SettingsExpander.HeaderIcon>
```

- Keep the icon style consistent with the existing `FontIcon Glyph="&#xEEA1;"` for CPU Boost; `F149` is the Segoe Fluent Icons `Dial4` glyph (not a separate "font icon library").
- The feature Enabled toggle is independent. Disable value editors while the feature is OFF, as current CPU Boost does.
- Use a dedicated suppression flag for programmatic updates **only if necessary** to prevent combo/toggle callbacks caused by rendering. Avoid unnecessary debouncing for discrete selections.
- Render the persisted desired choice separately from raw Windows current values; never display an unsupported current percentage as a fabricated 99% or 100% desired selection.
- A failed/unavailable policy read or persistence failure should be visible through an independent new `InfoBar`, without disturbing CPU Boost/TDP/Power Mode info or controls.
- Preserve existing TDP slider draft/debounce, CPU Boost and Power Mode selection behavior, and battery-charge-limit behavior.

## 6. RESW / localization contract

Add all new visible UI-owned text in BOTH of these **existing** files (no new localization subsystem):

- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw`
- `src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw`

**Required exact key/value pairs:**

| RESW key | en-US | ko-KR |
|---|---|---|
| `Device_MaximumProcessorStateExpander.Header` | `Maximum Processor State` | `최대 프로세서 상태` |
| `Device_MaximumProcessorStateAcCard.Header` | `AC` | `AC` |
| `Device_MaximumProcessorStateDcCard.Header` | `DC` | `DC` |

Suggested descriptive copy to register through `Device_MaximumProcessorStateExpander.Description` in BOTH RESWs (may be refined as long as truthful):

- `en-US`: `Limit the Windows maximum processor performance state to 99% or 100%. Turning this off stops applying the setting but does not restore the previous value.`
- `ko-KR`: `Windows 최대 프로세서 성능 상태를 99% 또는 100%로 설정합니다. 끄면 설정 적용만 중지하며 이전 값으로 복원하지 않습니다.`

Use matching element `x:Uid` values with English fallback XAML properties, and the existing `MainUiStrings.Get(...)` for code-behind errors / status / options that need localization. Both languages must have the same complete new key set, nonempty values, and compatible format placeholders. The user explicitly requested the Korean header and the identical AC/DC labels; unlike the historical October 14 RESW extraction work order, this feature must **not** replace that required Korean header with English placeholder text.

Suggested additional message keys (choose minimal actual messages needed, but localize all UI-authored copy):

- `Device_MaximumProcessorState_SettingsUnavailable`
- `Device_MaximumProcessorState_SettingsUnwritable`
- `Device_MaximumProcessorState_UpdateFailed`
- `Device_MaximumProcessorState_RuntimeInterrupted`
- `Device_MaximumProcessorState_LastChangeFailed` (format placeholder if shown).

Keep backend `FailureMessage` untouched, as with current Device UI; do not translate a runtime string by guessing its text.

The **existing** TDP / CPU Boost / Power Mode AC/DC headers already read `AC` / `DC` in both language RESWs on the reviewed `main`. **Do not rewrite them or change unrelated translations.** No translation service, language selector, or new PRI build route.

Verify keys are present in the source RESWs and the actual **published** Main UI PRI using the current `scripts/verify-ui-localization-pri.ps1` workflow.

## 7. Frontend / IPC integration without Overlay exposure

Use a tiny typed frontend projection, e.g. `FrontendMaximumProcessorStateSnapshot` with:

- Current AC/DC raw percentages or Unknown/Unavailable (actual Windows read)
- Saved desired AC/DC 99/100 choices, with explicit no-config OFF semantics
- `Enabled`, `PersistenceWritable`, and `LastFailure`.

Return a typed mutation result that differentiates `Succeeded`, `PersistenceFailed`, `ApplyFailed` and includes the refreshed snapshot, like CPU Boost. Ensure UI can show an actual failure rather than merely snap back.

Extend the existing `FrontendDeviceQuickSettingsSnapshot` as an **additional optional/default-safe child**; keep its existing four child snapshots and consumers intact. Add a narrowly scoped focused `CaptureMaximumProcessorStateAsync()` where it simplifies aggregate composition (matching `CaptureCpuBoostAsync`); no new full-page capture route is needed. Add the three **Device-only** mutation operations:

```text
SetDeviceMaximumProcessorStateEnabledAsync(bool enabled)
SetDeviceMaximumProcessorStateAcAsync(int percent)
SetDeviceMaximumProcessorStateDcAsync(int percent)
```

Use narrow typed request DTOs and exactly corresponding `FrontendRpcMethod` entries, `NamedPipeAddonFrontendClient` calls and `NamedPipeAddonFrontendServer` dispatch. Validation **must occur at the Runtime boundary**: only 99/100; neither named-pipe clients nor UI are privileged to submit 0..98.

Advance `FrontendTransportProtocol.CurrentVersion` **once** from the value found on implementation HEAD (reviewed as 83 on 2026-10-15; do NOT hardcode 84 if main has changed) and add its normal history comment. Keep existing transport error and cancellation conventions.

**Overlay exclusion is strict:** The shared aggregate may carry the additional read-only child because the Device Main UI already uses it; **do not** modify `QuickSettingsPresentation.BuildDevice()`, `QuickSettingsRowId`, `QuickSettingsMutationAdapter`, `OverlayPage`, Overlay XAML, Overlay IPC, or Steam QAM. No new user-visible row for this setting outside the Device page. Changes to aggregate serialization needed for the Main UI may be tested, but do not add Overlay behavior.

## 8. Runtime integration / lifecycle

- Instantiate the one new feature-specific Runtime in `AddonProcessHost` using the existing `ProfileStore` and shared `ProfileMutationGate`; inject the SAME instance into `InProcessAddonFrontendControl`.
- Integrate only into existing startup and appropriate resume/performance reconciliation points so an **enabled** saved setting is applied after Windows power state changes. If the feature is disabled or uninitialized, those paths cause **zero** Windows writes.
- Do **not** add the new setting to `GameProfile`, `XboxGameProfile`, `ResolvedActiveProfile`, or `ReconcileEffectiveGameProfile` as a per-game override. If a normal lifecycle method reconciles Device-owned performance settings, invoking the feature's **global-only** reconcile there is permissible only when required to keep it active; do not couple its value to the active game.
- Use current scheme at each operation, no permanent scheme GUID caching; when a third-party application switches power schemes, don't invent a new scheme watcher just for this setting. Follow existing normal process-start/resume policy.
- Never spawn a second process, Windows service, periodic poller, controller re-enumeration operation, or another state authority.

## 9. Implementation/testing examples and acceptance checks

These are **automated/source-level** requirements for local Codex, not on-device testing.

**Policy example (adapt to existing style and names):**

```csharp
private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
private static readonly Guid MaximumProcessorState = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

private static bool IsAllowedPercent(int percent) => percent is 99 or 100;

// Validate BEFORE ProfileStore.Save and BEFORE PowerWrite*ValueIndex.
if (!IsAllowedPercent(requestedPercent))
    return MaximumProcessorStateMutationResult.InvalidTarget(...);
```

If adding an `InvalidTarget` typed outcome better expresses a malformed raw IPC request, include it consistently in Runtime and frontend; do not map an invalid value to 99 or 100. The example illustrates validation, not a mandate to add extra wrappers.

**Minimum unit / source / transport regression coverage:**

1. A legacy `profiles.json` without the new optional member loads successfully; opening/capturing first-run Device state performs zero writes and leaves existing Windows AC/DC raw percentages unchanged, including a nonstandard value (e.g. AC 85, DC 90).
2. The first user ON request saves and attempts 100/100; OFF saves Enabled=false but does not write 100, restore a prior value, or erase desired 99/100 selections. Subsequent re-enable reapplies stored choices.
3. AC=99 affects only AC; DC=99 affects only DC. 99→100 applies exactly 100 to the selected side. An attempted `98`, `0`, `150` or malformed stored value never reaches the Windows writer.
4. Persistence failure causes no Windows write; power-policy write failure or `PowerSetActiveScheme` failure is reported (not false-success), desired state remains saved, existing child snapshots remain available.
5. Startup/resume when ON reconciles saved pair; OFF/unconfigured does no write. Steam/Xbox game activation does not alter this Device-only setting or any existing per-game policy.
6. Single aggregate Device capture returns existing child snapshots even when the new child fails; Main UI named-pipe mutation round-trip and wire-version validation work.
7. Device UI has its own icon `F149`, expander order CPU Boost → Maximum Processor State → Windows Power Mode, independent toggle, AC/DC dropdowns 100% and 99%, and failure InfoBar. Verify no Overlay / Quick Settings or game-profile additions.
8. Both RESWs have all new keys and the exact localized header + AC/DC values, with valid published PRI and no localization regression for existing Device text.
9. Existing `dotnet build`, relevant Runtime/frontend/UI tests, and published artifact/PRI verification continue to pass. Keep any new tests narrow; no synthetic pathological concurrency or unused generalized abstraction.

**Post-merge product-owner hardware observations** (not Codex requirements / not merge blockers): compare four Boost ON/OFF × Max State 99/100 combinations and Windows current-value readback under AC/DC; record observed clocks/thermal/power. Do not promise Turbo is always disabled at 99% without measured hardware evidence.

## 10. Delivery constraints

Submit **one focused implementation PR** based on the current work order. Do not combine this with general UI cleanup, CPU Boost mode simplification, changing existing RESW translations, Windows Tweaks redesign, or controller-lifecycle changes. The work order should be implemented with one clear Runtime owner and the existing frontend protocol pattern.

The PR description should identify files affected, describe first-run/no-write and OFF/no-restore behavior, list build/test/PRI verification commands with actual results, and explicitly state **physical MSI Claw verification is the user's post-merge responsibility**.
