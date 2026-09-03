# SHARED-FRONTEND-01 Addendum — Explicit Frontend Surface Exposure Policy

> **Date:** 2026-09-03  
> **Status:** Authoritative addendum for `SHARED_FRONTEND_01_DEVICE_QUICK_SETTINGS_SNAPSHOT_WORK_ORDER.md`  
> **Scope:** Main UI / Steam QAM / Addon Overlay feature exposure boundaries

---

## 1. Why this addendum exists

`SHARED_FRONTEND_01_DEVICE_QUICK_SETTINGS_SNAPSHOT_WORK_ORDER.md` introduces a shared typed Device Quick Settings snapshot for the features that are already common to the current Main UI and Steam QAM and are candidates for the Addon Overlay.

That must **not** be interpreted as:

```text
all Addon settings
→ one aggregate
→ expose everything to Main UI + QAM + Overlay
```

That is not the product direction.

The correct rule is:

> **Runtime feature authority may be shared, while frontend surface exposure remains explicitly selective per feature and per UI surface.**

A feature being implemented in the Addon does **not** imply that it belongs in Steam QAM or the Addon Overlay.

A feature may intentionally be:

```text
Main UI only
Main UI + QAM
Main UI + Overlay
Main UI + QAM + Overlay
QAM only, if a future product requirement explicitly calls for it
Overlay only, if a future product requirement explicitly calls for it
```

The supported surface set is a product/UI decision for each feature.

---

## 2. Authority and exposure are separate questions

For every feature, distinguish these two questions.

### Question A — who owns the real state?

Example:

```text
Battery Charge Limit
→ one Runtime-owned feature authority
```

or:

```text
CPU Boost
→ CpuBoostRuntime
```

This answers:

- who reads current state;
- who persists desired state where applicable;
- who performs hardware/Windows mutation;
- who verifies operation/readback;
- who handles lifecycle/reconcile policy.

### Question B — which UI surfaces may expose it?

This is independent.

Example:

```text
Battery Charge Limit
→ Main UI only
```

Then the correct architecture is:

```text
Runtime Battery authority
        ↓
Main UI frontend contract / .Frontend

QAM        → no Battery API
Overlay    → no Battery message
```

Do not expose a feature to QAM/Overlay merely because the Runtime can provide it.

---

## 3. `FrontendDeviceQuickSettingsSnapshot` is a curated projection, not the Addon's settings database

For `SHARED-FRONTEND-01`, the aggregate is intentionally limited to the selected Device Quick Settings that are already shared by the existing Main UI and QAM implementation:

```text
FrontendDeviceQuickSettingsSnapshot
├─ CpuBoost
├─ Tdp
└─ PowerMode
```

This aggregate means:

> these specific features have a useful shared frontend projection.

It does **not** mean:

> every Device/global setting must eventually be added here.

Therefore future features must not be added to `FrontendDeviceQuickSettingsSnapshot` automatically just because they appear on the Main UI Device page or are globally scoped.

Before adding any future child member, the work order must explicitly establish that the feature belongs to the shared Quick Settings subset.

---

## 4. Correction to the Battery Charge Limit extensibility example

The original PR1 work order used Battery Charge Limit as an example of a future typed extension.

Read that example conditionally.

Correct interpretation:

```text
future Battery Charge Limit Runtime feature implemented
        ↓
product decides exposure scope
        ↓
if Main UI only
    → keep it out of shared QAM/Overlay Quick Settings projection

if later explicitly approved for Quick Settings surfaces
    → then add the smallest typed shared projection needed by those surfaces
```

Do **not** add a Battery Charge Limit field to `FrontendDeviceQuickSettingsSnapshot` merely because the feature becomes production-ready.

Do **not** pre-reserve a placeholder field.

Do **not** expose its WMI mutation through QAM or `.Overlay` without an explicit feature work order approving that surface.

The same rule applies to future:

- fan/fan curve controls;
- vibration strength;
- controller-device settings;
- display/device options;
- telemetry-related settings;
- any other Main UI feature.

---

## 5. Main UI remains the superset-capable management surface

The desktop Main UI may expose product configuration that is intentionally unsuitable for a compact Quick Settings surface.

Examples of reasons a feature may remain Main-UI-only:

- destructive or high-impact operation;
- complex multi-step configuration;
- setup/install/recovery workflow;
- rarely changed preference;
- large amount of explanatory UI;
- diagnostic/developer functionality;
- feature that does not make sense during gameplay;
- hardware control whose quick mutation would be undesirable;
- product choice to keep QAM/Overlay focused and small.

Do not force Main UI parity with QAM or Overlay.

Main UI is allowed to have a broader feature set.

---

## 6. QAM exposure must remain explicit

Steam QAM already has an explicit bridge allowlist in `QamFrontendBridge`.

Preserve that architectural property.

A new Runtime/frontend method does not automatically become a QAM method.

For every new QAM-exposed feature:

```text
product explicitly approves QAM exposure
→ add the exact bridge operation(s)
→ add the exact qam.js UI
→ preserve QAM-specific admission policy
```

If the feature is not approved for QAM:

```text
QamFrontendBridge
→ no operation for that feature
```

Do not replace this explicit allowlist with:

```text
generic InvokeFrontendMethod
reflection dispatch
pass-through IAddonFrontendControl
feature registry
schema-less method forwarding
```

QAM should continue to see only the operations it actually needs.

---

## 7. Overlay exposure must remain explicit and narrower than the full frontend API

The same rule is even more important for `.Overlay`.

Existing architecture already says:

```text
.Overlay
≠ full IAddonFrontendControl transport
```

Future Overlay feature work must add only the exact snapshot/mutation messages required by approved Overlay controls.

A Runtime feature can exist without any `.Overlay` representation.

A Main UI feature can exist without any `.Overlay` representation.

A QAM feature can exist without any `.Overlay` representation.

Do not create:

```text
Overlay receives complete AppSettings
Overlay receives every FrontendDeviceQuickSettings member automatically
Overlay generic feature invocation
Overlay generic settings dictionary
```

The `.Overlay` wire remains an explicit product allowlist.

---

## 8. The shared aggregate does not mandate identical consumers

Even when a feature belongs to the shared aggregate, each surface may still present only the subset it needs.

Conceptually:

```text
shared typed snapshot
        ↓
Main UI uses A + B + C
QAM uses A + B + C
Overlay may use A + C
```

This is acceptable if product requirements choose that exposure.

Do not add a requirement that every consumer render every member of a shared DTO.

Likewise, do not split a Runtime authority simply because the UI surfaces differ.

The intended reuse boundary is:

```text
shared truth / typed data contract where useful
≠ forced UI parity
```

---

## 9. No dynamic exposure matrix or feature registry

This selective exposure policy does **not** justify a new metadata/configuration framework.

Do not add:

```text
FeatureSurfaceMatrix
FrontendCapabilityRegistry
FeatureVisibilityRegistry
SurfacePolicyManager
FeatureDescriptor[]
VisibleInMainUi / VisibleInQam / VisibleInOverlay flags
runtime-loaded exposure schema
plugin/provider discovery
```

The number of product features and surfaces is small.

Use explicit code and focused work orders.

Preferred model:

```text
feature work order
→ explicitly says supported surfaces
→ only those transports/UI consumers are changed
```

This is simpler and easier to review than a second authority describing UI visibility.

---

## 10. PR1-specific implementation rule

`SHARED-FRONTEND-01` remains focused on:

```text
CPU Boost
TDP
Windows Power Mode
```

because current source already has Main UI and QAM consumers for all three.

The PR must not broaden its aggregate by scanning other `IAddonFrontendControl` methods or Main UI settings.

Specifically, do not pull unrelated existing features into the aggregate, including examples such as:

- Center M authority/startup controls;
- launch-at-startup;
- logging/developer settings;
- OEM1/WING mapping;
- setup/prerequisite operations;
- vibration diagnostic;
- fan diagnostic;
- sensor diagnostic;
- game catalog/profile management outside the separately defined Profile scope.

Those remain outside this Device Quick Settings aggregate unless a later explicit product work order says otherwise.

---

## 11. Future work-order requirement

Every future shared-frontend feature work order must include an explicit section:

```text
Supported frontend surfaces
```

with a concrete decision such as:

```text
Main UI: Yes
Steam QAM: No
Addon Overlay: No
```

or:

```text
Main UI: Yes
Steam QAM: Yes
Addon Overlay: Yes
```

Do not infer surface exposure from:

- feature location in the Main UI;
- whether it is Device/global;
- whether a typed frontend contract exists;
- whether another compact surface exposes a similar feature;
- whether adding another transport method is technically easy.

Only the explicit product work order decides exposure.

---

## 12. Review standard

Review future PRs for accidental surface widening.

A real blocker includes:

```text
Main-UI-only feature accidentally exposed through QAM or .Overlay
```

or:

```text
new generic pass-through makes future IAddonFrontendControl methods reachable from a surface without explicit review
```

Do not block on theoretical concerns about having separate explicit switch/dispatch cases across surfaces. That duplication is intentional when it preserves a clear allowlist and avoids a generic authority/registry abstraction.

The design target is:

> **one Runtime authority per real feature, typed frontend projections where useful, and an explicit product-selected allowlist for each UI surface. Shared backend truth must never be confused with mandatory frontend parity.**
