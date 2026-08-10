namespace SteamInputAddonforClaw.Controllers.Detection;

public interface IControllerIdentityExclusionSource
{
    bool IsExcluded(ControllerDeviceInfo device);
    bool HasUncertainOwnership => false;
}

public sealed class EmptyControllerIdentityExclusionSource : IControllerIdentityExclusionSource
{
    public static EmptyControllerIdentityExclusionSource Instance { get; } = new();

    private EmptyControllerIdentityExclusionSource()
    {
    }

    public bool IsExcluded(ControllerDeviceInfo device) => false;
}
