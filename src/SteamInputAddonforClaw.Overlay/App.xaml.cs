using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Contracts.Overlay;
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
            OverlayLog.Info("Transport", "Overlay command loop starting.");
            await _client.RunAsync(HandleCommandAsync, HandleNavigationAsync, HandleTabOrderAsync).ConfigureAwait(false);
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
    private Task HandleTabOrderAsync(IReadOnlyList<OverlayTabId> order)
    {
        OverlayLog.Info("TabOrder", "Authoritative tab order received.", ("Count", order.Count));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _window?.ApplyTabOrder(order);
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
