using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text.Json;

namespace SteamInputAddonforClaw.Prerequisites;

internal static class UsbIpWin2PackageMetadata
{
    public static readonly Version BundledVersion = new(0, 9, 7, 7);
    public const string InstallerFileName = "USBip-0.9.7.7-x64.exe";
    public const string InstallerSha256 = "51620FA5F9F8BE5932BC9D786DEEE557CE06D5407A99CAB490DCFAC71F185FEA";
    public static string InstallerPath => Path.Combine(AppContext.BaseDirectory, "Dependencies", "UsbIpWin2", InstallerFileName);
    public static bool VerifyInstaller() => File.Exists(InstallerPath) && string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(InstallerPath))), InstallerSha256, StringComparison.OrdinalIgnoreCase);
}

internal sealed record UsbIpWin2PackageState(bool Installed, string? Version, bool InspectionSucceeded, bool PackageEntryPresent, string? QuietUninstallString = null);
internal interface IUsbIpWin2PackageProbe { UsbIpWin2PackageState Inspect(); }
internal sealed class WindowsUsbIpWin2PackageProbe : IUsbIpWin2PackageProbe
{
    private const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{199505b0-b93d-4521-a8c7-897818e0205a}_is1";
    public UsbIpWin2PackageState Inspect()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Key);
            var version = key?.GetValue("DisplayVersion") as string;
            return new(!string.IsNullOrWhiteSpace(version), version, true, key is not null, key?.GetValue("QuietUninstallString") as string);
        }
        catch { return new(false, null, false, false); }
    }
}

internal enum UsbIpWin2ProvisioningReceiptState { InstallStarted, Provisioned, InstalledPendingReboot, AttemptFailed, AttemptCancelled }
internal sealed record UsbIpWin2ProvisioningReceipt(int SchemaVersion, UsbIpWin2ProvisioningReceiptState State, Guid AttemptId, string InstallerVersion, string InstallerSha256, PrerequisiteStatus PreProvisioningStatus, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, string? ObservedInstalledVersion, string? FailureReason = null, int? InstallerExitCode = null)
{
    public const int CurrentSchemaVersion = 1;
    public bool IsValid => SchemaVersion == CurrentSchemaVersion && AttemptId != Guid.Empty && PreProvisioningStatus == PrerequisiteStatus.Missing && Version.TryParse(InstallerVersion, out _) && InstallerSha256.Length == 64 && InstallerSha256.All(Uri.IsHexDigit);
}
internal sealed class UsbIpWin2ProvisioningReceiptStore
{
    private readonly string _path;
    private readonly Func<string, ProvisioningStorageAssessment> _storageInspector;

    public UsbIpWin2ProvisioningReceiptStore(string path, Func<string, ProvisioningStorageAssessment>? storageInspector = null)
    {
        _path = path;
        _storageInspector = storageInspector ?? ProvisioningStorageSecurity.Inspect;
    }

    public UsbIpWin2ReceiptLoadResult Load()
    {
        var directory = Path.GetDirectoryName(_path);
        if (directory is null) return new(null, true);
        var security = _storageInspector(directory);
        if (security.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate) return new(null, true);
        if (!File.Exists(_path)) return new(null, false);
        try { var receipt = JsonSerializer.Deserialize<UsbIpWin2ProvisioningReceipt>(File.ReadAllText(_path)); return receipt is { IsValid: true } ? new(receipt, false) : new(null, true); } catch { return new(null, true); }
    }
    public void Save(UsbIpWin2ProvisioningReceipt receipt)
    {
        if (!receipt.IsValid) throw new InvalidDataException();
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException();
        if (_storageInspector(directory).Status != ProvisioningStorageStatus.Trusted) throw new InvalidOperationException("Provisioning storage is unsafe.");
        var temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, receipt);
                stream.Flush(true);
            }

            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal sealed record UsbIpWin2ReceiptLoadResult(UsbIpWin2ProvisioningReceipt? Receipt, bool IsCorrupt);
