using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.WindowsTweaks;

internal sealed record WindowsTweakObservation(
    bool Supported,
    bool Managed,
    bool? ConfiguredEnabled,
    bool? EffectiveEnabled,
    string? Detail = null,
    bool ExplorerRestartPending = false,
    bool IsMixed = false);

internal interface IWindowsTweaksOperatingSystem
{
    WindowsTweakObservation Capture(FrontendWindowsTweakId id, CancellationToken cancellationToken);
    void Set(FrontendWindowsTweakId id, bool enabled, CancellationToken cancellationToken);
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
            return new(false, "This Windows setting is unavailable for the identified device.", await CaptureAsync(cancellationToken).ConfigureAwait(false));

        var before = await CaptureOneAsync(id, cancellationToken).ConfigureAwait(false);
        if (!before.CanChange || !before.ConfiguredEnabled.HasValue)
        {
            var current = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            return new(false, "Windows does not currently allow this setting to be changed.", current);
        }

        Exception? writeFailure = null;
        if (before.ConfiguredEnabled.Value != enabled || before.State == FrontendWindowsTweakState.Mixed)
        {
            try
            {
                await Task.Run(() => _operatingSystem.Set(id, enabled, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A platform write can be interrupted after Windows has applied part of it. Always
                // read back once without the cancelled request token before propagating cancellation.
                await CaptureAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                writeFailure = exception;
            }
        }

        var after = await CaptureAsync(CancellationToken.None).ConfigureAwait(false);
        var changed = after.Tweaks.SingleOrDefault(row => row.Id == id);
        var readbackVerified = changed is not null
            && changed.ConfiguredEnabled == enabled
            && changed.State is not (FrontendWindowsTweakState.Unknown or FrontendWindowsTweakState.Unsupported or FrontendWindowsTweakState.Managed or FrontendWindowsTweakState.Mixed);

        if (writeFailure is null && readbackVerified)
            return new(true, null, after);

        var detail = writeFailure is null
            ? "Windows did not verify the requested state."
            : FormatSanitizedFailure(writeFailure);
        if (changed is not null)
            after = after with { Tweaks = after.Tweaks.Select(row => row.Id == id ? changed with { Detail = detail } : row).ToArray() };

        return new(false, detail, after);
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
            return WindowsTweaksStateMapper.Map(id, observation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(id, FrontendWindowsTweakState.Unknown, null, null, false, FormatSanitizedFailure(exception));
        }
    }

    private static string FormatSanitizedFailure(Exception exception) =>
        $"Windows setting operation failed ({exception.GetType().Name}, 0x{exception.HResult:X8}).";
}
