using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.GameDetection.Windows;

internal readonly record struct GameProcessGenerationKey(uint ProcessId, long CreationTime);

internal readonly record struct GameProcessImageQueryResult(bool Succeeded, string? ImagePath, int ErrorCode);

internal interface IGameProcessGeneration : IDisposable
{
    uint ProcessId { get; }
    GameProcessGenerationKey Key { get; }
    bool IsSignaled { get; }

    GameProcessImageQueryResult QueryImagePath();

    event Action<IGameProcessGeneration>? Exited;
}

internal sealed record GameProcessOpenResult(IGameProcessGeneration? Generation, int ErrorCode);

internal interface IWindowsGameProcessSource
{
    GameProcessOpenResult Open(uint processId);
}

internal sealed class WindowsGameProcessSource : IWindowsGameProcessSource
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;

    public GameProcessOpenResult Open(uint processId)
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
        return new(new WindowsGameProcessGeneration(processId, creationTime, handle), 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out FileTime creationTime, out FileTime exitTime, out FileTime kernelTime, out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }
}

internal sealed class WindowsGameProcessGeneration : IGameProcessGeneration
{
    private const uint WaitObject0 = 0;
    private const int MaximumNativeStringLength = 32 * 1024;

    private readonly EventWaitHandle _processWaitHandle;
    private readonly RegisteredWaitHandle _registeredWait;
    private readonly object _imagePathSync = new();
    private GameProcessImageQueryResult? _imagePathQueryResult;
    private int _disposed;

    internal WindowsGameProcessGeneration(uint processId, long creationTime, SafeProcessHandle handle)
    {
        ProcessId = processId;
        Key = new GameProcessGenerationKey(processId, creationTime);
        Handle = handle;
        _processWaitHandle = CreateWaitHandle(handle);
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _processWaitHandle,
            static (state, _) => ((WindowsGameProcessGeneration)state!).OnExited(),
            this,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    public uint ProcessId { get; }
    public GameProcessGenerationKey Key { get; }
    public event Action<IGameProcessGeneration>? Exited;

    internal SafeProcessHandle Handle { get; }

    public bool IsSignaled => WaitForSingleObject(Handle, 0) == WaitObject0;

    public GameProcessImageQueryResult QueryImagePath()
    {
        lock (_imagePathSync)
        {
            _imagePathQueryResult ??= QueryImagePath(Handle);
            return _imagePathQueryResult.Value;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _registeredWait.Unregister(null);
        _processWaitHandle.Dispose();
        Handle.Dispose();
        Exited = null;
    }

    internal static GameProcessImageQueryResult QueryImagePath(SafeProcessHandle process)
    {
        var buffer = new StringBuilder(MaximumNativeStringLength);
        var length = (uint)buffer.Capacity;
        if (QueryFullProcessImageNameW(process, 0, buffer, ref length))
            return new(true, buffer.ToString(0, checked((int)length)), 0);

        return new(false, null, Marshal.GetLastWin32Error());
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

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder imageName, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
