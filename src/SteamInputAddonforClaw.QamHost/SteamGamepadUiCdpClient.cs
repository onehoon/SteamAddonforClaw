using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>
/// Minimal CEF DevTools (CDP) client: enough to list targets, connect to one, and run
/// <c>Runtime.evaluate</c>. Not a general-purpose browser automation library.
/// </summary>
public sealed class SteamGamepadUiCdpClient : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly CdpCommandCorrelator _correlator = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoop;
    public event Action<string>? AddonQamConsoleMessage;

    public SteamGamepadUiCdpClient(Uri devToolsEndpoint)
    {
        _http = new HttpClient { BaseAddress = devToolsEndpoint, Timeout = ConnectTimeout };
    }

    public async Task<IReadOnlyList<CdpTarget>> ListTargetsAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ConnectTimeout);

        await using var stream = await _http.GetStreamAsync("/json", cts.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);

        var targets = new List<CdpTarget>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            targets.Add(new CdpTarget(
                Id: GetStringOrEmpty(element, "id"),
                Type: GetStringOrEmpty(element, "type"),
                Title: GetStringOrEmpty(element, "title"),
                Url: GetStringOrEmpty(element, "url"),
                WebSocketDebuggerUrl: element.TryGetProperty("webSocketDebuggerUrl", out var ws)
                    ? ws.GetString()
                    : null));
        }

        return targets;
    }

    public async Task ConnectAsync(CdpTarget target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
        {
            throw new InvalidOperationException("Target has no webSocketDebuggerUrl; cannot connect.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ConnectTimeout);

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), cts.Token).ConfigureAwait(false);
        _socket = socket;

        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoop = RunReceiveLoopAsync(socket, _correlator, _receiveLoopCts.Token, AddonQamConsoleMessage);
        await SendCommandAsync("Runtime.enable", parameters: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <c>Runtime.evaluate</c> with the given JS expression and returns the raw JSON result.</summary>
    public async Task<string> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        return await SendCommandAsync("Runtime.evaluate", new { expression, awaitPromise = false, returnByValue = true }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SendCommandAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        if (_socket is not { State: WebSocketState.Open })
            throw new InvalidOperationException("Not connected to a CDP target.");

        var id = _correlator.NextId();
        var payload = JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ResponseTimeout);

        var responseTask = _correlator.RegisterAsync(id, cts.Token);
        var bytes = Encoding.UTF8.GetBytes(payload);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cts.Token).ConfigureAwait(false);

        return await responseTask.ConfigureAwait(false);
    }

    private static async Task RunReceiveLoopAsync(ClientWebSocket socket, CdpCommandCorrelator correlator, CancellationToken cancellationToken, Action<string>? consoleMessage)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(messageStream.ToArray());
                TryDispatch(json, correlator, consoleMessage);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (WebSocketException)
        {
            // Connection dropped (e.g. Steam/GamepadUI closed); nothing further to correlate.
        }
    }

    private static void TryDispatch(string json, CdpCommandCorrelator correlator, Action<string>? consoleMessage)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("method", out var method) && method.GetString() == "Runtime.consoleAPICalled" &&
            document.RootElement.TryGetProperty("params", out var parameters) && parameters.TryGetProperty("args", out var args))
        {
            foreach (var arg in args.EnumerateArray())
                if (arg.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.StartsWith("[SteamInputAddon:QAM]", StringComparison.Ordinal))
                    consoleMessage?.Invoke(text);
        }
        if (document.RootElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
        {
            correlator.TryComplete(id, json);
        }
    }

    private static string GetStringOrEmpty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    public async ValueTask DisposeAsync()
    {
        _receiveLoopCts?.Cancel();
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "QamHost shutdown", CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
            }
        }

        _socket?.Dispose();
        _receiveLoopCts?.Dispose();
        _http.Dispose();
    }
}
