using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Status;

internal interface IControllerDisplayNameResolver { string Resolve(ControllerDeviceInfo device); }

internal sealed class WindowsControllerDisplayNameResolver : IControllerDisplayNameResolver
{
    public string Resolve(ControllerDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.FriendlyName)) return device.FriendlyName;
        return device.VendorId is not null || device.ProductId is not null
            ? $"Controller ({FormatId("VID", device.VendorId)} / {FormatId("PID", device.ProductId)})"
            : "Unknown Controller";
    }

    internal static string FormatId(string label, ushort? value) => value is null ? $"{label} Unknown" : $"{label} {value.Value:X4}";
}

internal static class ExternalControllerStatusCardFactory
{
    public static IReadOnlyList<StatusCardViewModel> Create(ExternalControllerAssessment assessment, IControllerDisplayNameResolver? nameResolver = null)
    {
        var resolver = nameResolver ?? new WindowsControllerDisplayNameResolver();
        return assessment.Status switch
        {
            ExternalControllerAssessmentStatus.Clear => [new("No External Controller", "Clear", "No external physical controller found.")],
            ExternalControllerAssessmentStatus.Indeterminate => [new("External Controller", "Indeterminate", "Controller state could not be verified.")],
            _ => assessment.ExternalControllers
                .Select(device => (Device: device, Name: resolver.Resolve(device)))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Device.VendorId)
                .ThenBy(item => item.Device.ProductId)
                .Select(item => new StatusCardViewModel(item.Name, "Detected", FormatDetails(item.Device)))
                .ToArray()
        };
    }

    public static string GetFirstControllerName(ExternalControllerAssessment assessment, IControllerDisplayNameResolver? nameResolver = null) =>
        assessment.ExternalControllers.Count == 0 ? "Unknown Controller" : (nameResolver ?? new WindowsControllerDisplayNameResolver()).Resolve(assessment.ExternalControllers[0]);

    private static string FormatDetails(ControllerDeviceInfo device)
    {
        if (device.VendorId is null && device.ProductId is null) return "Physical external controller";
        var values = new List<string>();
        if (device.VendorId is not null) values.Add($"VID {device.VendorId.Value:X4}");
        if (device.ProductId is not null) values.Add($"PID {device.ProductId.Value:X4}");
        return string.Join(" · ", values);
    }
}
