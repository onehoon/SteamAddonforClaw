using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Wing;

/// <summary>
/// Resolves a delivered WING gesture (Gamebar Button / Event88) to its configured
/// <see cref="FrontButtonAction"/> and dispatches it. Same domain resolution and action semantics as
/// the Center M path: the domain is captured fresh from the actual Full1902 SteamDeck presentation,
/// then the Gamebar binding for that domain is executed through the shared front-button action
/// executor. A Gamebar action failure is logged and observation continues -- the WING path keeps its
/// existing epoch/authority lifetime behaviour, it does not fail open to a native Center M.
/// </summary>
internal sealed class WingActionDispatcher
{
    private readonly Func<FrontButtonMappingSettings> _captureMapping;
    private readonly Func<bool> _captureSteamPresentationActive;
    private readonly FrontButtonActionExecutor _executor;

    internal WingActionDispatcher(
        Func<FrontButtonMappingSettings> captureMapping,
        Func<bool> captureSteamPresentationActive,
        FrontButtonActionExecutor executor)
    {
        _captureMapping = captureMapping ?? throw new ArgumentNullException(nameof(captureMapping));
        _captureSteamPresentationActive = captureSteamPresentationActive ?? throw new ArgumentNullException(nameof(captureSteamPresentationActive));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    internal void Dispatch(WingGesture gesture)
    {
        _ = gesture; // One action per physical press per domain; the gesture value no longer selects anything.
        try
        {
            var mapping = _captureMapping();
            var domain = _captureSteamPresentationActive() ? FrontButtonDomain.Steam : FrontButtonDomain.Normal;
            var binding = mapping.Resolve(FrontButtonKind.Gamebar, domain);
            _executor.Execute("Wing.Action", FrontButtonKind.Gamebar, domain, binding);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Wing.Action", "WING action selection/execution failed; observation continues.", exception);
        }
    }
}
