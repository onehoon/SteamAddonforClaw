# Work Order — Shared User-Action Process Launcher and Execution Privilege

**Date:** 2026-10-08  
**Status:** Ready for implementation  
**Target repository:** `onehoon/SteamAddonforClaw`  
**Target branch:** `main`  
**Reviewed main baseline:** `04a0ef56b104209719e9a9e7529957421c8c96d2`  
**Implementation shape:** One focused PR, including code, contracts, tests, and a narrow architecture-doc update  
**Implementation rule:** Re-read latest `main` before editing. This SHA records the review point, not a pinned implementation base.

---

## 0. Goal and product decision

The Full1902 Runtime intentionally runs with a **High-integrity administrator token** so that the existing Runtime-owned WING/Game Bar suppression remains effective even when an elevated game has foreground focus.

**Do not change that architecture.**

However, user-configured external actions currently inherit the elevated Runtime token because both Shortcut and front-button executable paths ultimately call `Process.Start` directly. The implementation must provide **one narrow shared user-action launch boundary**:

- Default external EXE / PowerShell launches run at **Medium integrity under the same interactive Windows user's identity**.
- User-configured EXE / PowerShell actions expose a **Run as administrator** checkbox, off by default, whose `true` value intentionally launches with the existing High Runtime token.
- Built-in Steam Big Picture, Steam client, and Xbox app launch actions stay **normal-user/Medium only** and expose no administrator option.
- Browser/URL actions also use the normal interactive-user shell/default browser; no administrator option.
- Screenshot uses its existing `NirCmdScreenshotCapture` path with **no changes** and no admin option.
- The policy applies to both **Shortcut EXE** and **front-button Launch Application** (both physical WING/Gamebar and Center M/OEM1, in both supported mapping domains).
- Shortcut PowerShell gets the same opt-in checkbox.
- **Never silently upgrade a Medium request to High** on launch failure or when the normal-user launch channel is unavailable.

Unify only **external process launching and privilege selection**. Do **not** combine Shortcut persistence, front-button mapping, gesture handling, UI, feature ownership, or execution authority.

The app is standalone Full1902. CTW integration is **not in scope**.

---

## 1. Mandatory current sources

Read these authority documents first:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
3. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md` (preserve existing device-authority rules; do not edit them)
4. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` (preserve lifecycle rules)
5. `docs/work-order/SHORTCUT_FOUNDATION_PR_C_RUNTIME_EXTERNAL_ACTION_ENGINE_WORK_ORDER_2026-09-24.md` (historical Shortcut launch contract)
6. `docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md`
7. `docs/work-order/1008_SHORTCUT_SCREENSHOT_ACTION_OWNED_FOLDER_WORK_ORDER.md` (latest Screenshot ownership; do not regress)

Read actual production code, not only the documents:

- `src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs`
- `src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs`
- `src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs`
- `src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs`
- `src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs`
- `src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs`
- `src/SteamInputAddonforClaw.Contracts/FrontButtons/FrontButtonMapping.cs`
- `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs`
- `src/SteamInputAddonforClaw/CenterM/Oem1ApplicationLauncher.cs`
- `src/SteamInputAddonforClaw/CenterM/Oem1BigPictureLauncher.cs`
- `src/SteamInputAddonforClaw/CenterM/FrontButtonXboxAppLauncher.cs`
- `src/SteamInputAddonforClaw/CenterM/FrontButtonActionExecutor.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw/Shortcuts/NirCmdScreenshotCapture.cs`
- `src/SteamInputAddonforClaw/Settings/SettingsStore.cs`
- Relevant tests under `tests/SteamInputAddonforClaw.Tests/` and `tests/SteamInputAddonforClaw.UiTests/`.

### 1.1 Source-verified facts at reviewed HEAD

- `ShortcutRuntime.ExecuteExecutable` and `ExecutePowerShell` build a `ProcessStartInfo` with `UseShellExecute = false` and then use the shared **within-Shortcut-only** `StartExternal` method.
- PowerShell uses system Windows PowerShell 5.1 with `-EncodedCommand` (UTF-16LE), no profile, noninteractive, and `CreateNoWindow = true`.
- Shortcut Steam targets are `steam://open/bigpicture` and `steam://open/main` through `UseShellExecute = true`.
- Shortcut Xbox uses `explorer.exe shell:AppsFolder\\<XboxGamingHomeAppIdentity.Aumid>`; the front-button Xbox launcher has an equivalent activation path and already describes Explorer delegation as crossing the High-to-Medium boundary.
- `Oem1ApplicationLauncher` is in fact used for **both** front buttons through `FrontButtonActionExecutor`, not only OEM1. It directly starts a literal `.exe` with `UseShellExecute = false`; its current validation intentionally prohibits generic shell/file-association execution.
- `FrontButtonLaunchApplicationBinding` is the positional record `(string ExecutablePath = "", string Arguments = "")`; `ControllerPage.BindingEditor` captures and reloads it.
- `ShortcutActionSpec` contains versioned JSON `Parameters`; `ShortcutRuntime` creates and validates EXE/PowerShell parameters and projects the existing editor DTOs.
- `ShortcutDocument.CurrentSchemaVersion` is **2**. `ShortcutRuntime.SupportedActionSchemaVersion` is **1**. These are **different schema layers**.
- `NirCmdScreenshotCapture` is its own bounded screenshot process path; it is **not** a user-configurable arbitrary EXE launch.
- The Full1902 privilege authority explicitly leaves only user-launched external actions for follow-up, forbids new privilege ownership infrastructure, and leaves existing elevated internal workers alone.

Do not treat `Process.Start(UseShellExecute = true)`, `explorer.exe`, or a `steam://` URI as proof that the target always runs Medium. Establish actual process-token behavior during validation, especially when Steam is **not** running yet.

---

## 2. Exact scope and privilege policy

| Action / caller | Default | Elevated option | Preserve |
| --- | --- | --- | --- |
| Shortcut `system.executable` | Medium | User-selected `RunAsAdministrator=true` -> High | Literal EXE-only restriction; arguments; working directory; fire-and-forget |
| Shortcut `system.powershell` | Medium | User-selected `RunAsAdministrator=true` -> High | PowerShell 5.1, encoded command, flags, no console/window policy |
| Shortcut `system.steam-big-picture` | Normal-user shell/Medium | Never | Exact existing URI and overlay-retirement policy |
| Shortcut `system.steam-client` | Normal-user shell/Medium | Never | Exact existing URI |
| Shortcut `system.xbox-app` | Normal-user packaged activation/Medium | Never | Existing AUMID and activation behavior |
| Shortcut `system.url` | Normal-user default browser | Never | Only HTTP(S), browser/file association behavior |
| Shortcut Screenshot | **Unchanged** | Never | Current screenshot destination, one-shot capture, timeout and ownership |
| Front buttons `LaunchApplication` | Medium | User-selected `RunAsAdministrator=true` -> High | Both physical buttons/domains, existing arguments, action dispatch |
| Front buttons `SteamBigPicture` | Normal-user shell/Medium | Never | Existing URI behavior |
| Front buttons `XboxApp` | Normal-user packaged activation/Medium | Never | Existing AUMID |
| Front-button hotkeys, Steam pulses, Quick Settings | **Unchanged** | N/A | Existing injection, WING suppression, and routing |

Do not broaden `LaunchApplication` to `.lnk`, scripts, documents, shell verbs, or arbitrary files. PowerShell remains available **only** through Shortcut's existing PowerShell action family.

Do not elevate anything merely because its configuration contains `RunAsAdministrator` unless that flag is valid for the specific EXE or PowerShell action family.

---

## 3. Shared execution seam: small and concrete

Add **one Runtime-local shared launcher**, e.g.:

```text
src/SteamInputAddonforClaw/Processes/UserProcessLauncher.cs
```

Suggested minimal shape (names may follow existing project style):

```csharp
internal sealed class UserProcessLauncher
{
    // Both Shortcut and front-button EXE call this.
    internal void LaunchExecutable(
        string executablePath,
        string arguments,
        bool runAsAdministrator,
        string? workingDirectory = null);

    // Shortcut PowerShell calls this with the existing encoded command.
    // PowerShell syntax/encoding remains the Shortcut action's responsibility.
    internal void LaunchPowerShell(
        string encodedCommand,
        bool runAsAdministrator);

    // Only the fixed built-ins and already validated HTTP(S) URL action
    // call the appropriate normal-user shell activation path.
    // Keep URI and packaged-app activation semantics separate from direct EXE.
}
```

This is **conceptual**, not a mandatory class API. It is fine to have one small launch method over a narrow request shape if simpler, but do not build a command bus, mediator, process-supervisor framework, generic action registry, background service, long-lived broker, separate manager per action kind, or more than one privilege owner.

Ownership rules:

- Runtime owns this launch facility. Frontends continue sending existing typed configuration/mutation intents or `TileId`, **not arbitrary process commands** as direct execution authority.
- `ShortcutRuntime` keeps its own tile lookup, type/version/schema validation, action-specific argument preparation, execution result classification and safe logging, and `CloseOverlayAfterLaunch` semantics.
- `FrontButtonActionExecutor` keeps button/domain validation, caller-specific error/fail-open handling, and gesture semantics.
- `Oem1ApplicationLauncher` can become a very small delegate/adapter to the shared launcher or be retired if composition can call the shared launcher directly. Do not keep a second EXE launching implementation.
- The two Steam URI callers and two Xbox activation callers may share the same underlying launch methods while retaining the current app-specific routes. Reuse the existing AUMID constant; do not create a duplicate catalog.
- User EXE/PowerShell process lifetime remains **not owned** by the Addon: no monitoring, process kill, watchdog, retries, state machine, or restart on child exit.
- Preserve `ShortcutRuntime`'s injected process-start test seam or replace it with an equivalently narrow fakeable seam. Do not launch arbitrary real apps from unit tests.

---

## 4. Correct High / Medium implementation (do not confuse shell behavior with token policy)

### High / opt-in administrator

The Runtime is already High. For `RunAsAdministrator=true`, start the specified, validated `.exe` or PowerShell process directly with its current High token.

- No second UAC prompt, `Verb="runas"`, credential dialog, or separate elevation helper.
- Do not change the Runtime, Main UI, or Overlay tokens.
- The checkbox is an explicit opt-in to administrator execution; make that clear in the UI.

### Medium / default

**Must actually create/activate as the same interactive user's normal Medium-integrity context**. Merely setting `UseShellExecute = false` does not reduce privilege. Merely using `explorer.exe` or a URI handler also does not guarantee a Medium target.

The project supports **one interactive administrator user + one session**, not FUS/RDP/over-the-shoulder credentials. Select the **smallest proven implementation** for this supported lifecycle. Reference candidates:

1. For literal EXE/PowerShell process creation: the current High admin token's UAC-linked limited token, when available and validated as the same SID and Medium integrity, used with the documented Windows process creation API (e.g. `CreateProcessWithTokenW`). Handle quoting/arguments, working directory, interactive desktop, process/thread handles, errors and cancellation correctly. `CreateProcessWithTokenW` may require `SeImpersonatePrivilege`; verify actual hardware behavior instead of assuming success.
2. For Steam URI, Xbox packaged-app activation, and normal-browser HTTP(S) URL: a normal-user interactive shell dispatch path (e.g. shell delegation) which preserves **URI/file-association and packaged-activation semantics**. Use the already working Xbox path as a reference, but **verify target token** on an elevated Runtime and test both existing-process activation and cold startup.

Do **not** route URL/Steam URI or packaged-app activation through a literal EXE `CreateProcess` call: protocol/AUMID activation is not the same API contract as starting an EXE. Likewise, do not use `cmd.exe /c start` to interpret user input or introduce new quoting/injection risk.

### Important: FSE / missing Explorer

This project supports Steam/Windows FSE and boot-time controller operation. A design which **requires an Explorer desktop window to exist** might fail under a supported user environment.

Before choosing Explorer COM/parent-process launch as the sole Medium implementation, verify it works in the actual FSE shell scenario. If it does not, use the smallest **bounded, one-shot** Medium shell activation mechanism supported by the existing app lifecycle; do not introduce a resident broker/service or another controller authority. Keep the URI/AUMID activation contract intact. Document the choice and why it works in both normal desktop and the supported FSE startup.

### Strict failure boundary

- If no valid Medium token or normal-user shell activation is available, the Medium request must **fail explicitly** with safe existing failure reporting. No retry at High, no silent `Process.Start` using the Runtime token.
- For `RunAsAdministrator=true`, failures also stay failures; no automatic Medium retry.
- No credentials, administrator account switches, or cross-user launch.
- If UAC is disabled so that the normal user's Medium split token is not available, report a meaningful action failure rather than falsely declaring a Medium launch or silently using High.
- A `.exe` manifest that explicitly requires elevation might reject Medium launch; do not circumvent it. The user can opt into the available High action where appropriate.
- Do not make a separate permanent token cache/authority or privilege-restoration state machine for theoretical edge cases.
- Do not claim that dispatching a URI to an already-running Steam process changed that process's integrity; launch/reuse follows the target application's actual lifecycle.

Microsoft references for the implementer:

- `CreateProcessWithTokenW`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithtokenw
- `IShellDispatch2::ShellExecute`: https://learn.microsoft.com/en-us/windows/win32/shell/ishelldispatch2-shellexecute
- `Execute in Explorer` sample: https://learn.microsoft.com/en-us/windows/win32/shell/samples-execinexplorer
- Microsoft discussion of shell vs direct CreateProcess: https://devblogs.microsoft.com/oldnewthing/20190425-00/?p=102443
- UAC-disabled limitation: https://devblogs.microsoft.com/oldnewthing/20150618-00/?p=45351

The listed APIs are **options to validate**, not permission to add multiple redundant fallback frameworks.

---

## 5. Persisted EXE / PowerShell preference

### 5.1 Shortcut action parameters

Preserve the existing `ShortcutDocument` format and `ShortcutActionSpec` ownership.

Add one **optional boolean** field only for `system.executable` and `system.powershell` schema-1 parameters:

```json
{
  "path": "C:\\Tools\\Application.exe",
  "arguments": "--example",
  "runAsAdministrator": false
}
```

```json
{
  "script": "Write-Output 'hello'",
  "runAsAdministrator": true
}
```

Rules:

- Missing `runAsAdministrator` => **false** (existing Shortcut JSON defaults to Medium).
- An explicitly present field must be JSON `true` or `false`; reject a string, number, null or object as invalid known action configuration. Do not rewrite malformed source just because it is projected.
- Persist the user's choice on Create/Update; preserve it when reopening and saving an existing tile.
- Keep known action `SchemaVersion = 1` if the change remains strictly backward-compatible; do **not** bump the enclosing `ShortcutDocument.CurrentSchemaVersion = 2` merely to add an optional action parameter.
- Preserve unknown `TypeId` and unsupported-newer schema round-tripping.
- Parameterless Steam/Xbox built-ins must still require exactly the empty object; the flag is **not accepted** on those families. Screenshot and URL likewise must not be granted an elevation flag.
- Preserve the existing command-line bounds and action-specific validation.

### 5.2 Front-button LaunchApplication binding

Extend the existing positional record with one **optional final parameter**, preserving compatibility with existing constructors and JSON:

```csharp
public sealed record FrontButtonLaunchApplicationBinding(
    string ExecutablePath = "",
    string Arguments = "",
    bool RunAsAdministrator = false);
```

Existing `SettingsStore` deserialization/mapping validation must continue accepting existing persisted front-button configuration without resetting all mappings to defaults. New writes may include the false value.

This flag belongs to `LaunchApplication` only, not the entire front-button binding or domain. The value may stay dormant while the user temporarily selects another front-button action, consistent with existing hotkey/path preservation.

Do **not** add a second global "Run as administrator" setting, per-button global privilege setting, schema framework, or AppSettings-level privilege authority.

---

## 6. Main App editing UI

### Shortcut Add/Edit dialog

Existing `ShortcutPage.xaml.cs` panels:

- `Application (.exe)`: path, Browse, arguments.
- `PowerShell`: script editor.

Add one `Run as administrator` checkbox in **each** of those two panels only.

- Unchecked by default for new EXE/PowerShell tiles.
- Rehydrate from the existing tile when editing, including `false` when JSON field is absent.
- Wire the selected value through `FrontendShortcutActionInput`, `FrontendShortcutEditorAction`, `ShortcutRuntime.ProjectEditorAction` and `TryBuildAction` into persisted `Parameters`.
- Keep existing editor validation, screenshot-folder staging, per-tile close-overlay toggle and built-in panel presentation unchanged.
- Add concise UI help text (e.g. "Runs with administrator privileges" / default normal privileges). Do not add a second confirmation dialog or a generic permissions page.

### Controller tab / Front Button Mapping

In `ControllerPage.BindingEditor`'s existing `Launch Application` configuration panel, add the same checkbox.

- Apply to both physical Gamebar/WING and Center M/OEM1 editors and both Normal and Steam domains.
- Restore the saved value in `Load(FrontButtonBinding)` and capture it in `Capture()`.
- Use the existing editor change notification and ordered settings save; do not create separate persistence/RPC for this bit.
- Keep hotkey configuration and other bindings unchanged.

The `Run as administrator` checkbox must **not** appear on Steam Big Picture, Steam, Xbox, URL or Screenshot built-ins.

---

## 7. Execution behavior and existing contracts

### Shortcut

Preserve:

- User execution by `TileId`, never a raw frontend-supplied `ProcessStartInfo` or script at execution time.
- Existing `ShortcutExecutionOutcome` classification and safe logging with `TileId`, `TypeId`, `Outcome`, exception category; no executable arguments/PowerShell body/URL in log.
- `CloseOverlayAfterLaunch` only when the launch request actually succeeded.
- PowerShell 5.1 `-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand` and `CreateNoWindow`.
- Literal EXE path + optional raw argument semantics, with executable folder as normal working directory.
- URL HTTP(S)-only validation and registered default browser behavior.
- Existing screenshot capture and its timeout, folder, error reporting.

### Front button

Preserve:

- One action per button press in the current resolved Normal/Steam presentation domain.
- `FrontButtonActionCapabilities` catalog/validation, WING Win+G suppression guard, no duplicate action per domain.
- `FrontButtonActionExecutor.Execute` returning `false` for a real replacement-action failure, with the caller's **existing Center M fail-open or Gamebar log/continue policy**.
- Fire-and-forget `LaunchApplication`, no child tracking, no toggle or termination behavior.

The shared launcher may throw classified launch exceptions or return a narrow failure result; each caller still translates the result into its **existing** outer failure semantics. Do not let a shared component swallow a failure and report success, or rewrite front-button fail-open policy as a new generic policy.

---

## 8. Integration wiring and minimality

Use the existing Runtime-host composition to provide the **same** `UserProcessLauncher` implementation to the Shortcut and front-button executable call paths. Preserve existing injection seams used by tests.

No new frontend pipe execution authority. Only the existing typed Shortcut editor payload needs the additional preference bit. The front-button mapping already passes through existing settings/transport contracts.

Avoid:

- New resident background process, Windows service, Task Scheduler execution service, new UAC helper, generic privilege broker, process scanner or runtime supervisor.
- A new `IProcessLauncher` interface solely for tests when an injectable delegate is enough.
- Second shell/application activation registry, second action model, duplicated Xbox AUMID constants, generic plugin/task/action architecture.
- Changes to Full1902 controller ownership, HIDHIDE, VIIPER, PID1901/PID1902 switching, Steam session detection, QAM page structure, screenshot ownership, gaming FSE registration, or Windows App SDK deployment.
- Any CTW-specific compatibility behavior.

One simple shared implementation and two existing caller adapters are preferred over restructuring either feature system.

---

## 9. Tests — focused, behavior-based

### 9.1 Unit / contract tests

Update and/or add focused tests to cover:

1. Shortcut legacy EXE/PowerShell action JSON missing `runAsAdministrator` => false/Medium, valid and editable.
2. Shortcut true and false round-trip through UI DTO/mutation/store/reload and yield the requested High/Medium launch mode.
3. Wrong-typed `runAsAdministrator` => invalid known action, no launch. Unknown/newer action types remain preserved.
4. UI checkbox present only for EXE/PowerShell; hidden for Steam, Xbox, URL and Screenshot; edits do not clear previously selected values.
5. Existing front-button JSON with two-field `FrontButtonLaunchApplicationBinding` remains valid, loads default false, saves/rehydrates. Both front buttons/domains obey the per-binding flag.
6. Shared launcher is called from both Shortcut EXE and front-button LaunchApplication; both enforce literal `.exe` only.
7. High choice uses High path only; Medium choice uses Medium path only. Failure at Medium never invokes the High start delegate; High failure never silently retries as Medium.
8. Existing PowerShell encoded command and no-window flags unchanged, including long-script bounds.
9. Built-in Steam URI and Xbox AUMID values unchanged, browser URL HTTP(S)-only, and shell actions are Medium-only by policy.
10. Screenshot still uses its existing `NirCmdScreenshotCapture` path and maintains save-folder behavior.
11. Shortcut execution failure does **not** close Overlay even if Close Overlay After Launch was selected.
12. Front-button execution failure still follows the existing Center M fail-open / Gamebar behavior, not Shortcut results.
13. `Shortcuts` load failure remains independent of Runtime/controller startup; existing process-launch seam tests remain deterministic.

Use fakes to assert intent and failure translation. Do not require real UAC or real external app execution inside unit tests.

### 9.2 Real Windows / Claw validation before release

Under actual elevated Full1902 Runtime, verify child process identity and **integrity level** (not only successful Process.Start), using a harmless diagnostic executable or a narrowly scoped PowerShell identity check:

- Shortcut EXE default => same user, **Medium**.
- Shortcut PowerShell default => same user, **Medium**.
- Shortcut EXE/PowerShell opt-in => same user, **High**.
- Front-button EXE default/opt-in => Medium/High respectively (try Gamebar and Center M mappings).
- Normal Steam already running -> Steam/Big Picture receives requested activation; Steam integrity is not changed.
- Cold-start Steam (not already running) -> newly created Steam is not accidentally High.
- Xbox app activation from elevated Runtime -> packaged app opens in normal user session.
- HTTP(S) URL -> user's registered browser in normal user session.
- Standard Windows desktop **and** Windows/Steam FSE (including no normal Explorer desktop when applicable) have a usable Medium launch path for supported actions.
- Bad target, unavailable linked Medium token/shell channel, denied launch, and cancellation => action fails cleanly, no High fallback, no Runtime crash.
- Existing gameplay WING suppression, including High game foreground, still works.
- Sleep/resume and application shutdown/restart do not affect controller routing or leak a long-lived user-action child/helper.

If a test environment disables UAC and cannot supply a valid Medium token, capture the unsupported failure explicitly rather than claiming the child launched unelevated.

---

## 10. Acceptance criteria

The PR is complete only when all of the following hold:

1. Elevated Full1902 Runtime remains the sole controller/device authority.
2. **One** shared user-action process-launch implementation is used by Shortcut EXE/PowerShell and front-button LaunchApplication, with no duplicate direct EXE launch policy.
3. The administrator checkbox is available, persisted and restored **only** for user-defined EXE/PowerShell; front-button LaunchApplication has equivalent opt-in.
4. Missing legacy setting defaults to `false`; existing persisted mappings/shortcuts still load without destructive reset.
5. Built-in Steam/Xbox/URL operations use normal interactive-user activation and preserve their previous functional targets.
6. Medium launch failure never escalates to High. High is used **only** after explicit, valid user opt-in.
7. Screenshot and existing Overlay/Controller/QAM behavior are not regressed.
8. Unit/contract tests pass; focused physical Windows validation demonstrates actual Medium/High tokens and FSE compatibility.
9. Update the **"Remaining external-process privilege boundary"** and current-status portions of `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` (and its README pointer if needed) to record the implemented policy and any clearly evidenced limitation. Do not rewrite old work orders or historical design decisions.
10. No speculative race-protection state, additional authority process/service, or CTW integration is introduced.

**Review priority:** Real user-visible wrong privilege, incorrect token/identity, failed Steam/Xbox activation, unsafe High fallback, broken persisted mappings, button fail-open regressions, Overlay close regressions, and supported Windows/FSE lifecycle failures. Do not block for hypothetical instruction-level races that are not plausible under the actual handheld lifecycle.
