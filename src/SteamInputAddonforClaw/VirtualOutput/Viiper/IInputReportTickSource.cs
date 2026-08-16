namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal interface IInputReportTickSource
{
    ValueTask<bool> WaitForTickAsync(CancellationToken cancellationToken);
}
