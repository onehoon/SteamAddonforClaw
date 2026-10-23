namespace SteamInputAddonforClaw.Controllers.Detection;

/// <summary>Identifies a failed native SetupAPI PnP enumeration or property-read call.</summary>
internal sealed class PnpDeviceReadException : InvalidOperationException
{
    internal PnpDeviceReadException(
        string operation,
        int win32Error,
        uint? propertyId = null,
        string? propertyName = null,
        uint? requiredSize = null,
        string? instanceId = null,
        uint? devInst = null,
        string? operationContext = null)
        : base(FormatMessage(operation, win32Error, propertyId, propertyName, requiredSize, instanceId, devInst, operationContext))
    {
        Operation = operation;
        Win32Error = win32Error;
        PropertyId = propertyId;
        PropertyName = propertyName;
        RequiredSize = requiredSize;
        InstanceId = instanceId;
        DevInst = devInst;
        OperationContext = operationContext;
    }

    internal string Operation { get; }
    internal int Win32Error { get; }
    internal uint? PropertyId { get; }
    internal string? PropertyName { get; }
    internal uint? RequiredSize { get; }
    internal string? InstanceId { get; }
    internal uint? DevInst { get; }
    internal string? OperationContext { get; }
    internal bool InstanceIdUnavailable => string.IsNullOrWhiteSpace(InstanceId);

    internal (string Key, object? Value)[] ToLogFields() =>
    [
        ("Operation", Operation),
        ("Property", PropertyName is null ? null : $"{PropertyName}={PropertyId}"),
        ("PropertyId", PropertyId),
        ("Win32Error", Win32Error),
        ("RequiredSize", RequiredSize),
        ("InstanceId", InstanceId),
        ("DevInst", DevInst),
        ("InstanceIdUnavailable", InstanceIdUnavailable),
        ("OperationContext", OperationContext),
    ];

    private static string FormatMessage(
        string operation,
        int win32Error,
        uint? propertyId,
        string? propertyName,
        uint? requiredSize,
        string? instanceId,
        uint? devInst,
        string? operationContext)
    {
        var property = propertyId is null ? string.Empty : $" {propertyName ?? "PnPProperty"}={propertyId}";
        var target = !string.IsNullOrWhiteSpace(instanceId)
            ? $" InstanceId={instanceId}."
            : devInst is not null
                ? $" DevInst={devInst}; InstanceIdUnavailable=true."
                : " InstanceIdUnavailable=true.";
        var context = string.IsNullOrWhiteSpace(operationContext) ? string.Empty : $" Context={operationContext}.";
        var size = requiredSize is null ? string.Empty : $" RequiredSize={requiredSize}.";
        return $"{operation}{property} failed with Win32Error={win32Error}.{size}{target}{context}";
    }
}
