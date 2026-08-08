using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Steam;

public sealed class SteamRunningAppIdRegistrySource : IRunningAppIdSource, IDisposable
{
    private const string SteamRegistryPath = "Software\\Valve\\Steam";
    private const string ValveRegistryPath = "Software\\Valve";
    private const string SoftwareRegistryPath = "Software";
    private const string RunningAppIdValueName = "RunningAppID";
    private const uint RegNotifyChangeName = 0x0000_0001;
    private const uint RegNotifyChangeLastSet = 0x0000_0004;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _watchTask;
    private bool _disposed;

    public SteamRunningAppIdRegistrySource()
    {
        _watchTask = Task.Run(WatchForChangesAsync);
    }

    public event EventHandler? Changed;

    public uint GetRunningAppId()
    {
        if (_disposed)
        {
            return 0;
        }

        try
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(SteamRegistryPath, writable: false);
            var rawValue = steamKey?.GetValue(RunningAppIdValueName);
            var runningAppId = ConvertRegistryValueToRunningAppId(rawValue);
            AppLog.Trace(
                "Steam",
                "RunningAppID registry value read.",
                ("RawType", rawValue?.GetType().Name ?? "null"),
                ("RawValue", rawValue),
                ("RunningAppID", runningAppId),
                ("Interpretation", rawValue is int ? "DwordBitPattern" : "SupportedRegistryRepresentation"));
            return runningAppId;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ObjectDisposedException)
        {
            AppLog.Warn(
                "Steam",
                "RunningAppID registry read failed.",
                exception,
                ("Action", "AssumeInactive"),
                ("RunningAppID", 0),
                ("Reason", "RegistryReadException"));
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellationTokenSource.Cancel();

        try
        {
            _watchTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    internal static uint ConvertRegistryValueToRunningAppId(object? value)
    {
        return value switch
        {
            int appId => unchecked((uint)appId),
            uint appId => appId,
            long appId when appId is >= 0 and <= uint.MaxValue => (uint)appId,
            ulong appId when appId <= uint.MaxValue => (uint)appId,
            _ => 0
        };
    }

    private Task WatchForChangesAsync()
    {
        try
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                using var registryKey = OpenKeyToWatch(out var changeFilter);
                if (registryKey is null || !WaitForRegistryChange(registryKey.Handle, changeFilter, _cancellationTokenSource.Token))
                {
                    return Task.CompletedTask;
                }

                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ObjectDisposedException)
        {
        }

        return Task.CompletedTask;
    }

    private static RegistryKey? OpenKeyToWatch(out uint changeFilter)
    {
        var steamKey = Registry.CurrentUser.OpenSubKey(SteamRegistryPath, writable: false);
        if (steamKey is not null)
        {
            changeFilter = RegNotifyChangeLastSet;
            return steamKey;
        }

        var valveKey = Registry.CurrentUser.OpenSubKey(ValveRegistryPath, writable: false);
        if (valveKey is not null)
        {
            changeFilter = RegNotifyChangeName;
            return valveKey;
        }

        changeFilter = RegNotifyChangeName;
        return Registry.CurrentUser.OpenSubKey(SoftwareRegistryPath, writable: false);
    }

    private static bool WaitForRegistryChange(SafeRegistryHandle registryKeyHandle, uint changeFilter, CancellationToken cancellationToken)
    {
        using var registryChanged = new EventWaitHandle(false, EventResetMode.AutoReset);
        var result = RegNotifyChangeKeyValue(
            registryKeyHandle,
            watchSubtree: false,
            changeFilter,
            registryChanged.SafeWaitHandle,
            asynchronous: true);

        if (result != 0)
        {
            return false;
        }

        WaitHandle.WaitAny([registryChanged, cancellationToken.WaitHandle]);
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle registryKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter,
        SafeWaitHandle eventHandle,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
