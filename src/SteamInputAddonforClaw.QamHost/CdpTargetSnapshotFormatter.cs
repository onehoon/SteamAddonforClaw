using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>Formats a redacted, read-only snapshot of the CEF DevTools target list.</summary>
internal static class CdpTargetSnapshotFormatter
{
    internal static string Format(string reason, IReadOnlyList<CdpTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(targets);

        var snapshot = new
        {
            Reason = reason,
            Count = targets.Count,
            Targets = targets.Select(target => new
            {
                target.Id,
                target.Type,
                target.Title,
                target.Url,
                WebSocketDebuggerUrlPresent = !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl),
            }).ToArray(),
        };

        return $"QAM CDP target snapshot. {JsonSerializer.Serialize(snapshot)}";
    }
}
