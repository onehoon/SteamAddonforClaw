using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices;

public enum HandheldDeviceResolutionStatus { Matched, Unsupported, Indeterminate, Ambiguous }

public sealed record HandheldDeviceResolution(
    HandheldDeviceResolutionStatus Status,
    IHandheldDeviceAdapter? Adapter,
    string Reason);

public sealed class HandheldDeviceRegistry
{
    private readonly IHandheldDeviceAdapter[] _adapters;

    public HandheldDeviceRegistry(IEnumerable<IHandheldDeviceAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToArray();
        if (_adapters.Any(adapter => adapter is null)) throw new ArgumentException("Device adapters cannot contain null entries.", nameof(adapters));
        if (_adapters.GroupBy(adapter => adapter.Descriptor.Id).Any(group => group.Count() > 1)) throw new ArgumentException("Device adapter IDs must be unique.", nameof(adapters));
    }

    public bool TryGetById(HandheldDeviceId deviceId, out IHandheldDeviceAdapter adapter)
    {
        adapter = _adapters.SingleOrDefault(candidate => candidate.Descriptor.Id == deviceId)!;
        return adapter is not null;
    }

    public HandheldDeviceResolution Resolve(DeviceProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var results = _adapters.Select(adapter => ProbeSafely(adapter, context)).ToArray();

        if (results.Any(result => result.Result.Status == DeviceProbeStatus.Indeterminate))
            return new(HandheldDeviceResolutionStatus.Indeterminate, null, "At least one handheld-device probe was indeterminate.");

        var matches = results.Where(result => result.Result.Status == DeviceProbeStatus.Match).ToArray();
        return matches.Length switch
        {
            0 => new(HandheldDeviceResolutionStatus.Unsupported, null, "No handheld-device adapter matched."),
            1 => new(HandheldDeviceResolutionStatus.Matched, matches[0].Adapter, matches[0].Result.Reason),
            _ => new(HandheldDeviceResolutionStatus.Ambiguous, null, "Multiple handheld-device adapters matched.")
        };
    }

    private static ProbeAttempt ProbeSafely(IHandheldDeviceAdapter adapter, DeviceProbeContext context)
    {
        try { return new(adapter, adapter.Probe(context)); }
        catch (Exception exception)
        {
            return new(adapter, new DeviceProbeResult(DeviceProbeStatus.Indeterminate, $"Probe failed: {exception.GetType().Name}."));
        }
    }

    private sealed record ProbeAttempt(IHandheldDeviceAdapter Adapter, DeviceProbeResult Result);
}
