# Work Order — Drain Pending Controller Mapping Saves Before Frontend Shutdown

Date: 2026-09-21
Repository: onehoon/SteamAddonforClaw
Baseline: main at or after 003d829b4eecba0aa0d9aa795a4b560d3c5ee7da (feat: add M1 and M2 controller mapping UI #557)
Scope: Main UI shutdown reliability for the existing front-button and M1/M2 mapping save chains only

---

## 1. Goal

Ensure that a user cannot lose the most recent Controller-page button-mapping edit merely because they close the Main UI immediately after changing a ComboBox.

The current UI already serializes mapping mutations through:

    _frontButtonSaveChain
    _backButtonSaveChain

but the normal Main UI shutdown path disposes the frontend named-pipe client without first waiting for those save chains to finish.

Fix only that shutdown gap.

Required product behavior:

    user changes controller mapping
    → UI queues the existing mapping save
    → user immediately closes Main UI
    → shutdown waits for already-queued front/back mapping saves
    → Runtime receives/persists the mutation when the transport is healthy
    → frontend client is disposed
    → UI exits

Do not redesign controller mapping, settings persistence, named-pipe transport, or Full1902 controller lifecycle.

---

## 2. Why this is a real product issue

This is a normal user lifecycle, not a theoretical scheduler race.

Current PR557-era flow:

    Controller ComboBox changed
    → QueueFrontButtonMutation(...) / QueueBackButtonMutation(...)
    → _frontButtonSaveChain / _backButtonSaveChain
    → SetFrontButtonMappingAsync(...) / SetBackButtonMappingAsync(...)
    → Runtime StartupSettingsCoordinator
    → settings.json

Current window-close flow:

    MainWindow.Closed
    → App.OnMainWindowClosed(...)
    → existing UI-local diagnostic cleanup
    → ShutdownAndExitAsync("WindowClosed")
    → UiShutdownCoordinator
    → DisposeFrontendAsync()
    → NamedPipeAddonFrontendClient.DisposeAsync()
    → pipe lifetime canceled / connection disposed
    → outstanding RPCs may fail

There is currently no shutdown join between those two flows.

Therefore this realistic sequence is possible:

    1. User selects M1 = A.
    2. QueueBackButtonMutation(...) updates the visible UI immediately.
    3. The save chain is still queued or awaiting the frontend RPC.
    4. User closes the Main UI immediately.
    5. App disposes NamedPipeAddonFrontendClient.
    6. The pending mapping RPC fails before Runtime persistence completes.
    7. Next UI launch shows the previous persisted mapping.

The same structural issue already exists for the older front-button mapping save chain.

Fix both together because they are the same UI-owned shutdown obligation.

---

## 3. Architecture constraints

Preserve the current Full1902 architecture.

Do not change:

- Center M authority;
- PID1901 / PID1902 transitions;
- DirectInput ownership;
- HidHide state;
- VIIPER ownership;
- Xbox360 / SteamDeck presentation switching;
- M1/M2 release-to-rearm behavior;
- publisher cadence;
- controller raw state;
- settings schema;
- frontend protocol version;
- RPC shapes;
- Runtime-side mutation authority.

This is a Main UI lifecycle fix only.

The Runtime remains the persistence authority.

The Main UI only ensures its already-queued mapping mutations get a chance to complete before it intentionally tears down its own transport.

---

## 4. Existing implementation to reuse

### 4.1 MainWindow save chains

src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs already owns:

    private Task _frontButtonSaveChain = Task.CompletedTask;
    private Task _backButtonSaveChain = Task.CompletedTask;

and queues mutations through the existing ordered-save helpers.

Do not replace those chains.

Do not create:

- a generic settings queue;
- a new mutation manager;
- a controller-settings coordinator;
- a background save service;
- a global shutdown registry.

The smallest correct design is to await the two existing chain tails.

### 4.2 Existing bounded shutdown owner

src/SteamInputAddonforClaw.UI/Lifecycle/UiShutdownCoordinator.cs already bounds frontend cleanup.

At the current baseline it is constructed with TimeSpan.FromSeconds(5).

Do not add another timeout specifically for mapping saves.

The existing shutdown bound is already the correct outer policy.

### 4.3 Existing save error handling

The front/back save chains already own:

- ordered mutations;
- authoritative Runtime readback;
- newest-edit rendering;
- rollback behavior on save failure.

Do not duplicate that logic in shutdown.

Shutdown should only wait for the existing tasks.

---

## 5. Required implementation

### 5.1 Add one narrow MainWindow drain method

Add one method on MainWindow with a narrow purpose, conceptually:

    internal Task DrainPendingControllerMappingSavesAsync()
    {
        var front = _frontButtonSaveChain;
        var back = _backButtonSaveChain;
        return Task.WhenAll(front, back);
    }

Exact naming may differ, but keep the responsibility explicit.

Requirements:

- capture the current chain tails;
- await both front-button and back-button chains;
- do not enqueue any new mutation;
- do not retry failed RPCs;
- do not modify UI state;
- do not write settings directly;
- do not call Runtime persistence APIs outside the existing save-chain methods.

Capturing the task references before awaiting is sufficient for the supported one-user / one-interactive-session UI lifecycle.

Do not add locks, epochs, barriers, or a generalized close-time mutation gate for hypothetical edits racing after the window-close path has already begun.

---

## 6. Shutdown ordering

The important ordering is:

    MainWindow close begins
    → existing page-specific shutdown cleanup as appropriate
    → drain already-queued controller mapping save chains
    → dispose frontend named-pipe client
    → exit UI

The transport must remain alive while the mapping drain is running.

Therefore do not put the drain after NamedPipeAddonFrontendClient.DisposeAsync().

A minimal integration may live in App.OnMainWindowClosed or in the existing frontend-disposal cleanup path, but prefer the placement that also protects any existing frontend-disposal shutdown path where MainWindow still exists.

Conceptually, a valid implementation is:

    private async Task DisposeFrontendAsync()
    {
        if (_mainWindow is not null)
            await _mainWindow.DrainPendingControllerMappingSavesAsync().ConfigureAwait(false);

        // existing frontend disposal follows
    }

or, if the current lifecycle structure makes it cleaner:

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_mainWindow is not null)
        {
            await _mainWindow.CloseVibrationTestForUiShutdownAsync().ConfigureAwait(true);
            await _mainWindow.CloseClawSensorProbeForUiShutdownAsync().ConfigureAwait(true);
            await _mainWindow.DrainPendingControllerMappingSavesAsync().ConfigureAwait(true);
        }

        await ShutdownAndExitAsync("WindowClosed").ConfigureAwait(true);
    }

The core invariant is:

> frontend transport disposal must not intentionally cancel an already-queued Controller mapping save that the Main UI itself owns.

Keep the implementation simple and consistent with the current shutdown coordinator.

---

## 7. Failure policy

A mapping save may still legitimately fail because:

- Runtime disconnected;
- named pipe failed;
- Runtime rejected/failed the mutation;
- settings write failed;
- application is already shutting down because Runtime disappeared.

Do not turn this work into guaranteed durable delivery machinery.

Required behavior:

    healthy transport + queued mapping save
    → wait for normal save completion before frontend disposal

    save already failed
    → drain completes according to the existing save-chain behavior
    → shutdown continues

    Runtime disconnected / transport unavailable
    → no retry framework
    → no reconnect solely to save
    → shutdown continues under existing policy

The existing UiShutdownCoordinator outer timeout remains the bounded escape hatch.

Do not block application exit indefinitely.

---

## 8. Front-button mapping must be covered too

Although this review was triggered by PR554–557 M1/M2 work, the existing front-button mapping has the same save-chain lifetime.

Fix both:

    _frontButtonSaveChain
    _backButtonSaveChain

Do not create two separate shutdown APIs unless the current code structure makes that clearly simpler.

One narrow Controller-mapping drain is preferred.

This is not a request to refactor the two independent save chains into one generic implementation.

They should remain independent during normal operation.

---

## 9. No protocol or Runtime changes

FrontendTransportProtocol.CurrentVersion must remain 38.

Do not modify:

- BackButtonMappingSettings;
- FrontButtonMappingSettings;
- RPC enums;
- named-pipe payloads;
- StartupSettingsCoordinator;
- SettingsStore;
- MsiClawAddonPresentation;
- Xbox360 mapper/publisher;
- SteamDeck mapper/publisher.

If implementation appears to require any of those, re-check the design.

This issue exists entirely because Main UI shutdown currently disposes its transport before explicitly joining its own queued mapping work.

---

## 10. Expected production files

Prefer a very small production diff.

Likely files:

    src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
    src/SteamInputAddonforClaw.UI/App.xaml.cs

Potentially no other production file should be necessary.

UiShutdownCoordinator.cs should not need architectural changes.

---

## 11. Tests

Add focused tests using the project's existing source-level/UI-lifecycle test style where practical.

Do not introduce a new UI test framework.

At minimum prove the following contract.

### 11.1 MainWindow exposes one drain over both chain tails

Verify the implementation waits for both:

    _frontButtonSaveChain
    _backButtonSaveChain

and does not replace them with a new generic queue/manager.

### 11.2 Shutdown drains before frontend disposal

The relevant source/order contract must prove:

    DrainPendingControllerMappingSavesAsync(...)

occurs before:

    _frontendClient.DisposeAsync(...)

on the intended normal close / frontend cleanup path.

### 11.3 Existing outer bound remains authoritative

Verify no new feature-local timeout/retry loop was introduced.

### 11.4 Protocol unchanged

Keep protocol v38.

### 11.5 Existing mapping tests remain green

All existing:

- front-button UI/save tests;
- back-button UI/save tests;
- named-pipe frontend transport tests;
- Full1902 presentation tests;

must continue to pass.

---

## 12. Manual validation

Use a real build with the Runtime healthy.

### Case A — M1 immediate close

    1. Open Controller.
    2. Change M1 from Disabled to A.
    3. Immediately close Main UI.
    4. Reopen Main UI.
    5. Verify M1 still shows A.
    6. In Xbox360 presentation, verify M1 emits A.

### Case B — M2 immediate close

Repeat with M2 and a different target such as Right Bumper.

### Case C — front button immediate close

Change one Gamebar/Center M mapping and immediately close/reopen.

Verify the last selected mapping remains persisted.

### Case D — several quick edits then close

    M1: Disabled → A → B → Right Bumper
    immediately close
    reopen
    → final persisted value must be Right Bumper

This validates that the existing serialized save chain still preserves latest ordered intent through shutdown.

### Case E — Runtime disconnect

With Runtime unavailable/disconnected, closing the UI must still terminate under the existing bounded shutdown policy.

Do not add reconnect/retry behavior.

---

## 13. Acceptance criteria

- [ ] Closing Main UI immediately after a front-button mapping edit does not intentionally cancel the queued save.
- [ ] Closing Main UI immediately after an M1/M2 mapping edit does not intentionally cancel the queued save.
- [ ] Both existing save-chain tails are drained before intentional frontend transport disposal.
- [ ] Existing save ordering and authoritative Runtime readback remain unchanged.
- [ ] Existing rollback/error handling remains unchanged.
- [ ] No direct settings write is added to the UI.
- [ ] No new retry/reconnect machinery is added.
- [ ] No new timeout is added; existing UiShutdownCoordinator bound remains authoritative.
- [ ] Protocol remains v38.
- [ ] No controller Runtime/lifecycle code changes.
- [ ] Debug build passes.
- [ ] Release build passes.
- [ ] Full test suite passes.
- [ ] git diff --check passes.

---

## 14. Anti-overengineering constraints

Do not add:

- a generic save manager;
- a generic settings mutation scheduler;
- an application-wide pending-operation registry;
- a new semaphore around all UI settings;
- per-setting cancellation tokens;
- retry policies;
- reconnect-on-close behavior;
- epochs/version barriers beyond the existing edit-version mechanism;
- new Runtime state;
- a new frontend protocol method;
- a new controller lifecycle state;
- shutdown persistence journals.

The realistic defect is simple:

    queued mapping save exists
    + UI intentionally destroys its own transport
    = last edit can be lost

The fix should be equally simple:

    await existing save-chain tails
    → then dispose transport

---

## 15. Review focus

When reviewing the implementation, focus on these concrete questions:

1. Can the Main UI still dispose the frontend transport while a queued front/back mapping save is unfinished?
2. Are both existing save chains covered?
3. Does shutdown stay bounded by the existing coordinator?
4. Is normal save ordering unchanged?
5. Is the implementation limited to the UI shutdown lifecycle?
6. Was any unnecessary manager/state/lock/retry abstraction introduced?
7. Does protocol v38 remain unchanged?
8. Are Full1902 controller ownership and presentation lifecycle untouched?

Do not block the PR for hypothetical instruction-level races after window teardown has already begun unless a realistic user path demonstrates incorrect persisted state.
