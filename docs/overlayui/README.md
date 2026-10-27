# Overlay UI Documentation Index

> **Current architecture (2026-09-22):** The WinUI3 Overlay is the only Addon-owned Quick Settings surface. Steam's native Quick Access Menu remains Steam-owned and is invoked only through the existing Steam Deck Quick Access system-button pulse. There is no Addon QAM tab, QamHost, CDP, or CEF patching integration. Any older QAM references in this folder are historical context only.

> **Repository index:** `docs/overlayui/README.md`
> **Date:** 2026-09-02

This repository index records Overlay policy precedence. The architecture and UI design references are preserved in Google Drive; implementation work orders are not retained in this documentation tree.

## Document hierarchy

1. [ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md](https://drive.google.com/file/d/12qwP48hjfmy5geXHM8CadFhiBhLLPQVk/view?usp=drivesdk)
   - process / IPC / controller capture / lifecycle / feature-authority boundaries
2. [ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md](https://drive.google.com/file/d/1sornwHrPAI0JRR-EfX3YB0cdYijgkuqK/view?usp=drivesdk)
   - visible shell / tabs / controller navigation / common-control UX / Shortcut design

## Current precedence / correction

The retained UI design reflects the completed OQ4 controller-capture direction.

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

The [architecture document](https://drive.google.com/file/d/12qwP48hjfmy5geXHM8CadFhiBhLLPQVk/view?usp=drivesdk) header still contains older planning-era wording that says the Overlay process/transport/capture path is design-only. That status text is stale: the POC/window/transport lifecycle and OQ4 capture foundations now exist on `main`. Treat the architecture document as an ownership/lifecycle contract, not as the current implementation-status tracker.

## Current UI contract snapshot

The Overlay UI design records these frozen decisions:

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
