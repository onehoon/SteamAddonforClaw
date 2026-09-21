using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class WindowsMsiClawModeWriter : IMsiClawModeWriter, IMsiClawGamepadModeIo
{
    private readonly IMsiClawHidDeviceInformationLookup _lookup;
    private readonly IMsiClawRawHidTransport _transport;
    internal WindowsMsiClawModeWriter(IMsiClawHidDeviceInformationLookup? lookup = null, IMsiClawRawHidTransport? transport = null)
    {
        _lookup = lookup ?? new WindowsMsiClawHidDeviceInformationLookup();
        _transport = transport ?? new WindowsMsiClawRawHidTransport();
    }
    public async Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken)
        => await WriteGamepadModeAsync(device, mode switch
        {
            MsiClawNativeMode.XInput => MsiClawGamepadMode.XInput,
            MsiClawNativeMode.DirectInput => MsiClawGamepadMode.DirectInput,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        }, cancellationToken).ConfigureAwait(false);

    public async Task<bool> WriteGamepadModeAsync(MsiClawControlHidDevice device, MsiClawGamepadMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (device.VerifiedIdentity.Confidence != MsiClawIdentityConfidence.Strong || (string.IsNullOrWhiteSpace(device.VerifiedIdentity.PhysicalDeviceKey) && !IsUsable(device.VerifiedIdentity.ContainerId))) return false;
        if (!MsiClawPhysicalIdentity.From(device.Device).StronglyMatches(device.VerifiedIdentity)) return false;
        var selector = HidDevice.GetDeviceSelector(device.UsagePage, device.Usage, MsiClawHardware.VendorId, device.Device.ProductId ?? 0);
        var infos = await _lookup.FindAsync(selector, cancellationToken).ConfigureAwait(false);
        var matching = SelectDeviceInformation(device, infos);
        if (matching is null) return false;
        var bytes = MsiClawModeCommand.BuildSwitch(mode);
        if (!await _transport.WriteAsync(matching.Id, bytes, cancellationToken).ConfigureAwait(false)) return false;
        AppLog.Debug("NativeMode", "MSI Claw mode command written.", ("PID", device.Device.ProductId), ("UsagePage", device.UsagePage), ("Usage", device.Usage), ("ReportLength", bytes.Length), ("Mode", mode));
        return true;
    }

    public async Task<byte[]?> ReadGamepadModeAsync(MsiClawControlHidDevice device, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (device.VerifiedIdentity.Confidence != MsiClawIdentityConfidence.Strong || (string.IsNullOrWhiteSpace(device.VerifiedIdentity.PhysicalDeviceKey) && !IsUsable(device.VerifiedIdentity.ContainerId))) return null;
        if (!MsiClawPhysicalIdentity.From(device.Device).StronglyMatches(device.VerifiedIdentity)) return null;
        var selector = HidDevice.GetDeviceSelector(device.UsagePage, device.Usage, MsiClawHardware.VendorId, device.Device.ProductId ?? 0);
        var infos = await _lookup.FindAsync(selector, cancellationToken).ConfigureAwait(false);
        var matching = SelectDeviceInformation(device, infos);
        if (matching is null) return null;

        var reports = await _transport.WriteAndReadAsync(
            matching.Id,
            MsiClawModeCommand.BuildReadGamepadMode(),
            reportLength: 64,
            maxReports: 4,
            timeout,
            cancellationToken).ConfigureAwait(false);
        if (reports is null) return null;

        return reports.FirstOrDefault(report =>
            MsiClawModeCommand.TryParseGamepadModeAck(report, out _));
    }

    internal static MsiClawHidDeviceInformation? SelectDeviceInformation(MsiClawControlHidDevice expected, IReadOnlyList<MsiClawHidDeviceInformation> candidates)
    {
        var matching = candidates.Where(info => MatchesIdentity(info, expected)).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    private static bool MatchesIdentity(MsiClawHidDeviceInformation info, MsiClawControlHidDevice expected)
    {
        if (!string.Equals(info.InstanceId, expected.Device.InstanceId, StringComparison.OrdinalIgnoreCase)) return false;
        return !IsUsable(expected.VerifiedIdentity.ContainerId) || info.ContainerId == expected.VerifiedIdentity.ContainerId;
    }
    private static bool IsUsable(Guid? value) => value is Guid guid && guid != Guid.Empty && guid != new Guid("00000000-0000-0000-ffff-ffffffffffff");
}

internal interface IMsiClawGamepadModeIo
{
    Task<bool> WriteGamepadModeAsync(MsiClawControlHidDevice device, MsiClawGamepadMode mode, CancellationToken cancellationToken);
    Task<byte[]?> ReadGamepadModeAsync(MsiClawControlHidDevice device, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed record MsiClawHidDeviceInformation(string Id, string? InstanceId, Guid? ContainerId);
internal interface IMsiClawHidDeviceInformationLookup { Task<IReadOnlyList<MsiClawHidDeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken); }
internal sealed class WindowsMsiClawHidDeviceInformationLookup : IMsiClawHidDeviceInformationLookup
{
    public async Task<IReadOnlyList<MsiClawHidDeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken)
    {
        var infos = await DeviceInformation.FindAllAsync(selector, new[] { "System.Devices.DeviceInstanceId", "System.Devices.ContainerId" }).AsTask(cancellationToken).ConfigureAwait(false);
        return infos.Select(info => new MsiClawHidDeviceInformation(info.Id,
            info.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var value) ? value as string : null,
            info.Properties.TryGetValue("System.Devices.ContainerId", out var container) ? container as Guid? : null)).ToArray();
    }
}
