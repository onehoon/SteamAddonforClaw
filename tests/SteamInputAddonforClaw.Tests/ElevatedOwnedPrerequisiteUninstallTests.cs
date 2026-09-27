using Microsoft.Win32;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ElevatedOwnedPrerequisiteUninstallTests
{
    [Fact]
    public void Execute_RemovesOwnedUsbIpBeforeOwnedHidHideAndDeletesProgramDataState()
    {
        using var fixture = new Fixture();
        fixture.WriteUsbReceipt(owned: true);
        fixture.HidReceipt.Store(new(HidReceipt(), false));
        fixture.UsbStates.Enqueue(UsbPackage(installed: true));
        fixture.UsbStates.Enqueue(UsbPackage(installed: false));
        fixture.HidStates.Enqueue(new(true, "1.5.230.0", true));
        fixture.HidStates.Enqueue(new(false, null, true));
        fixture.Candidates.Add(HidCandidate());

        var result = fixture.Execute();

        Assert.True(result.Succeeded, result.Reason);
        Assert.True(result.RestartRequired);
        Assert.Equal(["usbip", "HidHide"], fixture.Runner.Packages);
        Assert.False(Directory.Exists(fixture.ProvisioningDirectory));
        Assert.False(Directory.Exists(fixture.ProgramDataParent));
    }

    [Fact]
    public void Execute_PreservesPreExistingAndUnknownPackages()
    {
        using var fixture = new Fixture();
        fixture.WriteUsbReceipt(owned: false);
        fixture.HidReceipt.Store(new(null, false));
        fixture.UsbStates.Enqueue(UsbPackage(installed: true));
        fixture.HidStates.Enqueue(new(true, "1.5.230.0", true));
        fixture.Candidates.Add(HidCandidate());

        var result = fixture.Execute();

        Assert.True(result.Succeeded, result.Reason);
        Assert.False(result.RestartRequired);
        Assert.Empty(fixture.Runner.Packages);
    }

    [Fact]
    public void Execute_PreservesHidHideWhenReceiptIsCorruptOrIdentityIsAmbiguous()
    {
        using var fixture = new Fixture();
        fixture.HidReceipt.Store(new(null, true));
        fixture.UsbStates.Enqueue(UsbPackage(installed: false));
        fixture.HidStates.Enqueue(new(true, "1.5.230.0", true));
        fixture.Candidates.Add(HidCandidate());
        fixture.Candidates.Add(HidCandidate() with { SubKey = "duplicate" });

        var result = fixture.Execute();

        Assert.True(result.Succeeded, result.Reason);
        Assert.Empty(fixture.Runner.Packages);
        Assert.False(result.RestartRequired);
    }

    [Fact]
    public void Execute_FailedOwnedUninstallerBlocksProgramDataCleanupAndRetainsReceipt()
    {
        using var fixture = new Fixture();
        fixture.WriteUsbReceipt(owned: true);
        fixture.UsbStates.Enqueue(UsbPackage(installed: true));
        fixture.Runner.ExitCode = 1603;

        var result = fixture.Execute();

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(fixture.UsbReceiptPath));
        Assert.True(Directory.Exists(fixture.ProvisioningDirectory));
    }

    [Fact]
    public void Execute_UsesRegisteredQuietUninstallCommandForOwnedUsbIpOnly()
    {
        using var fixture = new Fixture();
        fixture.WriteUsbReceipt(owned: true);
        fixture.UsbStates.Enqueue(UsbPackage(installed: true));
        fixture.UsbStates.Enqueue(UsbPackage(installed: false));

        var result = fixture.Execute();

        Assert.True(result.Succeeded, result.Reason);
        Assert.Single(fixture.Runner.Commands);
        Assert.Equal("C:\\Program Files\\usbip\\unins.exe", fixture.Runner.Commands[0].FileName);
        Assert.Equal("/VERYSILENT", fixture.Runner.Commands[0].Arguments);
        Assert.True(result.RestartRequired);
    }

    [Fact]
    public void Execute_UsesExactHidHideRegisteredCommandWhenOnlyHidHideIsOwned()
    {
        using var fixture = new Fixture();
        fixture.HidReceipt.Store(new(HidReceipt(), false));
        fixture.UsbStates.Enqueue(UsbPackage(installed: false));
        fixture.HidStates.Enqueue(new(true, "1.5.230.0", true));
        fixture.HidStates.Enqueue(new(false, null, true));
        fixture.Candidates.Add(HidCandidate());

        var result = fixture.Execute();

        Assert.True(result.Succeeded, result.Reason);
        Assert.Single(fixture.Runner.Commands);
        Assert.Equal("C:\\Program Files\\HidHide\\unins.exe", fixture.Runner.Commands[0].FileName);
        Assert.Equal("/VERYSILENT", fixture.Runner.Commands[0].Arguments);
        Assert.True(result.RestartRequired);
    }

    private static UsbIpWin2PackageState UsbPackage(bool installed) => installed
        ? new(true, "0.9.8.1", true, true,
            "\"C:\\Program Files\\usbip\\unins.exe\" /UNINSTALL",
            "\"C:\\Program Files\\usbip\\unins.exe\" /VERYSILENT")
        : new(false, null, true, false);

    private static HidHideUninstallCandidate HidCandidate() => new(
        "HidHide", "1.5.230.0", "Nefarius Software Solutions e.U.",
        SubKey: "{6E7F8B00-1D9A-4DBA-9F1A-52E8E702F401}",
        WindowsInstaller: false,
        UninstallString: "\"C:\\Program Files\\HidHide\\unins.exe\" /UNINSTALL",
        QuietUninstallString: "\"C:\\Program Files\\HidHide\\unins.exe\" /VERYSILENT");

    private static HidHideProvisioningReceipt HidReceipt() => new(
        HidHideProvisioningReceipt.CurrentSchemaVersion,
        HidHideProvisioningReceiptState.Provisioned,
        Guid.NewGuid(),
        "1.5.230.0",
        HidHidePackageMetadata.InstallerSha256,
        PrerequisiteStatus.Missing,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "1.5.230.0");

    private sealed class Fixture : IDisposable
    {
        internal string ProgramDataParent { get; } = Path.Combine(Path.GetTempPath(), "OwnedPrerequisiteUninstall", Guid.NewGuid().ToString("N"));
        internal string ProvisioningDirectory => Path.Combine(ProgramDataParent, "provisioning");
        internal string UsbReceiptPath => Path.Combine(ProvisioningDirectory, "usbip-win2.json");
        internal Queue<UsbIpWin2PackageState> UsbStates { get; } = new();
        internal Queue<HidHidePackageState> HidStates { get; } = new();
        internal List<HidHideUninstallCandidate> Candidates { get; } = [];
        internal FakeHidReceipt HidReceipt { get; } = new();
        internal FakeRunner Runner { get; } = new();
        private readonly UsbIpWin2ProvisioningReceiptStore _usbStore;

        internal Fixture()
        {
            Directory.CreateDirectory(ProvisioningDirectory);
            _usbStore = new(UsbReceiptPath, static _ => new(ProvisioningStorageStatus.Trusted, "Test"));
            File.WriteAllText(Path.Combine(ProvisioningDirectory, "hidhide.json"), "receipt-state");
        }

        internal void WriteUsbReceipt(bool owned)
        {
            _usbStore.Save(new(
                UsbIpWin2ProvisioningReceipt.CurrentSchemaVersion,
                UsbIpWin2ProvisioningReceiptState.Provisioned,
                Guid.NewGuid(), "0.9.8.1", UsbIpWin2PackageMetadata.InstallerSha256,
                PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "0.9.8.1",
                InstalledByAddon: owned));
        }

        internal OwnedPrerequisiteUninstallResult Execute() => new ElevatedOwnedPrerequisiteUninstall(
            _usbStore,
            HidReceipt,
            () => UsbStates.Dequeue(),
            () => HidStates.Count > 0 ? HidStates.Dequeue() : new(false, null, true),
            view => view == RegistryView.Registry64 ? Candidates : Array.Empty<HidHideUninstallCandidate>(),
            Runner,
            static _ => true,
            ProvisioningDirectory).Execute();

        public void Dispose()
        {
            if (Directory.Exists(ProgramDataParent)) Directory.Delete(ProgramDataParent, recursive: true);
        }
    }

    private sealed class FakeHidReceipt : IHidHideProvisioningReceiptStore
    {
        private HidHideReceiptLoadResult _load = new(null, false);
        internal void Store(HidHideReceiptLoadResult load) => _load = load;
        public HidHideReceiptLoadResult Load() => _load;
        public void Save(HidHideProvisioningReceipt receipt) => _load = new(receipt, false);
    }

    private sealed class FakeRunner : IUninstallProcessRunner
    {
        internal int ExitCode { get; set; }
        internal List<string> Packages { get; } = [];
        internal List<RegisteredUninstallCommand> Commands { get; } = [];
        public bool TryRun(string fileName, string arguments, out int exitCode)
        {
            Commands.Add(new(fileName, arguments));
            Packages.Add(fileName.Contains("usbip", StringComparison.OrdinalIgnoreCase) ? "usbip" : "HidHide");
            exitCode = ExitCode;
            return true;
        }
    }
}
