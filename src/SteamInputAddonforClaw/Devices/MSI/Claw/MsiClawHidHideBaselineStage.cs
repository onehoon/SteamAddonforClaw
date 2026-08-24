using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>Normalizes HidHide to the Addon's non-restoring routing baseline at each route start.</summary>
internal sealed class MsiClawHidHideBaselineStage : IRoutingPipelineStage
{
    private readonly IHidHideClient _hidHide;
    private readonly string _executablePath;
    private readonly Func<IReadOnlyCollection<string>> _resolveTrustedOfficialApplications;
    private IReadOnlyList<string> _trustedOfficialApplicationPaths = [];
    private bool _prepared;

    internal MsiClawHidHideBaselineStage(
        IHidHideClient hidHide,
        string executablePath,
        Func<IReadOnlyCollection<string>>? resolveTrustedOfficialApplications = null)
    {
        _hidHide = hidHide ?? throw new ArgumentNullException(nameof(hidHide));
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("The Addon executable path must be fully qualified.", nameof(executablePath));
        _executablePath = Path.GetFullPath(executablePath);
        _resolveTrustedOfficialApplications = resolveTrustedOfficialApplications
            ?? (() => new HidHideTrustedApplicationPathResolver().Resolve());
    }

    public RoutingStageKind Kind => RoutingStageKind.HidHideBaseline;

    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RoutingStageOperationResult.Success("HidHideBaselineAvailable"));
    }

    public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inspection = Inspect();
        if (inspection is null) return ValueTask.FromResult(Failure("HidHideInspectionUnavailable"));
        var failure = ValidateInspection(inspection);
        if (failure is not null) return ValueTask.FromResult(Failure(failure));
        _prepared = true;
        return ValueTask.FromResult(RoutingStageOperationResult.Success("HidHideBaselineReady"));
    }

    public ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        if (!_prepared) return ValueTask.FromResult(Failure("HidHideBaselineNotPrepared"));
        var inspection = Inspect();
        if (inspection is null) return ValueTask.FromResult(Failure("HidHideInspectionUnavailable"));
        var failure = ValidateInspection(inspection);
        if (failure is not null) return ValueTask.FromResult(Failure(failure));
        _trustedOfficialApplicationPaths = ResolveTrustedOfficialApplications();

        var hiddenRemoved = 0;
        var foreignApplicationsRemoved = 0;
        var applicationAdded = 0;
        var requiredApplications = _trustedOfficialApplicationPaths
            .Append(_executablePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var entry in (inspection.HiddenDeviceEntries ?? []).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Try(() => _hidHide.RemoveHiddenDevice(entry)))
                return Fail("RemoveHiddenDeviceFailed", hiddenRemoved, foreignApplicationsRemoved, applicationAdded, started);
            hiddenRemoved++;
        }

        foreach (var entry in inspection.ApplicationWhitelist.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsAllowedApplication(entry)) continue;
            if (!Try(() => _hidHide.RemoveApplication(entry)))
                return Fail("RemoveApplicationFailed", hiddenRemoved, foreignApplicationsRemoved, applicationAdded, started);
            foreignApplicationsRemoved++;
        }

        foreach (var required in requiredApplications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inspection.ApplicationWhitelist.Any(entry => PathEquals(entry, required))) continue;
            if (!Try(() => _hidHide.AddApplication(required)))
                return Fail("AddApplicationFailed", hiddenRemoved, foreignApplicationsRemoved, applicationAdded, started);
            applicationAdded++;
        }

        var verification = Inspect();
        if (verification is null) return Fail("VerificationInspectionUnavailable", hiddenRemoved, foreignApplicationsRemoved, applicationAdded, started);
        failure = ValidateBaseline(verification);
        if (failure is not null) return Fail(failure, hiddenRemoved, foreignApplicationsRemoved, applicationAdded, started);

        AppLog.Info("HidHideBaseline", "HidHide baseline normalized.",
            ("AlreadyNormalized", hiddenRemoved == 0 && foreignApplicationsRemoved == 0 && applicationAdded == 0),
            ("HiddenEntriesRemoved", hiddenRemoved),
            ("ForeignWhitelistEntriesRemoved", foreignApplicationsRemoved),
            ("RequiredApplicationsAdded", applicationAdded),
            ("TrustedOfficialApplicationCount", _trustedOfficialApplicationPaths.Count),
            ("Result", "Success"),
            ("Reason", "HidHideBaselineReady"),
            ("ElapsedMs", Elapsed(started)));
        return ValueTask.FromResult(RoutingStageOperationResult.Success("HidHideBaselineReady"));
    }

    public ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _prepared = false;
        return ValueTask.FromResult(RoutingStageOperationResult.Success("HidHideBaselineNonRestoring"));
    }

    private HidHideInspection? Inspect()
    {
        try { return _hidHide.Inspect(); }
        catch (Exception exception)
        {
            AppLog.Warn("HidHideBaseline", "HidHide baseline inspection failed.", exception, ("Reason", "HidHideInspectionUnavailable"));
            return null;
        }
    }

    private string? ValidateInspection(HidHideInspection inspection)
    {
        if (inspection.Status == HidHideInspectionStatus.NotInstalled) return "HidHideNotInstalled";
        if (!inspection.IsConfigurationReadable || inspection.Status is HidHideInspectionStatus.AccessDenied or HidHideInspectionStatus.ConfigurationUnavailable)
            return "HidHideInspectionUnavailable";
        if (inspection.IsInverseWhitelist || inspection.Status == HidHideInspectionStatus.InverseWhitelist) return "HidHideInverseWhitelist";
        if (inspection.HasUnresolvedApplicationWhitelistEntries) return "HidHideUnresolvedWhitelist";
        return null;
    }

    private string? ValidateBaseline(HidHideInspection inspection)
    {
        var failure = ValidateInspection(inspection);
        if (failure is not null) return failure;
        if ((inspection.HiddenDeviceEntries ?? []).Count != 0) return "HiddenDeviceVerificationFailed";
        var requiredApplications = _trustedOfficialApplicationPaths
            .Append(_executablePath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (requiredApplications.Any(required => !inspection.ApplicationWhitelist.Any(entry => PathEquals(entry, required))))
            return "RequiredWhitelistVerificationFailed";
        if (inspection.ApplicationWhitelist.Any(entry => !IsAllowedApplication(entry))) return "ForeignWhitelistVerificationFailed";
        return null;
    }

    private bool IsAllowedApplication(string path) => PathEquals(path, _executablePath) || _trustedOfficialApplicationPaths.Any(trusted => PathEquals(path, trusted));

    private IReadOnlyList<string> ResolveTrustedOfficialApplications() => (_resolveTrustedOfficialApplications() ?? [])
        .Where(IsCanonicalPath)
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool Try(Func<bool> operation)
    {
        try { return operation(); }
        catch { return false; }
    }

    private ValueTask<RoutingStageOperationResult> Fail(string reason, int hiddenRemoved, int foreignApplicationsRemoved, int applicationAdded, long started)
    {
        AppLog.Warn("HidHideBaseline", "HidHide baseline normalization failed.", null,
            ("HiddenEntriesRemoved", hiddenRemoved),
            ("ForeignWhitelistEntriesRemoved", foreignApplicationsRemoved),
            ("RequiredApplicationsAdded", applicationAdded),
            ("TrustedOfficialApplicationCount", _trustedOfficialApplicationPaths.Count),
            ("Result", "Failure"),
            ("Reason", reason),
            ("ElapsedMs", Elapsed(started)));
        return ValueTask.FromResult(Failure(reason));
    }

    private static bool IsCanonicalPath(string path)
    {
        try { return Path.IsPathFullyQualified(path); }
        catch { return false; }
    }

    private static bool PathEquals(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static RoutingStageOperationResult Failure(string reason) => RoutingStageOperationResult.Failure(reason);
    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
