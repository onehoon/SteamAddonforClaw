using SteamInputAddonforClaw.Prerequisites;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UsbIpWin2ProvisioningReceiptStoreTests
{
    [Fact]
    public void Save_FirstReceipt_CanBeLoadedWithoutTemporaryFile()
    {
        using var fixture = new ReceiptStoreFixture();
        var expected = CreateReceipt();

        fixture.Store.Save(expected);

        Assert.Equal(expected, fixture.Store.Load().Receipt);
        Assert.True(File.Exists(fixture.Path));
        Assert.Empty(fixture.TemporaryFiles);
    }

    [Fact]
    public void Save_ExistingReceipt_ReplacesItWithoutSharingViolation()
    {
        using var fixture = new ReceiptStoreFixture();
        var first = CreateReceipt() with { AttemptId = Guid.NewGuid() };
        var second = CreateReceipt() with { AttemptId = Guid.NewGuid(), State = UsbIpWin2ProvisioningReceiptState.Provisioned };

        fixture.Store.Save(first);
        fixture.Store.Save(second);

        Assert.Equal(second, fixture.Store.Load().Receipt);
        Assert.Empty(fixture.TemporaryFiles);
    }

    [Fact]
    public void Save_RepeatedReceipts_PreservesLastState()
    {
        using var fixture = new ReceiptStoreFixture();
        var receipts = Enumerable.Range(0, 3).Select(_ => CreateReceipt() with { AttemptId = Guid.NewGuid() }).ToArray();

        foreach (var receipt in receipts) fixture.Store.Save(receipt);

        Assert.Equal(receipts[^1], fixture.Store.Load().Receipt);
        Assert.Empty(fixture.TemporaryFiles);
    }

    [Fact]
    public void Save_InvalidReceipt_DoesNotCorruptExistingReceipt()
    {
        using var fixture = new ReceiptStoreFixture();
        var expected = CreateReceipt();
        fixture.Store.Save(expected);

        Assert.Throws<InvalidDataException>(() => fixture.Store.Save(expected with { InstallerSha256 = "invalid" }));

        Assert.Equal(expected, fixture.Store.Load().Receipt);
        Assert.Empty(fixture.TemporaryFiles);
    }

    [Fact]
    public void Load_LegacyV1FirstInstallReceiptWithoutUpgradeFields_RemainsValid()
    {
        using var fixture = new ReceiptStoreFixture();
        var expected = CreateReceipt();
        var json = JsonNode.Parse(JsonSerializer.Serialize(expected))!.AsObject();
        json.Remove(nameof(UsbIpWin2ProvisioningReceipt.PreInstallationStatus));
        json.Remove(nameof(UsbIpWin2ProvisioningReceipt.PreviousInstalledVersion));
        File.WriteAllText(fixture.Path, json.ToJsonString());

        Assert.Equal(expected, fixture.Store.Load().Receipt);
        Assert.False(fixture.Store.Load().IsCorrupt);
    }

    [Fact]
    public void Save_UpgradeReceiptWithOlderOrigin_LoadsAsValid()
    {
        using var fixture = new ReceiptStoreFixture();
        var expected = CreateReceipt() with
        {
            PreProvisioningStatus = PrerequisiteStatus.Incompatible,
            PreInstallationStatus = ComponentInstallationStatus.UpdateRequired,
            PreviousInstalledVersion = "0.9.7.6"
        };

        fixture.Store.Save(expected);

        Assert.Equal(expected, fixture.Store.Load().Receipt);
    }

    [Theory]
    [InlineData("0.9.8.0")]
    [InlineData("0.9.8.1")]
    [InlineData("unknown")]
    public void Load_InvalidUpgradeOrigin_IsCorrupt(string previousInstalledVersion)
    {
        using var fixture = new ReceiptStoreFixture();
        var receipt = CreateReceipt() with
        {
            PreProvisioningStatus = PrerequisiteStatus.Incompatible,
            PreInstallationStatus = ComponentInstallationStatus.UpdateRequired,
            PreviousInstalledVersion = previousInstalledVersion
        };
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(receipt));

        var loaded = fixture.Store.Load();

        Assert.Null(loaded.Receipt);
        Assert.True(loaded.IsCorrupt);
    }

    [Theory]
    [InlineData((int)ComponentInstallationStatus.Installed)]
    [InlineData((int)ComponentInstallationStatus.ExistingUnverified)]
    [InlineData((int)ComponentInstallationStatus.Incompatible)]
    [InlineData((int)ComponentInstallationStatus.Indeterminate)]
    public void Load_NonInstallableReceiptOrigin_IsCorrupt(int statusValue)
    {
        using var fixture = new ReceiptStoreFixture();
        var receipt = CreateReceipt() with { PreInstallationStatus = (ComponentInstallationStatus)statusValue };
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(receipt));

        var loaded = fixture.Store.Load();

        Assert.Null(loaded.Receipt);
        Assert.True(loaded.IsCorrupt);
    }

    private static UsbIpWin2ProvisioningReceipt CreateReceipt() => new(
        1,
        UsbIpWin2ProvisioningReceiptState.InstallStarted,
        Guid.NewGuid(),
        UsbIpWin2PackageMetadata.BundledVersion.ToString(),
        UsbIpWin2PackageMetadata.InstallerSha256,
        PrerequisiteStatus.Missing,
        DateTimeOffset.UtcNow,
        null,
        null);

    private sealed class ReceiptStoreFixture : IDisposable
    {
        private readonly string _directory = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "SteamInputAddonforClaw-tests", Guid.NewGuid().ToString("N"));
        private readonly string _fileName = "usbip-win2-test-" + Guid.NewGuid().ToString("N") + ".json";

        public ReceiptStoreFixture()
        {
            Directory.CreateDirectory(_directory);
            Path = global::System.IO.Path.Combine(_directory, _fileName);
            Store = new UsbIpWin2ProvisioningReceiptStore(Path, static _ => new(ProvisioningStorageStatus.Trusted, "Test"));
        }

        public string Path { get; }
        public UsbIpWin2ProvisioningReceiptStore Store { get; }
        public IEnumerable<string> TemporaryFiles => Directory.EnumerateFiles(_directory, _fileName + ".tmp-*");

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
