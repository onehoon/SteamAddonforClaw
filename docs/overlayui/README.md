# Overlay UI Documentation Index

> **Canonical folder:** `docs/overlayui/`
> **Date:** 2026-09-02

This folder is the canonical home for the Addon Quick Settings Overlay architecture, UI/navigation design, implementation roadmap, and focused UI work orders.

## Document hierarchy

1. `ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
   - process / IPC / controller capture / lifecycle / feature-authority boundaries
2. `ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
   - visible shell / tabs / controller navigation / common-control UX / Shortcut design
3. `OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
   - current implementation sequence derived from the architecture + UI design and latest `main`
4. `OQ5_UI_01_FIVE_TAB_OVERLAY_SHELL_WORK_ORDER.md`
   - first focused implementation work order

## Current precedence / correction

The implementation plan and UI design are aligned with the completed OQ4 controller-capture direction.

One older statement remains inside `ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`: section 14.1-14.4 describes Steam QAM and Addon Overlay as visible-XOR surfaces and proposes closing one before showing the other. **That section is superseded.**

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

When the older architecture section conflicts with OQ4, the UI design, or the current implementation PR plan, the newer OQ4/UI policy above wins.

## Implementation-state note

The architecture document header still contains older planning-era wording that says the Overlay process/transport/capture path is design-only. That status text is stale: the POC/window/transport lifecycle and OQ4 capture foundations now exist on `main`. Treat the architecture document as an ownership/lifecycle contract, not as the current implementation-status tracker.

## UI-plan consistency check

The current `OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md` is consistent with the UI design on the important frozen decisions:

- five horizontal tabs: Device / Profile / Controller / Shortcut / Setting;
- user-configurable tab order;
- first tab in that order is selected on every Show;
- no separate default-tab setting and no last-tab restore;
- LB/RB = previous/next tab;
- LT/RT/X/Y reserved;
- B = global Overlay close;
- A = select/activate;
- DPad + both sticks = directional navigation;
- slider Left/Right adjusts immediately without an A/edit mode;
- standard WinUI 3 sizing initially;
- existing QAM-style relaxed slider commit behavior;
- no generalized row reorder;
- Shortcut uses four fixed configurable slots;
- Runtime remains feature and persistence authority.

The only material conflict found during this review is the superseded Steam-QAM visible-XOR section described above.
