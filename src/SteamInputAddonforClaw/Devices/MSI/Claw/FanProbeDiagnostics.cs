namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiFanHelperInfo(
    int ProcessId,
    string Executable,
    bool Elevated,
    string ProcessArchitecture,
    string OsArchitecture);

internal sealed record MsiFanWmiVersion(
    bool Succeeded,
    byte[] RawPayload,
    int? Major,
    int? Minor,
    string Stage,
    string? ExceptionType = null,
    int? HResult = null,
    int? ManagementStatus = null,
    bool UsedFallback = false);

internal sealed record MsiFanOperationResult(
    bool Succeeded,
    string Operation,
    string Method,
    int Block,
    byte[] Payload,
    byte[] RequestPackage,
    int LogicalPayloadLength,
    int WmiPackageLength,
    string Stage,
    string? ExceptionType,
    int? HResult,
    int? ManagementStatus,
    bool UsedFallback,
    bool InvokeReturnedNormally,
    bool OutputObjectPresent);

internal interface IMsiFanDiagnosticTransport
{
    bool TryGetHelperInfo(out MsiFanHelperInfo info);
    bool TryGetWmiVersion(out MsiFanWmiVersion version);
    bool TryGetMethodInventory(out string[] methods);
    MsiFanOperationResult InvokeFanDiagnostic(string operation, int block, byte[]? payload);
}
