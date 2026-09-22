using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using SteamInputAddonforClaw.Diagnostics;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.WindowsGaming;

internal sealed record SteamFseRegistrationResult(bool Succeeded, string? FailureReason);

internal interface ISteamFseRegistrationClient
{
    Task<SteamFseRegistrationResult> EnsureRegisteredAsync(CancellationToken cancellationToken = default);
}

internal sealed class SteamFseRegistrationClient : ISteamFseRegistrationClient
{
    private static readonly TimeSpan RegistrationTimeout = TimeSpan.FromSeconds(60);

    public async Task<SteamFseRegistrationResult> EnsureRegisteredAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "Windows Gaming Full Screen Experience is supported only on Windows.");

        var packagePath = Path.Combine(AppContext.BaseDirectory, SteamFsePackageContract.PackageRelativePath);
        var certificatePath = Path.Combine(AppContext.BaseDirectory, SteamFsePackageContract.CertificateRelativePath);
        if (!File.Exists(packagePath) || !File.Exists(certificatePath))
            return new(false, "The fixed Gaming Home package or public certificate is missing from the installation.");

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return new(false, "The Addon executable path could not be determined for elevated registration.");

        Process? process = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process = Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = SteamFseElevatedRegistration.Argument,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            });
            if (process is null)
                return new(false, "Windows did not start the elevated Gaming Home registration.");

            // The elevated child owns temporary Developer Mode and certificate cleanup.
            // Once it has started, do not let request cancellation terminate it before its finally runs.
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(RegistrationTimeout, CancellationToken.None)
                .ConfigureAwait(false);
            return process.ExitCode == 0
                ? new(true, null)
                : new(false, $"Gaming Home registration exited with code {process.ExitCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, "The Gaming Home registration was cancelled.");
        }
        catch (TimeoutException)
        {
            AppLog.Warn("SteamFSE", "Elevated Gaming Home registration timed out; the child was left running for self-cleanup.");
            return new(false, "The Gaming Home registration timed out.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Elevated Gaming Home registration could not be started.", exception);
            return new(false, "The Gaming Home registration could not be started. UAC may have been cancelled.");
        }
        finally
        {
            process?.Dispose();
        }
    }
}

internal static class SteamFseElevatedRegistration
{
    internal const string Argument = "--register-fse-home";
    private const string DeveloperModeKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";
    private const string DeveloperModeValueName = "AllowDevelopmentWithoutDevLicense";
    private static readonly TimeSpan PackageRegistrationTimeout = TimeSpan.FromSeconds(45);

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows())
            return 1;

        try
        {
            Register();
            AppLog.Info("SteamFSE", "Fixed Gaming Home package registration completed.");
            return 0;
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Fixed Gaming Home package registration failed.", exception);
            return 1;
        }
    }

    private static void Register()
    {
        var packagePath = Path.Combine(AppContext.BaseDirectory, SteamFsePackageContract.PackageRelativePath);
        var certificatePath = Path.Combine(AppContext.BaseDirectory, SteamFsePackageContract.CertificateRelativePath);
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("The fixed Gaming Home package was not found.", packagePath);
        if (!File.Exists(certificatePath))
            throw new FileNotFoundException("The fixed Gaming Home public certificate was not found.", certificatePath);

        using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
        if (!string.Equals(certificate.Subject, SteamFsePackageContract.CertificateSubject, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The fixed Gaming Home certificate subject does not match the package publisher.");

        var developerMode = CaptureDeveloperMode();
        var certificateAdded = false;
        try
        {
            certificateAdded = EnsureTrustedPeopleCertificate(certificate);
            if (!developerMode.Enabled)
                SetDeveloperMode(true);

            RegisterPackage(packagePath);
            VerifyPackageReadback();
        }
        finally
        {
            try
            {
                RestoreDeveloperMode(developerMode);
            }
            finally
            {
                if (certificateAdded)
                    RemoveTrustedPeopleCertificate(certificate);
            }
        }
    }

    private static void RegisterPackage(string packagePath)
    {
        new PackageManager()
            .AddPackageAsync(new Uri(Path.GetFullPath(packagePath)), dependencyPackageUris: null, DeploymentOptions.None)
            .AsTask()
            .WaitAsync(PackageRegistrationTimeout)
            .GetAwaiter()
            .GetResult();
    }

    private static void VerifyPackageReadback()
    {
        var package = new WindowsSteamFsePackageEnumeration()
            .FindCurrentUserPackages()
            .Where(item => string.Equals(item.IdentityName, SteamFsePackageContract.PackageIdentityName, StringComparison.Ordinal))
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        if (package is null || package.Version < SteamFsePackageContract.FixedPackageVersion ||
            string.IsNullOrWhiteSpace(WindowsSteamFsePackageProbe.TryGetAumid(package)))
            throw new InvalidOperationException("Fixed Gaming Home package registration did not pass exact identity readback.");
    }

    private static bool EnsureTrustedPeopleCertificate(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);
        if (existing.Count != 0)
            return false;

        store.Add(certificate);
        return true;
    }

    private static void RemoveTrustedPeopleCertificate(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        store.Remove(certificate);
    }

    private static DeveloperModeSnapshot CaptureDeveloperMode()
    {
        using var key = Registry.LocalMachine.OpenSubKey(DeveloperModeKeyPath);
        if (key is null)
            return new(false, false, null, RegistryValueKind.DWord);

        var value = key.GetValue(DeveloperModeValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
            return new(false, false, null, RegistryValueKind.DWord);

        return new(true, IsEnabled(value), value, key.GetValueKind(DeveloperModeValueName));
    }

    private static void SetDeveloperMode(bool enabled)
    {
        using var key = Registry.LocalMachine.CreateSubKey(DeveloperModeKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows AppModelUnlock registry key is unavailable.");
        key.SetValue(DeveloperModeValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        if (!IsEnabled(key.GetValue(DeveloperModeValueName)))
            throw new InvalidOperationException("Windows Developer Mode could not be enabled or verified.");
    }

    private static void RestoreDeveloperMode(DeveloperModeSnapshot snapshot)
    {
        using var key = Registry.LocalMachine.CreateSubKey(DeveloperModeKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows AppModelUnlock registry key is unavailable.");
        if (!snapshot.Exists)
        {
            key.DeleteValue(DeveloperModeValueName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(DeveloperModeValueName, snapshot.Value!, snapshot.Kind);
        if (IsEnabled(key.GetValue(DeveloperModeValueName)) != snapshot.Enabled)
            throw new InvalidOperationException("Windows Developer Mode could not be restored.");
    }

    private static bool IsEnabled(object? value) => value switch
    {
        int number => number != 0,
        long number => number != 0,
        byte number => number != 0,
        string text when int.TryParse(text, out var number) => number != 0,
        _ => false,
    };

    private sealed record DeveloperModeSnapshot(bool Exists, bool Enabled, object? Value, RegistryValueKind Kind);
}
