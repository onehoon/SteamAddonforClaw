using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.WindowsTweaks;

internal sealed record WindowsTweakObservation(
    bool Supported,
    bool Managed,
    bool? ConfiguredEnabled,
    bool? EffectiveEnabled,
    string? Detail = null,
    bool ExplorerRestartPending = false,
    bool IsMixed = false);

internal readonly record struct WindowsTweakApplyResult(bool RestartRequired = false);

internal interface IWindowsTweaksOperatingSystem
{
    WindowsTweakObservation Capture(FrontendWindowsTweakId id, CancellationToken cancellationToken);
    WindowsTweakApplyResult Set(FrontendWindowsTweakId id, bool enabled, CancellationToken cancellationToken);
}

internal static class WindowsTweaksStateMapper
{
    internal static FrontendWindowsTweakSnapshot Map(FrontendWindowsTweakId id, WindowsTweakObservation observation)
    {
        var supportsMixed = id is FrontendWindowsTweakId.InstantContextMenus
            or FrontendWindowsTweakId.DisableWindowsWidgets
            or FrontendWindowsTweakId.DisableEdgeBackground
            or FrontendWindowsTweakId.DisableStartupAppDelay;
        var isMixed = supportsMixed && observation.IsMixed;
        var state = !observation.Supported
            ? FrontendWindowsTweakState.Unsupported
            : observation.Managed
                ? FrontendWindowsTweakState.Managed
                : isMixed
                    ? FrontendWindowsTweakState.Mixed
                    : observation.ExplorerRestartPending
                        ? FrontendWindowsTweakState.ExplorerRestartPending
                        : observation.ConfiguredEnabled is { } configured
                            && observation.EffectiveEnabled is { } effective
                            && configured != effective
                                ? configured
                                    ? FrontendWindowsTweakState.EnablePendingRestart
                                    : FrontendWindowsTweakState.DisablePendingRestart
                                : observation.EffectiveEnabled is { } current
                                    ? current ? FrontendWindowsTweakState.Enabled : FrontendWindowsTweakState.Disabled
                                    : FrontendWindowsTweakState.Unknown;

        var configuredEnabled = isMixed ? false : observation.ConfiguredEnabled;
        var effectiveEnabled = isMixed ? null : observation.EffectiveEnabled;
        var canChange = observation.Supported
            && !observation.Managed
            && state != FrontendWindowsTweakState.Unknown
            && configuredEnabled.HasValue;
        return new(id, state, configuredEnabled, effectiveEnabled, canChange, observation.Detail);
    }
}

internal sealed class WindowsTweaksRuntime
{
    private readonly IWindowsTweaksOperatingSystem _operatingSystem;
    private readonly IReadOnlyList<FrontendWindowsTweakDefinition> _definitions;

    internal WindowsTweaksRuntime(IWindowsTweaksOperatingSystem operatingSystem, bool isKnownA2vmModel)
    {
        _operatingSystem = operatingSystem ?? throw new ArgumentNullException(nameof(operatingSystem));
        _definitions = FilterApplicableDefinitions(FrontendWindowsTweakCatalog.All, isKnownA2vmModel);
    }

    internal async Task<FrontendWindowsTweaksSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Task.WhenAll(_definitions.Select(definition => CaptureOneAsync(definition.Id, cancellationToken)))
            .ConfigureAwait(false);
        return new(rows);
    }

    internal async Task<FrontendWindowsTweakMutationResult> SetAsync(
        FrontendWindowsTweakId id,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_definitions.Any(definition => definition.Id == id))
            return new(false, "This Windows setting is unavailable for the identified device.",
                new(id, FrontendWindowsTweakState.Unknown, null, null, false, "This setting is unavailable for the identified device."));

        Exception? writeFailure = null;
        var applyResult = default(WindowsTweakApplyResult);
        try
        {
            applyResult = await Task.Run(() => _operatingSystem.Set(id, enabled, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            writeFailure = exception;
        }

        var updated = await CaptureOneAsync(id, CancellationToken.None).ConfigureAwait(false);
        if (applyResult.RestartRequired
            && id == FrontendWindowsTweakId.VirtualMachinePlatform
            && updated.ConfiguredEnabled == enabled
            && updated.State is FrontendWindowsTweakState.Enabled or FrontendWindowsTweakState.Disabled)
        {
            updated = updated with
            {
                State = enabled ? FrontendWindowsTweakState.EnablePendingRestart : FrontendWindowsTweakState.DisablePendingRestart,
                EffectiveEnabled = !enabled,
                Detail = "Windows requires a restart to complete this change.",
            };
        }

        var readbackVerified = updated.ConfiguredEnabled == enabled
            && updated.State is not (FrontendWindowsTweakState.Unknown or FrontendWindowsTweakState.Unsupported or FrontendWindowsTweakState.Managed or FrontendWindowsTweakState.Mixed);

        if (writeFailure is null && readbackVerified)
            return new(true, null, updated);

        if (id == FrontendWindowsTweakId.VirtualMachinePlatform)
        {
            if (writeFailure is VmpCommandException vmpFailure)
                LogVmpFailure(vmpFailure, updated.State);
            else if (writeFailure is not null)
                LogVmpFailure(enabled ? VmpOperation.Enable : VmpOperation.Disable, writeFailure, updated.State);
            else
                LogVmpReadbackFailure(enabled ? VmpOperation.Enable : VmpOperation.Disable, updated.State);
        }

        var detail = id == FrontendWindowsTweakId.VirtualMachinePlatform
            ? FormatVmpMutationFailure(enabled)
            : writeFailure is null
                ? "Windows did not verify the requested state."
                : FormatSanitizedFailure(writeFailure);
        return new(false, detail, updated);
    }

    internal static IReadOnlyList<FrontendWindowsTweakDefinition> FilterApplicableDefinitions(
        IEnumerable<FrontendWindowsTweakDefinition> definitions,
        bool isKnownA2vmModel) => definitions
            .Where(definition => definition.Applicability == FrontendWindowsTweakApplicability.AllSupportedWindows
                || (definition.Applicability == FrontendWindowsTweakApplicability.A2vmOnly && isKnownA2vmModel))
            .ToArray();

    private async Task<FrontendWindowsTweakSnapshot> CaptureOneAsync(FrontendWindowsTweakId id, CancellationToken cancellationToken)
    {
        try
        {
            var observation = await Task.Run(() => _operatingSystem.Capture(id, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            var snapshot = WindowsTweaksStateMapper.Map(id, observation);
            if (id == FrontendWindowsTweakId.VirtualMachinePlatform
                && snapshot.State is FrontendWindowsTweakState.Unknown or FrontendWindowsTweakState.Unsupported)
            {
                AppLog.Warn("WindowsTweaks.VMP", "Virtual Machine Platform returned an unusable feature state.", null,
                    ("Operation", VmpOperation.Read),
                    ("Completion", "Completed"),
                    ("ProcessExitCode", 0),
                    ("WindowsErrorId", snapshot.State == FrontendWindowsTweakState.Unsupported ? "FeatureUnavailable" : "UnknownFeatureState"),
                    ("WindowsErrorDetail", observation.Detail),
                    ("ReadbackState", snapshot.State));
            }
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (id == FrontendWindowsTweakId.VirtualMachinePlatform)
            {
                if (exception is VmpCommandException vmpFailure)
                    LogVmpFailure(vmpFailure, FrontendWindowsTweakState.Unknown);
                else
                    LogVmpFailure(VmpOperation.Read, exception, FrontendWindowsTweakState.Unknown);
                return new(id, FrontendWindowsTweakState.Unknown, null, null, false,
                    "Virtual Machine Platform state could not be read. Check Windows servicing configuration.");
            }

            return new(id, FrontendWindowsTweakState.Unknown, null, null, false, FormatSanitizedFailure(exception));
        }
    }

    private static string FormatVmpMutationFailure(bool enabled) => enabled
        ? "Virtual Machine Platform could not be enabled. Check Windows servicing configuration."
        : "Virtual Machine Platform could not be disabled. Check Windows servicing configuration.";

    private static void LogVmpFailure(VmpCommandException exception, FrontendWindowsTweakState readbackState) =>
        AppLog.Warn("WindowsTweaks.VMP", "Virtual Machine Platform command failed.", null,
            ("Operation", exception.Operation),
            ("Completion", exception.Completion),
            ("ProcessExitCode", exception.ProcessExitCode),
            ("WindowsErrorCode", exception.WindowsErrorCode is { } code ? $"0x{code:X8}" : null),
            ("WindowsErrorId", exception.WindowsErrorId),
            ("WindowsErrorCategory", exception.WindowsErrorCategory),
            ("WindowsErrorDetail", exception.WindowsErrorDetail),
            ("ReadbackState", readbackState));

    private static void LogVmpFailure(
        VmpOperation operation,
        Exception exception,
        FrontendWindowsTweakState readbackState) =>
        AppLog.Warn("WindowsTweaks.VMP", "Virtual Machine Platform operation failed before a process result was available.", null,
            ("Operation", operation),
            ("Completion", exception is OperationCanceledException ? "Cancelled" : "Failed"),
            ("ProcessExitCode", null),
            ("WindowsErrorCode", $"0x{exception.HResult:X8}"),
            ("WindowsErrorId", exception.GetType().Name),
            ("WindowsErrorCategory", null),
            ("WindowsErrorDetail", exception is OperationCanceledException ? "Operation was cancelled." : exception.GetType().Name),
            ("ReadbackState", readbackState));

    private static void LogVmpReadbackFailure(
        VmpOperation operation,
        FrontendWindowsTweakState readbackState) =>
        AppLog.Warn("WindowsTweaks.VMP", "Virtual Machine Platform did not verify the requested state.", null,
            ("Operation", operation),
            ("Completion", "Completed"),
            ("ProcessExitCode", 0),
            ("WindowsErrorCode", null),
            ("WindowsErrorId", "ReadbackMismatch"),
            ("WindowsErrorCategory", null),
            ("WindowsErrorDetail", "The requested feature state was not observed after the command."),
            ("ReadbackState", readbackState));

    private static string FormatSanitizedFailure(Exception exception) =>
        $"Windows setting operation failed ({exception.GetType().Name}, 0x{exception.HResult:X8}).";
}
