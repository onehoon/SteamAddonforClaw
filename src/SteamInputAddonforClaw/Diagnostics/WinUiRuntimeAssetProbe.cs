namespace SteamInputAddonforClaw.Diagnostics;

internal static class WinUiRuntimeAssetProbe
{
    private static readonly string[] RequiredAssets = [
        "App.xbf",
        "MainWindow.xbf",
        "SteamInputAddonforClaw.pri",
        "Assets\\AppIcon.ico"
    ];

    public static IReadOnlyList<WinUiRuntimeAssetState> Inspect(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        return RequiredAssets.Select(asset =>
        {
            var file = new FileInfo(Path.Combine(baseDirectory, asset));
            return new WinUiRuntimeAssetState(asset, file.Exists, file.Exists ? file.Length : null);
        }).ToArray();
    }
}

internal sealed record WinUiRuntimeAssetState(string Name, bool Exists, long? SizeBytes);
