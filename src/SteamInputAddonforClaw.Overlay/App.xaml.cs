using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public partial class App : Application
{
    private OverlayWindow? _window;
    private DispatcherQueue? _dispatcherQueue;
    private NamedPipeOverlayClient? _client;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        OverlayLog.Info("App", "OnLaunched entered.");
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        OverlayLog.Info("App", "DispatcherQueue acquired.");
        _window = new OverlayWindow();
        _window.OutsideClickDismissRequested += OnOutsideClickDismissRequested;
        _window.TabOrderMoveRequested += OnTabOrderMoveRequested;
        _window.ProfileCatalogRequestRequested += OnProfileCatalogRequestRequested;
        _window.ProfilePageRequestRequested += OnProfilePageRequestRequested;
        _window.ClawHudEnabledRequested += OnClawHudEnabledRequested;
        _window.ClawHudSettingMutationRequested += OnClawHudSettingMutationRequested;
        _window.ShortcutExecutionRequested += OnShortcutExecutionRequested;
        OverlayLog.Info("App", "OverlayWindow constructed.", ("Hwnd", _window.HandleForDiagnostics));
        _window.Closed += (_, _) => { OverlayLog.Info("Window", "Closed received."); Exit(); };
        OverlayLog.Info("Window", "Initial hidden preparation started.");
        _window.PrepareHidden();
        OverlayLog.Info("Window", "Initial hidden preparation completed.");
        _ = ConnectAndRunAsync();
    }

    private void OnOutsideClickDismissRequested(OverlayOutsideClick outsideClick)
    {
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() => _ = SendDismissRequestedAsync(outsideClick)))
            OverlayLog.Warn("Input", "Could not enqueue outside-click dismissal request.");
    }

    private async Task SendDismissRequestedAsync(OverlayOutsideClick outsideClick)
    {
        OverlayLog.Info("Input", "Outside click dismissal requested",
            ("OverlayHwnd", _window?.HandleForDiagnostics),
            ("Message", outsideClick.MessageName),
            ("PointerX", outsideClick.PointerX), ("PointerY", outsideClick.PointerY),
            ("WindowLeft", outsideClick.WindowBounds.X), ("WindowTop", outsideClick.WindowBounds.Y),
            ("WindowRight", outsideClick.WindowBounds.X + outsideClick.WindowBounds.Width),
            ("WindowBottom", outsideClick.WindowBounds.Y + outsideClick.WindowBounds.Height),
            ("ForegroundHwnd", outsideClick.ForegroundHwnd));
        try
        {
            if (_client is null) throw new InvalidOperationException("Overlay transport client is unavailable.");
            await _client.SendDismissRequestedAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            OverlayLog.Error("Transport", "Outside click dismissal request failed; Overlay remains Runtime-owned.", exception);
        }
    }

    private async Task ConnectAndRunAsync()
    {
        try
        {
            _client = new NamedPipeOverlayClient(FrontendPipeEndpoint.CreateOverlayForCurrentUser());
            // SF-V2-07/09 section 10.2/12.1: App owns the transport client; the Window's Device and
            // Profile bindings receive only this narrow mutation delegate, never the client itself.
            _window?.ConfigureQuickSettings(intent => _client.SendQuickSettingsMutationAsync(intent));
            OverlayLog.Info("Transport", "Overlay command loop starting.");
            await _client.RunAsync(HandleCommandAsync, HandleNavigationAsync, HandleTabOrderAsync,
                HandleQuickSettingsPageAsync, HandleClawHudAsync, HandleProfileCatalogAsync,
                HandleProfilePageAsync, HandleShortcutStateAsync).ConfigureAwait(false);
            OverlayLog.Info("Transport", "Overlay command loop ended.");
        }
        catch (Exception exception)
        {
            OverlayLog.Error("Transport", "Overlay transport loop failed.", exception);
            System.Diagnostics.Debug.WriteLine($"Overlay transport failed: {exception}");
            _dispatcherQueue?.TryEnqueue(() => Exit());
        }
        finally
        {
            if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    // OQ5-UI-09: authoritative tab order from the Runtime. Used for the mandatory initial snapshot
    // (the returned Task must complete before the client reports Ready) and any later republish.
    // Marshalled through the existing DispatcherQueue; completes only after the shell has applied it.
    private Task HandleTabOrderAsync(AddonQuickSettingsTabOrderSnapshot state)
    {
        OverlayLog.Info("TabOrder", "Authoritative tab-order state received.", ("Available", state.Available), ("Count", state.Rows.Count));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _window?.ApplyTabOrderState(state);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                OverlayLog.Error("TabOrder", "Applying the authoritative tab order failed.", exception);
                completion.TrySetException(exception);
            }
        }))
        {
            completion.TrySetException(new InvalidOperationException("Overlay dispatcher is unavailable for tab-order application."));
        }
        return completion.Task;
    }

    // SF-V2-07/09 section 10.1/12.2: marshal a shared QuickSettingsPageSnapshot (Device or Profile --
    // the same shared product contract, SF-V2-05/08) to the UI thread and complete only after
    // the Window/binder has applied it. No WinUI row creation ever runs on the pipe read thread.
    private Task HandleQuickSettingsPageAsync(QuickSettingsPageSnapshot page)
    {
        OverlayLog.Debug("QuickSettings", "Quick Settings page received.",
            ("PageId", page.PageId), ("Available", page.Available), ("SectionCount", page.Sections.Count));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _window?.ApplyQuickSettingsPage(page);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                OverlayLog.Error("Device", "Applying the Quick Settings page failed.", exception);
                completion.TrySetException(exception);
            }
        }))
        {
            completion.TrySetException(new InvalidOperationException("Overlay dispatcher is unavailable for Quick Settings page application."));
        }
        return completion.Task;
    }

    private Task HandleProfileCatalogAsync(OverlayProfileCatalogState state)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try { _window?.ApplyProfileCatalogState(state); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
            completion.TrySetException(new InvalidOperationException("Overlay dispatcher is unavailable for Profile catalog application."));
        return completion.Task;
    }

    private Task HandleProfilePageAsync(OverlayProfilePageResponse response)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try { _window?.ApplyProfilePageResult(response); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
            completion.TrySetException(new InvalidOperationException("Overlay dispatcher is unavailable for Profile page application."));
        return completion.Task;
    }

    private Task HandleClawHudAsync(FrontendClawHudSnapshot snapshot)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _window?.ApplyClawHudSnapshot(snapshot);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                OverlayLog.Error("ClawHUD", "Applying the ClawHUD state failed.", exception);
                completion.TrySetException(exception);
            }
        }))
            completion.TrySetException(new InvalidOperationException("Overlay dispatcher is unavailable for ClawHUD state application."));
        return completion.Task;
    }

    private Task HandleShortcutStateAsync(FrontendShortcutDashboardSnapshot snapshot)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try { _window?.ApplyShortcutState(snapshot); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
            completion.TrySetException(new InvalidOperationException("Overlay dispatcher is unavailable for Shortcut state application."));
        return completion.Task;
    }

    // PR3: forward the typed move and apply only the authoritative mutation result.
    private void OnTabOrderMoveRequested(AddonQuickSettingsTabOrderMoveIntent intent) => _ = SendTabOrderMoveAsync(intent);

    private void OnClawHudEnabledRequested(bool enabled) => _ = SendClawHudEnabledAsync(enabled);

    private void OnClawHudSettingMutationRequested(FrontendClawHudMutationIntent intent) => _ = SendClawHudSettingAsync(intent);

    private Task OnShortcutExecutionRequested(Guid tileId) => SendShortcutExecutionAsync(tileId);

    private async Task SendShortcutExecutionAsync(Guid tileId)
    {
        try
        {
            if (_client is null) throw new InvalidOperationException("Overlay transport client is unavailable.");
            var result = await _client.SendShortcutExecuteAsync(tileId).ConfigureAwait(false);
            await DispatchShortcutUiAsync(() => _window?.ApplyShortcutExecutionResult(result)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Shortcut", "Shortcut execution request failed.", null,
                ("ExceptionType", exception.GetType().Name));
            try { await DispatchShortcutUiAsync(() => _window?.ApplyShortcutExecutionFailure()).ConfigureAwait(false); }
            catch { OverlayLog.Warn("Shortcut", "Could not enqueue Shortcut execution failure state."); }
        }
    }

    private Task DispatchShortcutUiAsync(Action apply)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try { apply(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
            completion.TrySetException(new InvalidOperationException("Overlay dispatcher is unavailable for Shortcut result application."));
        return completion.Task;
    }

    private async Task SendClawHudEnabledAsync(bool enabled)
    {
        try
        {
            if (_client is null) throw new InvalidOperationException("Overlay transport client is unavailable.");
            var result = await _client.SendClawHudEnabledAsync(enabled).ConfigureAwait(false);
            ApplyClawHudMutationResult(result);
        }
        catch (Exception exception) { ApplyClawHudFailure(exception.Message); }
    }

    private async Task SendClawHudSettingAsync(FrontendClawHudMutationIntent intent)
    {
        try
        {
            if (_client is null) throw new InvalidOperationException("Overlay transport client is unavailable.");
            var result = await _client.SendClawHudMutationAsync(intent).ConfigureAwait(false);
            ApplyClawHudMutationResult(result);
        }
        catch (Exception exception) { ApplyClawHudFailure(exception.Message); }
    }

    private void ApplyClawHudMutationResult(FrontendClawHudMutationResult result)
    {
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            _window?.ApplyClawHudSnapshot(result.Snapshot);
            if (!result.Succeeded)
                _window?.ApplyClawHudFailure(result.FailureMessage ?? "ClawHUD update failed.");
        }))
            OverlayLog.Warn("ClawHUD", "Could not enqueue authoritative ClawHUD mutation result.");
    }

    private void ApplyClawHudFailure(string message)
    {
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() => _window?.ApplyClawHudFailure(message)))
            OverlayLog.Warn("ClawHUD", "Could not enqueue ClawHUD failure state.");
    }

    private async Task SendTabOrderMoveAsync(AddonQuickSettingsTabOrderMoveIntent intent)
    {
        try
        {
            if (_client is null) return;
            var result = await _client.SendTabOrderMoveAsync(intent).ConfigureAwait(false);
            if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
            {
                _window?.ApplyTabOrderState(result.State);
                if (!result.Succeeded)
                    OverlayLog.Warn("TabOrder", result.FailureMessage ?? "Tab order update failed.");
            }))
                OverlayLog.Warn("TabOrder", "Could not enqueue authoritative tab-order result.");
        }
        catch (Exception exception)
        {
            OverlayLog.Error("TabOrder", "Tab-order move request failed; Overlay remains Runtime-owned.", exception);
        }
    }

    // OQ4: semantic navigation from the Runtime capture path. Marshal UI work through the existing
    // DispatcherQueue only -- no HWND activation/focus, no SendInput synthesis, no local controller
    // reads. B/Back stays Runtime-owned through the existing DismissRequested path; the shell does
    // not consume it locally (OQ5-UI-01 keeps B close authority where OQ4 put it).
    private Task HandleNavigationAsync(OverlayNavigationAction action)
    {
        OverlayLog.Debug("Navigation", $"{action} received.");
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                switch (action)
                {
                    case OverlayNavigationAction.Back:
                        if (_window?.TryHandleBack() != true)
                            _ = SendBackDismissAsync();
                        break;
                    case OverlayNavigationAction.PreviousTab:
                        _window?.SelectPreviousTab();
                        break;
                    case OverlayNavigationAction.NextTab:
                        _window?.SelectNextTab();
                        break;
                    case OverlayNavigationAction.NavigateUp:
                        _window?.NavigateUp();
                        break;
                    case OverlayNavigationAction.NavigateDown:
                        _window?.NavigateDown();
                        break;
                    case OverlayNavigationAction.NavigateLeft:
                        _window?.AdjustSelectedRow(-1);
                        break;
                    case OverlayNavigationAction.NavigateRight:
                        _window?.AdjustSelectedRow(+1);
                        break;
                    case OverlayNavigationAction.Accept:
                        _window?.ActivateSelectedRow();
                        break;
                }
            }
            catch (Exception exception)
            {
                OverlayLog.Error("Navigation", $"{action} handler failed.", exception);
            }
        }))
        {
            OverlayLog.Warn("Navigation", $"Could not enqueue navigation action {action}.");
        }
        return Task.CompletedTask;
    }

    private async Task SendBackDismissAsync()
    {
        try
        {
            if (_client is null) throw new InvalidOperationException("Overlay transport client is unavailable.");
            OverlayLog.Info("Navigation", "Back requested root dismissal.");
            await _client.SendDismissRequestedAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            OverlayLog.Error("Transport", "Back dismissal request failed; Overlay remains Runtime-owned.", exception);
        }
    }

    private void OnProfileCatalogRequestRequested() => _ = SendProfileCatalogRequestAsync();
    private void OnProfilePageRequestRequested(uint appId) => _ = SendProfilePageRequestAsync(appId);

    private async Task SendProfileCatalogRequestAsync()
    {
        try { if (_client is not null) await _client.SendProfileCatalogRequestAsync().ConfigureAwait(false); }
        catch (Exception exception) { OverlayLog.Error("Profile", "Profile catalog request failed.", exception); }
    }

    private async Task SendProfilePageRequestAsync(uint appId)
    {
        try { if (_client is not null) await _client.SendProfilePageRequestAsync(appId).ConfigureAwait(false); }
        catch (Exception exception) { OverlayLog.Error("Profile", "Profile page request failed.", exception, ("AppId", appId)); }
    }

    private Task HandleCommandAsync(OverlayCommand command)
    {
        OverlayLog.Info("Command", $"{command} received.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null)
        {
            var exception = new InvalidOperationException("Overlay dispatcher is unavailable.");
            OverlayLog.Error("Command", $"{command} handler failed.", exception);
            completion.TrySetException(exception);
        }
        else if (!_dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (_window is null) throw new InvalidOperationException("Overlay window is unavailable.");
                switch (command)
                {
                    case OverlayCommand.Show:
                        await _window.ShowForPocAsync();
                        break;
                    case OverlayCommand.Hide:
                        await _window.HideForPocAsync();
                        break;
                    case OverlayCommand.Shutdown:
                        _window.Close();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(command));
                }
                OverlayLog.Info("Command", $"{command} completed.");
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                OverlayLog.Error("Command", $"{command} handler failed.", exception);
                completion.TrySetException(exception);
            }
        }))
        {
            var exception = new InvalidOperationException("Overlay dispatcher enqueue failed.");
            OverlayLog.Error("Command", $"{command} handler failed.", exception);
            completion.TrySetException(exception);
        }
        return completion.Task;
    }
}
