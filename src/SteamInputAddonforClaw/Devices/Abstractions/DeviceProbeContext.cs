namespace SteamInputAddonforClaw.Devices.Abstractions;

public sealed class DeviceProbeContext
{
    public DeviceProbeContext(
        IEnumerable<DeviceProbePnpDevice>? presentPnpDevices = null,
        string? systemManufacturer = null,
        string? systemProductName = null,
        string? baseBoardProduct = null)
    {
        PresentPnpDevices = (presentPnpDevices ?? []).ToArray();
        SystemManufacturer = systemManufacturer;
        SystemProductName = systemProductName;
        BaseBoardProduct = baseBoardProduct;
    }

    public IReadOnlyList<DeviceProbePnpDevice> PresentPnpDevices { get; }
    public string? SystemManufacturer { get; }
    public string? SystemProductName { get; }
    public string? BaseBoardProduct { get; }
}

public sealed record DeviceProbePnpDevice
{
    public DeviceProbePnpDevice(string instanceId, IEnumerable<string>? hardwareIds = null, ushort? vendorId = null, ushort? productId = null)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("A PnP instance ID is required.", nameof(instanceId));
        InstanceId = instanceId;
        HardwareIds = (hardwareIds ?? []).ToArray();
        VendorId = vendorId;
        ProductId = productId;
    }

    public string InstanceId { get; }
    public IReadOnlyList<string> HardwareIds { get; }
    public ushort? VendorId { get; }
    public ushort? ProductId { get; }
}
