# Work Order — Align Virtual Machine Platform and Memory Integrity with MIT Windows Tweaks Implementations

> Date: 2026-10-14
> Product: Steam Addon for Claw — standalone Full PID1902 on Windows 11
> Target: one focused follow-up PR against the current `main` after merged PR #781
> Implementation owner: local Codex
> Real-device testing: user after merge; **never a PR/CI/blocking requirement**
> Scope: **only Virtual Machine Platform (VMP), Memory Integrity (HVCI), and their directly related result/error handling and tests**

## 1. Background and observed failure

Build **0.1.379.0** logs from 2026-10-14 demonstrate:

- `VirtualMachinePlatform` ON failed **twice**, with `Succeeded=False, State=Disabled`. This is a genuine unsuccessful feature change; the final readback remained Disabled.
- `MemoryIntegrity` ON succeeded with `State=EnablePendingRestart` and OFF succeeded with `State=Disabled`. **Memory Integrity is not the failed feature in these logs.**
- `InstantContextMenus` (Faster Menus), Edge Background, Classic Context Menu, Password on Wake and Startup App Delay operations recorded successful results when exercised. **Do not reopen their implementation in this PR.**
- The UI displayed a red exception-looking failure message for VMP, but the log currently records only `Succeeded` and `State`; the exact Windows servicing error code is **not available**. Do not claim the cause has been conclusively identified.

**Confirmed code divergence**: `WindowsTweaksOperatingSystem.BuildDismFeatureMutationArguments(true)` constructs:

```text
dism.exe /Online /Enable-Feature /FeatureName:VirtualMachinePlatform /NoRestart /English /LimitAccess
```

The referenced MIT utilities instead use the DISM **PowerShell cmdlets** with `-All -NoRestart` and without `-LimitAccess`. `-LimitAccess` explicitly blocks Windows Update as a feature-file source; lack of `-All` omits the referenced parent-feature behavior. Neither divergence alone proves what caused the two failures, but we should remove both divergences rather than preserve a bespoke restrictive command.

The current Runtime turns any actual mutation exception into `Windows setting operation failed (<ExceptionType>, 0x<HR>)`. That is an **implementation-centric user error**, not an actionable description.

## 2. Read and follow these project sources

- `docs/Full 1902 Implementation/README.md` (authority precedence)
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` (Runtime/controller owner)
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` (elevated Runtime is already the single privileged owner; no new elevation)
- `docs/work-order/2026-10-13_WINDOWS_11_TWEAKS_TAB_WORK_ORDER.md` (existing HVCI/VMP feature semantics; this work order supersedes the narrow VMP enable-command choices)
- `docs/work-order/2026-10-13_WINDOWS_TWEAKS_WIDGETS_EDGE_STARTUP_DELAY_WORK_ORDER.md` (the three new controls remain implemented)
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksOperatingSystem.cs`
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksRuntime.cs`
- `src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweakRegistry.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml.cs`
- `tests/SteamInputAddonforClaw.Tests/WindowsTweaksOperatingSystemContractTests.cs`
- `tests/SteamInputAddonforClaw.Tests/WindowsTweaksRuntimeTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/WindowsTweaksUiContractTests.cs`
- `THIRD_PARTY_NOTICES.md`

**Current behavior that should not be disrupted**:

- `WindowsTweaksRuntime.SetAsync` now returns only the mutated ID and captures that ID once. Do **not** revert to the pre-#781 whole-catalog capture or global `StateInvalidated` refresh.
- `IWindowsTweaksOperatingSystem` is the existing narrow OS seam; do not invent a Tweaks engine or a new process owner.
- VMP `RestartRequired` can be surfaced by the existing `WindowsTweakApplyResult`; `FrontendWindowsTweakState.EnablePendingRestart`/`DisablePendingRestart` and the eight-entry catalog already exist.
- The Runtime and Main UI are elevated within the existing same-user, single-interactive-session Full1902 design. No `runas` hop is necessary for a child Windows Optional Features command.
- The daily warning is already in place. Never automatically restart Windows or Explorer.

## 3. MIT source comparison and selected reference implementation

### Primary VMP reference — Raphire / Win11Debloat (MIT)

- [License](https://github.com/Raphire/Win11Debloat/blob/master/LICENSE)
- [Scripts/Features/Windows-OptionalFeatures.ps1](https://github.com/Raphire/Win11Debloat/blob/master/Scripts/Features/Windows-OptionalFeatures.ps1)
- [Scripts/Features/Invoke-Changes.ps1](https://github.com/Raphire/Win11Debloat/blob/master/Scripts/Features/Invoke-Changes.ps1)

**Exact behavior to use**:

```powershell
# Enable:
Enable-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -All -NoRestart -ErrorAction Stop

# Disable:
Disable-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -NoRestart -ErrorAction Stop

# Read:
Get-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -ErrorAction Stop
```

Win11Debloat's `EnableWindowsSubsystemForLinux` feature enables VMP **and then WSL**. **Copy only the VMP optional-feature operation, not the surrounding WSL workflow.** The inverse must disable **only VMP**, not WSL, Hyper-V, Windows Hypervisor Platform, Sandbox, or other features.

It uses a nonblocking job and awaits successful completion (or returns failure). Match **its completed-operation success policy**, not a fire-and-forget process-launch success.

### Cross-check — Chris Titus Tech WinUtil / Sophia Script (both MIT)

- [WinUtil license](https://github.com/ChrisTitusTech/winutil/blob/main/LICENSE)
- [WinUtil feature application](https://github.com/ChrisTitusTech/winutil/blob/main/functions/private/Invoke-WinUtilFeatureInstall.ps1): `Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart -ErrorAction Stop`
- [Sophia Script license](https://github.com/farag2/Sophia-Script-for-Windows/blob/main/LICENSE)
- [Sophia Windows 11 optional-features implementation](https://github.com/farag2/Sophia-Script-for-Windows/blob/main/src/Sophia_Script_for_Windows_11/Module/Sophia.psm1): uses `Enable-WindowsOptionalFeature ... -All -NoRestart` and `Disable-WindowsOptionalFeature ... -NoRestart`

These independently corroborate the selected **Windows Optional Features cmdlet** approach.

### HVCI reference — gangg111 / VBS (MIT)

- [License](https://github.com/gangg111/VBS/blob/main/LICENSE) — MIT, copyright (c) 2026 DriftKing
- [vbs_core.py](https://github.com/gangg111/VBS/blob/main/vbs_core.py) — reads and writes the HVCI `Enabled` value in `...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity`
- [VBS.cmd](https://github.com/gangg111/VBS/blob/main/VBS.cmd) — `Enabled=0` for HVCI OFF; corresponding restored `Enabled=1` for ON

**Important distinction:** This reference app also provides a much broader **Disable Hypervisor Security / VBS / Credential Guard** operation and UEFI-lock management. Our **Memory Integrity** switch is intentionally **HVCI-only**. Do **not** copy those other operations or its lock-bypass/EFI boot changes.

**Current repo already matches the MIT HVCI write contract**:

```text
HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity
  Enabled  REG_DWORD  1 = ON; 0 = OFF
```

Thus use the MIT source to **confirm parity**, retaining the existing small `WindowsTweakRegistry.Write` operation instead of swapping in Python, `reg.exe`, or a new powershell.exe for this DWORD. A rewrite of identical code is not an improvement.

### Windows OS authoritative contracts (cross-check; not MIT sources)

- [Microsoft Enable-WindowsOptionalFeature](https://learn.microsoft.com/en-us/powershell/module/dism/enable-windowsoptionalfeature): `-All` enables **required parent features**, `-LimitAccess` prevents Windows Update fallback, `-NoRestart` prevents an automatic restart.
- [Microsoft Disable-WindowsOptionalFeature](https://learn.microsoft.com/en-us/powershell/module/dism/disable-windowsoptionalfeature): no `-Remove` is necessary or desired.
- [Microsoft Get-WindowsOptionalFeature](https://learn.microsoft.com/en-us/powershell/module/dism/get-windowsoptionalfeature): authoritative typed feature `State`.
- [Microsoft DISM feature servicing](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-operating-system-package-servicing-command-line-options?view=windows-11): optional feature parent/file-source behavior.
- [Microsoft Enable memory integrity](https://learn.microsoft.com/en-us/windows/security/threat-protection/device-guard/enable-virtualization-based-protection-of-code-integrity): HVCI Enabled value, non-UEFI-lock guidance, DeviceGuard service ID 2 for configured/running, firmware/driver compatibility.

**Licensing:** Only directly copied or translated MIT code requires preserving the relevant upstream copyright and complete MIT notice in a shipped notice/location, updating `THIRD_PARTY_NOTICES.md` accordingly. An implementation written afresh to match Windows' documented commands and feature behavior can list the repos as design references without incorrectly attributing unrelated app code. Do not copy Winhance (PolyForm Shield) or GPL implementation.

## 4. Replace ONLY the VMP OS implementation with the proven cmdlet flow

### 4.1 OS execution

Use Windows' **inbox 64-bit Windows PowerShell 5.1**, which includes the DISM module on supported Windows 11 systems, as the implementation vehicle for the MIT implementation's exact operations. Execute **from the existing elevated Runtime**, not from the Main UI.

- Resolve the Windows PowerShell executable under the OS system directory (`%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` when running x64).
- Run hidden/non-interactive using `-NoLogo -NoProfile -NonInteractive -Command`. Do not use `-ExecutionPolicy Bypass`, `runas`, a new persistent helper, or a bundled PowerShell runtime.
- The feature name is an **in-code literal** `VirtualMachinePlatform`; never concatenate user-supplied strings into PowerShell commands.
- Use the three **exact** commands in section 3, including `-ErrorAction Stop`, `-All` for enable and `-NoRestart` on both mutations. **Never add `-LimitAccess`, `-Remove`, or other named features.**
- Allow standard Windows optional-feature servicing/source selection on explicit ON requests. This can legitimately obtain missing feature files through configured Windows sources/Windows Update; do not implement a separate downloader or forcibly block OS servicing. `-All` can enable **required parent** features, **not** arbitrary unrelated features/WSL.
- **Await command completion** before reporting success. A process successfully launched is **not** a successful feature mutation.
- Ensure a PowerShell *terminating error* yields a **nonzero process exit** (e.g. narrowly wrap the cmdlet with `$ErrorActionPreference='Stop'; try { ...; exit 0 } catch { ...; exit 1 }`). Bare `powershell.exe -Command ...` without deliberate error signaling is insufficient.
- Preserve cancellation/OS shutdown behavior: drain redirected stdout and stderr concurrently, honor the passed cancellation token, bound servicing duration appropriately for an operation that may require a Windows Update source, and dispose/terminate the owned child on cancellation or timeout. Avoid arbitrary retry loops and multiple servicing processes. No automatic restart.
- A restrictive/missing PowerShell/DISM module or disabled servicing capability should return a clear unsupported/unavailable/error outcome rather than a fake toggle success. Do not bypass Windows organization policy or tamper with the component store.

A narrow VMP-only function in `WindowsTweaksOperatingSystem` is sufficient; reuse the existing process-launch conventions. **Do not add a general PowerShell executor, scripting engine, OS feature manager, separate status authority, or persistent installation state.**

### 4.2 Typed state and success

**Read** only the `VirtualMachinePlatform` feature using `Get-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -ErrorAction Stop`. Use its `State` property (prefer a small machine-readable output marker or direct property conversion); **do not parse formatted human-language tables or output of `Get-WindowsOptionalFeature | Format-List`**.

Map:

| Feature state | UI and mutation meaning |
| --- | --- |
| `Enabled` | Configured ON; ON success after readback |
| `Disabled` | Configured OFF; OFF success after readback |
| `EnablePending` / `Enable Pending` | ON requested; pending reboot (success if operation completed) |
| `DisablePending` / `Disable Pending` | OFF requested; pending reboot (success if operation completed) |
| `DisabledWithPayloadRemoved` / payload missing | Do not mislabel as enabled; show appropriate unavailable/state; enable may need standard servicing source |
| Unknown, cmdlet failure, unrecognized state | Unknown/Unavailable/Managed only as actually evidenced, **never OFF by default** |

Where the returned Windows optional-feature mutation result exposes restart-needed information, preserve it through the **existing** `WindowsTweakApplyResult(RestartRequired: ...)` and the existing targeted `WindowsTweaksRuntime.SetAsync` mapping. If readback already reports a pending state, do not overwrite that with an optimistic Enabled/Disabled state. A successful process exit must still be accompanied by a matching target ID readback to report success.

**Do not automatically enable or disable Microsoft-Windows-Subsystem-Linux**; Win11Debloat's WSL toggle is broader than our VMP-only switch.

### 4.3 Minimal sketch (illustrative; retain existing code ownership)

```powershell
# Script fragment executed by the elevated Runtime's short-lived
# inbox Windows PowerShell child. The feature name is constant.
$ErrorActionPreference = 'Stop'
try {
    $result = Enable-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -All -NoRestart -ErrorAction Stop
    # Emit narrowly structured success/restart info for the C# caller.
    Write-Output ('VMP_RESTART=' + [string]($result.RestartNeeded -eq $true))
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
```

For disable, replace **only** the cmdlet and arguments with `Disable-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -NoRestart -ErrorAction Stop`. For capture, write a deterministic state marker from `(Get-WindowsOptionalFeature ...).State.ToString()`.

This is an example of the **one** short-lived command path; it is not permission to embed arbitrary scripts or execute data from the UI.

## 5. HVCI parity and safety: preserve the verified narrow implementation

Review `CaptureMemoryIntegrity`, `ResolveMemoryIntegrityConfiguredState`, `SetMemoryIntegrity`, `HasMemoryIntegrityPolicy`, `IsMemoryIntegrityLocked`, and the state mapper against the MIT implementation and the Microsoft HVCI contract.

**Required contract:**

1. **Only** the HVCI scenario's `Enabled` REG_DWORD is written: ON `1`, OFF `0`. The existing `WindowsTweakRegistry.Write(Registry.LocalMachine, HvciRegistryPath, "Enabled", enabled ? 1 : 0, RegistryValueKind.DWord)` already does this correctly.
2. Keep prior OS-managed GPO/MDM and `Locked=1` gates. **Do not disable security ownership checks merely because a gaming-tweak script writes directly without them**; full Windows HVCI/UEFI safety takes precedence.
3. Preserve `Win32_DeviceGuard` `SecurityServicesConfigured` and `SecurityServicesRunning` service ID **2** to distinguish **configured** and **actually running**. These are not interchangeable. ON with `EnablePendingRestart` in 379 was a successful configuration write, **not a failure**.
4. No writes to `EnableVirtualizationBasedSecurity`, `RequirePlatformSecurityFeatures`, `Mandatory`, `Locked`, `WasEnabledBy`, Credential Guard, System Guard, BCD, SecConfig.efi, or EFI/Secure Boot. In particular, do **not** add another VBS/HVCI restore/backup subsystem.
5. Do not automatically turn VMP ON/OFF when the Memory Integrity toggle is pressed; these remain independent controls.
6. Keep the existing configured-vs-effective/pending/failure distinction and existing daily protection warning. Never claim Windows currently runs HVCI merely from `Enabled=1`.
7. **If no meaningful HVCI defect is found, make no HVCI production change** beyond focused parity tests/documentation. The v379 evidence does not justify rewriting the working registry path.

Example existing write (keep if unchanged):

```csharp
if (HasMemoryIntegrityPolicy() || IsMemoryIntegrityLocked())
    throw new UnauthorizedAccessException("Memory Integrity is managed or firmware-locked.");

WindowsTweakRegistry.Write(
    Registry.LocalMachine,
    HvciRegistryPath,
    "Enabled",
    enabled ? 1 : 0,
    RegistryValueKind.DWord);
```

The guard may remain split as two statements to preserve distinct error details; no new helper is required.

## 6. Fix user-facing VMP failure presentation without concealing real failure

Current `WindowsTweaksRuntime.FormatSanitizedFailure` yields:

```text
Windows setting operation failed (Win32Exception, 0x...)
```

This is what appears as a red exception-like error in the Main UI.

For a VMP failure:

- Preserve the existing **per-ID** OS readback as truth. If the feature is still `Disabled`, leave its toggle OFF; if an enable/disable is pending, show pending, not hard-off.
- Display one short user message such as `Virtual Machine Platform could not be enabled. Check the Windows servicing configuration.` or `Virtual Machine Platform could not be disabled.` as appropriate. If an actionable Windows error code is available, it may be appended in a compact form, but **do not show .NET exception type names, process command lines, or raw PowerShell output**.
- **Internally** record actionable failure evidence with existing `AppLog` (VMP only): operation Enable/Disable/Read, command completion vs startup failure, process exit code, bounded error code/details from Windows PowerShell, and readback state. Keep only non-sensitive output (avoid full environment details/user paths). This is part of correct error handling, **not** a request for another diagnostic-only PR.
- A failed command remains failed even if the UI happens to show the prior desired state; do not reinterpret error as success. Conversely, a completed feature command with a matching pending state should not display a red exception merely because reboot is needed.
- Preserve the existing failure-message surface and one-row frontend mutation contract; do **not** add a new dialog or toast, and do not alter error UI of unrelated Tweaks unless needed for correct mapping.

## 7. Test plan (ordinary code/CI only)

Update **existing focused tests** rather than adding a generalized PowerShell framework.

1. **Exact VMP enable invocation:** Verify named feature `VirtualMachinePlatform`, cmdlet `Enable-WindowsOptionalFeature`, `-Online`, `-All`, `-NoRestart`, `-ErrorAction Stop`, **no `-LimitAccess`**, no `-Remove`, no WSL/Hyper-V/Sandbox feature names.
2. **Exact VMP disable invocation:** `Disable-WindowsOptionalFeature`, `-Online`, `-NoRestart`, `-ErrorAction Stop`; no `-All`/`-Remove`; no other feature names.
3. **Targeted capture:** `Get-WindowsOptionalFeature` for the single VMP name; verify each Enabled/Disabled/EnablePending/DisablePending and unknown/payload-unavailable mapping. Do not depend on localized English table output.
4. **No process-launch false success:** A child process starting successfully but returning a terminating PowerShell error must produce `Succeeded=False` and truthful readback. A result requiring restart maps to pending; no automatic reboot.
5. **Operational failures:** simulated missing PowerShell/module, cancellation, servicing failure, bounded timeout, and stale/mismatched readback remain failure/Unknown as appropriate, never invented ON/OFF.
6. **HVCI contract tests:** exact DWORD value and path, no adjacent VBS/CredentialGuard/UEFI/BCD writes, managed/lock prevention, `Win32_DeviceGuard` service ID 2, configured-vs-running restart states, unknown types/values.
7. **UI tests:** VMP failure message is brief and not a raw `Win32Exception`/type name; successful pending state is not red failure; other seven Tweaks cards remain unchanged.
8. **Regression:** full solution builds, focused Runtime/OS/frontend/UI tests, and existing controller/transport tests. **Do not execute the actual Windows Optional Feature or HKLM HVCI mutations in CI**; test argument construction, parsing, result mapping, and a stubbed process-result seam. If the old `WindowsTweaksOperatingSystemContractTests` explicitly assert `/LimitAccess` and **absence of `/All`**, replace those old assertions: they describe the now-obsolete implementation and must not block the correct MIT-aligned command.

No live Windows device, sleep/resume, boot, Windows servicing, firmware or security-toggle exercise is required by local Codex or is a PR-review blocker. Post-merge real-device verification belongs to the user.

## 8. Exclusions and definition of done

**Out of scope:** Widgets/Home support, Edge Background, Startup App Delay, Faster Menus, Classic Context Menu, Password on Wake, additional Tweaks cards, generic tuning engine, Winhance/GPL source code, HVCI UEFI bypass, Windows Security UAC changes, WSL installation, arbitrary Hyper-V/VBS disablement, additional task/service/runtime helpers, Full1902 routing/HidHide/VIIPER/PID1901↔PID1902 lifecycle changes, or any new automatic restart.

**Done when:** VMP uses the Win11Debloat-aligned Optional Features command semantics without `LimitAccess`, ON/OFF operations complete before declaring success, typed targeted readback remains truthful (including pending restart), UI no longer displays raw exception identifiers, and HVCI's direct DWORD implementation is shown equivalent to the MIT implementation with its Windows policy/firmware safeguards intact. Changes remain small, compile/test cleanly, and are ready for merge without making real hardware testing a blocker.
