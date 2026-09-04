# Steam Addon for Claw — App UI Information Architecture

> **Date:** 2026-09-04  
> **Status:** Design authority for the next app navigation/UI cleanup  
> **Scope:** Main navigation, page ownership, Status removal, Settings cleanup, controller/device responsibility split, and placement rules for planned features

---

## 1. Goal

The current app navigation reflects an earlier product phase where routing status and startup preferences were exposed directly to the user.

Full1902 changes that product model substantially:

```text
Center M Enabled
→ MSI / stock controller authority

Center M Disabled
→ Addon Runtime controller authority
→ PID1902 desired continuously
→ Addon background Runtime required

Steam/BPM inactive
→ Xbox360 presentation

Steam/BPM active
→ SteamDeck presentation
```

The UI should now be organized around what the user is actually trying to configure:

1. the handheld device;
2. the controller and its buttons/behavior;
3. per-game overrides;
4. help;
5. advanced component diagnostics and developer tools.

The target is not to create more pages. The target is to give every user-facing function one obvious home.

---

## 2. Final Navigation Order

The main NavigationView order should become:

```text
Device
Controller
Profile
How to Use

----------------
Settings
```

`Settings` remains the standard NavigationView Settings destination.

### Remove

```text
Status
```

Do not replace Status with a new Home, Dashboard, Overview, or equivalent placeholder page.

### Default page

After Status is removed:

```text
Default page = Device
```

Device is the most appropriate first page because it describes and configures the physical handheld baseline that all other functions depend on.

---

## 3. Page Ownership Model

Use the following ownership rule throughout future UI work.

### Device

`Device` owns settings that apply to the MSI Claw as a handheld system rather than specifically to controller input.

Examples:

```text
Device identity / support information
MSI Center M authority
TDP Control
CPU Boost
Windows Power Mode
Fan Control
Battery Charge Limit
```

### Controller

`Controller` owns controller input, physical controller behavior, button behavior, controller lighting, and controller vibration.

Examples:

```text
WING mapping
OEM1 / Center M button mapping
M1 mapping in Xbox360 presentation
M2 mapping in Xbox360 presentation
Joystick LED
Vibration Strength
```

### Profile

`Profile` owns per-game overrides only.

Examples already implemented:

```text
TDP
Intel FPS Limit
CPU Boost
Windows Power Mode
Resolution
```

A feature should not automatically appear in Profile merely because it exists globally.

Only add a per-game override when there is a real product use case for applying that value differently per game.

### Settings

`Settings` owns app-level diagnostics and advanced/hidden entry points that do not belong to a normal configuration page.

Target contents:

```text
Required Components / Components
Developer Menu
```

It should no longer contain a user-facing Windows startup preference.

---

## 4. Remove the Status Page

The current Status page contains three kinds of information:

```text
1. Device identity / supported-device information
2. Current Steam game status
3. Routing Components state
```

These do not justify a dedicated page anymore.

### 4.1 Device identity moves to Device

Move the useful device summary from Status to the top of Device.

Suggested presentation:

```text
Device

[MSI Claw 8 AI+ / EX AI+]
Board / model / GPU information
Supported
```

Keep this compact. It is identity/context, not a second status dashboard.

### 4.2 Current Steam game status

Do not preserve the current Status-page Steam-game row solely to maintain feature parity.

The Profile page already owns game-centric behavior.

If a current-game indicator is still useful after the navigation cleanup, place it in or near the Profile page header rather than recreating Status elsewhere.

It is also acceptable to omit the indicator entirely if it does not materially help the user.

### 4.3 Routing Components moves to Settings

Move user-readable component state to Settings.

The user should still be able to inspect whether the required controller stack is available without entering Developer Menu.

Initial component set:

```text
HidHide
usbip-win2
VIIPER / VIIPERZ
```

The exact display name should match the final product terminology used by the runtime.

Recommended UI:

```text
Settings

[Required Components]
HidHide          Ready
usbip-win2       Ready
VIIPER           Ready

[Developer Menu]
Developer-only diagnostics
```

This section is primarily diagnostic and should normally be read-only.

Do not expose internal repair/mutation controls merely because component state is visible here.

Runtime lifecycle/reconciliation remains the authority for component setup and recovery.

---

## 5. Remove `Launch at Windows startup`

The current Settings page exposes:

```text
Launch at Windows startup
Developer Menu
```

The startup option should be removed.

### Reason

Under Full1902, background startup is no longer an ordinary user preference while the Addon owns controller authority.

The current product contract is:

```text
Center M Disabled
→ Addon Runtime authority
→ Addon Runtime mandatory
→ required startup task must exist and verify successfully
```

Therefore startup registration is lifecycle infrastructure, not a preference toggle.

The user should not be presented with a control that implies the required Runtime can safely be disabled while Addon Controller Mode remains active.

### UI requirement

Remove:

```text
Launch at Windows startup
```

from Settings.

Do not replace it with:

```text
Always on
Required
Managed automatically
```

or another disabled/read-only card.

There is no need to spend permanent UI space explaining an internal lifecycle requirement.

### Product-code direction

The eventual cleanup should remove obsolete preference semantics rather than merely hide the card.

The authority transition owns the startup requirement:

```text
Center M Disabled
→ startup task required and verified

Center M Enabled
→ Addon no longer owns the controller
```

The exact stock-mode startup cleanup policy should follow the active Full1902 authority documents and the implementation work order that performs the removal. This UI design does not create a second startup authority or persisted user preference.

---

## 6. Device Page — Final Responsibility

The Device page becomes the home for system-wide handheld configuration.

### Current features to keep

```text
TDP Control
CPU Boost
Windows Power Mode
```

### Move here from Controller

```text
MSI Center M authority / Enable-Disable transition
```

The Center M control does not fundamentally describe a button.

It selects controller authority:

```text
Center M Enabled  → MSI / stock authority
Center M Disabled → Addon Runtime authority
```

That makes it a device-level control and a better fit for Device than Controller.

### Move here from Status

```text
Device identity / support summary
```

### Planned future features

Add when implemented:

```text
Fan Control
Battery Charge Limit
```

### Suggested long-term order

```text
Device

[Device identity / support]

[MSI Center M]

[TDP Control]
[CPU Boost]
[Windows Power Mode]

[Fan Control]            -- only when implemented
[Battery Charge Limit]   -- only when implemented
```

Do not create empty placeholders for future features.

---

## 7. Controller Page — Final Responsibility

The Controller page becomes the single user-facing home for controller behavior.

Its meaning should be clear:

> Configure controller buttons and controller-specific hardware behavior.

### 7.1 Button Mapping

The first logical section is button mapping.

Target features:

```text
WING
OEM1 / Center M button
M1
M2
```

#### WING / OEM1

Full1902 changes the meaning of these mappings because Steam/BPM now selects presentation rather than enabling/disabling a separate routing mode.

Do not preserve wording based on the removed model such as:

```text
when Steam Input Routing is inactive
```

Mapping labels/descriptions must instead describe the actual presentation/state in which the mapping applies.

#### M1 / M2

M1/M2 mapping is specifically important for Xbox360 presentation.

Xbox360 does not expose native SteamDeck rear buttons, so Full1902 needs an explicit product policy for how rear physical buttons behave in Xbox360 presentation.

These controls belong in Controller, not Device or Profile.

### 7.2 Controller Settings

The second logical section is controller hardware behavior.

Planned features:

```text
Joystick LED
Vibration Strength
```

These are global controller settings and belong in Controller even though the underlying implementation may communicate with physical device/EC/native APIs.

The user thinks of them as controller behavior, so the UI should follow the user-facing mental model rather than the internal transport.

### Suggested long-term structure

```text
Controller

[Button Mapping]
WING
OEM1
M1
M2

[Controller Settings]
Joystick LED
Vibration Strength
```

Again, only implemented features should be visible.

---

## 8. Profile Page — Keep Game-Specific

Profile remains the per-game configuration surface.

Current functionality is already aligned with that role.

Keep the existing game catalog/detail flow and current overrides such as:

```text
TDP
Intel FPS Limit
CPU Boost
Windows Power Mode
Resolution
```

### Do not mirror every Device/Controller setting

Do not automatically add profile versions of:

```text
Battery Charge Limit
Joystick LED
Vibration Strength
Fan Control
M1/M2 mapping
```

A per-game control should be added only after a concrete game-specific need is established.

Examples:

- Battery charge limit is inherently device-wide and should not be profile-based.
- Fan control may become profile-capable later if a real per-game fan policy is designed.
- LED/vibration may become profile-capable only if users actually need game-specific values.

Avoid creating a generic override framework solely for hypothetical future settings.

---

## 9. Settings Page — New Role

Settings remains in the app.

It is no longer a collection of ordinary preferences.

Its near-term purpose is:

```text
Settings

[Required Components]
HidHide          <status>
usbip-win2       <status>
VIIPER           <status>

[Developer Menu]
Developer-only diagnostics
```

### Required Components behavior

The component section should:

- be visible to ordinary users;
- show concise state only;
- support troubleshooting/support screenshots;
- avoid exposing implementation-detail controls;
- avoid duplicating Runtime lifecycle authority;
- avoid presenting transient internal state as a new persisted app state.

Possible high-level display values:

```text
Ready
Unavailable
Error
```

Use the smallest set actually supported by existing reliable component-state sources.

Do not invent a richer state machine purely for UI presentation.

### Developer Menu

Keep Developer Menu in Settings as it exists conceptually today.

It remains conditional according to the existing developer-menu enable mechanism.

Developer diagnostics remain separate from normal user diagnostics.

Examples of developer-only pages/features include existing sensor/fan/vibration probes and other synthetic/testing tools.

Do not promote developer test tools into ordinary navigation solely because Status is being removed.

---

## 10. Future Feature Placement Matrix

| Feature | Page | Notes |
|---|---|---|
| Device identity / support | Device | Moved from Status |
| MSI Center M authority | Device | Moved from Controller |
| TDP | Device | Existing global control |
| CPU Boost | Device | Existing global control |
| Windows Power Mode | Device | Existing global control |
| Fan Control | Device | Future; add only when implemented |
| Battery Charge Limit | Device | Future; global device setting |
| WING mapping | Controller | Button behavior |
| OEM1 / Center M button mapping | Controller | Button behavior |
| M1 mapping | Controller | Primarily Xbox360 presentation policy |
| M2 mapping | Controller | Primarily Xbox360 presentation policy |
| Joystick LED | Controller | Future controller hardware behavior |
| Vibration Strength | Controller | Future controller hardware behavior |
| Per-game TDP | Profile | Existing override |
| Per-game FPS Limit | Profile | Existing override |
| Per-game CPU Boost | Profile | Existing override |
| Per-game Power Mode | Profile | Existing override |
| Per-game Resolution | Profile | Existing override |
| HidHide state | Settings | User-readable diagnostic |
| usbip-win2 state | Settings | User-readable diagnostic |
| VIIPER state | Settings | User-readable diagnostic |
| Developer Menu | Settings | Conditional developer entry |
| Launch at Windows startup | Removed | Runtime lifecycle requirement, not a preference |
| Status page | Removed | Information redistributed to owning pages |

---

## 11. No Empty / Coming-Soon Cards

Do not create cards for unimplemented future features.

Do not add:

```text
Fan Control          Coming Soon
Battery Limit        Coming Soon
Joystick LED         Coming Soon
Vibration Strength   Coming Soon
M1 / M2              Coming Soon
```

### Why

Placeholder cards:

- make unfinished functionality look user-accessible;
- create dead UI;
- force layout decisions before the actual feature design exists;
- often require later rework when implementation constraints are known;
- increase visual noise without adding capability.

The design document defines where future features belong. That is sufficient until each feature is implemented.

When implementation lands, add the real card/expander as part of that feature PR.

---

## 12. Navigation State Cleanup

The navigation state should converge toward the following normal user pages:

```text
Device
Controller
Profile
HowToUse
Settings
```

Developer-only destinations can remain internal/conditional as required.

### Remove normal Status destination

Delete the normal navigation destination/state for Status once its useful contents have been migrated.

### Default state

Change the initial page selection from:

```text
Status
```

to:

```text
Device
```

### Settings remains standard NavigationView Settings

Keep the standard Settings destination rather than creating another top-level Settings menu item.

---

## 13. UI Migration Map

```text
CURRENT

Status
 ├ Device identity/support
 ├ Steam Game
 └ Routing Components

How to Use

Controller
 ├ MSI Center M authority
 └ Center M Button mapping

Device
 ├ TDP
 ├ CPU Boost
 └ Windows Power Mode

Profile
 └ Per-game overrides

Settings
 ├ Launch at Windows startup
 └ Developer Menu
```

becomes:

```text
TARGET

Device
 ├ Device identity/support
 ├ MSI Center M authority
 ├ TDP
 ├ CPU Boost
 ├ Windows Power Mode
 ├ Fan Control               [future]
 └ Battery Charge Limit      [future]

Controller
 ├ Button Mapping
 │  ├ WING                   [when implemented/finalized]
 │  ├ OEM1
 │  ├ M1                     [future]
 │  └ M2                     [future]
 └ Controller Settings
    ├ Joystick LED           [future]
    └ Vibration Strength     [future]

Profile
 └ Per-game overrides

How to Use

Settings
 ├ Required Components
 │  ├ HidHide
 │  ├ usbip-win2
 │  └ VIIPER
 └ Developer Menu
```

The `[future]` entries are architecture documentation only and must not be rendered as empty UI.

---

## 14. Scope Boundaries

This UI architecture intentionally does not redesign:

- Full1902 controller authority;
- HidHide ownership/reconciliation;
- VIIPER native ownership or teardown;
- PID1901/PID1902 transition sequencing;
- Steam/BPM detection;
- WING/OEM1 low-level suppression implementation;
- M1/M2 native input acquisition;
- fan, battery, LED, or vibration hardware protocols;
- per-game override persistence architecture.

Those systems should remain owned by their existing lifecycle/feature layers.

This document only defines where their user-facing controls and diagnostics belong.

Do not create a new UI state authority, manager, wrapper, or persistence layer merely to realize this navigation structure.

---

## 15. Design Principles

### 15.1 One obvious owner per feature

```text
Handheld/system setting     → Device
Controller behavior         → Controller
Game-specific override      → Profile
App diagnostics/dev entry   → Settings
Help                         → How to Use
```

### 15.2 UI follows product semantics, not implementation transport

Joystick LED and vibration may technically be implemented through device/native APIs, but users understand them as controller behavior.

Therefore they belong to Controller.

### 15.3 Lifecycle requirements are not preferences

Mandatory Addon background startup under Addon controller authority must not be presented as an optional user setting.

### 15.4 Diagnostics do not need a dashboard

HidHide/usbip/VIIPER state remains inspectable under Settings without keeping a dedicated Status page.

### 15.5 No speculative UI

Future feature placement should be documented now, but UI is added only when the feature exists.

### 15.6 Avoid overengineering

This information architecture should be implemented primarily by moving/removing existing UI and wiring.

Do not add new abstractions solely because pages are reorganized.

Preserve the existing real lifecycle safety boundaries and owners.

---

## 16. Recommended Implementation Sequence

This design can be implemented incrementally.

### UI cleanup PR

A focused first PR can perform:

```text
1. Reorder main navigation to Device → Controller → Profile → How to Use
2. Make Device the default page
3. Remove Status navigation/page wiring
4. Move device identity/support summary to Device
5. Move MSI Center M authority card from Controller to Device
6. Move Routing Components user-readable state to Settings
7. Remove Launch at Windows startup card and obsolete user-preference UI wiring
8. Keep Developer Menu under Settings
9. Update Controller copy so it no longer references removed Steam Input Routing semantics
```

Do not add future Fan/Battery/M1/M2/LED/Vibration placeholder cards in this PR.

Future feature PRs should add their own UI only when the underlying feature is implemented.

---

## 17. Final Target

The desired user mental model is:

```text
Device
→ How should my Claw itself operate?

Controller
→ How should my controller/buttons/LED/vibration behave?

Profile
→ What should change for this specific game?

How to Use
→ How do I use the Addon?

Settings
→ Are required components healthy, and where are developer tools?
```

This structure should remain valid as Fan Control, Battery Charge Limit, M1/M2 mapping, Joystick LED, and Vibration Strength are added without requiring another navigation redesign.
