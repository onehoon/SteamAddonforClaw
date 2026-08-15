namespace SteamInputAddonforClaw.Prerequisites;

internal interface IRuntimePayloadFileSystem
{
    bool FileExists(string path);
}

internal sealed class RuntimePayloadFileSystem : IRuntimePayloadFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
}

internal interface IRuntimePayloadHashProvider
{
    string GetSha256(string path);
}

internal sealed class RuntimePayloadHashProvider : IRuntimePayloadHashProvider
{
    public string GetSha256(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}

internal sealed class ViiperRuntimeInspector(IRuntimePayloadFileSystem fileSystem, string expectedPayloadPath, IRuntimePayloadHashProvider? hashProvider = null)
{
    internal const string PayloadFileName = "libVIIPER.dll";
    internal static string PayloadRelativePath => Path.Combine("Dependencies", "Viiper", PayloadFileName);
    // Pins the Phase 2B2 canonical Steam Deck output-callback ABI adoption libVIIPER.dll build (see Dependencies/Viiper/PROVENANCE.md).
    internal const string ExpectedPayloadSha256 = "B2050EA357A6B663A97C5EDE9AB01A134162CCB3661D96E318897F40A29B59EA";

    public ViiperRuntimeInspector()
        : this(new RuntimePayloadFileSystem(), Path.Combine(AppContext.BaseDirectory, PayloadRelativePath), new RuntimePayloadHashProvider())
    {
    }

    public PrerequisiteAssessment Inspect()
    {
        try
        {
            if (!fileSystem.FileExists(expectedPayloadPath)) return new(PrerequisiteKind.Viiper, PrerequisiteStatus.Missing, "ViiperPayloadMissing");
            if (hashProvider is not null && !string.Equals(hashProvider.GetSha256(expectedPayloadPath), ExpectedPayloadSha256, StringComparison.OrdinalIgnoreCase))
                return new(PrerequisiteKind.Viiper, PrerequisiteStatus.Unusable, "ViiperPayloadHashMismatch");
            return new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "ViiperRuntimeReady");
        }
        catch
        {
            return new(PrerequisiteKind.Viiper, PrerequisiteStatus.Indeterminate, "ViiperPayloadInspectionFailed");
        }
    }
}
