namespace SteamInputAddonforClaw.Controllers.Detection;

public interface IControllerDeviceEnumerator
{
    IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices();

    IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, ushort productId)
    {
        var devices = EnumeratePresentDevices();
        var wanted = devices.Where(d => d.Present && d.VendorId == vendorId && d.ProductId == productId).ToArray();
        var ids = wanted.SelectMany(d => d.AncestorInstanceIds.Append(d.ParentInstanceId ?? string.Empty)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return devices.Where(d => wanted.Contains(d) || ids.Contains(d.InstanceId)).ToArray();
    }

    ControllerDeviceInfo? FindPresentDevice(string instanceId) =>
        EnumeratePresentDevices().SingleOrDefault(d => d.Present && string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

    bool IsPresent(ushort vendorId, ushort productId) => EnumeratePresentDevices(vendorId, productId).Count != 0;
}
