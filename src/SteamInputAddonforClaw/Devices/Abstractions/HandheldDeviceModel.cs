namespace SteamInputAddonforClaw.Devices.Abstractions;

public readonly record struct HandheldDeviceModelId(string Value)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
}

public sealed record HandheldDeviceModelDescriptor(
    HandheldDeviceModelId Id,
    HandheldDeviceId FamilyId,
    string DisplayName,
    string HardwareModelId)
{
    // Application composition policy, not part of the public adapter contract. Unknown models
    // remain ineligible unless their descriptor explicitly opts into a feature.
    internal HandheldModelFeatures Features { get; init; } = HandheldModelFeatures.None;
}

public enum HandheldDeviceModelResolutionStatus { Matched, Unsupported, Indeterminate }

public sealed record HandheldDeviceModelResolution(
    HandheldDeviceModelResolutionStatus Status,
    HandheldDeviceModelDescriptor? Model,
    string Reason);

public interface IHandheldDeviceModelResolver
{
    HandheldDeviceModelResolution Resolve(DeviceProbeContext context);
}
