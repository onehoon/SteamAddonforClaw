# RE: MSI Claw Joystick LED Control

Status: this document retains the historical protocol/reverse-engineering notes and
records the current production references. HHC's current exact firmware map includes EX
`0x0411` and `0x0414`; CG3EM/MS-1T91 field firmware `0x0419` is now an explicit
SteamAddon exact entry resolving to the same `0x024A` RGB base. SteamAddon's Basic
Static implementation still uses exact table matches and fails closed for every other
unknown version. This does not claim that the `0x0419` SteamAddon path has passed
physical hardware acceptance.

## 2026-10-18 — Full1902 LED animation implementation design and handoff

> **Status: approved scope / implementation plan, NOT an implemented feature.** The current
> shipped implementation remains **Basic Static only** until the PRs below are merged.
> This section is the continuing design and handoff authority for the four LED PRs;
> it supplements the Full1902 lifecycle documents, not a new controller architecture.

### Evidence and precedence for a new Codex session

Read these in order before implementation:

1. `docs/Full 1902 Implementation/README.md` and its ordered controller-authority documents.
   **Full1902 is a standalone App; CTW is NOT a runtime dependency or integration target.**
2. This document, including the corrected packet/readback fields below.
3. The completed MSI REA report on the developer machine:
   `C:\GoogleDrive\Addon\MSI\MSI_CENTER_M_LED_FAN_DEEP_REA_REPORT.md`
   (2026-10-18 `Actual UI Cross-Check and LED Protocol Completion` section).
   When supplied as an attachment, the updated copy is
   `MSI_CENTER_M_LED_FAN_DEEP_REA_REPORT(1).md`. The separate Manual Fan Control
   chapter is background only and must not be changed by these LED PRs.
4. All nine MSI Center M reference screenshots:
   `C:\GoogleDrive\Addon\MSI\센터엠LED실제UI\` — `20261018-122702.jpg`,
   `20261018-122724.jpg`, `20261018-122730.jpg`, `20261018-122736.jpg`,
   `20261018-122742.jpg`, `20261018-132443.jpg`, `20261018-132453.jpg`,
   `20261018-132459.jpg`, `20261018-132507.jpg`.
5. Existing `docs/work-order/FULL1902_CONTROLLER_LED_STATIC_BASIC_WORK_ORDER_2026-10-03.md`,
   `docs/work-order/OVERLAY_SHARED_PRODUCTION_CONTROLS_PARITY_WORK_ORDER_2026-10-04.md`,
   then the **current implementation**, not historical work orders in isolation.

The MSI `UC_MysticLight` / `API_MysticLight` / `API_ControlMode` / server binaries are
**different versions**. The reported UI→API→HID relationship is confirmed in each
inspected managed artifact, but not as a single matched-build runtime trace.
Calculated HID commands are **serializer-derived examples, not USB captures**.
No PID1902 animated-effect physical acceptance or ROM durability is established.

### Locked product scope

| Feature | Target |
| --- | --- |
| Physical controller | Existing strongly-identified Addon-owned **PID1902** vendor control HID only |
| LED On/Off | Keep existing global Enabled; off means effective brightness 0, saved settings retained |
| Brightness | Retain the Addon's existing **0–100** preference, not MSI's registry-only 0/100 toggle |
| Named styles | **Static (Steady), Breathing, ColorCycle, Wave** |
| Color choice | **Rainbow/Type1** fixed MSI pattern OR **Custom/Type2** RGB swatches |
| Custom palette lengths | Static **1**; Breathing **2**; ColorCycle **3**; Wave **4** |
| Animated speed | Slow / Medium / Fast for Breathing, ColorCycle, Wave |
| Direction | Clockwise / Counterclockwise for Wave only |
| Settings ownership | Existing one `AppSettings.ControllerLed`, `settings.json`, `StartupSettingsCoordinator` |
| Frontend surfaces | Main Controller page and existing Overlay Controller section share the same typed mutation |
| Device persistence | **No SyncToROM**; preserve Addon settings and reapply on existing owned-controller recovery |

**Explicitly out of scope:** MSI Profile 1/2/3, InfoMode, internal Customize
enum, separate Off *effect* tile, individual ring-segment/ABXY coloring,
battery/SoC/AI-based colors, per-game LED profiles, additional frontend pages,
MSI DLL/SDK/service calls, CTW integration, new animation scheduler/manager,
new firmware probing, new process/ownership authority, and speculative race machinery.
The global **On/Off** toggle remains supported; it is not the MSI Off effect tile.

### MSI mode semantics: source-confirmed versus chosen product defaults

**Source-confirmed (static REA):**

- `RGBModeType.Type1` is MSI's **Rainbow/default preset**, not a random effect and
  not a fifth animation mode. For Static, the two stick rings each contain
  red/yellow/green/blue and the grouped ABXY entry is red, in **one** frame.
- `RGBModeType.Type2` uses ordered Custom swatches. MSI's gear picker opens a
  swatch editor, not a per-physical-zone editor; its Done/Cancel staging and
  five recent colors do not require reproduction in Addon.
- For the inspected `SyncToLights` UI receiver, every style uses
  `ActiveAnimationIndex=0` and clears **all eight frames of bank 0** before
  creating the selected style. The other three banks are not mapped to the
  four named modes. One unused helper selects bank 1 but is not reached by
  the inspected UI route. **Do not implement four effect-specific banks.**

| Style | Type1 / Rainbow frame recipe | Type2 / Custom frame recipe | Count | Slow / Medium / Fast stored speed |
| --- | --- | --- | ---: | --- |
| Static | One ring pattern red/yellow/green/blue per stick; ABXY red | One selected RGB color over all nine triplets | 1 | Fixed `3` |
| Breathing | Pattern, then black | Color 1, black, Color 2, black | 2 / 4 | `9 / 6 / 3` |
| ColorCycle | Six solid frames: red, yellow, green, cyan, blue, magenta | Three solid frames in swatch order | 6 / 3 | `7 / 5 / 3` |
| Wave | Four rotating red/yellow/green/blue ring patterns; ABXY current leading color | Four rotating custom-color ring patterns; ABXY leading custom color | 4 | `6 / 3 / 0` |

Each frame is 9 × RGB triplets (27 bytes); ring assignments and ABXY in the
table are **serialized data**, not a demonstrated physical zone map.
For counterclockwise Wave the color orders (each ring) are
`RYGB → YGBR → GBRY → BRYG`; clockwise is
`RYGB → BRYG → GBRY → YGBR`. Type2 uses the same positional rotation.
The speed byte is `abs(internalSpeed - 20)`, with the above per-style mappings;
do not serialize the UI enum ordinal as the speed byte.

**Custom swatch initial values recovered from MSI `List_Profile_Init`:**

| Style | Type2 initial colors |
| --- | --- |
| Static | `#00FFFF` |
| Breathing | `#00FF80`, `#FFB90F` |
| ColorCycle | `#FF0000`, `#FF3C3C`, `#FF9696` |
| Wave | `#FF0000`, `#FF3C3C`, `#FF9696`, `#FFFFFF` |

**Observed on the user's unmodified Center M UI, adopted as Addon defaults**
(but not proven as binary initializer constants): Breathing **Medium**,
ColorCycle **Slow**, Wave **Medium / Counterclockwise**.
Keep **LED disabled on first install**, **brightness 100**, and **Static**
as the initial style. **Do not reset existing saved Static RGB** (including
the legacy default white `#FFFFFF`) when reading older settings.
The initial Rainbow-vs-Custom selection for an entirely new install is not
proven by the REA as an enum initializer: select one consistent explicit
Addon default during PR2; *never* mislabel the Type1 pattern as the old
single-color Static setting. For migrated Static settings, retain its saved
single color as Custom/Type2 semantics; changing style must not destroy it.
Keep independent per-style color/speed/direction preferences so that switching
back does not discard the user's edits; use typed fields rather than a generic
dynamic profile/preset framework.

### Byte-exact HID implementation boundary

For the **inspected `0x024A` profile base**, the 883-byte `LightProfile` layout is:

`base+0` selector `ActiveAnimationIndex` (1 byte);
`base+1..220` bank 0 (220 bytes);
`base+221..440` bank 1;
`base+441..660` bank 2;
`base+661..880` bank 3;
`base+881..882` audio/rsv bytes.

Bank 0 is **4-byte header** (`ActiveKeyframeCount`, `0x09`,
stored speed, brightness) followed by **8 × 27-byte frames**.
Thus frame 0 is `base+5` (at `0x024F` for `0x024A`).
For all planned named effects, use selector 0 and bank 0 only.
Never assume the other 662 profile bytes are all zero or safe to overwrite.

WriteProfile is a 64-byte HID output report:
`0F 00 00 3C 21 01 <big-endian-offset> <payload-len-1..55> <payload>`,
zero-padded to 64 bytes. Byte **[5]** is the fixed HID profile number **1**;
the displayed MSI software tabs 1/2/3 do **not** map to it. Profile data begins
at report byte **[9]**; at `base`, byte **[9]** is selector 0 and byte **[10]**
is **active frame count**, NOT effect id. This is why `00 04 09 03 64` in
the observed readback means selector 0, **count 4**, constant 09,
speed 03, brightness 100.

MSI's own differential writer maintains a cached full serialized profile,
finds the first/last changed bytes, and emits ≤55-byte chunks before an optional
ROM sync. **Addon need not duplicate that cache or differential logic.**
Recommended simple deterministic animation writer: build the selected
**221-byte selector+bank-0 span** (including zeros for unused frames)
and split into at most 55-byte WriteProfile payloads, preserving the rest
of `LightProfile` untouched. That is normally five 64-byte reports for
221 bytes. Avoid per-frame timers or CPU animation; firmware runs the
submitted keyframes. Do not issue `0x22 SyncToROM`.

**Existing production Static** currently uses **four** writes:
a 32-byte header+frame write at `base`, then identical 27-byte frames
at `base+32`, `base+59`, `base+86`; `ActiveKeyframeCount=1`.
This legacy implementation is not a full bank update and should not
be silently described as equivalent to the new full-bank writer.
PR1 must keep the production Static routine intact; switch the runtime
to the generalized verified builder in PR2, with golden-byte tests
and explicit old-settings behavior. The old unused frames must not be
treated as active keyframes. Do not introduce a second parallel persistent
LED owner or on-device read/diff cache just for optimization.

**Firmware boundary:** current Basic Static knows both `0x024A` and
`0x01FA` bases through exact firmware-version matches. The recent MSI
883-byte serializer is established for the inspected `0x024A` path;
it is **not** proof of identical multi-frame behavior at `0x01FA`.
Keep the existing Static admission/behavior for older exact versions,
and restrict the animation path to explicitly supported exact
model/firmware/layout combinations; do not infer support from nearest
firmware, broad version ranges, or another PID. New physical acceptance
remains a **post-merge user test**, not a Codex PR blocker.

### Existing ownership/lifecycle and compatibility contracts

- Center M **Enabled**: MSI / stock owns PID1901; Addon performs **no** LED HID writes.
- Center M **Disabled**: Addon Runtime owns strongly proven PID1902
  `0xFFF0/0x0040` control HID. Keep its existing resolver,
  physical identity, model/firmware readback and fail-closed checks.
- Keep the existing single persisted desired LED state and **ordered
  frontend mutation → settings save → best-effort hardware apply** path.
  A failed LED apply is reported but must not take down a healthy controller.
- Reapply the current LED preference after existing successful physical
  reacquisition/recovery. LED settings are independent of Steam/BPM,
  virtual Xbox360/SteamDeck/DualSense presentation, and game profiles.
- Preserve settings migration from legacy `Enabled, Brightness, Red,
  Green, Blue` JSON. Missing/malformed animation members get bounded
  defaults **for this feature only**. Never reset unrelated app settings.
- Main UI and Overlay currently both edit `ControllerLedSettings`.
  During staged rollout, existing Static RGB editors must **not silently
  rewrite or reset a non-Static mode/palette**. Until PR4 achieves full
  parity, either restrict the legacy Overlay color editor to Static
  or make an explicitly defined style-aware mutation; retain global
  Enabled/Brightness and preserve every new field.
- Keep one shared typed frontend contract / named-pipe version and update
  all actual writers/readers when the record shape changes. No second
  Overlay LED settings model or authority.
- Hardware testing is the **user's role after merge**. Codex work orders
  require build/unit/contract tests, not a physical device or manual
  test as an acceptance gate. Only realistic lifecycle failures
  (resume/restart/recovery/device loss/failed write) merit guards.
  No extra epochs, locks, state machines, or managers solely for
  pathological asynchronous interleavings.

### Ordered implementation PR plan (four bounded PRs)

These are **phase labels PR1–PR4**, not assigned GitHub PR numbers.
Prepare an actionable work order for each **after reading current main**;
do not assume the 2026-10-18 source snapshot remains current.
One active owner and one settings/mutation path must survive each merge.

| Phase | Main deliverable | Excluded until next phase |
| --- | --- | --- |
| **PR1 — Pure HID protocol** | Correct the legacy byte semantics in this doc; extend/test pure encoder for all four modes, Type1/Type2, 1–6 active frames, exact speed/direction, fixed slot 1, ≤55-byte chunking, unused-frame clearing, exact-address rejection; golden vectors including static, six-color cycle and Wave→Breath. Keep current live Static writes and persisted/frontend contracts unchanged. | No live mode enablement, settings schema migration, UI |
| **PR2 — Single settings + Runtime** | Extend typed `ControllerLedSettings` and `settings.json` loading/saving with backward compatibility and per-mode preferences; reuse `StartupSettingsCoordinator`, existing apply callback/owner and PID1902 HID guard; activate the PR1 encoder for approved models/firmware; reapply on existing recovery; update both frontend transports/contracts and protocol version together. Ensure interim Main/Overlay legacy widgets cannot corrupt non-Static mode. | No new user-facing mode pickers or separate LED manager |
| **PR3 — Main Controller UI** | Extend existing WinUI 3 `ControllerPage` Joystick LED expander with style, Rainbow/Custom, mode-dependent 1–4 color pickers, speed, Wave direction, defaults and localization. Reuse its existing mutation/save chain and shared snapshots; no new detail page, no MSI-style profile tabs. | No separate UI state authority or new Overlay implementation |
| **PR4 — Overlay parity** | Extend existing Overlay Controller LED controls to the same four modes, swatches, speed and direction via the *same* `ControllerLedSettings` mutation, with existing Overlay navigation/commit style. Remove interim restrictions introduced only for PR2/PR3. | No new Overlay LED schema, game profile overrides or generic control engine |

**Cross-PR acceptance (automated only for Codex):**

1. Pure byte-vector tests: all mode/Type1/Type2 recipes, frame
   counts, speed mappings, Wave direction, bank-zero only, exact bytes
   at report offsets and ordered 55-byte max chunking; full inactive
   frame zeroing on shorter-mode re-apply.
2. Existing Static golden-output tests pass in PR1; after PR2, old
   saved `Enabled/Brightness/RGB` still produce the expected uniform
   Static behavior. Default off never lights the hardware through
   a stale active setting.
3. No LED WriteProfile on weak/ambiguous identity, wrong PID/usage,
   unsupported model/firmware, Center M Enabled, or failed admission;
   no `SyncToROM` introduced.
4. Persistence and frontend round-trip tests preserve all mode-specific
   settings across reopen, style switch, Main/Overlay mutation,
   restart/reacquire, and old settings load. Intermediary legacy
   Overlay edits do not discard animation fields.
5. Multi-report failures stop the sequence, report the failure,
   and do not alter controller authority, HidHide, VIIPER,
   physical mode, or user-saved desired state. A subsequent normal
   reapply generates a complete intended bank 0.
6. Updated build/tests and localized UI contracts compile without
   requiring any physical hardware. **Hardware effect correctness
   is explicitly pending post-merge user validation**, not a PR
   acceptance blocker.

### Handoff checklist / status ledger

**As of 2026-10-18:** REA and user scope decisions are complete;
the four animation PRs are **not started**. Only Basic Static
is in production. Mark a phase implemented only from a merged
GitHub commit; update this ledger after each merge.

| Phase | Status at design handoff | Merge reference / follow-up |
| --- | --- | --- |
| PR1 Pure protocol | **Planned** | Not assigned |
| PR2 Settings/Runtime | **Planned** | Not assigned |
| PR3 Main UI | **Planned** | Not assigned |
| PR4 Overlay parity | **Planned** | Not assigned |

Open, **non-blocking** evidence gaps: MSI artifact versions do not
form a matched runtime release; the `0x01FA` animation layout is not
confirmed; physical per-zone ordering is unresolved (and zone-editor
support is out of scope); global MSI registry toggle → ControlMode
sync is not established (Addon already has its own brightness path);
bank 1–3 selector semantics and ROM persistence are out of scope.
Do not open more REA rounds for these gaps unless an implemented
feature genuinely depends on them or a post-merge physical test
reveals a reachable correctness issue.

### Exact current code anchors

- `src/SteamInputAddonforClaw.Contracts/ControllerLed/ControllerLedSettings.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedProtocol.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedController.cs`
- `src/SteamInputAddonforClaw/Settings/AppSettings.cs`,
  `src/SteamInputAddonforClaw/Settings/SettingsStore.cs`,
  `src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`,
  `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`,
  `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`,
  `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml` and `.xaml.cs`,
  `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs`
- `tests/SteamInputAddonforClaw.Tests/ControllerLedTests.cs`,
  `ControllerLedFrontendTests.cs`,
  `ControllerLedLifecycleContractTests.cs`,
  `tests/SteamInputAddonforClaw.UiTests/ControllerLedUiContractTests.cs`

## 2026-10-10 A2VM-family production admission

The production Static path now admits A2VM 7 and A2VM 8 only when the exact current PID1902 HID attributes report firmware `0x0230`; the existing exact map resolves that version to RGB base `0x024A` and emits the same four frame writes. This is an A2VM-family policy based on the A2VM 8 matching ReadProfile response and the reported shared firmware generation. A2VM 7 has not been physically tested, and physical LED effects have not been verified on A2VM 8 either. CG3EM/EX retains only its existing exact firmware mappings. Unknown versions and other models reporting `0x0230` remain rejected. LED profile writes are separate from runtime rumble report `0x05` and from the left/right vibration-strength pair profile write.

## Transport and packet

LED control uses the controller vendor HID channel, not WMI:

```text
PID 1901: usage page 0xFFA0 / usage 0x0001
PID 1902: usage page 0xFFF0 / usage 0x0040
Output report: 64 bytes, preamble 0F 00 00 3C
Command:       21 01 (WriteProfile)
Historical sync command: 22 (SyncToROM; not issued by the current static setter)
```

The firmware-specific RGB profile base is selected by the exact firmware table below.
The first 32 profile bytes starting at base (as carried in a 64-byte
WriteProfile report) are decoded below for the inspected `0x024A` layout.
Report byte [5] is **HID profile number 1**; bytes [6..7] are the
address and [8] is the payload length. These are **report** offsets,
not offsets within the profile payload:

| Report byte | Meaning |
| ---: | --- |
| `[9]` | `ActiveAnimationIndex`, observed `0x00` (bank 0) |
| `[10]` | Bank 0 `ActiveKeyframeCount`, `1..8`; **not an effect id** |
| `[11]` | Bank constant `0x09` |
| `[12]` | Stored animation speed; Static uses `0x03` |
| `[13]` | Brightness; Addon currently `0..100` (`0x64` = 100) |
| `[14..40]` | First frame: nine RGB triplets (27 bytes) |

The first eight serialized RGB entries describe two sets of four ring
quadrants; the ninth describes the **grouped ABXY buttons**.
The examined MSI managed serializer orders **left-ring 0..3,
right-ring 4..7, ABXY 8**, while the earlier Addon/CTW note
assumed **right before left**. The actual PID1902 **physical**
quadrant/left-right mapping is **not established**. Current
single-color Static sends the same RGB to all nine entries, so it
cannot distinguish either ordering. Do not treat the old physical
zone map as proven or implement independent zone editing from it.
This is a 27-byte RGB frame, not an EC/WMI command.

## Readback

The working read command is `ReadProfile (0x04)`, not the older guessed
`ReadRGBStatus (0x0D)`:

```text
request:  0F 00 00 3C 04 01 <addrHi> <addrLo> 20
response: 10 00 00 3C 05 01 <addrHi> <addrLo> 20 00
          <frame-count> 09 <speed> <brightness> <9 x RGB>
```

The response acknowledgement is `0x05` at byte `[4]`; in the inspected profile layout `[9]` is the bank selector and `[10]` is the active frame count (for example `0x04` = four frames), while `[11]` is the observed `0x09` block constant. The actual
A2VM 8 / MS-1T52 firmware `0x0230` response prefix read at candidate address
`0x024A` was:

```text
10 00 00 3C 05 01 02 4A 20 00 04 09 03 64 ...
```

The response included 27 RGB bytes at `[14..40]`. The current Basic Static setter
uses the exact firmware/model admission and fills all four frame slots instead of
reading profile state on each edit.

## Current firmware address evidence

Handheld Companion's current MSI Claw source maps the following USB firmware versions
to RGB profile addresses. In particular, its MS-1T91 / EX entries are present in the
current table, superseding the older note that `0x0411` was absent.

| Firmware version | RGB address |
| ---: | ---: |
| `0x0163` | `0x01FA` |
| `0x0166` | `0x024A` |
| `0x0167` | `0x024A` |
| `0x0211` | `0x01FA` |
| `0x0217` | `0x024A` |
| `0x0219` | `0x024A` |
| `0x0308` | `0x024A` |
| EX `0x0411` | `0x024A` |
| EX `0x0414` | `0x024A` |
| EX `0x0419` | `0x024A` |

SteamAddon additionally has one exact A2VM-family entry: firmware `0x0230` → `0x024A`, admitted for models `msi.claw.a2vm.7` and `msi.claw.a2vm.8` only. Runtime still reads the actual current HID firmware; model identity alone never substitutes for the version check.

Source: [Handheld Companion `ClawA1M.cs`](https://github.com/Valkirie/HandheldCompanion/blob/master/HandheldCompanion/Devices/MSI/ClawA1M.cs).
HHC's exact MS-1T91 table currently ends at `0x0414`. The tested CG3EM/MS-1T91
reports controller firmware `0x0419`; CTW's deployed production resolver selects the
nearest EX entry (`0x0414`) and therefore `0x024A` for that firmware. SteamAddon does
not adopt CTW's general nearest-match policy: `0x0419` is promoted to one explicit
known entry, while every other unknown version still fails closed with zero profile
writes. Physical SteamAddon LED acceptance for `0x0419` remains pending.

On 2026-10-09, the A2VM 8 / MS-1T52 `VersionNumber=0x0230` ReadProfile response at
candidate `0x024A` echoed the requested profile index/address/length and returned
the expected block, including **active frame count `0x04`**, constant `0x09`, and 27 RGB bytes.
SteamAddon enables the existing four-write **Static** path on A2VM 7/8 only when
the current PID1902 control-HID attributes report firmware `0x0230`. The production
apply boundary rejects the same firmware on CG3EM, unknown models, or any other
model. A2VM 7 uses the shared-family policy and is not individually hardware-tested.
This exact admission does not add nearest-version fallback or a firmware range.

The matching readback supports the address selection but does **not** prove that
Static writes produce the desired physical LED behavior; that acceptance remains
pending user testing for both A2VM 7 and A2VM 8. The explicit Developer-only A2VM 8 read probe remains
read-only: it issues one bounded `ReadProfile` query at candidate `0x024A`, index
1, block length `0x20`, only on the current healthy Addon-owned PID1902
`0xFFF0/0x0040` control HID with exact firmware `0x0230` and Center M exactly
Disabled. It validates the 64-byte response and logs only the bounded profile
block. The probe itself issues no LED `WriteProfile` (`0x21`) or `SyncToROM`
command and does not claim a physical LED change.

## Historical device-specific RE notes

| Controller firmware | Historical evidence | RGB address |
| --- | --- | --- |
| A2VM `0x229` | on-device read/write RE; nearest firmware table entry | `02 4A` |
| A2VM `0x308` | device control-surface RE | `02 4A` |
| A2VM 7/8 `0x0230` | A2VM 8 matching PID1902 ReadProfile response; family-scoped Static production admission | `02 4A` |
| EX `0x0411` | current HHC table | `02 4A` |
| EX `0x0414` | current HHC table | `02 4A` |
| EX `0x0419` | CG3EM/MS-1T91 field firmware; CTW nearest-match resolves to EX `0x0414` | `02 4A` |

The table is evidence for address selection, not a SteamAddon hardware acceptance
result. Neither A2VM 7 nor A2VM 8 has physical LED-effect acceptance recorded here;
`0x0419` is an explicit exact SteamAddon entry but still awaits physical LED
smoke-test confirmation. All other unknown versions remain unsupported; do not probe
arbitrary EEPROM addresses or generalize CTW's nearest-match behavior.

## CTW production Static reference

The historical CTW `release/v0.3.98.0` static setter writes the value `0x01` in what newer MSI serializer evidence identifies as the **active frame-count** byte (formerly called a Static mode byte in the older reference), with speed `0x03`,
and one RGB triplet repeated across all nine zones. It writes the base header block,
then fills the remaining three frame slots with identical raw 27-byte RGB payloads at
`base + 32`, `base + 59`, and `base + 86`. It does not issue SyncToROM in its normal
static setter. Source: [CTW `MsiClawLedController.cs`](https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/Devices/MSIClaw/MsiClawLedController.cs).

At this 2026-10-18 design handoff, SteamAddon **currently implements only Basic Static**:
On/Off, global brightness, and one uniform color. Breathing, ColorCycle, Wave,
Rainbow/Custom, speed and direction are **planned**, not shipped (see PR1–PR4 above).
Battery/SoC and per-zone editing remain **out of scope**.

The SteamAddon base packet contains the Static header and first frame. The three
follow-on packets contain raw frames only (no effect header).

The firmware version must exactly match an enabled entry: the exact versions in the
table above or the separately scoped A2VM 7/8 / `0x0230` admission. Effective hardware
brightness is the saved brightness while enabled and zero while disabled; the saved
brightness and color are retained. SteamAddon sends no `0x22` SyncToROM command.

## Effects and stale-frame hazard

**Historical experiments (superseded interpretation):** older write-side
notes labeled `0x01` Static, `0x06` Breathing, and `0x04` a breathing
effect. The newer byte-exact MSI profile serializer identifies this
field as **active frame count**, not the effect id. Do **not** implement
the old `0x01/0x04/0x06` effect-id interpretation.
The current plan is the deterministic multi-frame bank-0 recipe
defined in the 2026-10-18 design section above, with stale-frame
clearing. A work order is still required before production coding;
hardware acceptance is a subsequent user activity, not a PR blocker.

## Evidence

- Completed MSI Center M UI-to-protocol REA:
  `C:\GoogleDrive\Addon\MSI\MSI_CENTER_M_LED_FAN_DEEP_REA_REPORT.md`
  (2026-10-18 revised, see the LED chapter, not the fan chapter).
- The nine screenshots in
  `C:\GoogleDrive\Addon\MSI\센터엠LED실제UI\`;
  [Google Drive MSI folder](https://drive.google.com/drive/folders/1YNYHO7O5QJ_khzKAkj3LygLCWPCz3sIU).

- [todo-read-led-firmware-state.md](https://drive.google.com/file/d/1SHAdriD_MF58d1Uu71Z-IVu1jppCPvSx/view)
- [todo-led-effects-msi-claw.md](https://drive.google.com/file/d/1477Q7JaAYj3lDF2iMlxyJYDpmqZzk9Lx/view)
- [clawtweaks-hid-protocol.md](https://drive.google.com/file/d/1TuI-21v5nT0u6hdSksUfKLuPCSHyB2d-/view)
- [device-a2vm-control-surfaces.md](https://drive.google.com/file/d/1ZHrmSEZbDq5s41RyhbmxyxCspfOCKhvy/view)
- [device-claw8ex-panther-lake.md](https://drive.google.com/file/d/1qWgcszUg4BFtllHrs3dP826sxAr3NZaF/view)
