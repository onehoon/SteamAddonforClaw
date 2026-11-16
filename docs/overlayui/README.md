# Overlay UI Documentation Index

> **Current architecture (2026-09-22):** The WinUI3 Overlay is the only Addon-owned Quick Settings surface. Steam's native Quick Access Menu remains Steam-owned and is invoked only through the existing Steam Deck Quick Access system-button pulse. There is no Addon QAM tab, QamHost, CDP, or CEF patching integration. Any older QAM references in this folder are historical context only.

> **Repository index:** `docs/overlayui/README.md`
> **Date:** 2026-11-16

This repository index records Overlay policy precedence and the current implementation contract. The architecture and UI design references are preserved in Google Drive; implementation work orders are not retained in this documentation tree.

## Document hierarchy

1. [ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md](https://drive.google.com/file/d/12qwP48hjfmy5geXHM8CadFhiBhLLPQVk/view?usp=drivesdk)
   - process / IPC / controller capture / lifecycle / feature-authority boundaries
2. [ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md](https://drive.google.com/file/d/1sornwHrPAI0JRR-EfX3YB0cdYijgkuqK/view?usp=drivesdk)
   - visible shell / tabs / controller navigation / common-control UX / Shortcut design

## Current precedence / correction

The linked Google Drive architecture and UI design documents preserve their historical planning baselines. Their descriptions of horizontal tabs, reserved Y input, root-level B dismissal, and fixed Shortcut slots are superseded by the current implementation snapshot below. This local snapshot describes the implementation on `main`; it does not rewrite those historical references or claim physical-device validation.

The retained UI design also reflects the completed OQ4 controller-capture direction.

One older statement remains in the [Overlay architecture document](https://drive.google.com/file/d/12qwP48hjfmy5geXHM8CadFhiBhLLPQVk/view?usp=drivesdk): section 14.1-14.4 describes Steam QAM and Addon Overlay as visible-XOR surfaces and proposes closing one before showing the other. **That section is superseded.**

Current product policy is:

```text
Steam QAM may remain visible behind
        ↓
Addon Overlay appears above it
        ↓
OQ4 capture keeps game-facing virtual controller output neutral
        ↓
Steam QAM/game behind receives no controller navigation
        ↓
Addon Overlay closes
        ↓
Steam QAM remains available behind
```

Therefore future Overlay UI work must **not**:

- close Steam QAM merely because Addon Overlay opens;
- inspect/poll Steam-QAM visibility for Overlay UI work;
- stop/restart `QamHost.exe`;
- repurpose `.Qam` for Overlay;
- add an OQ3-B visible-surface manager.

The Main UI relationship is different and remains mutually exclusive with the Addon Overlay through the existing OQ3-A retirement path.

When the older architecture section conflicts with OQ4, the UI design, or current implementation, the newer OQ4/UI policy above wins.

## Implementation-state note

The [architecture document](https://drive.google.com/file/d/12qwP48hjfmy5geXHM8CadFhiBhLLPQVk/view?usp=drivesdk) header still contains older planning-era wording that says the Overlay process/transport/capture path is design-only. That status text is stale: the Overlay process, transport, lifecycle, and OQ4 capture foundations are implemented on `main`. Treat the architecture document as an ownership/lifecycle contract, not as the current implementation-status tracker. The linked UI design is a historical design baseline; use the current source and snapshot below for shipped tab geometry and Shortcut layout.

## Current UI contract snapshot (verified on main, 2026-11-16)

1. **Tabs:** Device / Profile / Controller / Shortcut / Setting appear in a left vertical icon rail. Runtime owns the order; users can change it in **Setting → Quick Overlay Tab Order**.
2. **New Show:** Every new Show starts at Root on the configured first tab. It does not restore the previous tab or an open detail page.
3. **LB/RB/Y:** LB and RB select the previous and next tab. Y performs the configured Quick Jump when frontend settings and its target are available; otherwise it has no effect.
4. **Page navigation:** A activates the selected row. D-pad and either stick navigate selectable rows vertically; Left/Right adjust compatible controls, including held numeric-slider repeat. Mouse and touch use the same native controls.
5. **Back and dismissal:** B or the header Back returns from an active one-level detail to its owning tab Root and restores the originating selection and scroll position. On Root, B uses the existing Runtime dismissal path. Outside click dismisses directly. A Runtime-forced Hide cancels unsubmitted drafts.
6. **Setting:** The Root contains only ClawHUD, Quick Settings, and Quick Overlay Tab Order menu rows. Their controls appear on their respective detail pages.
7. **Controller:** Root contains Controller for Steam, Gyroscope, M1/M2 mapping, the Joystick LED menu row, and Left/Right vibration controls. LED detail contains Enable LED, Brightness, a passive Color swatch, and RGB controls. Brightness/color options collapse when LED is Off. Hardware eligibility and pending-edit authority remain with the existing feature path.
8. **Device/Profile:** Runtime snapshots remain authoritative. Value-only changes update compatible rows incrementally and reuse native Slider/ToggleSwitch controls. Profile artwork and profile state remain on the Profile page.
9. **Shortcut:** Runtime supplies a dynamic three-column tile dashboard with independent two-dimensional selection; it is not a fixed four-slot layout.
10. **Authority:** Runtime owns feature availability, persisted state, and mutation settlement. Overlay projects that state and sends typed intent. There is no global navigation stack or independent feature-state authority.

The superseded Steam-QAM visible-XOR proposal and historical UI descriptions are separate from the current implementation contract above. Steam QAM remains Steam-owned and may remain visible behind the Addon Overlay; Main UI and Addon Overlay remain mutually exclusive through the existing retirement path.
