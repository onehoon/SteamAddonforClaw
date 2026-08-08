namespace SteamInputAddonforClaw.Startup;

internal interface IUpdateGate
{
    Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken);
}

internal enum UpdateGateResult
{
    Continue,
    RestartScheduled
}
