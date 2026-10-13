# Work Order — Combine Menu Animation and MenuShowDelay into One “Faster Menus” Toggle

> **Date:** 2026-10-13  
> **Product:** Steam Addon for Claw — standalone Full PID1902  
> **Delivery:** One focused local-Codex PR  
> **Scope:** Only change the behavior and label of the existing `Instant Context Menus` tweak  
> **Physical-device validation:** User responsibility **after merge**, never a coding/CI/PR blocker

## 1. Goal

Replace the existing **Instant Context Menus** user-facing card with **Faster Menus**. Retain **one existing toggle**, but control both:

1. Windows menu fade/slide animation through `SystemParametersInfoW(SPI_GET/SETMENUANIMATION)`.
2. The delay before a submenu appears through `SystemParametersInfoW(SPI_GET/SETMENUSHOWDELAY)`, corresponding to the user's supplied `HKCU\Control Panel\Desktop\MenuShowDelay` value.

This changes only the existing menu tweak. **Do not add another card or a sixth setting**, and do not implement the unrelated `AutoStartDelay` or `StartupDelayInMSec` proposals.

The user-supplied **`MenuShowDelay.reg`** contains:

```reg
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Control Panel\Desktop]
"MenuShowDelay"="10"
```

This is a **REG_SZ** value; `10` is milliseconds. The upload does **not** contain an OFF definition. Per the agreed UI design, OFF explicitly sets **400 ms**. This is a fixed restore-to-conventional-Windows-setting action, **not** a backup/restore of a user's preexisting custom value.

## 2. Required reading and current-code baseline

Read these documents and the current `main` before coding:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/2026-10-13_WINDOWS_11_TWEAKS_TAB_WORK_ORDER.md`
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksOperatingSystem.cs`
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksRuntime.cs`
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweakRegistry.cs`
- `src/SteamInputAddonforClaw.Contracts/Frontend/WindowsTweaksFrontendContracts.cs`
- `src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml` and `.xaml.cs`
- Existing `WindowsTweaksOperatingSystemContractTests`, `WindowsTweaksRuntimeTests`, and `WindowsTweaksUiContractTests`.

The currently merged implementation:

- Registers `FrontendWindowsTweakId.InstantContextMenus` among five tweak IDs.
- Captures animation with `SPI_GETMENUANIMATION = 0x1002`.
- Sets animation with `SPI_SETMENUANIMATION = 0x1003` using `SPIF_UPDATEINIFILE | SPIF_SENDCHANGE`.
- Does not read or modify `MenuShowDelay`; the previous work order explicitly excluded it. **This new work order overrides only that exclusion for this combined card.**
- Uses `WindowsTweaksRuntime.SetAsync` for a per-item mutation, OS readback, sanitized failure, and typed frontend transport. Do not add another settings owner or per-tweak persistent state.

Microsoft's Win32 API explicitly supports:

- `SPI_GETMENUSHOWDELAY = 0x006A` — reads the submenu delay through a pointer to a DWORD.
- `SPI_SETMENUSHOWDELAY = 0x006B` — sets the submenu delay using `uiParam` (milliseconds).

Reference: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow

**Semantic distinction:** `MenuShowDelay` affects submenu-hover delay, not the shell's initial right-click menu construction or extension loading. Avoid claims that this makes every context menu appear instantaneously.

## 3. Single-toggle behavior (exact requirements)

| Faster Menus setting | ON | OFF |
| --- | --- | --- |
| Menu animation | Disabled (`SPI_SETMENUANIMATION`, value `0`) | Enabled (value `1`) |
| Submenu show delay | **10 ms** (`SPI_SETMENUSHOWDELAY`) | **400 ms** |
| UI | One card labelled **Faster Menus** | Same card; no additional submenu switch |

- Preserve the existing card's position under **Registry Tweaks** and the **Gaming Performance** section.
- Use a concise accurate description, e.g. `Disable menu animation and reduce submenu delay to 10 ms.`
- The existing tweak **ID must remain** `FrontendWindowsTweakId.InstantContextMenus` (and existing control names can remain) to minimize RPC, serialization, and test changes. Change only the user-visible label/description in both the catalog and XAML.
- The initial capture and every mutation readback must inspect **both** OS settings. Do not derive the switch exclusively from the animation flag, a saved preference, or the registry key's existence.

### State interpretation

| Animation observed | Delay observed | State |
| --- | --- | --- |
| OFF | Exactly 10 ms | **ON** |
| ON | Exactly 400 ms | **OFF** |
| Any other successful combination, including 10 ms with animation ON, or 400/200 ms with animation OFF | Any | **Mixed / custom**, not a verified ON or OFF |
| Either read fails | — | **Unknown / unavailable**, with error; do not imply OFF |

A single ToggleSwitch has no third physical position. For **Mixed**, show an explicit `Mixed`/custom detail and retain an **actionable** switch: it may be presented visually as OFF with a `Mixed — turn ON to apply both Faster Menus values` detail, but must **not** claim the OS is fully in the OFF/default pair. An ON request from Mixed must run both setter actions, and a subsequent OFF request must set both OFF values. Do not silently hide/disable the switch for a valid but nonstandard combination. A truly failed OS read must still disable changes until an accurate capture succeeds.

The existing `WindowsTweaksStateMapper` maps a null configured state to `Unknown` and refuses changes; do **not** reuse that pattern for a valid Mixed configuration. Implement the smallest explicit representation needed, e.g.:

- An optional `IsMixed = false` boolean at the end of `WindowsTweakObservation`.
- Append `Mixed` to `FrontendWindowsTweakState` **at the end**, preserving existing enum numeric values; prioritize it in the mapper while keeping `ConfiguredEnabled = false` and `CanChange = true` for this feature's Mixed presentation.
- Render a clear `Mixed` state label in `WindowsTweaksPage.FormatState` instead of claiming verified `Off`.
- If the frontend enum/serialized contract changes, update the existing frontend transport protocol version/comment and impacted contract tests as this repository requires.

Do not introduce a generic three-state control, a second action button, a new manager, or persistence for Mixed.

## 4. OS implementation

Extend `CaptureInstantContextMenus()` and `SetInstantContextMenus(bool enabled)` **in place** in `WindowsTweaksOperatingSystem.cs`.

Reuse the existing `SystemParametersInfoW` platform boundary and Win32 error handling. Add only the required constants and a small local read helper if clarity warrants it. For the delay:

```csharp
private const uint SpiGetMenuShowDelay = 0x006A;
private const uint SpiSetMenuShowDelay = 0x006B;

// GET: pvParam points to the output delay in milliseconds.
var delayMs = 0;
if (!SystemParametersInfoW(SpiGetMenuShowDelay, 0, ref delayMs, 0))
    throw new Win32Exception(Marshal.GetLastWin32Error());

// SET: uiParam is the requested millisecond delay; pvParam is not used.
var ignored = 0;
uint targetDelay = enabled ? 10u : 400u;
if (!SystemParametersInfoW(
        SpiSetMenuShowDelay, targetDelay, ref ignored,
        SpifUpdateIniFile | SpifSendChange))
    throw new Win32Exception(Marshal.GetLastWin32Error());
```

The existing `ref int` P/Invoke is 32-bit and suffices for the documented DWORD delay values; avoid an extra native wrapper unless a demonstrable signature issue requires one.

For a switch operation:
1. Write the requested menu-animation value.
2. Write the requested submenu-delay value.
3. Let `WindowsTweaksRuntime.SetAsync` recapture both and verify the **combined** state.
4. If either platform call fails, propagate the error. The existing Runtime failure path must recapture and display the **actual**, possibly Mixed, pair. Do not report success from only one successful setter.
5. Both writes must run for a requested action even if one constituent value already matches. Avoid extra rollback machinery for this user preference; a retry of ON can reconcile a partial failure.

Using `SPI_SETMENUSHOWDELAY` with the existing persistence/change-notification flags is preferred over writing only the REG_SZ by hand: it applies the system parameter via Windows and persists the selected user preference. The supplied `.reg` is the requested **value** specification, not a mandate to bypass the native setter. Do not redundantly write both via `WindowsTweakRegistry.Write` and SPI unless evidence proves SPI persistence insufficient on supported Windows 11. A readback of both SPI values is the primary verification; any additional registry assertion must account for Windows' own persistence behavior.

No forced Explorer restart, Windows restart, or logout.

## 5. Tests and acceptance criteria

Focused automated tests must cover:

1. ON mapping only when animation=OFF **and** delay=10.
2. OFF mapping only when animation=ON **and** delay=400.
3. All mixed variants (including animation OFF + delay=400; animation ON + delay=10; custom delay=200) remain explicitly Mixed, visible, and actionable, never silently reported as fully Off.
4. Failed read of either API maps to Unknown/unavailable; readback is not guessed.
5. ON and OFF both invoke animation **and** delay setters with the exact `uiParam` values 10/400 and the existing persistence/change flags; no `MenuShowDelay=0` substitution.
6. After a partial write failure, Runtime reports failure and the actual combined readback, without claiming success.
7. Catalog and XAML both display **Faster Menus**, with an accurate description, while retaining exactly five cards and the existing `FrontendWindowsTweakId.InstantContextMenus` RPC ID.
8. The remainder of Tweaks stays unchanged: no change to Memory Integrity, VMP, Classic Context Menu, Disable Password on Wake, warning acknowledgement, model filtering, or controller ownership/lifecycle.

Use ordinary unit/contract tests and the repository's relevant build/test checks; no tests that actually mutate the host's menu preferences on CI. **Do not require physical-device testing by local Codex or make it a merge blocker**. After merge, the user may check actual Windows menu behavior on the MSI Claw.

## 6. Scope restrictions

- No changes to `Disable_AutoStartDelay.reg`, `Disable_StartupDelayInMSec.reg`, Task Scheduler, services, startup, or shell performance unrelated to this menu toggle.
- No additional registry tweak cards, global animation policies, mouse timing settings, `MenuShowDelay=0` experimentation, WinUtil-style config engine, or extra helper processes.
- Do not touch CTW integration; Full1902 is standalone.
- Supported product baseline remains one Windows administrator user / one interactive session. Avoid hypothetical race hardening or new owner/state/epoch/barrier machinery.
- Keep the existing one-warning-per-local-day policy and immediate-on-toggle write/readback; no Apply All or restart banner.

**Definition of done:** exactly the preexisting menu toggle is renamed **Faster Menus** and coherently configures/reads both menu animation and a 10/400-ms submenu delay; unknown/mixed states are honest; other five-card Tweaks behavior remains intact.
