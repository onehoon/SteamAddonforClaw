namespace SteamInputAddonforClaw.Prerequisites;

internal sealed class RuntimePrerequisiteInspector(
    HidHidePrerequisiteInspector hidHideInspector,
    UsbIpWin2PrerequisiteInspector usbIpWin2Inspector,
    ViiperRuntimeInspector viiperInspector) : IRuntimePrerequisiteInspector
{
    public RuntimePrerequisiteAssessment Inspect() => new(
        hidHideInspector.Inspect(),
        usbIpWin2Inspector.Inspect(),
        viiperInspector.Inspect());
}
