using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

public static class FrontendTransportProtocol { public const int CurrentVersion = 1; }
public static class FrontendPipeEndpoint
{
    public static string CreateForCurrentUserSession()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var identity = $"{sid}:{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"SteamInputAddonforClaw.Frontend.{Convert.ToHexString(hash.AsSpan(0, 8))}";
    }
}

public class FrontendTransportException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class FrontendProtocolException(string message) : FrontendTransportException(message);
public sealed class FrontendRemoteException(FrontendRemoteErrorCode code, string message) : FrontendTransportException(message) { public FrontendRemoteErrorCode Code { get; } = code; }

internal enum FrontendWireMessageKind { Handshake, HandshakeAccepted, Request, CancelRequest, Response, Notification, ProtocolError }
internal enum FrontendRpcMethod { Unknown = 0, GetBootstrap, CaptureStatus, SetLaunchAtWindowsStartup, SetRouteInSteamBigPicture, SetLogLevel, SuppressDeveloperMenuWarning, SetDeveloperTestMode, RunPrerequisiteSetup, GenerateEnvironmentReport }
internal enum FrontendNotificationKind { StateInvalidated }
public enum FrontendRemoteErrorCode { ProtocolMismatch, InvalidMessage, UnsupportedMethod, OperationFailed, Cancelled }
internal sealed record FrontendWireError(FrontendRemoteErrorCode Code, string Message);
internal sealed record FrontendWireEnvelope(int ProtocolVersion, FrontendWireMessageKind Kind, long? RequestId = null, FrontendRpcMethod? Method = null, FrontendNotificationKind? Notification = null, JsonElement? Payload = null, FrontendWireError? Error = null);
internal sealed record SetLaunchAtWindowsStartupRequest(bool Enabled);
internal sealed record SetRouteInSteamBigPictureRequest(bool Enabled);
internal sealed record SetLogLevelRequest(FrontendLogLevel Level);
internal sealed record SetDeveloperTestModeRequest(bool Enabled);

internal static class FrontendWireCodec
{
    internal const int MaxFrameBytes = 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new() { Converters = { new FrontendRpcMethodJsonConverter(), new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) } };
    internal static async Task WriteAsync(Stream stream, FrontendWireEnvelope envelope, SemaphoreSlim gate, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
        if (data.Length is 0 or > MaxFrameBytes) throw new FrontendProtocolException("Invalid frame length.");
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, data.Length);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { await stream.WriteAsync(prefix, token).ConfigureAwait(false); await stream.WriteAsync(data, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    internal static async Task<FrontendWireEnvelope> ReadAsync(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4]; await ReadExactlyAsync(stream, prefix, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > MaxFrameBytes) throw new FrontendProtocolException("Invalid frame length.");
        var data = new byte[length]; await ReadExactlyAsync(stream, data, token).ConfigureAwait(false);
        try { return JsonSerializer.Deserialize<FrontendWireEnvelope>(data, Json) ?? throw new FrontendProtocolException("Invalid JSON frame."); }
        catch (JsonException exception) { throw new FrontendProtocolException($"Invalid JSON frame: {exception.Message}"); }
    }
    internal static async Task ReadExactlyAsync(Stream stream, Memory<byte> target, CancellationToken token)
    { var offset = 0; while (offset < target.Length) { var read = await stream.ReadAsync(target[offset..], token).ConfigureAwait(false); if (read == 0) throw new EndOfStreamException(); offset += read; } }
    internal static JsonElement Payload<T>(T value) => JsonSerializer.SerializeToElement(value, Json);
    internal static T Decode<T>(JsonElement? value)
    {
        var element = value ?? throw new FrontendProtocolException("Missing payload.");
        return element.Deserialize<T>(Json) ?? throw new FrontendProtocolException("Invalid payload.");
    }
}

internal sealed class FrontendRpcMethodJsonConverter : JsonConverter<FrontendRpcMethod>
{
    public override FrontendRpcMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("RPC method must be a string.");

        var value = reader.GetString();
        return value is not null && Enum.TryParse<FrontendRpcMethod>(value, ignoreCase: false, out var method) && method != FrontendRpcMethod.Unknown
            ? method
            : FrontendRpcMethod.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, FrontendRpcMethod value, JsonSerializerOptions options)
    {
        if (value == FrontendRpcMethod.Unknown || !Enum.IsDefined(value))
            throw new JsonException("Unknown RPC method cannot be written.");

        writer.WriteStringValue(value.ToString());
    }
}
