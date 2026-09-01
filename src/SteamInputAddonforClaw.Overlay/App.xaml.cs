using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Overlay;

public partial class App : Application
{
    private OverlayWindow? _window;
    private DispatcherQueue? _dispatcherQueue;
    private NamedPipeOverlayClient? _client;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _window = new OverlayWindow();
        _window.Closed += (_, _) => Exit();
        _window.PrepareHidden();
        _ = ConnectAndRunAsync();
    }

    private async Task ConnectAndRunAsync()
    {
        try
        {
            _client = new NamedPipeOverlayClient(FrontendPipeEndpoint.CreateOverlayForCurrentUser());
            await _client.RunAsync(HandleCommandAsync).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Overlay transport failed: {exception}");
            _dispatcherQueue?.TryEnqueue(() => Exit());
        }
        finally
        {
            if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task HandleCommandAsync(OverlayCommand command)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                if (_window is null) throw new InvalidOperationException("Overlay window is unavailable.");
                switch (command)
                {
                    case OverlayCommand.Show:
                        _window.ShowForPoc();
                        break;
                    case OverlayCommand.Hide:
                        _window.HideForPoc();
                        break;
                    case OverlayCommand.Shutdown:
                        _window.Close();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(command));
                }
                completion.TrySetResult();
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) != true)
            completion.TrySetException(new InvalidOperationException("Overlay dispatcher is unavailable."));
        return completion.Task;
    }
}
