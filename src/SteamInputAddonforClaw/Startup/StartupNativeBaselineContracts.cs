namespace SteamInputAddonforClaw.Startup;

internal sealed record StartupNativeBaselineResult(bool IsSafeToContinue, string Reason);

internal interface IStartupNativeBaselineValidator
{
    Task<StartupNativeBaselineResult> ValidateAsync(CancellationToken cancellationToken);
}
