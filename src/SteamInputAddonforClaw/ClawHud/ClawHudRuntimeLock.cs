using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamInputAddonforClaw.ClawHud;

internal sealed record ClawHudRuntimeLock(
    int SchemaVersion,
    string RuntimeVersion,
    string Tag,
    string Asset,
    string SourceCommit,
    string Sha256)
{
    internal const int SupportedSchemaVersion = 1;
    internal const string RepositoryBaseUrl = "https://github.com/onehoon/ClawHUD";
    internal const string ExpectedAsset = "ClawHUDRuntime.zip";

    private static readonly Regex VersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Hex40Pattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Hex64Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal Uri DownloadUri => new($"{RepositoryBaseUrl}/releases/download/{Tag}/{Asset}", UriKind.Absolute);

    internal static ClawHudRuntimeLock Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("ClawHUD lock must be a JSON object.");

        var schemaVersion = ReadInt32(root, "schema_version");
        var runtimeVersion = ReadString(root, "runtime_version");
        var tag = ReadString(root, "tag");
        var asset = ReadString(root, "asset");
        var sourceCommit = ReadString(root, "source_commit");
        var sha256 = ReadString(root, "sha256");

        if (schemaVersion != SupportedSchemaVersion) throw new InvalidDataException($"Unsupported ClawHUD lock schema: {schemaVersion}.");
        if (!VersionPattern.IsMatch(runtimeVersion)) throw new InvalidDataException("ClawHUD runtime_version must be MAJOR.MINOR.PATCH.");
        if (!string.Equals(tag, $"steamaddon-runtime-v{runtimeVersion}", StringComparison.Ordinal)) throw new InvalidDataException("ClawHUD lock tag does not match runtime_version.");
        if (!string.Equals(asset, ExpectedAsset, StringComparison.Ordinal)) throw new InvalidDataException("ClawHUD lock asset is not the expected Runtime ZIP.");
        if (!Hex40Pattern.IsMatch(sourceCommit)) throw new InvalidDataException("ClawHUD source_commit must be exactly 40 hexadecimal characters.");
        if (!Hex64Pattern.IsMatch(sha256)) throw new InvalidDataException("ClawHUD sha256 must be exactly 64 hexadecimal characters.");

        return new ClawHudRuntimeLock(schemaVersion, runtimeVersion, tag, asset, sourceCommit.ToLowerInvariant(), sha256.ToLowerInvariant());
    }

    private static int ReadInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
            throw new InvalidDataException($"ClawHUD lock field '{propertyName}' is missing or invalid.");
        return value;
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidDataException($"ClawHUD lock field '{propertyName}' is missing or invalid.");
        return property.GetString()!;
    }
}
