using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Xbox;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.Diagnostics.XboxSession;

internal enum XboxGameProcessInspectionDisposition
{
    ProcessImageFailure,
    NoPackage,
    PackageIdentityFailure,
    ConfigNegative,
    ExecutableMismatch,
    Matched,
}

internal readonly record struct XboxGameProcessGenerationKey(uint ProcessId, long CreationTime);

internal interface IXboxGameProcessGeneration : IDisposable
{
    uint ProcessId { get; }
    XboxGameProcessGenerationKey Key { get; }
    bool IsSignaled { get; }
    event Action<IXboxGameProcessGeneration>? Exited;
}

internal sealed record XboxGameProcessOpenResult(IXboxGameProcessGeneration? Generation, int ErrorCode);

internal sealed record XboxGameProcessInspection(
    XboxGameProcessInspectionDisposition Disposition,
    string? FailureReason,
    FrontendXboxSessionDiagnosticGame? Game);

internal interface IXboxGameProcessIdentityProbe
{
    XboxGameProcessOpenResult Open(uint processId);
    Task<XboxGameProcessInspection> InspectAsync(IXboxGameProcessGeneration generation, CancellationToken cancellationToken);
}

internal sealed class WindowsXboxGameProcessIdentityProbe : IXboxGameProcessIdentityProbe
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitObject0 = 0;
    private const uint ErrorSuccess = 0;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint ErrorProcNotFound = 127;
    private const int MaximumNativeStringLength = 32 * 1024;

    private static readonly (string Name, uint Value)[] PackagePathTypes =
    [
        ("Install", 0),
        ("Effective", 2),
        ("Mutable", 1),
        ("MachineExternal", 3),
        ("UserExternal", 4),
        ("EffectiveExternal", 5),
    ];

    public XboxGameProcessOpenResult Open(uint processId)
    {
        if (processId == 0)
            return new(null, 87);

        var handle = OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, processId);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            return new(null, error);
        }

        if (!GetProcessTimes(handle, out var creation, out _, out _, out _))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            return new(null, error);
        }

        var creationTime = ((long)creation.High << 32) | creation.Low;
        return new(new WindowsXboxGameProcessGeneration(processId, creationTime, handle), 0);
    }

    public async Task<XboxGameProcessInspection> InspectAsync(
        IXboxGameProcessGeneration generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (generation is not WindowsXboxGameProcessGeneration process)
            throw new ArgumentException("The process generation was not created by the Windows identity probe.", nameof(generation));

        var imageSucceeded = TryQueryImagePath(process.Handle, out var runningProcessPath, out var imageError);
        var packageFullNameQuery = QueryProcessString(process.Handle, GetPackageFullName);
        var packageFamilyNameQuery = QueryProcessString(process.Handle, GetPackageFamilyName);
        var applicationUserModelIdQuery = QueryProcessString(process.Handle, GetApplicationUserModelId);
        var packageId = QueryPackageId(process.Handle);
        IReadOnlyList<FrontendXboxSessionDiagnosticPackagePath> paths = packageFullNameQuery.ResultCode == ErrorSuccess && !string.IsNullOrWhiteSpace(packageFullNameQuery.Value)
            ? QueryPackagePaths(packageFullNameQuery.Value)
            : [];
        var configLocationResolution = packageFullNameQuery.ResultCode == ErrorSuccess && !string.IsNullOrWhiteSpace(packageFullNameQuery.Value)
            ? XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
                packageFullNameQuery.Value,
                static (userSecurityId, packageFullName) =>
                {
                    var package = new PackageManager().FindPackageForUser(userSecurityId, packageFullName);
                    return package is null
                        ? null
                        : new XboxGamePackageConfigMetadata(
                            () => package.EffectiveLocation?.Path,
                            () => package.InstalledLocation?.Path);
                })
            : new XboxGamePackageConfigLocationResolution([], null);
        return await XboxGameProcessIdentityEvaluator.InspectAsync(
            generation,
            new XboxGameProcessIdentityEvidence(
                imageSucceeded ? (int)ErrorSuccess : imageError,
                imageSucceeded ? runningProcessPath : null,
                packageFullNameQuery.ResultCode,
                packageFullNameQuery.Value,
                packageFamilyNameQuery.ResultCode,
                packageFamilyNameQuery.Value,
                applicationUserModelIdQuery.Value,
                applicationUserModelIdQuery.ResultCode,
                packageId.Name,
                packageId.Publisher,
                packageId.PublisherId,
                packageId.ResourceId,
                packageId.Architecture,
                packageId.Version,
                configLocationResolution.Locations,
                configLocationResolution.FailureReason,
                paths),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool TryQueryImagePath(SafeProcessHandle process, out string path, out int error)
    {
        var buffer = new StringBuilder(MaximumNativeStringLength);
        var length = (uint)buffer.Capacity;
        if (QueryFullProcessImageNameW(process, 0, buffer, ref length))
        {
            path = buffer.ToString(0, checked((int)length));
            error = 0;
            return true;
        }

        path = string.Empty;
        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static (string? Value, int ResultCode) QueryProcessString(SafeProcessHandle process, ProcessStringQuery query)
    {
        uint length = 0;
        var result = query(process, ref length, null);
        if (result != ErrorInsufficientBuffer || length is 0 or > MaximumNativeStringLength)
            return (null, result);

        var value = new StringBuilder(checked((int)length));
        result = query(process, ref length, value);
        return result == ErrorSuccess ? (value.ToString().TrimEnd('\0'), result) : (null, result);
    }

    private static PackageIdentityFields QueryPackageId(SafeProcessHandle process)
    {
        uint length = 0;
        var result = GetPackageId(process, ref length, 0);
        if (result != ErrorInsufficientBuffer || length == 0 || length > 1024 * 1024)
            return new(null, null, null, null, null, null, result);

        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            result = GetPackageId(process, ref length, buffer);
            if (result != ErrorSuccess)
                return new(null, null, null, null, null, null, result);

            var id = Marshal.PtrToStructure<PackageIdNative>(buffer);
            return ProjectPackageId(id, result);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static PackageIdentityFields ProjectPackageId(PackageIdNative id, int resultCode)
    {
        var architecture = id.ProcessorArchitecture switch
        {
            0 => "X86",
            5 => "Arm",
            9 => "X64",
            11 => "Neutral",
            12 => "Arm64",
            _ => id.ProcessorArchitecture.ToString(),
        };
        var version = $"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}";
        return new(
            Marshal.PtrToStringUni(id.Name),
            Marshal.PtrToStringUni(id.Publisher),
            Marshal.PtrToStringUni(id.PublisherId),
            Marshal.PtrToStringUni(id.ResourceId),
            architecture,
            version,
            resultCode);
    }

    private static IReadOnlyList<FrontendXboxSessionDiagnosticPackagePath> QueryPackagePaths(string packageFullName)
    {
        var result = new List<FrontendXboxSessionDiagnosticPackagePath>(PackagePathTypes.Length);
        foreach (var (name, value) in PackagePathTypes)
        {
            try
            {
                uint length = 0;
                var code = GetPackagePathByFullName2(packageFullName, value, ref length, null);
                if (code != ErrorInsufficientBuffer || length is 0 or > MaximumNativeStringLength)
                {
                    result.Add(new(name, code, null));
                    continue;
                }

                var path = new StringBuilder(checked((int)length));
                code = GetPackagePathByFullName2(packageFullName, value, ref length, path);
                result.Add(new(name, code, code == ErrorSuccess ? path.ToString() : null));
            }
            catch (EntryPointNotFoundException)
            {
                result.Add(new(name, checked((int)ErrorProcNotFound), null));
            }
        }

        return result;
    }

    internal sealed record PackageIdentityFields(
        string? Name,
        string? Publisher,
        string? PublisherId,
        string? ResourceId,
        string? Architecture,
        string? Version,
        int ResultCode);

    private delegate int ProcessStringQuery(SafeProcessHandle process, ref uint length, StringBuilder? value);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PackageVersion
    {
        public ushort Revision;
        public ushort Build;
        public ushort Minor;
        public ushort Major;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PackageIdNative
    {
        public uint Reserved;
        public uint ProcessorArchitecture;
        public PackageVersion Version;
        public nint Name;
        public nint Publisher;
        public nint ResourceId;
        public nint PublisherId;
    }

    private sealed class WindowsXboxGameProcessGeneration : IXboxGameProcessGeneration
    {
        private readonly EventWaitHandle _processWaitHandle;
        private readonly RegisteredWaitHandle _registeredWait;
        private int _disposed;

        internal WindowsXboxGameProcessGeneration(uint processId, long creationTime, SafeProcessHandle handle)
        {
            ProcessId = processId;
            Key = new XboxGameProcessGenerationKey(processId, creationTime);
            Handle = handle;
            _processWaitHandle = CreateWaitHandle(handle);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _processWaitHandle,
                static (state, _) => ((WindowsXboxGameProcessGeneration)state!).OnExited(),
                this,
                Timeout.Infinite,
                executeOnlyOnce: true);
        }

        public uint ProcessId { get; }
        public XboxGameProcessGenerationKey Key { get; }
        public event Action<IXboxGameProcessGeneration>? Exited;

        internal SafeProcessHandle Handle { get; }

        public bool IsSignaled => WaitForSingleObject(Handle, 0) == WaitObject0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _registeredWait.Unregister(null);
            _processWaitHandle.Dispose();
            Handle.Dispose();
            Exited = null;
        }

        private void OnExited()
        {
            if (Volatile.Read(ref _disposed) == 0)
                Exited?.Invoke(this);
        }

        private static EventWaitHandle CreateWaitHandle(SafeProcessHandle process)
        {
            var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
            waitHandle.SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
            return waitHandle;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out FileTime creationTime, out FileTime exitTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder imageName, ref uint size);

    [DllImport("kernel32.dll", EntryPoint = "GetPackageFullName", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(SafeProcessHandle process, ref uint length, StringBuilder? value);

    [DllImport("kernel32.dll", EntryPoint = "GetPackageFamilyName", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder? value);

    [DllImport("kernel32.dll", EntryPoint = "GetApplicationUserModelId", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? value);

    [DllImport("kernel32.dll", EntryPoint = "GetPackageId")]
    private static extern int GetPackageId(SafeProcessHandle process, ref uint length, nint buffer);

    [DllImport("kernelbase.dll", EntryPoint = "GetPackagePathByFullName2", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName2(string packageFullName, uint pathType, ref uint length, StringBuilder? path);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
