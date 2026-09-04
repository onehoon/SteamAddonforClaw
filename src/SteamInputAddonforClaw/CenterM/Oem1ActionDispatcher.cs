using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Resolves an <see cref="Oem1GesturePolicyRequest"/> (Center M Button / Event41) to its configured
/// <see cref="FrontButtonAction"/> and dispatches it. The mapping DOMAIN is selected first by
/// capturing whether the actual Full1902 SteamDeck presentation is active right now (App UI PR-C
/// section 5) -- never routing eligibility or a persisted setting -- and only then is the Center M
/// binding for that domain resolved and executed. The domain fact and the mapping are both captured
/// fresh at every dispatch, never cached.
/// </summary>
internal sealed class Oem1ActionDispatcher
{
    private readonly Func<FrontButtonMappingSettings> _captureMapping;
    private readonly Func<bool> _captureSteamPresentationActive;
    private readonly FrontButtonActionExecutor _executor;

    /// <param name="captureSteamPresentationActive">App UI PR-C section 5: <see langword="true"/> when
    /// the active Full1902 presentation is SteamDeck (Steam Game / Big Picture mapping domain),
    /// <see langword="false"/> for Xbox360 / no presentation (Normal mapping domain). Captured fresh
    /// per dispatch.</param>
    internal Oem1ActionDispatcher(
        Func<FrontButtonMappingSettings> captureMapping,
        Func<bool> captureSteamPresentationActive,
        FrontButtonActionExecutor executor)
    {
        _captureMapping = captureMapping ?? throw new ArgumentNullException(nameof(captureMapping));
        _captureSteamPresentationActive = captureSteamPresentationActive ?? throw new ArgumentNullException(nameof(captureSteamPresentationActive));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <summary>
    /// Dispatches the resolved Center M action. Returns <see langword="false"/> when a domain-supported
    /// action was actually invoked and its execution threw, OR when the domain/mapping capture itself
    /// threw before an action could be resolved. The SteamDeck presentation being inactive is never a
    /// failure -- it is the Normal-mapping case -- and neither is a binding capability validation
    /// refuses. The caller treats a false return as a Center M replacement-backend failure: custom
    /// gesture authority is revoked and native Center M restored, per the fail-open contract.
    /// </summary>
    internal bool Dispatch(Oem1GesturePolicyRequest request)
    {
        _ = request; // The product model is one action per physical press per domain; the gesture value no longer selects anything.
        try
        {
            var mapping = _captureMapping();
            var domain = _captureSteamPresentationActive() ? FrontButtonDomain.Steam : FrontButtonDomain.Normal;
            var binding = mapping.Resolve(FrontButtonKind.CenterM, domain);
            return _executor.Execute("CenterM.Oem1", FrontButtonKind.CenterM, domain, binding);
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Oem1", "OEM1 replacement action selection/execution failed.", exception);
            return false;
        }
    }
}
