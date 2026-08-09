namespace SteamInputAddonforClaw.Devices.Abstractions;

public enum DeviceProbeStatus { Match, NoMatch, Indeterminate }

public sealed record DeviceProbeResult
{
    public DeviceProbeResult(DeviceProbeStatus status, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A probe reason is required.", nameof(reason));
        Status = status;
        Reason = reason;
    }

    public DeviceProbeStatus Status { get; }
    public string Reason { get; }
}
