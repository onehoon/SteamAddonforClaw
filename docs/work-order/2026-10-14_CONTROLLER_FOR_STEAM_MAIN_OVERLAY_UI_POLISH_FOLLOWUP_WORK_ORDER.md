# Work Order — Controller for Steam UI Polish: Native Main Expander and Overlay Row Parity

**Date:** 2026-10-14  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** current \`main\` after PR #792 (squash commit \`b294850e9a1b3f38d6db4035a76416b608c233a3\`)  
**Scope:** one focused UI/frontend follow-up PR  
**Executor:** local Codex (source changes, build, automated tests). Physical handheld verification is the user's **post-merge** responsibility and is not a PR blocker.

## 1. Decision — exact user-approved visual contract

The Main App **does not** get a separate top-level Gyroscope card or a separate Gyroscope expander.

Main App Controller tab:

\`\`\`text
┌──────────────────────────────────────────────────────────────────────┐
│ [gamepad] Controller for Steam       [Steam Deck  ▼]  [Expand  v]  │
│           Controller used by Steam games / Big Picture              │
└──────────────────────────────────────────────────────────────────────┘
        ↓ when the SAME expander is opened
  ┌──────────────────────────────────────────────────────────────────┐
  │ Gyroscope                                          [On / Off]    │
  └──────────────────────────────────────────────────────────────────┘
\`\`\`

- Replace the current top-level **SettingsCard** with the existing/native **CommunityToolkit \`SettingsExpander\`** pattern.
- The expander header contains **Controller for Steam** and the existing controller ComboBox. Do **not** render a redundant \`Controller\` text label alongside the ComboBox.
- Its \`SettingsExpander.Items\` contains **one** nested \`SettingsCard\` with the title **Gyroscope** and the existing \`GyroscopeEnabledToggle\`.
- The nested Gyroscope row appears only when expanded; the header's ComboBox remains visible and usable while collapsed.
- Initial expansion may be **collapsed** (consistent with existing settings expanders). Expanding/collapsing must not mutate either setting.
- No nested Gyroscope SettingsExpander, no detached Gyroscope top-level card, no new reusable control/helper.

Overlay Controller tab:

\`\`\`text
Controller for Steam              <- section heading, same as M1/M2
    Controller         < Steam Deck >
    Gyroscope          [ On / Off ]

M1 / M2                          <- untouched existing section
    M1                 ...
    M2                 ...
\`\`\`

- The new two rows **must use exactly the same section/detail indentation and row appearance implementation as M1/M2**.
- \`Controller\` and \`Gyroscope\` labels use the **same normal body style**. Only \`Controller for Steam\` is the strong section heading.
- Controller changes must **not** visibly dim, blink, temporarily toggle, or disable the unrelated Gyroscope switch.
- Use existing Overlay row/navigation components; no special CSS/XAML styles, layout values, alternate section-card factory, or Steam-only component.

## 2. Authority and existing-code evidence

Use the project authority docs and source before editing:

- \`docs/Full 1902 Implementation/README.md\` (and its referenced current Full1902 architecture/authority documents).
- \`docs/work-order/2026-10-14_CONTROLLER_FOR_STEAM_MAIN_OVERLAY_GYROSCOPE_WORK_ORDER.md\` — previously implemented function and runtime policies. This **follow-up supersedes only its Main/Overlay layout instructions**; it does not change the global setting/presentation behavior.
- \`docs/overlayui/README.md\` and current Overlay UI design policy.

Verified at current \`main\`:

| File | Concrete observation |
| --- | --- |
| \`src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml\` | Top of Controller page is \`SettingsCard\` with \`StackPanel\` containing a redundant \`Controller\` TextBlock/ComboBox row plus Gyroscope row. ComboBox includes \`Unavailable\` as a third, disabled \`ComboBoxItem\`. |
| \`src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs\` | \`RenderSteamVirtualController()\` selects \`"Unavailable"\` for unprovable active presentation. \`ApplyGyroscopeEnabled\` and existing edit handlers already suppress feedback and use authoritative Runtime state. |
| \`src/SteamInputAddonforClaw.UI/Strings/{en-US,ko-KR}/Resources.resw\` | \`Controller_SteamVirtualControllerCard.Header\`, \`Controller_SteamControllerRow.Text\`, \`Controller_GyroscopeRow.Text\`, \`Controller_SteamControllerUnavailable.Content\` need to be aligned with the revised element/property structure. |
| \`src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs\` | New \`steamSection\` currently inserts \`OverlayValueRow("Controller")\` and \`OverlayToggleRow("Gyroscope", ..., strongLabel: true)\` directly under section heading. Existing \`mappingSection\` uses \`CreateOverlayDetailStack()\` for M1/M2. |
| \`src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs\` | \`CreateControllerSection\` uses \`CreateOverlaySectionHeadingRow\`; \`CreateOverlayDetailStack\` uses the canonical \`QamDetailIndent\` and \`CreateOverlaySectionCard\` is the shared card presentation. |
| \`src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs\` | \`strongLabel: true\` applies \`QamBodyStrongTextStyle\` instead of the normal \`QamBodyTextStyle\`. \`ApplyState\` reassigns \`ToggleSwitch.IsEnabled\` and \`IsOn\` on every call. |
| \`src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs\` | \`_controllerForSteamMutationInFlight\` currently gates **both** controller and gyroscope rows. Controller edit sets it true then renders both, so Gyroscope's \`IsEnabled\` changes \`true → false → true\`; this is the observed visual blink/dim sequence. |
| \`src/SteamInputAddonforClaw.Overlay/App.xaml.cs\` | Controller and Gyroscope edits currently use the same \`OnControllerForSteamEditRequested\` event and \`SendFrontendSettingsMutationAsync(..., controllerForSteam: true)\` completion path. |
| \`src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayClient.cs\` | Existing \`_productionSettingsMutationGate\` already serializes correlated settings requests. Keep it; do not add a second IPC gate or global state authority. |

This task is **not** an IMU sensor, gamepad routing, or Full1902 ownership change.

## 3. Main App: change the card into a native SettingsExpander

### Required layout

Change only the first Controller page section:

1. Use \`ctcontrols:SettingsExpander\` as the parent.
2. Preserve \`Controller for Steam\` header, current header icon and a concise description.
3. Place \`SteamVirtualControllerComboBox\` as the header's **direct content** using the same pattern by which the existing \`ControllerLedExpander\` places a \`ToggleSwitch\` in its header. Avoid a label+ComboBox wrapper.
4. Move \`GyroscopeEnabledToggle\` into \`ctcontrols:SettingsExpander.Items\` → one \`ctcontrols:SettingsCard Header="Gyroscope"\`. Preserve the existing toggle name and \`Toggled\` handler.
5. Keep an accessible expand/collapse affordance independently clickable from the ComboBox; using the ComboBox must not unexpectedly expand/collapse the section.
6. No extra label, extra manual indent, nested Expander, or additional top-level Gyroscope card. The toolkit's template owns the layout and nested indentation.

Implementation sketch: match the exact current WinUI/Toolkit XAML syntax and \`x:Uid\` contract; this is a structural example, not a license to replace existing bindings.

\`\`\`xml
<ctcontrols:SettingsExpander
    x:Name="SteamVirtualControllerExpander"
    x:Uid="Controller_SteamVirtualControllerExpander"
    Header="Controller for Steam"
    IsExpanded="False">
    <ctcontrols:SettingsExpander.HeaderIcon>
        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE7FC;" />
    </ctcontrols:SettingsExpander.HeaderIcon>

    <!-- Use the toolkit's existing header-content slot. -->
    <Grid>
        <ComboBox x:Name="SteamVirtualControllerComboBox"
                  MinWidth="240"
                  SelectionChanged="SteamVirtualControllerComboBox_SelectionChanged">
            <ComboBoxItem x:Uid="Controller_SteamDeckOption"
                          Content="Steam Deck" Tag="SteamDeck" />
            <ComboBoxItem x:Uid="Controller_DualSenseEdgeOption"
                          Content="DualSense Edge" Tag="DualSenseEdge" />
        </ComboBox>
        <TextBlock x:Name="SteamVirtualControllerUnavailableText"
                   x:Uid="Controller_SteamControllerUnavailable"
                   Text="Unavailable"
                   Visibility="Collapsed" />
    </Grid>

    <ctcontrols:SettingsExpander.Items>
        <ctcontrols:SettingsCard x:Uid="Controller_GyroscopeCard"
                                 Header="Gyroscope">
            <ToggleSwitch x:Name="GyroscopeEnabledToggle"
                          x:Uid="Controller_GyroscopeToggle"
                          IsOn="True"
                          OffContent="" OnContent=""
                          Toggled="GyroscopeEnabledToggle_Toggled" />
        </ctcontrols:SettingsCard>
    </ctcontrols:SettingsExpander.Items>
</ctcontrols:SettingsExpander>
\`\`\`

**Important implementation detail:** the sketch's \`Grid\` children overlap without mutually exclusive visibility. Ensure the actual implementation sets \`Visibility\`/placement correctly, uses the native SettingsExpander header content alignment, and does not accidentally put \`TextBlock\` on top of the ComboBox. Prefer a simple one-at-a-time display slot. Preserve existing \`SteamVirtualControllerActivityText\` explanatory status where useful but avoid a permanently oversized header.

### Controller dropdown: only two choices

The user specifically objects to **'사용할 수 없음' / 'Unavailable' appearing as a selectable dropdown option**.

- Remove the \`ComboBoxItem Tag="Unavailable"\` entirely. Exactly two real choices remain: \`Steam Deck\` and \`DualSense Edge\`.
- When Runtime \`ControllerForSteam.Available == false\` (unable to prove the applied Steam-facing controller), show a **read-only Unavailable status in place of** the ComboBox, and lock controller selection. Do not falsely show the saved global choice as the active controller.
- When Runtime says editable outside Steam games/BPM, show the saved global preference. When a Steam game/BPM is active, show actual applied SteamDeck/Edge and disable **only** the ComboBox. Gyroscope must remain usable in the nested row.
- While a Steam game/BPM is active, do **not** disable the entire \`SettingsExpander\`: users still must be able to expand it and toggle Gyroscope.
- Keep existing \`ControllerPage.xaml.cs\` Runtime projection, mutation guards, error InfoBar and re-read behavior. Update only minimal visual branches for new ComboBox/Unavailable text; do not duplicate Runtime policy.

### Localization

- Synchronize \`x:Uid\` names/RESW member properties for both \`en-US\` and \`ko-KR\`. A \`SettingsCard\` \`Header\` property becomes \`SettingsExpander.Header\` with the correct \`x:Uid\`, and a \`ComboBoxItem.Content\` \`Unavailable\` translation becomes \`TextBlock.Text\` if reusing that UID.
- The Gyroscope nested card's \`Header\` must be localized (\`Gyroscope\` / \`자이로스코프\`), without a duplicate external Gyroscope label.
- Remove obsolete \`Controller_SteamControllerRow.Text\` / prior Gyroscope-TextBlock keys if no longer referenced. Keep other strings intact.
- Preserve exact visible option labels \`Steam Deck\` and \`DualSense Edge\`; **do not restore \`(Default)\`**. The actual global default remains SteamDeck.

## 4. Overlay: use the same established section/detail/row layout as M1/M2

Do not add a new controller-specific indentation API or custom margin. Change \`BuildControllerPage\` in \`src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs\` along the lines of:

\`\`\`csharp
var steamSection = CreateControllerSection("Controller for Steam");
var steamDetails = CreateOverlayDetailStack();

_steamControllerRow = new OverlayValueRow(
    "Controller", FormatSteamController,
    RequestSteamControllerChange, OverlayValueButtonKind.DiscreteChoice);
AddControllerRow(steamDetails, rows,
    _steamControllerRow.Container, _steamControllerRow.Capabilities);

_gyroscopeRow = new OverlayToggleRow(
    "Gyroscope", RequestGyroscopeChange); // normal label; no strongLabel: true
AddControllerRow(steamDetails, rows,
    _gyroscopeRow.Container, _gyroscopeRow.Capabilities);

steamSection.Children.Add(steamDetails);
var steamCard = CreateOverlaySectionCard(steamSection);
SetOverlaySectionSeparator(steamCard, visible: false);
page.Children.Add(steamCard);
\`\`\`

- This is literally the M1/M2 section layout path: \`CreateControllerSection\` → \`CreateOverlayDetailStack\` → existing \`AddControllerRow\`/RowChrome → \`CreateOverlaySectionCard\`.
- The normal \`OverlayValueRow\` label is already \`QamBodyTextStyle\`. For Gyroscope remove \`strongLabel: true\` so it uses **the same** body text style.
- Keep selection/focus registration, Controller Left/Right, pointer arrow handling, controller two-choice range (0/1 only when editable), and read-only sentinel range from the PR #792 fix.
- Do **not** indent the heading, manually set an arbitrary \`Margin\` on either row, or adjust the shared global \`QamDetailIndent\` / \`QamRowPadding\` resources.
- Preserve the rest of the Controller tab (M1/M2, Joystick LED, vibration) and other Overlay tabs.

## 5. Overlay: fix cross-row Gyroscope flicker without new lifecycle machinery

The current problem is confirmed in \`OverlayWindow.Controller.cs\`:

\`\`\`csharp
_controllerForSteamMutationInFlight = true; // controller change
RenderControllerForSteamRows();
...
_gyroscopeRow?.ApplyState(
    _frontendSettingsAvailable && !_controllerForSteamMutationInFlight,
    _frontendSettingsSnapshot?.GyroscopeEnabled == true);
\`\`\`

That code transiently disables the unrelated Gyroscope switch for every Controller edit. A normal controller change therefore causes the dim/undim visual flicker.

**Required behavior:**

- A Controller selection edit only makes the **Controller selection row** busy/temporarily noneditable. It must not toggle Gyroscope availability, Gyroscope \`IsOn\` or its visual state.
- A Gyroscope edit can run during Steam/BPM lock and must not transiently disable the unrelated Controller row merely because Gyroscope itself is pending.
- Gyroscope's own in-flight edit may temporarily prevent repeat Gyroscope edits; it must still settle to Runtime-confirmed On/Off after success, rejection, error or reconnect.
- Avoid reassigning \`ToggleSwitch.IsOn\` or \`IsEnabled\` when both are already correct. If modifying \`OverlayToggleRow.ApplyState\`, perform a small equality guard in that existing shared primitive, not a new style/row class. The central defect is the **cross-row busy gate**, not the mere existence of a shared ToggleSwitch renderer.
- Continue to rely on the existing correlated \`NamedPipeOverlayClient\` settings-mutation gate to serialize writes. Do not add a second queue, global lock, epoch, reconciliation worker or new Runtime setting.
- If per-row UI pending flags are required, keep them strictly local to \`OverlayWindow\`; route the already-known request kind (controller vs gyroscope) through \`App.xaml.cs\` mutation completion/failure callbacks so only the correct flag is cleared. A single shared flag is incorrect for two visually independent controls.
- Preserve the **actual requested value** while its own edit is pending; do not flash the old authoritative value and then switch back on successful readback. Do not treat a response for a Controller edit as a Gyroscope edit acknowledgment (or vice versa).
- Runtime authoritative readback wins at settlement. Existing Overlay state refresh on frontend invalidation remains the normal recovery path if a request fails.

Illustrative **UI-only** gating (adapt to the actual existing methods and response handler; do not blindly add both flags if a simpler change is possible):

\`\`\`csharp
var controllerEditable =
    _frontendSettingsAvailable &&
    _controllerForSteam.Available &&
    _controllerForSteam.CanChangeController &&
    !_steamControllerMutationInFlight;

var gyroEditable = _frontendSettingsAvailable &&
                   !_gyroscopeMutationInFlight;

// Apply Controller and Gyroscope state independently;
// changing one setting must not call ApplyState with
// IsAvailable=false on the other just because of its pending request.
\`\`\`

If a Controller edit and Gyroscope edit happen close together, the existing IPC gate can serialize them; do not invent timing-specific synchronization. Keep the current request/result correlation and explicit \`false\` Gyroscope payload semantics.

## 6. Regression requirements (software-only)

Update only directly affected tests, and add small targeted checks where needed:

### Main UI / localization

- Top-level Controller for Steam is \`SettingsExpander\`, not \`SettingsCard\`; the Controller selection ComboBox is visible as its collapsed header content.
- One Gyroscope \`SettingsCard\` inside \`SettingsExpander.Items\`; **no second top-level Gyroscope card or Gyroscope expander**.
- No redundant Controller label; exactly two real ComboBoxItem choices; no \`Unavailable\` ComboBoxItem.
- Unavailable text is read-only, correctly localized, and shown only for an unavailable Runtime projection.
- Steam/BPM lock disables only selection; Gyroscope remains available and header still expands; idle/Xbox-only selection remains editable.
- Selected SteamDeck/Edge values, global SteamDeck default, profile inheritance/Use default and in-progress readback/error display remain identical to PR #792.
- Both RESW languages/PRI resources resolve current x:Uid members with no orphaned or property-mismatched keys.

### Overlay

- Controller for Steam and M1/M2 build their nested rows with the same \`CreateOverlayDetailStack\` layout path; no direct unindented rows under the Steam section heading.
- Controller and Gyroscope labels use the same normal text style; section heading remains strong.
- Controller choice remains limited to SteamDeck and DualSenseEdge (no editable Unavailable sentinel).
- During a Controller-only mutation, Gyroscope toggle remains enabled, its On/Off remains unchanged, and no false Gyroscope edit event occurs. During a Gyroscope-only mutation, Controller display/policy is not altered.
- Toggling Gyroscope commits the explicit true/false request once and settles from the authoritative response.
- Pointer / controller navigation still selects and adjusts both rows correctly, and the existing M1/M2, LED, vibration rows remain unchanged.
- Backend rejection, pipe failure/reconnection and temporary Runtime unavailability restore honest read-only status without stale saved-choice masquerading as active.

Suggested test files:

- \`tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs\`
- \`tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs\`
- \`tests/SteamInputAddonforClaw.UiTests/OverlayValueRowTests.cs\` (only if choice-range regression needs adjustment)
- Existing Overlay toggle/transport tests as appropriate; do not broaden the protocol solely to test visual changes.

Run Release solution build, the current Runtime/UiTests projects, relevant localization/PRI validation, and \`git diff --check\`. Report commands and results in the PR description.

## 7. Strictly out of scope

- No AppSettings/schema changes, gyro sensor reader changes, IMU map changes, new motion reader gate, VIIPER publisher changes, Steam Input profile/gyro mapping changes or XBOX360 controller changes.
- No change to actual Steam game / BPM manual-controller lock policy or applied-presentation Runtime projection. Keep SteamDeck as the **real default**, and never put \`(Default)\` back on its visible choice.
- No changes to PID1902/PID1901 restore, HidHide ownership, MSI Center M, physical recovery/re-enumeration, routing rollback/fail-close, suspend/hibernate/resume, shutdown, crash recovery or VIIPER teardown. Current safety invariants must not be weakened.
- Do not introduce CTW dependencies/integration. Standalone Full1902 is the only supported application scope.
- One Windows user and one interactive session are the product assumptions. No RDP, Fast User Switching or unsupported multi-session infrastructure.
- No speculative race defenses: no new owner/manager, watcher, background poller, retry engine, locks, epochs or barriers solely for narrow timing-only scenarios.
- **Local Codex must not require physical-device tests and missing hardware validation must never be a PR review/merge blocker.** User will test the merged build on the real MSI Claw.

## 8. Definition of done

1. Main App shows a compact native **Controller for Steam SettingsExpander** with one header ComboBox and one nested Gyroscope card, not a two-row top-level SettingsCard.
2. Main dropdown shows exactly **Steam Deck** and **DualSense Edge**, with no third Unavailable option or redundant Controller caption. Actual-unavailable state is conveyed honestly outside dropdown options.
3. Overlay Steam section aligns identically with the pre-existing M1/M2 section, and Gyroscope is normal-weight like Controller.
4. Controller mutation does not blink or disable Gyroscope. Gyroscope mutation does not accidentally alter Controller display/selection.
5. Full1902 Runtime settings, Steam/BPM lock, saved/default choices, gyro data gating and hardware ownership remain behaviorally unchanged.
6. Targeted automated tests/build pass and the implementation PR documents what changed. Hardware checks occur after merge by the user.
