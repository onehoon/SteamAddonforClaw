using System.Text;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ProfileGameCatalogScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Catalog.", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScanAsync_MergesLibrariesAndAllAccounts_DeduplicatesAndOrders()
    {
        var second = Path.Combine(_root, "Library2");
        WriteManifest(_root, 10, "Zed"); WriteManifest(second, 20, "Alpha");
        WriteShortcuts("111", (30, "Beta"), (20, "not used"));
        WriteShortcuts("222", (30, "duplicate"), (40, "Gamma"));
        var entries = await new ProfileGameCatalogScanner(() => _root).ScanAsync();

        Assert.Equal(new uint[] { 20, 30, 40, 10 }, entries.Select(x => x.AppId));
        Assert.Equal(ProfileGameSource.Steam, entries.Single(x => x.AppId == 20).Source);
        Assert.Equal(4, entries.Count);
    }

    [Fact]
    public async Task ScanAsync_IgnoresMissingOrMalformedOptionalInputs_AndIsFresh()
    {
        WriteManifest(_root, 1, "First");
        var scanner = new ProfileGameCatalogScanner(() => _root);
        var first = await scanner.ScanAsync();
        File.Delete(Path.Combine(_root, "steamapps", "appmanifest_1.acf"));
        var second = await scanner.ScanAsync();
        Assert.Single(first); Assert.Empty(second);
    }

    [Fact]
    public async Task ScanAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var task = new ProfileGameCatalogScanner(() => _root).ScanAsync(cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public async Task ScanAsync_KeepsValidGamesWhenOneConfiguredLibraryIsUnavailable()
    {
        var valid = Path.Combine(_root, "ValidLibrary");
        WriteManifest(valid, 50, "Valid");
        var missing = Path.Combine(_root, "MissingLibrary");
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{valid.Replace("\\", "\\\\")}\" }} \"2\" {{ \"path\" \"{missing.Replace("\\", "\\\\")}\" }} }}");

        var entries = await new ProfileGameCatalogScanner(() => _root).ScanAsync();

        Assert.Contains(entries, entry => entry.AppId == 50);
    }

    private void WriteManifest(string library, uint id, string name)
    {
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));
        File.WriteAllText(Path.Combine(library, "steamapps", $"appmanifest_{id}.acf"), $"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"{name}\" }}");
        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        if (!string.Equals(library, _root, StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"), $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{library.Replace("\\", "\\\\")}\" }} }}");
    }

    private void WriteShortcuts(string account, params (uint Id, string Name)[] entries)
    {
        var path = Path.Combine(_root, "userdata", account, "config"); Directory.CreateDirectory(path);
        using var stream = File.Create(Path.Combine(path, "shortcuts.vdf")); using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write((byte)0); WriteString(writer, "shortcuts");
        var index = 0;
        foreach (var (id, name) in entries) { writer.Write((byte)0); WriteString(writer, index++.ToString()); writer.Write((byte)2); WriteString(writer, "appid"); writer.Write(unchecked((int)id)); writer.Write((byte)1); WriteString(writer, "AppName"); WriteString(writer, name); writer.Write((byte)8); }
        writer.Write((byte)8);
    }
    private static void WriteString(BinaryWriter writer, string value) { writer.Write(Encoding.UTF8.GetBytes(value)); writer.Write((byte)0); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
