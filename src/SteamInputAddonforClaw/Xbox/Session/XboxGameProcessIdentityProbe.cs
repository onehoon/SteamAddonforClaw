using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.GameDetection.Windows;
using SteamInputAddonforClaw.Xbox;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.Xbox.Session;

internal interface IXboxGameProcessIdentityProbe
{
    GameProcessOpenResult Open(uint processId);

    Task<XboxGameProcessInspection> InspectAsync(IGameProcessGeneration generation, CancellationToken cancellationToken);
}

internal sealed class WindowsXboxGameProcessIdentityProbe : IXboxGameProcessIdentityProbe
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorInsufficientBuffer = 122;
    private const int MaximumNativeStringLength = 32 * 1024;

    private readonly IWindowsGameProcessSource _processSource;

    internal WindowsXboxGameProcessIdentityProbe(IWindowsGameProcessSource? processSource = null) =>
        _processSource = processSource ?? new WindowsGameProcessSource();

    public GameProcessOpenResult Open(uint processId) => _processSource.Open(processId);

    public async Task<XboxGameProcessInspection> InspectAsync(
        IGameProcessGeneration generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (generation is not WindowsGameProcessGeneration process)
            throw new ArgumentException("The process generation was not created by the Windows process source.", nameof(generation));

        var imageQuery = generation.QueryImagePath();
        var packageFullNameQuery = QueryProcessString(process.Handle, GetPackageFullName);
        var packageFamilyNameQuery = QueryProcessString(process.Handle, GetPackageFamilyName);
        var configMetadata = packageFullNameQuery.ResultCode == ErrorSuccess && !string.IsNullOrWhiteSpace(packageFullNameQuery.Value)
            ? XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
                packageFullNameQuery.Value,
                static (userSecurityId, packageFullName) =>
                {
                    var package = new PackageManager().FindPackageForUser(userSecurityId, packageFullName);
                    return package is null
                        ? null
                        : new XboxGamePackageConfigMetadata(
                            () => package.EffectiveLocation?.Path,
                            () => package.InstalledLocation?.Path)
                        {
                            DisplayName = () => package.DisplayName,
                            Name = () => package.Id.Name,
                        };
                })
            : new XboxGamePackageConfigLocationResolution([], null);

        return await XboxGameProcessIdentityEvaluator.InspectAsync(
            generation,
            new XboxGameProcessIdentityEvidence(
                imageQuery.ErrorCode,
                imageQuery.ImagePath,
                packageFullNameQuery.ResultCode,
                packageFullNameQuery.Value,
                packageFamilyNameQuery.ResultCode,
                packageFamilyNameQuery.Value,
                configMetadata.PackageDisplayName,
                configMetadata.PackageName,
                configMetadata.Locations,
                configMetadata.FailureReason),
            cancellationToken).ConfigureAwait(false);
    }

    private static (string? Value, int ResultCode) QueryProcessString(SafeProcessHandle process, ProcessStringQuery query)
    {
        uint length = 0;
        var result = query(process, ref length, null);
        if (result != ErrorInsufficientBuffer || length is 0 or > MaximumNativeStringLength)
            return (null, result);

        var value = new StringBuilder(checked((int)length));
        result = query(process, ref length, value);
        return result == ErrorSuccess ? (value.ToString().TrimEnd('\0'), result) : (null, result);
    }

    private delegate int ProcessStringQuery(SafeProcessHandle process, ref uint length, StringBuilder? value);

    [DllImport("kernel32.dll", EntryPoint = "GetPackageFullName", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(SafeProcessHandle process, ref uint length, StringBuilder? value);

    [DllImport("kernel32.dll", EntryPoint = "GetPackageFamilyName", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder? value);
}
