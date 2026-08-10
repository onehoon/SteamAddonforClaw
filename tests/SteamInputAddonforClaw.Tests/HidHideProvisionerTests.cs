using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using System.Security.Cryptography;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HidHideProvisionerTests
{
    [Fact]
    public void BundledInstaller_HashMatchesRuntimeMetadata()
    {
        var installer = Path.Combine(AppContext.BaseDirectory, "Dependencies", "HidHide", HidHidePackageMetadata.InstallerFileName);
        Assert.True(File.Exists(installer));
        Assert.Equal(HidHidePackageMetadata.InstallerSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))));
    }

    [Fact]
    public void ProvisioningReceiptPath_IsMachineWideAndOutsideVelopackRoot()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Assert.StartsWith(programData, VelopackAppPaths.HidHideProvisioningReceiptPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SteamInputAddonforClaw\\hidhide-provisioning.json", VelopackAppPaths.HidHideProvisioningReceiptPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SteamInputAddonforClaw\\provisioning", VelopackAppPaths.HidHideProvisioningReceiptPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedEnvironment_BlocksBeforeProcessLaunch()
    {
        var runner = new FakeRunner();
        var result = await Create(runner, context: Context(ControllerEnvironmentCompatibilityStatus.Unsupported)).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Blocked, result.Kind);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task ExternalControllerOrSteamSession_BlocksBeforeProcessLaunch()
    {
        var runner = new FakeRunner();
        var external = Context() with { ExternalController = new(ExternalControllerAssessmentStatus.ExternalPresent, 1, []) };
        var steam = Context() with { Steam = SteamSessionState.FromRunningAppId(10) };
        Assert.Equal(HidHideProvisioningResultKind.Blocked, (await Create(runner, context: external).ProvisionAsync(CancellationToken.None)).Kind);
        Assert.Equal(HidHideProvisioningResultKind.Blocked, (await Create(runner, context: steam).ProvisionAsync(CancellationToken.None)).Kind);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task ExistingReadyHidHide_IsNeverProvisionedOrReinstalled()
    {
        var runner = new FakeRunner();
        var store = new FakeStore();
        var context = Context() with { HidHide = new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "test") };
        var result = await Create(runner, store, context: context).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.AlreadyReady, result.Kind);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task ExistingPackageWithMissingControlDevice_IsNotReinstalled()
    {
        var runner = new FakeRunner();
        var result = await Create(runner, package: new(true, "1.5.230.0", true)).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Blocked, result.Kind);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task ReceiptSaveFailure_BlocksBeforeProcessLaunch()
    {
        var runner = new FakeRunner();
        var store = new FakeStore { ThrowOnSave = true };
        var result = await Create(runner, store).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Failed, result.Kind);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task UacCancellation_UpdatesReceiptWithoutExitCodePath()
    {
        var store = new FakeStore();
        var runner = new FakeRunner
        {
            Result = new(ElevatedProcessResultKind.CancelledBeforeStart),
            OnRun = () => Assert.Equal(HidHideProvisioningReceiptState.InstallStarted, store.Receipt?.State)
        };
        var result = await Create(runner, store).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Cancelled, result.Kind);
        Assert.Equal(HidHideProvisioningReceiptState.AttemptCancelled, store.Receipt?.State);
    }

    [Fact]
    public async Task SuccessfulInstaller_IsProvisionedOnlyAfterActualValidation()
    {
        var store = new FakeStore();
        var runner = new FakeRunner { Result = new(ElevatedProcessResultKind.Completed, 0) };
        var result = await Create(runner, store, HidHideInspectionStatus.Available, packageProbe: new SequencedPackageProbe(new(false, null, true), new(true, "1.5.230.0", true))).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Installed, result.Kind);
        Assert.Equal(HidHideProvisioningReceiptState.Provisioned, store.Receipt?.State);
        Assert.Equal("1.5.230.0", store.Receipt?.ObservedInstalledVersion);
    }

    [Fact]
    public async Task CorruptReceipt_BlocksWithoutOverwriteOrProcessLaunch()
    {
        var store = new FakeStore { Corrupt = true };
        var runner = new FakeRunner();
        var result = await Create(runner, store).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Blocked, result.Kind);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task ExistingProvisionedReceiptWithMissingDriver_DoesNotReinstall()
    {
        var store = new FakeStore { Receipt = Receipt(HidHideProvisioningReceiptState.Provisioned) };
        var runner = new FakeRunner();
        var result = await Create(runner, store).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Blocked, result.Kind);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task ConcurrentCalls_LaunchOnlyOneInstaller()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeRunner { OnRunAsync = async () => await gate.Task };
        var provisioner = Create(runner);
        var first = provisioner.ProvisionAsync(CancellationToken.None);
        await runner.Started.Task;
        var second = await provisioner.ProvisionAsync(CancellationToken.None);
        gate.SetResult();
        await first;
        Assert.Equal(HidHideProvisioningResultKind.AlreadyInProgress, second.Kind);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public void CancelledBeforeStartReceipt_NeverPromotesToProvisioned()
    {
        var receipt = Receipt(HidHideProvisioningReceiptState.AttemptCancelled);
        var store = new FakeStore { Receipt = receipt };
        var provisioner = Create(new FakeRunner(), store, HidHideInspectionStatus.Available, new(true, "1.5.230.0", true));
        provisioner.Reconcile();
        Assert.Equal(HidHideProvisioningReceiptState.AttemptCancelled, store.Receipt?.State);
    }

    [Fact]
    public void InstallStartedAndStillMissing_RemainsPendingForLaterReconciliation()
    {
        var receipt = Receipt(HidHideProvisioningReceiptState.InstallStarted);
        var store = new FakeStore { Receipt = receipt };
        Create(new FakeRunner(), store).Reconcile();
        Assert.Equal(HidHideProvisioningReceiptState.InstallStarted, store.Receipt?.State);
    }

    [Fact]
    public void InstallStartedWithExpectedPackageButControlDeviceNotReady_BecomesPendingReboot()
    {
        var receipt = Receipt(HidHideProvisioningReceiptState.InstallStarted);
        var store = new FakeStore { Receipt = receipt };
        Create(new FakeRunner(), store, package: new(true, "1.5.230.0", true)).Reconcile();
        Assert.Equal(HidHideProvisioningReceiptState.InstalledPendingReboot, store.Receipt?.State);
        Assert.Equal("1.5.230.0", store.Receipt?.ObservedInstalledVersion);
    }

    [Fact]
    public void HistoricalReceiptVersion_IsValidAndReconcilesAgainstItsOwnVersion()
    {
        var receipt = Receipt(HidHideProvisioningReceiptState.InstallStarted) with { InstallerVersion = "1.4.999.0" };
        var store = new FakeStore { Receipt = receipt };
        Create(new FakeRunner(), store, HidHideInspectionStatus.Available, new(true, "1.4.999.0", true)).Reconcile();
        Assert.Equal(HidHideProvisioningReceiptState.Provisioned, store.Receipt?.State);
    }

    [Fact]
    public void InspectionFailure_DoesNotPromoteAnUnresolvedReceipt()
    {
        var receipt = Receipt(HidHideProvisioningReceiptState.InstallStarted);
        var store = new FakeStore { Receipt = receipt };

        Create(new FakeRunner(), store, HidHideInspectionStatus.Available, new(true, "1.5.230.0", false)).Reconcile();

        Assert.Same(receipt, store.Receipt);
    }

    [Fact]
    public async Task LiveSafetyState_IsRecheckedImmediatelyBeforeProcessStart()
    {
        var allowed = Context();
        var blocked = allowed with { ExternalController = new(ExternalControllerAssessmentStatus.ExternalPresent, 1, []) };
        var safety = new FakeSafetyProvider(allowed, blocked);
        var store = new FakeStore();
        var runner = new FakeRunner();
        var result = await Create(runner, store, safety: safety).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Blocked, result.Kind);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(HidHideProvisioningReceiptState.AttemptCancelled, store.Receipt?.State);
    }

    [Fact]
    public async Task MissingInstallerHash_BlocksBeforeElevation()
    {
        var runner = new FakeRunner();
        var result = await Create(runner, integrity: _ => false).ProvisionAsync(CancellationToken.None);
        Assert.Equal(HidHideProvisioningResultKind.Failed, result.Kind);
        Assert.Equal(0, runner.Calls);
    }

    private static HidHideProvisioner Create(FakeRunner runner, FakeStore? store = null, HidHideInspectionStatus inspection = HidHideInspectionStatus.NotInstalled, HidHidePackageState? package = null, Func<string, bool>? integrity = null, IHidHideProvisioningSafetyStateProvider? safety = null, IHidHidePackageProbe? packageProbe = null, HidHideProvisioningContext? context = null) => new(
        new HidHidePrerequisiteInspector(new FakeHidHide(inspection)),
        packageProbe ?? new FakePackageProbe(package ?? new(false, null, true)),
        store ?? new FakeStore(), runner, () => "test-installer.exe", integrity ?? (_ => true), safety ?? new FakeSafetyProvider(context ?? Context()));

    private static HidHideProvisioningContext Context(ControllerEnvironmentCompatibilityStatus compatibility = ControllerEnvironmentCompatibilityStatus.Supported) => new(
        new(compatibility, compatibility == ControllerEnvironmentCompatibilityStatus.Supported ? ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported : ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion),
        new(ExternalControllerAssessmentStatus.Clear, 0, []),
        SteamSessionState.FromRunningAppId(0),
        new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "test"), true);

    private static HidHideProvisioningReceipt Receipt(HidHideProvisioningReceiptState state) => new(1, state, Guid.NewGuid(), "1.5.230.0", HidHidePackageMetadata.InstallerSha256, PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, null, null);

    private sealed class FakeHidHide(HidHideInspectionStatus status) : IHidHideClient
    {
        public HidHideInspection Inspect() => new(status, new HashSet<string>());
        public bool AddApplication(string executablePath) => true;
        public bool RemoveApplication(string executablePath) => true;
    }
    private sealed class FakePackageProbe(HidHidePackageState state) : IHidHidePackageProbe { public HidHidePackageState Inspect() => state; }
    private sealed class SequencedPackageProbe(params HidHidePackageState[] states) : IHidHidePackageProbe
    {
        private int _index;
        public HidHidePackageState Inspect() => states[Math.Min(_index++, states.Length - 1)];
    }
    private sealed class FakeStore : IHidHideProvisioningReceiptStore
    {
        public HidHideProvisioningReceipt? Receipt;
        public bool Corrupt;
        public bool ThrowOnSave;
        public int SaveCalls;
        public HidHideReceiptLoadResult Load() => new(Receipt, Corrupt);
        public void Save(HidHideProvisioningReceipt receipt) { SaveCalls++; if (ThrowOnSave) throw new IOException(); Receipt = receipt; }
    }
    private sealed class FakeRunner : IElevatedProcessRunner
    {
        public int Calls;
        public ElevatedProcessResult Result = new(ElevatedProcessResultKind.Completed, 1);
        public Action? OnRun;
        public Func<Task>? OnRunAsync;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ElevatedProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            Calls++; Started.TrySetResult(); OnRun?.Invoke(); if (OnRunAsync is not null) await OnRunAsync(); return Result;
        }
    }
    private sealed class FakeSafetyProvider(params HidHideProvisioningContext[] contexts) : IHidHideProvisioningSafetyStateProvider
    {
        private int _index;
        public Task<HidHideProvisioningContext> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(contexts[Math.Min(_index++, contexts.Length - 1)]);
    }
}
