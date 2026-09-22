using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsAppRuntimePrerequisiteTests
{
    private const string Family = WindowsAppRuntimeMetadata.FrameworkPackageFamilyName;

    [Fact]
    public void Package_probe_classifies_old_pinned_and_newer_x64_versions()
    {
        Assert.Equal(WindowsAppRuntimeAvailability.UpdateRequired, Probe(Package("2.3.1.0", "X64")).Inspect());
        Assert.Equal(WindowsAppRuntimeAvailability.UpdateRequired, Probe(Package("2.4.0.0", "X64")).Inspect());
        Assert.Equal(WindowsAppRuntimeAvailability.UpdateRequired, Probe(Package("2.5.0.0", "X64")).Inspect());
        Assert.Equal(WindowsAppRuntimeAvailability.Ready, Probe(Package("2.5.1.0", "X64")).Inspect());
        Assert.Equal(WindowsAppRuntimeAvailability.Ready, Probe(Package("2.6.0.0", "X64")).Inspect());
    }

    [Fact]
    public void Package_probe_rejects_missing_or_non_x64_runtime()
    {
        Assert.Equal(WindowsAppRuntimeAvailability.Missing, Probe().Inspect());
        Assert.Equal(WindowsAppRuntimeAvailability.Missing, Probe(Package("2.5.1.0", "X86")).Inspect());
        Assert.Equal(WindowsAppRuntimeAvailability.Missing, Probe(new WindowsAppRuntimePackageInfo("OtherFamily", "2.5.1.0", "X64")).Inspect());
    }

    [Fact]
    public void Package_probe_fails_closed_for_malformed_evidence_or_enumeration_failure()
    {
        Assert.Equal(WindowsAppRuntimeAvailability.Indeterminate, Probe(Package("not-a-version", "X64")).Inspect());
        Assert.Equal(WindowsAppRuntimeAvailability.Indeterminate,
            new WindowsAppRuntimePackageProbe(new ThrowingEnumeration()).Inspect());
    }

    [Fact]
    public async Task Already_ready_runtime_does_not_start_elevated_setup()
    {
        var runner = new FakeRunner();
        var prerequisite = new WindowsAppRuntimePrerequisite(
            new FakeProbe(WindowsAppRuntimeAvailability.Ready), runner, () => Environment.ProcessPath);

        Assert.True(await prerequisite.EnsureAvailableAsync());
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Missing_runtime_runs_one_helper_and_requires_parent_readback()
    {
        var probe = new FakeProbe(WindowsAppRuntimeAvailability.Missing);
        var runner = new FakeRunner
        {
            OnRun = () => probe.State = WindowsAppRuntimeAvailability.Ready,
        };
        var prerequisite = new WindowsAppRuntimePrerequisite(probe, runner, () => Environment.ProcessPath);

        Assert.True(await prerequisite.EnsureAvailableAsync());
        Assert.Equal(1, runner.Calls);
        Assert.Equal(ElevatedWindowsAppRuntimeSetup.Argument, runner.Arguments);
    }

    [Fact]
    public async Task Helper_failure_still_gets_parent_readback_and_cannot_claim_success()
    {
        var probe = new FakeProbe(WindowsAppRuntimeAvailability.Missing);
        var runner = new FakeRunner
        {
            Result = new ElevatedProcessResult(ElevatedProcessResultKind.Completed, 1),
            OnRun = () => probe.State = WindowsAppRuntimeAvailability.Ready,
        };
        var prerequisite = new WindowsAppRuntimePrerequisite(probe, runner, () => Environment.ProcessPath);

        Assert.False(await prerequisite.EnsureAvailableAsync());
        Assert.Equal(1, runner.Calls);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task Concurrent_surface_requests_share_one_setup_attempt()
    {
        var probe = new FakeProbe(WindowsAppRuntimeAvailability.Missing);
        var runner = new FakeRunner
        {
            OnRun = () => probe.State = WindowsAppRuntimeAvailability.Ready,
        };
        var prerequisite = new WindowsAppRuntimePrerequisite(probe, runner, () => Environment.ProcessPath);

        var results = await Task.WhenAll(prerequisite.EnsureAvailableAsync(), prerequisite.EnsureAvailableAsync());

        Assert.All(results, Assert.True);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public void Elevated_setup_skips_acquisition_when_runtime_is_ready()
    {
        var acquired = false;
        var launched = false;
        var result = ElevatedWindowsAppRuntimeSetup.Execute(
            new FakeProbe(WindowsAppRuntimeAvailability.Ready),
            _ => TrustedStorage(),
            (_, _, _) =>
            {
                acquired = true;
                return Task.FromResult(InstallerAcquisitionResult.Failure("unexpected"));
            },
            (_, _) =>
            {
                launched = true;
                return 0;
            },
            "C:\\ProgramData\\SteamInputAddonforClaw\\provisioning",
            (_, _) => true);

        Assert.Equal(0, result);
        Assert.False(acquired);
        Assert.False(launched);
    }

    [Fact]
    public void Elevated_setup_uses_pinned_descriptor_and_parent_readback()
    {
        var probe = new SequenceProbe(WindowsAppRuntimeAvailability.Missing, WindowsAppRuntimeAvailability.Ready);
        var root = Path.Combine(Path.GetTempPath(), "WindowsAppRuntimeSetupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? acquiredPath = null;
        string? component = null;
        string? installerArguments = null;
        try
        {
            var result = ElevatedWindowsAppRuntimeSetup.Execute(
                probe,
                _ => TrustedStorage(),
                (descriptor, directory, _) =>
                {
                    component = descriptor.Component;
                    Assert.Equal(WindowsAppRuntimeMetadata.InstallerDownloadUri, descriptor.DownloadUri);
                    Assert.Equal(WindowsAppRuntimeMetadata.InstallerSha256, descriptor.InstallerSha256);
                    acquiredPath = Path.Combine(directory, descriptor.InstallerFileName);
                    File.WriteAllText(acquiredPath, "verified by fake acquisition");
                    return Task.FromResult(new InstallerAcquisitionResult(true, acquiredPath, "Acquired", 1));
                },
                (path, arguments) =>
                {
                    Assert.Equal(acquiredPath, path);
                    installerArguments = arguments;
                    return 0;
                },
                root,
                (_, _) => true);

            Assert.Equal(0, result);
            Assert.Equal("WindowsAppRuntime", component);
            Assert.Equal("--quiet", installerArguments);
            Assert.False(File.Exists(acquiredPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Elevated_setup_does_not_launch_after_hash_validation_failure()
    {
        var launched = false;
        var result = ElevatedWindowsAppRuntimeSetup.Execute(
            new SequenceProbe(WindowsAppRuntimeAvailability.Missing),
            _ => TrustedStorage(),
            (_, directory, _) =>
            {
                var path = Path.Combine(directory, WindowsAppRuntimeMetadata.InstallerFileName);
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, "tampered");
                return Task.FromResult(new InstallerAcquisitionResult(true, path, "Acquired", 1));
            },
            (_, _) =>
            {
                launched = true;
                return 0;
            },
            Path.Combine(Path.GetTempPath(), "WindowsAppRuntimeSetupTests", Guid.NewGuid().ToString("N")),
            (_, _) => false);

        Assert.Equal(1, result);
        Assert.False(launched);
    }

    [Fact]
    public void Elevated_setup_does_not_claim_success_when_post_install_probe_is_not_ready()
    {
        var result = ElevatedWindowsAppRuntimeSetup.Execute(
            new SequenceProbe(WindowsAppRuntimeAvailability.Missing, WindowsAppRuntimeAvailability.UpdateRequired),
            _ => TrustedStorage(),
            (_, directory, _) =>
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, WindowsAppRuntimeMetadata.InstallerFileName);
                File.WriteAllText(path, "verified by fake acquisition");
                return Task.FromResult(new InstallerAcquisitionResult(true, path, "Acquired", 1));
            },
            (_, arguments) =>
            {
                Assert.Equal("--quiet", arguments);
                return 0;
            },
            Path.Combine(Path.GetTempPath(), "WindowsAppRuntimeSetupTests", Guid.NewGuid().ToString("N")),
            (_, _) => true);

        Assert.Equal(1, result);
    }

    private static WindowsAppRuntimePackageProbe Probe(params WindowsAppRuntimePackageInfo[] packages) =>
        new(new FakeEnumeration(packages));

    private static WindowsAppRuntimePackageInfo Package(string version, string architecture) =>
        new(Family, version, architecture);

    private static ProvisioningStorageAssessment TrustedStorage() =>
        new(ProvisioningStorageStatus.Trusted, "test");

    private sealed class FakeEnumeration(IReadOnlyList<WindowsAppRuntimePackageInfo> packages) : IWindowsAppRuntimePackageEnumeration
    {
        public IReadOnlyList<WindowsAppRuntimePackageInfo> FindCurrentUserPackages() => packages;
    }

    private sealed class ThrowingEnumeration : IWindowsAppRuntimePackageEnumeration
    {
        public IReadOnlyList<WindowsAppRuntimePackageInfo> FindCurrentUserPackages() => throw new InvalidOperationException("test");
    }

    private sealed class FakeProbe(WindowsAppRuntimeAvailability state) : IWindowsAppRuntimePackageProbe
    {
        public WindowsAppRuntimeAvailability State { get; set; } = state;
        public int Calls { get; private set; }
        public WindowsAppRuntimeAvailability Inspect()
        {
            Calls++;
            return State;
        }
    }

    private sealed class SequenceProbe(params WindowsAppRuntimeAvailability[] states) : IWindowsAppRuntimePackageProbe
    {
        private int _index;
        public WindowsAppRuntimeAvailability Inspect() => states[Math.Min(Interlocked.Increment(ref _index) - 1, states.Length - 1)];
    }

    private sealed class FakeRunner : IElevatedProcessRunner
    {
        public int Calls { get; private set; }
        public string? Arguments { get; private set; }
        public Action? OnRun { get; init; }
        public ElevatedProcessResult Result { get; init; } = new(ElevatedProcessResultKind.Completed, 0);

        public Task<ElevatedProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            Calls++;
            Arguments = arguments;
            OnRun?.Invoke();
            return Task.FromResult(Result);
        }
    }
}
