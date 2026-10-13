# Work Order — Three Follow-up Windows 11 Tweaks: Widgets, Edge Background, and Startup App Delay

> **Date:** 2026-10-13
> **Product:** Steam Addon for Claw — standalone Full PID1902
> **Delivery:** One focused implementation PR against the latest `main` (after squash-merged PR #779)
> **Scope:** Exactly three additional individually controlled Windows Tweaks cards; no unrelated optimization
> **Coding:** Local Codex
> **Hardware validation:** Performed by the user **after merge**; no hardware/device test may be a CI, review, or merge blocker.

## 1. Decision and scope

Add the following three settings to the **existing Tweaks → Registry Tweaks** group:

1. **Disable Windows Widgets** — prevent Windows Widgets using the documented machine Widgets policy, **only where that policy is supported**.
2. **Disable Edge Background** — suppress both Microsoft Edge Startup Boost and Edge background mode using documented Edge policies.
3. **Disable Startup App Delay** — set the two *user-provided* Explorer startup-delay registry values to zero.

Do **not** add these to PR #779. PR #779 has already been squash-merged and implements **Faster Menus**. This work order describes a subsequent PR. Leave Faster Menus, all other existing Tweaks settings, and their lifecycle untouched.

The purpose is optional reduction of **some** background launches, not a guaranteed reduction of idle RAM or Windows kernel boot time. Do not display invented MB/ms savings or claim that Windows Widgets/Edge processes are immediately stopped.

### Sources and research limits

Authoritative behavior and policy paths:

- Widgets policy: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests — `AllowNewsAndInterests`, `HKLM\SOFTWARE\Policies\Microsoft\Dsh`, `REG_DWORD`, `0 = not allowed`, `1 = allowed`. The published applicability covers Windows 11 **Pro, Enterprise, Education and supported IoT Enterprise** editions; **Windows 11 Home is not listed**. Do not claim policy support on Home by merely observing the registry value.
- Edge Startup Boost policy: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/startupboostenabled — mandatory-policy `HKLM\SOFTWARE\Policies\Microsoft\Edge\StartupBoostEnabled`.
- Edge background mode policy: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/backgroundmodeenabled — mandatory-policy `HKLM\SOFTWARE\Policies\Microsoft\Edge\BackgroundModeEnabled`.
- Windows optimization benchmarking: https://github.com/Raphire/Win11Debloat/wiki/Features. **Important:** contemporary Win11Debloat's Widgets option may remove app packages instead of relying only on registry policy. **Do not emulate uninstall/removal**; it is not reversible by our single toggle and is out of scope. A report of a permission/UCPD write failure also exists: https://github.com/memstechtips/Winhance/issues/780. Do not disable UCPD or security mechanisms to force a registry write.
- **User-provided** `Disable_StartupDelayInMSec.reg` contains exactly:
  
  ```reg
  [HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Serialize]
  "StartupDelayInMSec"=dword:00000000
  "WaitForIdleState"=dword:00000000
  ```
  
  This is the input specification, **not a Microsoft-supported performance guarantee**. The impact of `WaitForIdleState` and actual improvement on current Windows 11 builds are not guaranteed by a documented platform contract.

**Explicitly excluded:** The other supplied `Disable_AutoStartDelay.reg` writes `HKLM\SYSTEM\CurrentControlSet\Control\AutoStartDelay=0` and affects delayed automatic services; it is **not** approved for implementation here.

## 2. Required project/code baseline

Read before implementation:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/2026-10-13_WINDOWS_11_TWEAKS_TAB_WORK_ORDER.md`
- `docs/work-order/2026-10-13_FASTER_MENUS_COMBINED_TOGGLE_WORK_ORDER.md`
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweakRegistry.cs`
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksOperatingSystem.cs`
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksRuntime.cs`
- `src/SteamInputAddonforClaw.Contracts/Frontend/WindowsTweaksFrontendContracts.cs`
- `src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml` and `.xaml.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`
- Focused Core/transport/UI Tweaks tests.

**Known current structure:**

- The Runtime is the existing elevated, authoritative process; the Main UI is a disposable frontend.
- `WindowsTweakRegistry.Read/Write/DeleteValue` already supports typed exact-value operations and distinguishes key/value absence.
- `WindowsTweaksOperatingSystem.Capture/Set` dispatches by `FrontendWindowsTweakId`.
- `WindowsTweaksRuntime` captures and rereads settings, preserves failures, supports `Mixed` for Faster Menus, and protects unsupported IDs.
- `FrontendWindowsTweakCatalog.All` and `FrontendWindowsTweaksSnapshot.Unavailable` currently list five entries.
- `WindowsTweaksPage` has **five fixed XAML cards** and fixed `GetControls/GetId` mappings; visibility and state are Runtime-snapshot-driven.
- `FrontendTransportProtocol.CurrentVersion` is **80** following PR #779, subject to any intervening main changes.

No CTW integration. No changes to PID1902/PID1901 routing, HidHide, VIIPER, startup tasks, controller watchdog, Sleep/Resume handling, or elevated Runtime ownership.

## 3. User-visible contract and ordering

Inside **Registry Tweaks**, retain all existing cards in their established order and append these three cards:

1. Disable Windows Widgets
2. Disable Edge Background
3. Disable Startup App Delay

(The **Gaming Performance** section remains unchanged. Total cards become **eight**.)

Use the existing `SettingsCard + ToggleSwitch + compact state/detail text` pattern. No new tab, Apply button, bulk optimizer, custom card factory, polling, per-toggle confirmation, reboot banner, or automatic Windows/Explorer restart.

Suggested short descriptions:

- **Disable Windows Widgets** — `Block the Windows Widgets experience where supported by Windows policy.`
- **Disable Edge Background** — `Disable Edge Startup Boost and background mode; existing processes may continue until closed.`
- **Disable Startup App Delay** — `Remove configured Explorer startup-app launch delays; actual startup gains may vary.`

Each **ON** means the *disable/optimization* action is configured. Each **OFF** removes only this tweak's exact configured restrictions and returns to Windows/app-defined behavior. It does **not** guarantee the feature becomes active (for example, Edge user preferences may remain off).

Preserve the existing once-per-local-day safety notice. If updating its copy, add one concise line explaining policy restrictions and that startup launches may cluster; do **not** add a second notification or reset the accepted date.

## 4. Exact registry contracts

Use **`Microsoft.Win32.Registry` through the existing `WindowsTweakRegistry` helper**, never `reg.exe`, PowerShell, Task Scheduler, or an external service.

| Toggle | Hive/path | Value name(s) and type | ON | OFF |
| --- | --- | --- | --- | --- |
| Disable Windows Widgets | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` | `AllowNewsAndInterests` / `REG_DWORD` | Write `0` | Delete the exact value; do not delete `Dsh` |
| Disable Edge Background | `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `StartupBoostEnabled` and `BackgroundModeEnabled` / both `REG_DWORD` | Write **both** `0` | Delete **both** exact values; do not delete `Edge` |
| Disable Startup App Delay | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Serialize` | `StartupDelayInMSec` and `WaitForIdleState` / both `REG_DWORD` | Write **both** `0` | Delete **both** exact values; do not delete `Serialize` |

Do not write policy value `1` to represent OFF. The **not-configured** state is represented by absence. Do not delete siblings, policy subtrees, user taskbar preferences, Edge settings, or app packages.

### 4.1 Widgets edition/support behavior — important

- The **documented** machine policy applies to Windows 11 Pro/Enterprise/Education/eligible IoT Enterprise editions, **not Windows 11 Home**. Use a small existing OS/SKU capability check (or one narrow implementation-local check if needed). Show **Unsupported** with a meaningful detail and disable the toggle when outside documented scope; do not write an unverified policy to Home and then claim success because the DWORD reads back.
- Prefer a reliable Windows edition identifier such as the OS `EditionID` (or a narrower existing product OS identification API). Do not infer an edition from the human-readable ProductName string. Keep checking lightweight, no WMI polling or new SKU manager.
- A missing `Dsh` key is ordinary Windows *unconfigured*, not a failure.
- On supported editions, the explicit policy `AllowNewsAndInterests=0` represents **configured** Widgets blocking. A policy readback proves configuration, **not** an immediate decrease in background-process RAM or immediate removal of an already-open Widgets surface.
- If access is denied, the policy is reapplied by Windows, or Windows rejects the change, expose the existing Managed/Unknown/failure behavior as supported by evidence. **Never** disable UCPD, remove a Widgets package, restart Explorer, kill a system process, or attempt a permission/ownership bypass.

### 4.2 Edge policy behavior

- These are machine-level **mandatory Edge policies**, distinct from Edge's optional `Recommended` policy keys or per-profile settings. Use exactly the documented path above.
- ON requires **both** DWORD values `0`; OFF requires **both absent**.
- Apply a single toggle to both values; on partial failure, reread both and display actual Mixed/error rather than claiming success. A later user toggle can retry; **do not** add generic multi-key transactions or persistent backup files solely for this feature.
- Detect an absent Edge installation only if a straightforward reliable existing check is available. If not, do not invent installation state; leave the setting as a policy configuration control with honest copy. Do not auto-install or launch Edge.
- Microsoft supports policy refresh, but an already-running Edge process need not disappear immediately. Do **not** kill Edge, forcibly close tabs, or claim that WebView2/Steam are disabled.

### 4.3 Explorer startup-app delay behavior

- This is **not** service `AutoStartDelay` and does not alter Full1902's scheduled Runtime startup.
- ON requires two DWORDs `0`; OFF requires both values **absent**. ON and OFF each touch both names within the single `Serialize` key.
- This is an optional registry tweak based on the **uploaded REG file**, not a guaranteed boot-time API. Do not promise faster kernel boot, PID1902 availability, or specific RAM savings.
- The configured state may be read immediately, but startup-launch scheduling only matters on future logins. **Do not** force sign-out, restart Explorer, or reboot to make the effect observable. Provide a concise static description if needed.
- Never touch other Explorer `Serialize` values or delete its parent key.

## 5. Reading state, Mixed, managed and failure behavior

**Never use a stored app preference as the source of truth.** Read the exact OS registry values each time the page opens and after any write.

For each new control:

- **ON:** All required values exist, are `REG_DWORD`, and equal `0`.
- **OFF:** All required values are absent (key may also be absent).
- **Mixed / Custom:** Any other *successfully read* combination, including one absent and one `0`, `1` values, or a wrong value kind. The card must not be falsely shown as verified ON or OFF. Use the existing `Mixed` state and make the optimization toggle actionable unless **a real external policy/permission restriction is established**.
- **Unknown / Unavailable:** A registry read actually failed, or required platform support cannot be determined. Do not turn exceptions into an assumed OFF.
- **Unsupported:** Windows 11/edition/platform does not support the requested setting's verified contract (particularly Widgets on Windows 11 Home).
- **Managed:** Proven GPO/MDM/other authoritative control prevents changes; do not override or fight it.

**Be precise about policy ownership:** A registry value under `SOFTWARE\Policies` alone cannot prove that Group Policy/MDM (versus a local administrator) owns it; and an exact `0` cannot tell whether this app originally wrote it. **Do not invent a provenance marker, local backup database, or an elaborate GPO inspector**, and do not claim a reliable ownership distinction without an actual authoritative signal. If an external policy/management signal is already available, respect it. Otherwise only modify the **explicitly requested, named values** on user action; on failed writes/policy reapplication surface error/Managed state, never disable OS protections. This is a local administrator Windows-tweak control, not an enterprise policy manager.

### Reuse the existing Mixed implementation correctly

PR #779 currently has an `IsMixed` observation, but `WindowsTweaksStateMapper.Map` only honors it when the tweak ID equals `InstantContextMenus`. Extend that explicit ID gate to the **three new IDs** (or minimally generalize to observations that intentionally set `IsMixed`, while leaving unrelated controls' semantics unchanged). Do not make other existing controls suddenly actionable.

Currently `WindowsTweaksPage.FormatState` has the **Faster Menus-specific** label:

```csharp
FrontendWindowsTweakState.Mixed =>
    "Mixed — turn On to apply both Faster Menus values"
```

This would give misleading guidance on Edge/Widgets/Startup cards. Replace it with a generic accurate line (e.g. `"Mixed — turn On to apply the tweak's values"`), or a small ID-specific text mapping, and update the Faster Menus UI test. Preserve Mixed `CanChange=true` and `WindowsTweaksRuntime.SetAsync`'s rule to **write even when the display's configured bool is false**, so a Mixed → ON request normalizes **all required values**. The existing single ToggleSwitch will display Mixed visually as OFF; the user may turn ON to normalize it, and then OFF to remove the values. Do not add a third toggle position.

The existing Runtime must **recapture** after write, including partial write failure, and return success only for an exact verified target state. Do not add retry loops/state machines or speculative locks. It is valid for a subsequent user action to reconcile a partial write.

### Implementation sketch (illustrative, not another engine)

```csharp
// Existing helper returns: KeyExists, ValueExists, Value, Kind.
static bool IsZeroDword(WindowsTweakRegistryValueRead r) =>
    r.ValueExists && r.Kind == RegistryValueKind.DWord &&
    r.Value is int dword && dword == 0;

// Example two-value capture:
// if (IsZeroDword(a) && IsZeroDword(b)) => enabled observation
// else if (!a.ValueExists && !b.ValueExists) => disabled observation
// else => IsMixed observation; keep it actionable.

// ON:
WindowsTweakRegistry.Write(Registry.LocalMachine, edgePath,
    "StartupBoostEnabled", 0, RegistryValueKind.DWord);
WindowsTweakRegistry.Write(Registry.LocalMachine, edgePath,
    "BackgroundModeEnabled", 0, RegistryValueKind.DWord);

// OFF:
WindowsTweakRegistry.DeleteValue(Registry.LocalMachine, edgePath, "StartupBoostEnabled");
WindowsTweakRegistry.DeleteValue(Registry.LocalMachine, edgePath, "BackgroundModeEnabled");
```

The Widgets one-value setting is also Mixed when an explicitly present DWORD `1`, another integer, or a wrong type exists; its OFF state is **absence**, not `1`.

## 6. Code touchpoints and acceptance

Keep edits local to the already owned Tweaks feature:

1. **Frontend contracts:** Append `DisableWindowsWidgets`, `DisableEdgeBackground`, `DisableStartupAppDelay` to `FrontendWindowsTweakId` (do **not** reorder existing enum values); add three catalog definitions and the three `Unavailable` placeholders.
2. **OS logic:** Add only corresponding capture/set cases and small relevant helpers/constants in `WindowsTweaksOperatingSystem.cs`; use existing `WindowsTweakRegistry` typed methods. Keep all existing HVCI/VMP/Faster Menus/Classic Menu/Power APIs unchanged.
3. **Runtime:** Reuse per-setting mutation, readback, optional model filtering, `Mixed` and errors. No new Runtime owner or persistent preference.
4. **Frontend UI:** Add three normal `SettingsCard` entries under Registry Tweaks, wire fixed `GetControls` and `GetId`, ensure the all-eight catalog/snapshot loop is valid. Respect `CanChange`, `Unsupported`, `Managed`, `Unknown`, and `Mixed`. Preserve the existing visibility/safety-notice gating.
5. **Transport:** Follow repository protocol policy. New enum/catalog wire members change the contract; bump `FrontendTransportProtocol.CurrentVersion` from **80 to 81** if still current, add a version comment, update the focused version assertions/fixtures. Do not change the independent Overlay transport protocol.
6. **Tests:** Update fixed five-item catalog/UI order expectations to eight, plus tests below.

### Focused automated test cases

- **Widgets:** absent → OFF, exact DWORD `0` → ON, DWORD `1` and wrong type → Mixed; Home/non-applicable edition → Unsupported, no write; denied write → failure + actual readback, not false success.
- **Edge:** both absent → OFF, both DWORD `0` → ON, one missing/one `0` → Mixed; `1`/wrong kind → Mixed; ON/OFF apply/delete exactly both values; partial second-value failure produces a Mixed snapshot and failed result.
- **Startup:** both absent → OFF, both DWORD `0` → ON, one absent or nonzero/wrong type → Mixed; OFF deletes exact two values and preserves unrelated `Serialize` siblings.
- **State mapping:** the three new IDs plus Faster Menus support Mixed without extending this semantics to Memory Integrity/Virtual Machine Platform/Disable Password on Wake.
- **UI:** eight and only eight cards, correct section headings/order, Mixed text is not Faster Menus-specific for the new items, unsupported controls are inert, no optimistic state.
- **Transport:** unique appended numeric enum IDs, protocol handshake version, roundtrip of the added tweak IDs/snapshots.
- **Regression:** preexisting five Tweaks, daily warning persistence, and existing Full1902 controller/transport tests pass.

Avoid running tests against real HKLM policy keys or editing the test machine's Explorer preferences. Use pure mapping tests/fake `IWindowsTweaksOperatingSystem` and disposable HKCU test registry keys as appropriate. Actual Widgets/Edge policy effect, Windows edition behavior, login-time performance, and hardware-specific operation are **post-merge validation by the user**, not blockers for local Codex or CI.

## 7. Strict out-of-scope

- **No** `AutoStartDelay`, `SvcHostSplitThresholdInKB`, SysMain/Prefetch/Pagefile/Memory Compression, services, scheduled task changes, Fast Startup or sleep/hibernate optimization.
- **No** Widgets app package removal, Edge process termination, WebView2 removal, Edge browser installation, taskbar button toggles, Start menu personalization, GPO refresh commands, UCPD changes, or policy-ownership bypass.
- **No** CTW, multi-user/RDP/Fast User Switching, generalized registry tweak framework, undo-history/registry exports, giant settings service, background polling, or speculative timing/race machinery.
- **No** numeric performance improvement claims without measurements.
- **No** automatic restart, shell restart, sign-out or reboot.

## 8. Definition of done

The existing **Registry Tweaks** group contains exactly **three additional individually configurable settings**, backed by current OS state and minimal typed registry operations. ON writes the precise approved values; OFF deletes only those named values; Mixed and failures remain accurate/actionable; unsupported Widgets editions are handled honestly; and all Full1902/controller/power lifecycle contracts remain unaffected. Code and ordinary automated tests provide merge readiness; the user validates Windows/handheld behavior after merge.
