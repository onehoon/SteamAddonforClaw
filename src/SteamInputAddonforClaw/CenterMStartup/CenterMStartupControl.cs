using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterMStartup;

/// <summary>Runtime-owned MSI Center M startup control (work order PR1). Deliberately narrow: it
/// reads the three startup roots (non-elevated) and writes them all at once through the privileged
/// helper. It is not a generic service/task administration framework, it never stops or starts
/// anything, and it makes no routing/controller decisions from the result -- a <c>Disabled</c>
/// configuration does not mean Center M has left the current Windows session (section 12).</summary>
internal sealed class CenterMStartupControl
{
    private readonly bool _available;
    private readonly CenterMStartupStateReader _reader;
    private readonly ICenterMStartupHelperInvoker _helper;

    internal CenterMStartupControl(bool available)
        : this(available, new CenterMStartupStateReader(), new CenterMStartupHelperClient()) { }

    internal CenterMStartupControl(bool available, CenterMStartupStateReader reader, ICenterMStartupHelperInvoker helper)
    {
        _available = available;
        _reader = reader;
        _helper = helper;
    }

    /// <summary>Enabled requires the service to be exactly <see cref="CenterMFoundationServiceMode.Automatic"/>
    /// (Manual/Other is not the reboot baseline); Disabled requires exactly
    /// <see cref="CenterMFoundationServiceMode.Disabled"/>. Anything else is
    /// <see cref="FrontendCenterMStartupState.Partial"/> -- surfaced, never auto-repaired
    /// (section 7 / PR #430 review).</summary>
    internal static FrontendCenterMStartupState Classify(bool server, bool updater, CenterMFoundationServiceMode service) =>
        server && updater && service == CenterMFoundationServiceMode.Automatic ? FrontendCenterMStartupState.Enabled
        : !server && !updater && service == CenterMFoundationServiceMode.Disabled ? FrontendCenterMStartupState.Disabled
        : FrontendCenterMStartupState.Partial;

    private static bool FoundationEnabledFlag(CenterMFoundationServiceMode service) =>
        service == CenterMFoundationServiceMode.Automatic;

    internal FrontendCenterMStartupSnapshot Capture()
    {
        if (!_available)
            return FrontendCenterMStartupSnapshot.Unavailable;

        if (!_reader.TryRead(out var server, out var updater, out var service, out var failure))
        {
            AppLog.Info("CenterM.Startup", "Startup state could not be read.", ("Reason", failure ?? "Unknown"));
            return FrontendCenterMStartupSnapshot.Unavailable with { FailureMessage = failure };
        }

        var state = Classify(server, updater, service);
        AppLog.Info("CenterM.Startup", "Startup state captured.", ("State", state), ("Server", server), ("Updater", updater), ("Service", service));
        return new FrontendCenterMStartupSnapshot(state, server, updater, FoundationEnabledFlag(service), null);
    }

    internal async Task<FrontendCenterMStartupMutationResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (!_available)
            return new FrontendCenterMStartupMutationResult(
                FrontendCenterMStartupMutationOutcome.Unavailable,
                FrontendCenterMStartupSnapshot.Unavailable,
                "MSI Center M startup control is unavailable on this device.");

        AppLog.Info("CenterM.Startup", enabled ? "Enable requested." : "Disable requested.");
        var helperResult = await _helper.SetEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);

        // Always report the freshest actual three-root state, re-read non-elevated. Never fabricate
        // the requested state after a cancelled/failed privileged operation (Addendum E).
        var snapshot = ReadSnapshotAfterMutation(helperResult);

        switch (helperResult.Outcome)
        {
            case CenterMStartupHelperOutcome.Cancelled:
                AppLog.Info("CenterM.Startup", "Mutation cancelled.", ("State", snapshot.State));
                return new FrontendCenterMStartupMutationResult(
                    FrontendCenterMStartupMutationOutcome.Cancelled, snapshot,
                    "Elevation was cancelled before the MSI Center M startup configuration was changed.");

            case CenterMStartupHelperOutcome.HelperUnavailable:
                AppLog.Warn("CenterM.Startup", "Mutation could not run.", null, ("Reason", helperResult.Error ?? "Unknown"));
                return new FrontendCenterMStartupMutationResult(
                    FrontendCenterMStartupMutationOutcome.Failed, snapshot,
                    helperResult.Error ?? "The MSI Center M startup configuration could not be changed.");

            default:
                var target = enabled ? FrontendCenterMStartupState.Enabled : FrontendCenterMStartupState.Disabled;
                if (helperResult.Ok && snapshot.State == target)
                {
                    AppLog.Info("CenterM.Startup", enabled ? "Enable verified. Restart required." : "Disable verified. Restart required.");
                    return new FrontendCenterMStartupMutationResult(
                        FrontendCenterMStartupMutationOutcome.Succeeded, snapshot, null);
                }

                AppLog.Warn("CenterM.Startup", "Mutation incomplete.", null, ("State", snapshot.State), ("Target", enabled), ("HelperError", helperResult.Error));
                return new FrontendCenterMStartupMutationResult(
                    FrontendCenterMStartupMutationOutcome.Failed, snapshot,
                    helperResult.Error ?? $"MSI Center M could not be fully {(enabled ? "enabled" : "disabled")}. The startup configuration is currently inconsistent.");
        }
    }

    private FrontendCenterMStartupSnapshot ReadSnapshotAfterMutation(CenterMStartupHelperResult helperResult)
    {
        if (_reader.TryRead(out var server, out var updater, out var service, out _))
            return new FrontendCenterMStartupSnapshot(Classify(server, updater, service), server, updater, FoundationEnabledFlag(service), null);

        // The non-elevated re-read failed; fall back to the helper's observation ONLY when it
        // actually read all three roots (SnapshotAvailable). Placeholder fields after a helper/read
        // failure are "not observed", never "observed disabled" (Addendum E / PR #430 review).
        if (helperResult.Outcome == CenterMStartupHelperOutcome.Completed && helperResult.SnapshotAvailable)
        {
            var s = helperResult.ServerTaskEnabled;
            var u = helperResult.UpdaterTaskEnabled;
            var mode = helperResult.FoundationServiceMode;
            return new FrontendCenterMStartupSnapshot(Classify(s, u, mode), s, u, FoundationEnabledFlag(mode), null);
        }

        return FrontendCenterMStartupSnapshot.Unavailable with { FailureMessage = helperResult.Error };
    }
}
