using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>A persisted XBOX profile keyed by canonical Xbox identity in
/// <see cref="ProfileDocument.XboxGames"/>. The key is authoritative; display name is metadata.</summary>
public sealed record XboxGameProfile
{
    public bool Enabled { get; init; }
    public bool Favorite { get; init; }
    public string? DisplayName { get; init; }
    public GamePerformanceOverrides Performance { get; init; } = new();
    public GameDisplayOverrides Display { get; init; } = new();
    public NonSteamGameControllerOverrides Controller { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
