namespace SteamInputAddonforClaw.Devices.Abstractions;

public readonly record struct HandheldDeviceModelId(string Value)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
}

public sealed record HandheldDeviceModelDescriptor(
    HandheldDeviceModelId Id,
    HandheldDeviceId FamilyId,
    string DisplayName,
    string HardwareModelId);

public enum HandheldDeviceModelResolutionStatus { Matched, Unsupported, Indeterminate }

public sealed record HandheldDeviceModelResolution(
    HandheldDeviceModelResolutionStatus Status,
    HandheldDeviceModelDescriptor? Model,
    string Reason);

public interface IHandheldDeviceModelResolver
{
    HandheldDeviceModelResolution Resolve(DeviceProbeContext context);
}
