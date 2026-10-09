# Work Order — Model-Aware Default Front-Button Mapping (EX / A2VM)

> **Date:** 2026-10-10  
> **Status:** Ready for local Codex implementation  
> **Source baseline:** SteamAddonforClaw main, inspected on 2026-10-10  
> **Scope:** First-install / absent / invalid front-button mapping defaults only  
> **Implementation:** One small PR; local Codex owns code and automated tests; the user owns post-merge physical-device verification.

## 1. Product decision

Keep **both existing front-button mapping cards** and **all existing user-editable actions**. Do not lock a button to the overlay, hide either card, restrict action choices, or alter front-button dispatch.

The **right-hand physical front button defaults to Quick Settings Overlay in BOTH Normal (Xbox360 presentation) and Steam (SteamDeck presentation)**. The left-hand physical button defaults to Steam Big Picture in Normal and Steam Button in Steam.

The physical left/right locations differ by model:

| Exact supported model | Physical left | Physical right | Normal: Gamebar (WING) | Normal: Center M (OEM1) | Steam: Gamebar (WING) | Steam: Center M (OEM1) |
| --- | --- | --- | --- | --- | --- | --- |
| Claw 8 EX / CG3EM (MS-1T91) | WING | Center M | SteamBigPicture | QuickSettingsOverlay | SteamButton | QuickSettingsOverlay |
| Claw 7 AI+ / A2VM (MS-1T42) | Center M | WING | QuickSettingsOverlay | SteamBigPicture | QuickSettingsOverlay | SteamButton |
| Claw 8 AI+ / A2VM (MS-1T52) | Center M | WING | QuickSettingsOverlay | SteamBigPicture | QuickSettingsOverlay | SteamButton |

**Important:** These are INITIAL DEFAULTS, not mandatory physical-button policies. The user can change all four mappings using the existing Controller page.

EX already has the requested default in current source. Only the **A2VM-specific default mapping** is new.

## 2. Read and respect the existing architecture

Before implementation, read:

- docs/Full 1902 Implementation/README.md and its current authority order;
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md;
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md;
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md;
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md;
- docs/work-order/APP_UI_PR_C_FRONT_BUTTON_MAPPING_AND_OVERLAY_ACTION_WORK_ORDER.md as historical mapping context, **not** as authority for older defaults.

The current main implementation, not older work-order default tables, is the source of truth for existing behavior. This standalone Full1902 product has no CTW integration requirement.

## 3. Verified current code paths

| File | Current role / observation |
| --- | --- |
| src/SteamInputAddonforClaw.Contracts/FrontButtons/FrontButtonMapping.cs | FrontButtonMappingSettings.Default already defines the EX-oriented four-slot mapping; mapping validation requires different actions per same-domain button. |
| src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawDeviceModels.cs | Existing exact identities: msi.claw.cg3em, msi.claw.a2vm.7, msi.claw.a2vm.8. Hardware model resolution uses exact BaseBoardProduct identities. |
| src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs | InitializeRuntimeAsync already has startupResult.HardwareDeviceModel after startup hardware assessment and before creating the Runtime composition. |
| src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs | AddonRuntimeCompositionFactory.Create presently calls new SettingsStore(...).Load() without a model, then constructs StartupSettingsCoordinator. |
| src/SteamInputAddonforClaw/Settings/SettingsStore.cs | Load currently creates/saves new AppSettings for a missing file; missing/invalid FrontButtonMapping falls back to the global Default; valid JSON mapping is loaded unchanged. |
| src/SteamInputAddonforClaw/Settings/AppSettings.cs | FrontButtonMapping property initializer uses global Default. |
| src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs | ChangeFrontButtonMapping persists a validated user selection; do not change this behavior. |
| src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml | Both Gamebar Button and Center M Button cards already exist for Normal and Steam domains. |
| tests/SteamInputAddonforClaw.Tests/FrontButtonMappingContractTests.cs | EX/global Default values are covered by existing tests. |
| tests/SteamInputAddonforClaw.Tests/FrontButtonMappingPersistenceTests.cs | Fallback/loading/round-trip behavior is covered and must remain correct. |

**Key integration finding:** Merely adding an A2VM default constant is insufficient. The production SettingsStore.Load() call currently happens without hardware-model context and immediately persists EX defaults on a fresh file. Selection must happen **before that load/save**, using the model already resolved by the StartupCoordinator.

## 4. Minimal requested implementation

### 4.1 Add only the A2VM default mapping

In FrontButtonMapping.cs:

- Preserve FrontButtonMappingSettings.Default as the current EX/global fallback, including its existing public meaning and tests.
- Add one explicit A2VM default mapping (e.g. A2vmDefault), using the existing FrontButtonDomainMapping, FrontButtonBinding.Of and FrontButtonAction types.
- No new enum, kind, action, setting, persistence field, manager, or action router.
- Both defaults must independently satisfy FrontButtonMappingValidation.Validate().

Illustrative target:

~~~csharp
public static FrontButtonMappingSettings A2vmDefault { get; } = new()
{
    Normal = new()
    {
        Gamebar = FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay),
        CenterM = FrontButtonBinding.Of(FrontButtonAction.SteamBigPicture)
    },
    Steam = new()
    {
        Gamebar = FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay),
        CenterM = FrontButtonBinding.Of(FrontButtonAction.SteamButton)
    }
};
~~~

This is a default factory/constant, not a runtime override of the user's choice.

### 4.2 Reuse the already-resolved exact hardware model

In AddonProcessHost.InitializeRuntimeAsync, pass startupResult.HardwareDeviceModel to AddonRuntimeCompositionFactory.Create using a narrowly scoped optional parameter (to minimize existing test-callsite churn).

Within Runtime composition, select A2vmDefault only when the model equals one of the existing exact IDs:

~~~csharp
var defaults =
    model == MsiClawDeviceModels.Claw7AiPlusA2vm.Id ||
    model == MsiClawDeviceModels.Claw8AiPlusA2vm.Id
        ? FrontButtonMappingSettings.A2vmDefault
        : FrontButtonMappingSettings.Default;
~~~

The snippet is illustrative; account for appropriate imports and the existing nullable HandheldDeviceModelId signature. Use the current model descriptor constants rather than board-name substring matching, a second WMI/PnP probe, or a new model resolver.

Unknown/null is a conservative fallback to the existing default and **must not** be interpreted as A2VM. The existing unsupported/indeterminate-hardware startup gate remains unchanged.

### 4.3 Feed the selected default into the existing SettingsStore load

Give SettingsStore.Load a narrow optional default-mapping argument (or equivalent minimal injection) to ensure all of its fallback paths use the selected mapping.

Required:

1. Missing settings file: create AppSettings with selected FrontButtonMapping default **before Save(defaults)**; save the model-correct default on initial creation.
2. Existing file with absent FrontButtonMapping: select model-correct default for that feature.
3. Existing file with malformed/incomplete/invalid FrontButtonMapping: select model-correct default for that feature, without resetting unrelated settings.
4. Whole-file JSON parse failure and ordinary Load fallback branches: in-memory AppSettings should also carry the model-correct front-button default; retain existing log-level behavior.
5. Existing **valid persisted FrontButtonMapping**: return it unchanged. Never overwrite, force-normalize, or reapply defaults to valid user edits at each startup.
6. Continue serializing the same FrontButtonMapping JSON shape. No schema or frontend/overlay transport protocol change.

For callers that do not supply a hardware model/default, retain the existing FrontButtonMappingSettings.Default behavior. Do not complicate unrelated settings or reset the complete settings file.

**Pre-release policy:** No migration, legacy-format conversion, preexisting-user-settings backfill, or forced reset is requested. On an already populated installation, a valid previously saved mapping remains valid and therefore remains unchanged. Fresh-default testing should use a clean/missing mapping or a fresh test settings path.

### 4.4 Preserve the entire existing UI and execution paths

Do not modify:

- ControllerPage.xaml or ControllerPage.xaml.cs card presence, labels, choices, uniqueness editor logic, or save plumbing;
- FrontButtonMappingValidation, capability matrix, or cross-button duplicate restriction;
- StartupSettingsCoordinator.ChangeFrontButtonMapping semantics;
- WING Event88 / Center M Event41 acquisition and dispatchers;
- Runtime-owned coordinated Overlay toggle, WinG suppression, Steam/BPM domain selection;
- Full1902 controller authority, PID1901/PID1902, HidHide, VIIPER, startup task, elevation, sleep/resume, and teardown.

Any code modification outside the narrowly necessary default selection/load path must be justified by a concrete compile/test dependency.

## 5. Automated verification (required of local Codex)

Add/adjust targeted automated tests without physical hardware:

1. Contract: EX/global Default is unchanged and valid; A2vmDefault matches **all four** table cells and is valid.
2. Identity: exact msi.claw.a2vm.7 and msi.claw.a2vm.8 select A2vmDefault; msi.claw.cg3em selects Default; unknown/null does not accidentally select A2VM. Exercise the production selection seam if practical.
3. Fresh file: SettingsStore.Load with A2VM default creates a settings file whose persisted four bindings are A2VM-correct; reload returns identical values.
4. Existing file missing mapping: A2VM fallback chosen, preserving an unrelated setting such as LogLevel.
5. Invalid/malformed mapping: A2VM fallback applies to that feature alone.
6. Valid customized mapping: reload on A2VM preserves the user's mapping exactly; it is not overwritten with A2vmDefault on startup.
7. Existing no-argument SettingsStore.Load behavior and EX/default tests continue to pass.
8. Keep build and relevant automated test suite green. No new transport/protocol or UI changes should be necessary.

Physical EX and A2VM button checks are the **user's post-merge verification role**, not a required Codex deliverable, PR blocker, or CI prerequisite. Do not invent unavailable hardware tests.

## 6. Acceptance criteria / boundaries

- On fresh Claw 8 EX settings, physical right Center M defaults to Overlay in Normal and Steam; left WING defaults to SteamBigPicture / SteamButton.
- On fresh Claw 7/8 A2VM settings, physical right WING defaults to Overlay in Normal and Steam; left Center M defaults to SteamBigPicture / SteamButton.
- Both front-button mapping cards remain visible and fully user-configurable on all supported models.
- A user change stays saved across launches and domain switches; defaults are never reasserted over valid saved settings.
- Only one existing FrontButtonMapping is persisted; no model-conditional dispatcher, UI authority, new lifecycle, manager, or migration is added.
- Existing Full1902 lifecycle / ownership and elevated-runtime contracts remain untouched.
- Complete the work as **one focused PR**. Implementation and automated tests are Codex responsibilities; real hardware verification is performed separately by the user after merge.
