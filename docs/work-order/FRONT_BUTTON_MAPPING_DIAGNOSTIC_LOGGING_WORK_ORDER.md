# Work Order — Add Front-Button Mapping Diagnostic Logging

## Goal

Improve observability for intermittent WING / OEM1 mapping incidents without changing routing behavior, lifecycle ownership, or persistence architecture.

The app is still pre-release, so **do not add any migration logic** for old `Oem1Mapping` / `WingMapping` settings.

## Scope

Keep this change small and diagnostic-only.

### 1. Log missing `FrontButtonMapping` fallback

In `SettingsStore.ReadFrontButtonMapping(...)`, when `FrontButtonMapping` is missing or is not a JSON object, keep the existing frozen-default fallback but emit a warning before returning it.

Example:

```csharp
if (!root.TryGetProperty("FrontButtonMapping", out var property)
    || property.ValueKind != JsonValueKind.Object)
{
    AppLog.Warn(
        "Settings",
        "Front-button mapping is missing; using defaults.",
        null,
        ("Reason", "MissingFrontButtonMapping"));

    return FrontButtonMappingSettings.Default;
}
```

Do not add schema-version handling or migration.

### 2. Log the effective mapping after load

After a valid mapping is loaded, emit one `DEBUG` line containing all four effective bindings:

```text
NormalGamebar=<action>
NormalCenterM=<action>
SteamGamebar=<action>
SteamCenterM=<action>
```

The log must reflect the mapping actually exposed by `StartupSettingsCoordinator` / runtime.

### 3. Log the mapping after a successful mutation/save

When `SetFrontButtonMappingAsync(...)` / `ChangeFrontButtonMapping(...)` successfully persists a mapping, emit one `DEBUG` line with the same four action values.

The goal is to make the following path visible in one runtime log:

```text
UI edit
  -> mapping mutation accepted
  -> settings saved
  -> effective 4-slot mapping
  -> physical-button dispatch
```

Do not add per-control verbose logging or duplicate logs at every layer.

## Out of Scope

Do **not** add:

- migration from old `Oem1Mapping` / `WingMapping` fields;
- new state, epoch, lock, manager, wrapper, or mapping cache;
- changes to WING/OEM1 domain selection;
- changes to Overlay or Steam Big Picture execution;
- changes to current frozen defaults in this PR;
- retries or synchronization intended only to defend against theoretical races.

## Validation

Add or update focused tests as appropriate to verify:

- missing `FrontButtonMapping` still falls back to `FrontButtonMappingSettings.Default`;
- valid mappings still round-trip unchanged;
- the diagnostic change does not alter mapping validation or dispatch behavior.

Run the existing front-button/settings test subset and the normal repository test suite required by CI.

## Acceptance Criteria

A future reproduction must allow the log alone to distinguish:

1. mapping missing -> frozen default fallback;
2. mapping loaded with specific four-slot values;
3. user edit successfully persisted with specific four-slot values;
4. runtime later dispatching a different or matching slot/action.

No product behavior should otherwise change.
