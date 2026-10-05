# Work Order — Windows Game Bar State Diagnostic Observer for WING Alternate Activation Path

## Status

Diagnostic-only follow-up to PR673.

Baseline:
- repository: onehoon/SteamAddonforClaw
- branch: main
- commit: 336949ea339f9750a61122cbe95dc8794b035b34
- date: 2026-10-05

This PR must not change controller routing, Full1902 authority, presentation selection, front-button mapping, Game Bar suppression policy, or current Win+G suppression behavior.

## 1. Read before implementation

Read and preserve:
- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md
- docs/work-order/WING_GAMEBAR_FOREGROUND_DIAGNOSTIC_LOGGING_WORK_ORDER_2026-10-05.md

Also review:
- src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs
- src/SteamInputAddonforClaw/GameBar/WingGameBarDiagnosticProbe.cs
- src/SteamInputAddonforClaw/Wing/WingEventGestureBridge.cs
- src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

Historical MSI/WING RE evidence is relevant:
- SteamAddon_WING_OEM2_Native_MSI_Suppression_RE_Report_2026-08-28.md
- SteamAddon_WING_OEM2_0828-2_Success_Log_Analysis_2026-08-28.md
- MSI_CENTER_M_ROUTING_LAUNCH_SUPPRESSION_RESEARCH_RESULT.md
- MSI_COMPLETE_RESEARCH_RESULT.md

Do not restore CTW integration or any retired Steam-session routing authority.

## 2. Goal

Observe Windows Game Bar state directly through Windows.Gaming.UI.GameBar and place these transitions on the same log timeline as Wing.Input, Event88, Wing.Action, SteamDeck.SystemButton, and PR673 foreground diagnostics.

Observe:
- GameBar.Visible
- GameBar.IsInputRedirected
- GameBar.VisibilityChanged
- GameBar.IsInputRedirectedChanged

Microsoft documents GameBar as a static Windows Runtime class for Game Bar visibility and input-redirection state. Both events may be raised from a background thread.

References:
- https://learn.microsoft.com/en-us/uwp/api/windows.gaming.ui.gamebar?view=winrt-26100
- https://learn.microsoft.com/en-us/uwp/api/windows.gaming.ui.gamebar.isinputredirected?view=winrt-26100
- https://learn.microsoft.com/en-us/uwp/api/windows.gaming.ui.gamebar.visibilitychanged?view=winrt-26100

The application already targets net10.0-windows10.0.26100.0. Do not add a new framework solely for this diagnostic unless compilation proves that a minimal Windows SDK projection reference is required.

## 3. Evidence motivating this PR

The 2026-10-05 Addon/Log/1005/02 reproduction provides three clean phases.

Phase A — BPM before game:
- six WING presses;
- Win+G keyboard input observed and suppressed;
- Event88 arrived roughly 295–306 ms later;
- PR673 foreground remained steamwebhelper / Steam Big Picture Mode.

Phase B — 007 First Light foreground:
- RunningAppID=12658904;
- Presentation remained SteamDeck;
- twenty WING presses produced Event88Accepted 20/20;
- PR673 foreground remained 007FirstLight.exe for every one-second probe;
- corresponding Win+G keyboard-hook events were 0/20;
- Xbox Game Bar was nevertheless visibly shown.

Phase C — game exit back to BPM:
- RunningAppID returned to 0;
- BigPictureActive remained true;
- Presentation remained SteamDeck;
- four WING presses immediately returned to Win+G observed/suppressed followed roughly 297–303 ms later by Event88;
- Steam menu presentation was normal again.

Additional control:
Launching 007FirstLight.exe directly without launching the game through Steam still reproduces WING -> Xbox Game Bar. Independently, Event88 executes the Normal-domain WING mapping and can launch Steam Big Picture.

Therefore the Game Bar symptom is not dependent on the Steam non-Steam AppID mismatch.

## 4. Historical RE correlation

Previous real-hardware MSI/WING RE identified two parallel branches.

Branch A — pre-Event88 Windows/Game Bar path:
WING -> GameBar.IsInputRedirected=True -> MSI QuickSettings interaction -> xinputmode_set -> Command Center / GoToXInputMode -> possible controller mode mutation.

Historical failure timelines showed GameBar.IsInputRedirected=True before Event88 delivery.

Branch B — Event88 multicast path:
Event88 -> Addon configured WING action;
Event88 -> MSI Center M Launcher ms-gamebar URI attempt;
Event88 -> MSI Center M Server vendor handling.

A prior successful suppression session showed many Launcher ms-gamebar URI attempts while GameBar.IsInputRedirected stayed false, GameBar.Visible stayed false, and no visible Game Bar appeared.

This PR must collect direct Windows Game Bar state evidence rather than assuming Event88 or the Launcher URI is the primary activation source.

## 5. Implementation scope

Add one small diagnostic observer under:
src/SteamInputAddonforClaw/GameBar/

Suggested name:
GameBarStateDiagnosticObserver.cs

The observer is:
- diagnostic-only;
- process-owned;
- read-only;
- non-authoritative;
- not a routing gate;
- not a suppression mechanism.

It must never decide whether a WING action is delivered.

## 6. Required observer behavior

Subscribe to:
- GameBar.VisibilityChanged
- GameBar.IsInputRedirectedChanged

Read:
- GameBar.Visible
- GameBar.IsInputRedirected

On successful startup, emit one initial DEBUG state snapshot, for example:

[GameBar.State] ObserverStarted
Visible=False
IsInputRedirected=False

Use actual values.

On VisibilityChanged, read both current properties and log:
- Trigger=VisibilityChanged
- Visible=current value
- IsInputRedirected=current value

On IsInputRedirectedChanged, read both current properties and log:
- Trigger=IsInputRedirectedChanged
- Visible=current value
- IsInputRedirected=current value

Keep both events even when they occur for one Game Bar invocation. Their ordering is useful evidence.

Do not debounce, coalesce, poll, or add another timing authority. Existing AppLog timestamps and thread IDs are sufficient.

## 7. Start before Event88, but do not block the hook owner thread

The observer must already be subscribed before WING is pressed.

Do not start it from:
- WingEventGestureBridge.OnEvent
- Event88Accepted
- WingGameBarDiagnosticProbe.Start

Those are too late because historical Branch A can precede Event88.

Use the current process startup ownership in AddonProcessHost.

The natural composition seam is StartRuntimeEventWatchers, which already installs the one process-owned WinGSuppressionGuard.

Critical constraint:
Keep _winGSuppressionGuard.Start() as the direct immediate message-loop-thread operation exactly as today.

Do not perform GameBar WinRT activation, property access, or event subscription synchronously on that native message-loop thread.

Immediately after hook installation, start the diagnostic observer through a background/off-thread initialization path that returns immediately.

Reason: this diagnostic must not risk delaying the WH_KEYBOARD_LL owner thread or changing hook reliability.

## 8. DEBUG gating

Preferred policy:
- DEBUG logging enabled at process startup -> initialize and subscribe;
- DEBUG logging disabled -> do not activate or subscribe.

The current reproduction already uses LogLevel=Debug.

Do not build dynamic log-level subscription machinery. Requiring process restart after enabling DEBUG is acceptable for this diagnostic.

## 9. Failure containment

All Windows.Gaming.UI.GameBar interaction is best-effort.

Possible failures:
- WinRT activation failure;
- initial property read failure;
- event subscription failure;
- event callback property read failure;
- event unsubscription failure during teardown.

Required policy:
diagnostic failure -> DEBUG evidence only -> normal product behavior continues.

Suggested records:
- GameBar.State / ObserverUnavailable / Operation=Subscribe / Exception=<type>
- GameBar.State / StateReadFailed / Trigger=<event> / Exception=<type>

Never throw from a Game Bar event callback into Windows.

Do not convert this diagnostic failure into product fail-close, routing rollback, or presentation change.

## 10. Threading

Microsoft documents both change events as potentially raised on a background/non-UI thread.

Therefore callbacks must:
- not marshal to WinUI;
- not touch frontend UI;
- not call Overlay UI;
- not acquire routing/presentation locks;
- not perform product async work;
- only read the two GameBar state properties and emit DEBUG logs.

No DispatcherQueue is needed.

## 11. Teardown

The observer owns exactly its two event subscriptions.

At normal process shutdown:
- unsubscribe VisibilityChanged;
- unsubscribe IsInputRedirectedChanged;
- dispose the observer.

Place teardown near the existing process-owned _winGSuppressionGuard.Dispose() lifetime boundary.

Do not tie observer lifetime to:
- RunningAppID;
- Big Picture entry/exit;
- Xbox360/SteamDeck presentation;
- game process lifetime;
- Overlay visibility.

## 12. Keep PR673 diagnostics intact

Do not remove or redesign WingGameBarDiagnosticProbe in this PR.

Its result is useful control evidence:
Xbox Game Bar can visibly appear while GetForegroundWindow continues to report 007FirstLight.exe.

The new observer supplements the foreground probe with Windows' own Game Bar state.

Temporary-diagnostic cleanup can be decided after root cause is proven.

## 13. Explicitly out of scope

Do not implement Game Bar suppression yet.

Do not add:
- GameBar.exe killing;
- Game Bar window closing;
- package uninstall/unregister;
- ms-gamebar protocol changes;
- registry/GPO global disable;
- UseNexusForGameBarEnabled writes;
- MSI Launcher retirement;
- MSI Server retirement;
- QuickSettings package modification;
- Command Center dummy/mutex/pipe interception;
- xinputmode_set blocking;
- WMI Event88 consumption;
- another keyboard/raw-input/controller hook;
- X360 fallback;
- AppID-specific handling;
- 007-specific handling.

Do not change WinGSuppressionGuard, Full1902 authority, HidHide, PID1901/PID1902 ownership, VIIPER, presentation policy, or front-button mapping.

## 14. No overengineering

Do not add:
- a GameBar manager;
- a GameBar authority state machine;
- a new routing gate;
- epoch/barrier state;
- retry loops;
- background polling;
- persistence or registry journals.

One observer with two event subscriptions and process-lifetime teardown is enough.

The only question this PR must answer is:

When WING is pressed in the affected game context, does Windows Game Bar enter Visible and/or IsInputRedirected state before Event88 even though no Win+G keyboard event reaches the existing hook?

## 15. Automated validation

Do not create a large WinRT abstraction solely to fake static GameBar events.

At minimum:
- existing WinGSuppressionGuardTests remain semantically unchanged;
- Full1902WinGSuppressionAuthorityTests still prove the one hook installation site and authority binding;
- WingEventGestureBridgeTests and PR673 diagnostics remain green;
- full repository test suite remains green.

If a small natural seam makes observer tests easy, focused tests may verify:
- start is idempotent;
- dispose unsubscribes once;
- callback failures are contained;
- callbacks only log state and never invoke product actions.

Do not add wrapper/interface layers solely for testability.

Run:
- dotnet build SteamInputAddonforClaw.slnx -c Debug
- dotnet build SteamInputAddonforClaw.slnx -c Release
- dotnet test SteamInputAddonforClaw.slnx -c Release
- git diff --check

## 16. Manual reproduction matrix

Run with LogLevel=Debug.

Test A — BPM control:
1. Enter BPM.
2. Press WING several times.
3. Confirm Win+G observed -> suppressed -> roughly 300 ms -> Event88Accepted -> SteamButton.
4. Observe GameBar.State.

Expected from prior evidence: Visible=False and IsInputRedirected=False.

Test B — 007 direct launch:
1. Ensure Game Bar is closed.
2. Launch 007FirstLight.exe directly.
3. Press WING once.
4. Compare Wing.Input, GameBar.State, Wing.Event, Wing.Action, and PR673 foreground logs.

Primary question:
Does GameBar.Visible and/or GameBar.IsInputRedirected become true while WinGSuppressionGuard receives no LWIN/G chord?

Test C — 007 through Steam:
Repeat while 007 is launched through the current non-Steam shortcut and Steam reports the problematic runtime AppID.

If direct and Steam launch show the same GameBar state transition, the activation path is independent of the Steam AppID mismatch.

Test D — return to BPM:
1. Exit the game while BPM remains active.
2. Press WING several times.
3. Confirm normal Win+G suppression returns.
4. Compare GameBar.State with the affected in-game sequence.

## 17. Interpretation

Outcome A:
No Win+G hook event -> GameBar.IsInputRedirected and/or Visible changes to true -> Event88 later.

Interpretation:
Windows Game Bar is entering its own controller/input-redirection/overlay state before the Addon Event88 action while the current keyboard suppression path sees no Win+G chord. This confirms a real alternate activation path and justifies a separate suppression-design investigation.

Outcome B:
No Win+G -> Event88 -> GameBar state changes afterward.

Interpretation:
Re-investigate Event88/MSI Launcher/QuickSettings ordering before designing a blocker. Do not assume the Addon action caused it.

Outcome C:
User visibly sees Game Bar but Visible=False and IsInputRedirected=False.

Interpretation:
The public Windows.Gaming.UI.GameBar state surface is insufficient for this MSI/Windows path. Capture that result and design the next targeted trace separately; do not expand this PR into ETW, UI Automation, or package interception.

## 18. Acceptance criteria

- DEBUG startup subscribes to GameBar.VisibilityChanged.
- DEBUG startup subscribes to GameBar.IsInputRedirectedChanged.
- Initial Visible and IsInputRedirected values are logged.
- Every observed event logs its trigger and both current state values.
- Subscription exists before Event88 can occur.
- GameBar WinRT initialization does not block the WH_KEYBOARD_LL message-loop owner thread.
- Event callbacks perform only state reads and DEBUG logging.
- Observer failures cannot alter routing, presentation, or WING delivery.
- Event subscriptions are removed during normal teardown.
- PR673 foreground diagnostics remain intact.
- WinGSuppressionGuard behavior remains unchanged.
- Full1902/HidHide/PID1902/VIIPER behavior remains unchanged.
- No Game Bar suppression mechanism is introduced.
- One real-hardware reproduction can place Windows Game Bar state transitions relative to Win+G suppression and Event88.

## 19. Follow-up decision

Only after this evidence is captured should a production suppression design be considered.

A future blocker is justified only if the alternate Game Bar path is realistically reproducible in the supported handheld lifecycle, attributable to WING/controller activation, user-visible or safety-relevant, and suppressible without broad Windows/MSI state ownership.

Prefer blocking the actual activation source over detecting Game Bar after it appears and killing or closing it.
