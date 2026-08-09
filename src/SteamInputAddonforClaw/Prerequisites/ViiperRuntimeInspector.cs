namespace SteamInputAddonforClaw.Prerequisites;

internal interface IRuntimePayloadFileSystem
{
    bool FileExists(string path);
}

internal sealed class RuntimePayloadFileSystem : IRuntimePayloadFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
}

internal sealed class ViiperRuntimeInspector(IRuntimePayloadFileSystem fileSystem, string expectedPayloadPath)
{
    internal const string PayloadFileName = "libVIIPER.dll";

    public ViiperRuntimeInspector()
        : this(new RuntimePayloadFileSystem(), Path.Combine(AppContext.BaseDirectory, PayloadFileName))
    {
    }

    public PrerequisiteAssessment Inspect()
    {
        try
        {
            return fileSystem.FileExists(expectedPayloadPath)
                ? new(PrerequisiteKind.Viiper, PrerequisiteStatus.Present, "ViiperPayloadPresentUnverified")
                : new(PrerequisiteKind.Viiper, PrerequisiteStatus.Missing, "ViiperPayloadMissing");
        }
        catch
        {
            return new(PrerequisiteKind.Viiper, PrerequisiteStatus.Indeterminate, "ViiperPayloadInspectionFailed");
        }
    }
}
