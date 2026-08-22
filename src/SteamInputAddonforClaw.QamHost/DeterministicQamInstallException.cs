namespace SteamInputAddonforClaw.QamHost;

public sealed class DeterministicQamInstallException(string message) : InvalidOperationException(message);
