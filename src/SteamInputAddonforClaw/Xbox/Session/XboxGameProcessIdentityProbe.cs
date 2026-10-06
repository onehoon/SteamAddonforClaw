using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Xbox;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.Xbox.Session;

internal readonly record struct XboxGameProcessGenerationKey(uint ProcessId, long CreationTime);

internal interface IXboxGameProcessGeneration : IDisposable
{
    uint ProcessId { get; }
    XboxGameProcessGenerationKey Key { get; }
    bool IsSignaled { get; }
    event Action<IXboxGameProcessGeneration>? Exited;
}

internal sealed record XboxGameProcessOpenResult(IXboxGameProcessGeneration? Generation, int ErrorCode);

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
    private const int MaximumNativeStringLength = 32 * 1024;

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
        var configMetadata = packageFullNameQuery.ResultCode == ErrorSuccess && !string.IsNullOrWhiteSpace(packageFullNameQuery.Value)
            ? XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
                packageFullNameQuery.Value,
                static (userSecurityId, packageFullName) =>
                {
                    var package = new PackageManager().FindPackageForUser(userSecurityId, packageFullName);
                    return package is null
                        ? null
                        : new XboxGamePackageConfigMetadata(
                            () => package.EffectiveLocation?.Path,
                            () => package.InstalledLocation?.Path)
                        {
                            DisplayName = package.DisplayName,
                            Name = package.Id.Name,
                        };
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
                configMetadata.PackageDisplayName,
                configMetadata.PackageName,
                configMetadata.Locations,
                configMetadata.FailureReason),
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

    private delegate int ProcessStringQuery(SafeProcessHandle process, ref uint length, StringBuilder? value);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
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

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
