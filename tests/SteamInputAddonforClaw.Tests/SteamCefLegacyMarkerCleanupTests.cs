using System.Text.Json;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("SteamCefLegacyMarkerCleanup")]
public sealed class SteamCefLegacyMarkerCleanupTests
{
    [Fact]
    public void Missing_ownership_evidence_does_not_delete_marker()
    {
        using var scope = new Scope();
        File.WriteAllText(scope.MarkerPath, string.Empty);

        Assert.True(SteamCefLegacyMarkerCleanup.RemoveOwnedMarker());
        Assert.True(File.Exists(scope.MarkerPath));
    }

    [Fact]
    public void Owned_marker_and_evidence_are_deleted()
    {
        using var scope = new Scope();
        File.WriteAllText(scope.MarkerPath, string.Empty);
        File.WriteAllText(scope.OwnershipPath, JsonSerializer.Serialize(new { SteamDirectory = scope.DirectoryPath }));

        Assert.True(SteamCefLegacyMarkerCleanup.RemoveOwnedMarker());
        Assert.False(File.Exists(scope.MarkerPath));
        Assert.False(File.Exists(scope.OwnershipPath));
    }

    [Fact]
    public void Malformed_ownership_evidence_is_preserved()
    {
        using var scope = new Scope();
        File.WriteAllText(scope.MarkerPath, string.Empty);
        File.WriteAllText(scope.OwnershipPath, "not-json");

        Assert.False(SteamCefLegacyMarkerCleanup.RemoveOwnedMarker());
        Assert.True(File.Exists(scope.MarkerPath));
        Assert.True(File.Exists(scope.OwnershipPath));
    }

    [Fact]
    public void Cleanup_failure_preserves_ownership_evidence_for_retry()
    {
        using var scope = new Scope();
        File.WriteAllText(scope.MarkerPath, string.Empty);
        File.WriteAllText(scope.OwnershipPath, JsonSerializer.Serialize(new { SteamDirectory = scope.DirectoryPath }));
        using var markerLock = new FileStream(scope.MarkerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.False(SteamCefLegacyMarkerCleanup.RemoveOwnedMarker());
        Assert.True(File.Exists(scope.OwnershipPath));
    }

    private sealed class Scope : IDisposable
    {
        internal Scope()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "steam-cef-cleanup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            OwnershipPath = Path.Combine(DirectoryPath, "steam-cef-marker.json");
            MarkerPath = Path.Combine(DirectoryPath, SteamCefLegacyMarkerCleanup.MarkerFileName);
            SteamCefLegacyMarkerCleanup.OwnershipPathProvider = () => OwnershipPath;
        }

        internal string DirectoryPath { get; }
        internal string OwnershipPath { get; }
        internal string MarkerPath { get; }

        public void Dispose()
        {
            SteamCefLegacyMarkerCleanup.OwnershipPathProvider = static () => SteamInputAddonforClaw.Install.AddonDataPaths.CefMarkerOwnershipPath;
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}

[CollectionDefinition("SteamCefLegacyMarkerCleanup", DisableParallelization = true)]
public sealed class SteamCefLegacyMarkerCleanupCollection
{
}
