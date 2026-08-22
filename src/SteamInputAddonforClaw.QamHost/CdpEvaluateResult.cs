using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>Interpreted outcome of a <c>Runtime.evaluate</c> response.</summary>
public sealed record CdpEvaluateResult(bool Succeeded, bool? BooleanValue, string? StringValue, string? ErrorText)
{
    /// <summary>
    /// Parses a raw <c>Runtime.evaluate</c> JSON-RPC response. Treats a CDP-level
    /// <c>exceptionDetails</c> as failure, and otherwise surfaces the result value as a boolean
    /// when the evaluated expression returned one (our qam.js contract).
    /// </summary>
    public static CdpEvaluateResult Parse(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("result", out var result))
        {
            return new CdpEvaluateResult(Succeeded: false, BooleanValue: null, StringValue: null, ErrorText: "no result field in CDP response");
        }

        if (result.TryGetProperty("exceptionDetails", out var exceptionDetails))
        {
            var text = exceptionDetails.TryGetProperty("text", out var t) ? t.GetString() : "exception thrown during evaluation";
            var description = exceptionDetails.TryGetProperty("exception", out var exception) &&
                              exception.TryGetProperty("description", out var d) ? d.GetString() : null;
            var errorText = string.IsNullOrWhiteSpace(description) ? text : $"{description} ({text})";
            return new CdpEvaluateResult(Succeeded: false, BooleanValue: null, StringValue: null, ErrorText: errorText);
        }

        if (!result.TryGetProperty("result", out var valueContainer) || !valueContainer.TryGetProperty("value", out var value))
        {
            return new CdpEvaluateResult(Succeeded: true, BooleanValue: null, StringValue: null, ErrorText: null);
        }

        var booleanValue = value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False
            ? value.GetBoolean()
            : (bool?)null;

        var stringValue = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new CdpEvaluateResult(Succeeded: true, BooleanValue: booleanValue, StringValue: stringValue, ErrorText: null);
    }
}
