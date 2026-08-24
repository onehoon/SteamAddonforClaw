using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

// Review fix (MAJOR): the Steam Input Routing master-switch PR renamed FrontendRpcMethod.
// SetRouteInSteamBigPicture -> SetSteamInputRoutingEnabled (and the matching request record /
// FrontendSettingsSnapshot property), which FrontendRpcMethodJsonConverter serializes by exact
// string name. Bumping the version here makes an old v1 peer fail the handshake up front instead
// of connecting successfully and only failing later as UnsupportedMethod/payload-deserialization
// errors once it tries the renamed RPC.
//
// Version 3: the OEM1 mapping framework adds FrontendRpcMethod.SetOem1Mapping and a required
// Oem1Mapping member on FrontendSettingsSnapshot. A v2 peer would connect, then fail every settings
// response deserialization on the new required member -- failing the handshake up front is the
// honest outcome.
//
// Version 4: the OEM1 hardware-availability gate adds a required Oem1MappingAvailable member on
// FrontendBootstrapSnapshot, for the same reason.
// Version 6: WING mapping persistence adds the WingMapping setting and SetWingMapping RPC.
// Version 7: Game Profile capture, catalog, and mutation RPCs.
// Version 8: Game Profile CPU Boost mutation changes from one combined AC/DC RPC
// to independent AC and DC RPCs.
// Version 10: per-game Display resolution snapshot and mutation RPC.
// Version 11: Windows Power Mode contracts and RPCs.
// Version 12: developer-only MSI fan probe contracts and RPCs.
// Version 13: Intel Game Profile FPS Limit snapshot and mutation RPCs.
// Version 14: SetSteamInputRoutingEnabled returns a typed mutation result instead of a settings snapshot.
// Version 15: virtual output identity uncertainty is removed from frontend status and routing contracts.
// Version 16: per-profile CPU Boost, TDP, and Power Mode ownership adds required Enabled members
// to game-profile frontend snapshots and adds three profile feature-enable RPCs. A v15 peer must
// fail the handshake before either side deserializes or invokes the new contract.
public static class FrontendTransportProtocol { public const int CurrentVersion = 16; }
public static class FrontendPipeEndpoint
{
    /// <summary>Supported product model is one Windows user, one interactive session -- the SID
    /// alone already uniquely identifies the pipe on the machine, so no Windows Terminal Services
    /// session component is included.</summary>
    public static string CreateForCurrentUser()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sid));
        return $"SteamInputAddonforClaw.Frontend.{Convert.ToHexString(hash.AsSpan(0, 8))}";
    }

    public static string CreateQamForCurrentUser()
    {
        var desktop = CreateForCurrentUser();
        return $"{desktop}.Qam";
    }
}

public class FrontendTransportException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class FrontendProtocolException(string message) : FrontendTransportException(message);
public sealed class FrontendRemoteException(FrontendRemoteErrorCode code, string message) : FrontendTransportException(message) { public FrontendRemoteErrorCode Code { get; } = code; }

internal enum FrontendWireMessageKind { Handshake, HandshakeAccepted, Request, CancelRequest, Response, Notification, ProtocolError }
internal enum FrontendRpcMethod { Unknown = 0, GetBootstrap, CaptureStatus, SetLaunchAtWindowsStartup, SetSteamInputRoutingEnabled, SetLogLevel, SetOem1Mapping, SetWingMapping, SuppressDeveloperMenuWarning, SetDeveloperTestMode, RunPrerequisiteSetup, GenerateEnvironmentReport, RunVibrationTest, OpenVibrationTestSession, CloseVibrationTestSession, CaptureCpuBoost, SetDeviceCpuBoostAc, SetDeviceCpuBoostDc, SetDeviceCpuBoostEnabled, CaptureTdp, SetDeviceTdp, SetDeviceTdpEnabled, OpenClawSensorProbe, StartClawSensorProbe, CaptureClawSensorProbe, NextClawSensorProbePhase, PreviousClawSensorProbePhase, StopClawSensorProbe, CloseClawSensorProbe, ScanProfileGames, CaptureGameProfile, CaptureActiveGameProfile, SetGameProfileEnabled, SetGameProfileCpuBoostEnabled, SetGameProfileCpuBoostAc, SetGameProfileCpuBoostDc, SetGameProfileTdpEnabled, SetGameProfileTdp, SetGameProfileFavorite, SetGameProfileResolution, CapturePowerMode, SetDevicePowerModeAc, SetDevicePowerModeDc, SetDevicePowerModeEnabled, SetGameProfilePowerModeEnabled, SetGameProfilePowerModeAc, SetGameProfilePowerModeDc, SetGameProfileFpsLimitEnabled, SetGameProfileFpsLimitAc, SetGameProfileFpsLimitDc, OpenFanProbe, RunFanProbe }
internal enum FrontendNotificationKind { StateInvalidated }
public enum FrontendRemoteErrorCode { ProtocolMismatch, InvalidMessage, UnsupportedMethod, OperationFailed, Cancelled }
internal sealed record FrontendWireError(FrontendRemoteErrorCode Code, string Message);
internal sealed record FrontendWireEnvelope(int ProtocolVersion, FrontendWireMessageKind Kind, long? RequestId = null, FrontendRpcMethod? Method = null, FrontendNotificationKind? Notification = null, JsonElement? Payload = null, FrontendWireError? Error = null);
internal sealed record SetLaunchAtWindowsStartupRequest(bool Enabled);
internal sealed record SetSteamInputRoutingEnabledRequest(bool Enabled);
internal sealed record SetLogLevelRequest(FrontendLogLevel Level);
internal sealed record SetOem1MappingRequest(SteamInputAddonforClaw.Contracts.Oem1.Oem1MappingSettings Mapping);
internal sealed record SetWingMappingRequest(SteamInputAddonforClaw.Contracts.Wing.WingMappingSettings Mapping);
internal sealed record SetDeveloperTestModeRequest(bool Enabled);
internal sealed record RunVibrationTestRequest(FrontendVibrationTestCommand Command);
internal sealed record RunFanProbeRequest(FrontendFanProbeOperation Operation);
internal sealed record SetDeviceCpuBoostAcRequest(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode Mode);
internal sealed record SetDeviceCpuBoostDcRequest(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode Mode);
internal sealed record SetDeviceCpuBoostEnabledRequest(bool Enabled);
internal sealed record SetDevicePowerModeAcRequest(SteamInputAddonforClaw.Contracts.DeviceProfiles.WindowsPowerMode Mode);
internal sealed record SetDevicePowerModeDcRequest(SteamInputAddonforClaw.Contracts.DeviceProfiles.WindowsPowerMode Mode);
internal sealed record SetDevicePowerModeEnabledRequest(bool Enabled);
internal sealed record SetDeviceTdpRequest(FrontendTdpConfiguration Configuration);
internal sealed record SetDeviceTdpEnabledRequest(bool Enabled);
internal sealed record CaptureGameProfileRequest(uint AppId);
internal sealed record SetGameProfileEnabledRequest(uint AppId, bool Enabled, string? DisplayName);
internal sealed record SetGameProfileCpuBoostEnabledRequest(uint AppId, bool Enabled);
internal sealed record SetGameProfileTdpEnabledRequest(uint AppId, bool Enabled);
internal sealed record SetGameProfilePowerModeEnabledRequest(uint AppId, bool Enabled);
internal sealed record SetGameProfileCpuBoostAcRequest(uint AppId, SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode Mode);
internal sealed record SetGameProfileCpuBoostDcRequest(uint AppId, SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode Mode);
internal sealed record SetGameProfilePowerModeAcRequest(uint AppId, SteamInputAddonforClaw.Contracts.DeviceProfiles.WindowsPowerMode Mode);
internal sealed record SetGameProfilePowerModeDcRequest(uint AppId, SteamInputAddonforClaw.Contracts.DeviceProfiles.WindowsPowerMode Mode);
internal sealed record SetGameProfileFpsLimitEnabledRequest(uint AppId, bool Enabled);
internal sealed record SetGameProfileFpsLimitAcRequest(uint AppId, int Fps);
internal sealed record SetGameProfileFpsLimitDcRequest(uint AppId, int Fps);
internal sealed record SetGameProfileTdpRequest(uint AppId, FrontendGameTdpConfiguration Configuration);
internal sealed record SetGameProfileFavoriteRequest(uint AppId, bool Favorite, string? DisplayName);
internal sealed record SetGameProfileResolutionRequest(uint AppId, FrontendGameResolution? Resolution, string? DisplayName);

internal static class FrontendWireCodec
{
    internal const int MaxFrameBytes = 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new FrontendRpcMethodJsonConverter(), new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };
    internal static async Task WriteAsync(Stream stream, FrontendWireEnvelope envelope, SemaphoreSlim gate, CancellationToken token)
        => await WriteAsync(stream, envelope, gate, token, token).ConfigureAwait(false);
    internal static async Task WriteAsync(Stream stream, FrontendWireEnvelope envelope, SemaphoreSlim gate, CancellationToken gateCancellationToken, CancellationToken writeCancellationToken)
        => await WriteAsync(stream, envelope, gate, gateCancellationToken, writeCancellationToken, null).ConfigureAwait(false);
    internal static async Task WriteAsync(Stream stream, FrontendWireEnvelope envelope, SemaphoreSlim gate, CancellationToken gateCancellationToken, CancellationToken writeCancellationToken, Action? frameStarted)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
        if (data.Length is 0 or > MaxFrameBytes) throw new FrontendProtocolException("Invalid frame length.");
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, data.Length);
        await gate.WaitAsync(gateCancellationToken).ConfigureAwait(false);
        try { frameStarted?.Invoke(); await stream.WriteAsync(prefix, writeCancellationToken).ConfigureAwait(false); await stream.WriteAsync(data, writeCancellationToken).ConfigureAwait(false); await stream.FlushAsync(writeCancellationToken).ConfigureAwait(false); }
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
        try
        {
            return element.Deserialize<T>(Json) ?? throw new FrontendProtocolException("Invalid payload.");
        }
        catch (JsonException exception)
        {
            throw new FrontendProtocolException($"Invalid payload: {exception.Message}");
        }
    }
}

internal sealed class FrontendRpcMethodJsonConverter : JsonConverter<FrontendRpcMethod>
{
    public override FrontendRpcMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("RPC method must be a string.");

        var value = reader.GetString();
        return value is not null &&
               Enum.TryParse<FrontendRpcMethod>(value, ignoreCase: false, out var method) &&
               method != FrontendRpcMethod.Unknown &&
               Enum.IsDefined(method) &&
               string.Equals(value, method.ToString(), StringComparison.Ordinal)
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
