using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.BackButtons;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>Optional controller-output overrides shared by non-Steam game profiles.</summary>
public sealed record NonSteamGameControllerOverrides
{
    public BackButtonMappingSettings? BackButtonMapping { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
