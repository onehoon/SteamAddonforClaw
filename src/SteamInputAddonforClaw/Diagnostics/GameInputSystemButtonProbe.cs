using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Diagnostics;

internal readonly record struct GameInputSystemButtonProbeNativeEvent(
    ulong TimestampMicroseconds,
    uint CurrentButtons,
    uint PreviousButtons,
    bool DeviceInfoSucceeded,
    string? DeviceInfoFailure,
    ushort? VendorId,
    ushort? ProductId,
    string? DisplayName,
    string? PnpPath,
    Guid? ContainerId,
    byte[]? DeviceId,
    byte[]? DeviceRootId,
    int? SupportedInput,
    int? SupportedSystemButtons);

internal interface IGameInputSystemButtonProbeSession
{
    bool Stop();
}

internal interface IGameInputSystemButtonProbeSessionFactory
{
    IGameInputSystemButtonProbeSession Register(Action<GameInputSystemButtonProbeNativeEvent> callback);
}

internal sealed class GameInputSystemButtonProbe
{
    private const uint GuideButton = 0x00000001;
    private const uint ShareButton = 0x00000002;
    private readonly object _lifecycleGate = new();
    private readonly object _snapshotGate = new();
    private readonly IGameInputSystemButtonProbeSessionFactory _sessionFactory;
    private readonly Func<bool> _debugLoggingEnabled;
    private FrontendGameInputSystemButtonProbeSnapshot _snapshot =
        new(true, FrontendGameInputSystemButtonProbeState.Ready, "Ready", 0, null);
    private IGameInputSystemButtonProbeSession? _session;
    private long _eventCount;
    private int _acceptEvents;

    internal GameInputSystemButtonProbe(
        Func<bool>? debugLoggingEnabled = null,
        IGameInputSystemButtonProbeSessionFactory? sessionFactory = null)
    {
        _debugLoggingEnabled = debugLoggingEnabled ?? (() => AppLog.IsEnabled(AppLogLevel.Debug));
        _sessionFactory = sessionFactory ?? new NativeGameInputSystemButtonProbeSessionFactory();
    }

    internal FrontendGameInputSystemButtonProbeSnapshot Capture()
    {
        lock (_snapshotGate) return _snapshot;
    }

    internal FrontendGameInputSystemButtonProbeSnapshot Start()
    {
        lock (_lifecycleGate)
        {
            var current = Capture();
            if (current.State == FrontendGameInputSystemButtonProbeState.Running || _session is not null)
                return current;

            AppLog.Info("GameInput.SystemButton", "ProbeStartRequested");
            if (!_debugLoggingEnabled())
            {
                var retryable = new FrontendGameInputSystemButtonProbeSnapshot(
                    true,
                    FrontendGameInputSystemButtonProbeState.Ready,
                    "Enable Debug logging before starting this diagnostic.",
                    current.EventCount,
                    current.LastEvent);
                Publish(retryable);
                AppLog.Info("GameInput.SystemButton", "ProbeUnavailable", ("Reason", "DebugLoggingRequired"));
                return retryable;
            }

            Volatile.Write(ref _acceptEvents, 1);
            try
            {
                _session = _sessionFactory.Register(OnNativeEvent);
                FrontendGameInputSystemButtonProbeSnapshot running;
                lock (_snapshotGate)
                {
                    running = new(true, FrontendGameInputSystemButtonProbeState.Running, "Running", _eventCount, _snapshot.LastEvent);
                    _snapshot = running;
                }
                AppLog.Info("GameInput.SystemButton", "ProbeStarted",
                    ("FocusPolicy", "EnableBackgroundGuideButton|EnableBackgroundShareButton"),
                    ("ButtonFilter", "Guide|Share"));
                return running;
            }
            catch (GameInputSystemButtonProbeStartException exception)
            {
                Volatile.Write(ref _acceptEvents, 0);
                var failed = exception.Unavailable
                    ? FrontendGameInputSystemButtonProbeSnapshot.Unavailable(exception.Message)
                    : new FrontendGameInputSystemButtonProbeSnapshot(
                        true, FrontendGameInputSystemButtonProbeState.Failed, exception.Message, _eventCount, Capture().LastEvent);
                Publish(failed);
                AppLog.Info("GameInput.SystemButton", exception.Unavailable ? "ProbeUnavailable" : "ProbeFailed",
                    ("Reason", exception.Reason));
                return failed;
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _acceptEvents, 0);
                var failed = new FrontendGameInputSystemButtonProbeSnapshot(
                    true, FrontendGameInputSystemButtonProbeState.Failed,
                    $"GameInput probe start failed: {exception.GetType().Name}.", _eventCount, Capture().LastEvent);
                Publish(failed);
                AppLog.Warn("GameInput.SystemButton", "ProbeFailed", exception,
                    ("Reason", exception.GetType().Name));
                return failed;
            }
        }
    }

    internal FrontendGameInputSystemButtonProbeSnapshot Stop()
    {
        lock (_lifecycleGate)
        {
            var current = Capture();
            if (current.State != FrontendGameInputSystemButtonProbeState.Running || _session is null)
                return current;

            Volatile.Write(ref _acceptEvents, 0);
            var session = _session;
            bool unregistered;
            try { unregistered = session.Stop(); }
            catch (Exception exception)
            {
                AppLog.Warn("GameInput.SystemButton", "Probe callback teardown failed; native lifetime was retained.",
                    exception, ("Reason", exception.GetType().Name));
                unregistered = false;
            }
            if (!unregistered)
            {
                var failed = new FrontendGameInputSystemButtonProbeSnapshot(
                    true, FrontendGameInputSystemButtonProbeState.Failed,
                    "GameInput callback could not be unregistered; native lifetime was retained.",
                    _eventCount, current.LastEvent);
                Publish(failed);
                AppLog.Error("GameInput.SystemButton", "ProbeFailed", null,
                    ("Reason", "UnregisterCallbackFailed"));
                return failed;
            }

            _session = null;
            var stoppedSnapshot = new FrontendGameInputSystemButtonProbeSnapshot(
                true, FrontendGameInputSystemButtonProbeState.Stopped, "Stopped", _eventCount, current.LastEvent);
            Publish(stoppedSnapshot);
            AppLog.Info("GameInput.SystemButton", "ProbeStopped", ("EventCount", _eventCount));
            return stoppedSnapshot;
        }
    }

    internal void Dispose() => Stop();

    private void OnNativeEvent(GameInputSystemButtonProbeNativeEvent nativeEvent)
    {
        if (Volatile.Read(ref _acceptEvents) == 0) return;

        FrontendGameInputSystemButtonEventSnapshot mapped;
        long count;
        lock (_snapshotGate)
        {
            if (Volatile.Read(ref _acceptEvents) == 0) return;
            count = ++_eventCount;
            mapped = MapEvent(count, nativeEvent);
            _snapshot = _snapshot with { EventCount = count, LastEvent = mapped };
        }

        AppLog.Debug("GameInput.SystemButton", "SystemButtonChanged",
            ("Sequence", mapped.Sequence),
            ("TimestampUs", mapped.TimestampMicroseconds),
            ("CurrentButtons", $"0x{mapped.CurrentButtonsRaw:X8}"),
            ("PreviousButtons", $"0x{mapped.PreviousButtonsRaw:X8}"),
            ("GuidePressed", mapped.GuidePressed),
            ("GuideReleased", mapped.GuideReleased),
            ("SharePressed", mapped.SharePressed),
            ("ShareReleased", mapped.ShareReleased),
            ("VendorId", mapped.VendorId),
            ("ProductId", mapped.ProductId),
            ("DisplayName", mapped.DisplayName),
            ("PnpPath", mapped.PnpPath),
            ("ContainerId", mapped.ContainerId),
            ("DeviceId", mapped.DeviceId),
            ("DeviceRootId", mapped.DeviceRootId),
            ("SupportedInput", mapped.SupportedInput),
            ("SupportedSystemButtons", mapped.SupportedSystemButtons),
            ("SupportedInputRaw", mapped.SupportedInputRaw),
            ("SupportedSystemButtonsRaw", mapped.SupportedSystemButtonsRaw),
            ("DeviceInfoSucceeded", mapped.DeviceInfoSucceeded),
            ("DeviceInfoFailure", mapped.DeviceInfoFailure));
    }

    internal static FrontendGameInputSystemButtonEventSnapshot MapEvent(
        long sequence,
        GameInputSystemButtonProbeNativeEvent nativeEvent) =>
        new(
            sequence,
            nativeEvent.TimestampMicroseconds,
            nativeEvent.CurrentButtons,
            nativeEvent.PreviousButtons,
            IsPressed(nativeEvent.CurrentButtons, nativeEvent.PreviousButtons, GuideButton),
            IsReleased(nativeEvent.CurrentButtons, nativeEvent.PreviousButtons, GuideButton),
            IsPressed(nativeEvent.CurrentButtons, nativeEvent.PreviousButtons, ShareButton),
            IsReleased(nativeEvent.CurrentButtons, nativeEvent.PreviousButtons, ShareButton),
            nativeEvent.DeviceInfoSucceeded,
            EmptyToNull(nativeEvent.DeviceInfoFailure),
            nativeEvent.VendorId is ushort vendorId ? $"0x{vendorId:X4}" : null,
            nativeEvent.ProductId is ushort productId ? $"0x{productId:X4}" : null,
            EmptyToNull(nativeEvent.DisplayName),
            EmptyToNull(nativeEvent.PnpPath),
            nativeEvent.ContainerId is Guid containerId ? containerId.ToString("B").ToUpperInvariant() : null,
            FormatDeviceId(nativeEvent.DeviceId),
            FormatDeviceId(nativeEvent.DeviceRootId),
            nativeEvent.SupportedInput,
            nativeEvent.SupportedInput is int supportedInput ? FormatSupportedInput(supportedInput) : null,
            nativeEvent.SupportedSystemButtons,
            nativeEvent.SupportedSystemButtons is int supportedButtons ? FormatSupportedSystemButtons(supportedButtons) : null);

    internal static string? FormatDeviceId(byte[]? value) =>
        value is null || value.All(static item => item == 0) ? null : Convert.ToHexString(value);

    internal static string FormatSupportedInput(int value)
    {
        var names = new List<string>();
        AddFlag(names, value, 0x0000000E, "Controller");
        AddFlag(names, value, 0x00040000, "Gamepad");
        AddFlag(names, value, 0x00000010, "Keyboard");
        AddFlag(names, value, 0x00000020, "Mouse");
        AddFlag(names, value, 0x00000040, "Sensors");
        AddFlag(names, value, 0x00010000, "ArcadeStick");
        AddFlag(names, value, 0x00020000, "FlightStick");
        AddFlag(names, value, 0x00080000, "RacingWheel");
        AddFlag(names, value, 0x01000000, "UiNavigation");
        return names.Count == 0 ? $"0x{unchecked((uint)value):X8}" : string.Join('|', names);
    }

    internal static string FormatSupportedSystemButtons(int value)
    {
        var names = new List<string>();
        AddFlag(names, value, (int)GuideButton, "Guide");
        AddFlag(names, value, (int)ShareButton, "Share");
        return names.Count == 0 ? $"0x{unchecked((uint)value):X8}" : string.Join('|', names);
    }

    private static bool IsPressed(uint current, uint previous, uint button) =>
        (current & button) != 0 && (previous & button) == 0;

    private static bool IsReleased(uint current, uint previous, uint button) =>
        (current & button) == 0 && (previous & button) != 0;

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void AddFlag(List<string> names, int value, int flag, string name)
    {
        if ((value & flag) == flag) names.Add(name);
    }

    private void Publish(FrontendGameInputSystemButtonProbeSnapshot snapshot)
    {
        lock (_snapshotGate) _snapshot = snapshot;
    }
}

internal sealed class GameInputSystemButtonProbeStartException(bool unavailable, string reason, string message, Exception? inner = null)
    : Exception(message, inner)
{
    internal bool Unavailable { get; } = unavailable;
    internal string Reason { get; } = reason;
}

internal sealed class NativeGameInputSystemButtonProbeSessionFactory : IGameInputSystemButtonProbeSessionFactory
{
    internal const int GameInputDefaultFocusPolicy = 0x00000000;
    internal const int GameInputEnableBackgroundGuideButton = 0x00000080;
    internal const int GameInputEnableBackgroundShareButton = 0x00000100;
    private const int GameInputSystemButtonGuide = 0x00000001;
    private const int GameInputSystemButtonShare = 0x00000002;

    public IGameInputSystemButtonProbeSession Register(Action<GameInputSystemButtonProbeNativeEvent> callback)
    {
        IGameInput? gameInput = null;
        var focusPolicyApplied = false;
        try
        {
            var createResult = GameInputCreate(out gameInput);
            if (createResult < 0 || gameInput is null)
                throw new GameInputSystemButtonProbeStartException(
                    true, $"GameInputCreate:0x{unchecked((uint)createResult):X8}",
                    "GameInput is unavailable or could not be created.");

            try
            {
                gameInput.SetFocusPolicy(
                    GameInputEnableBackgroundGuideButton | GameInputEnableBackgroundShareButton);
                focusPolicyApplied = true;
            }
            catch (Exception exception)
            {
                throw new GameInputSystemButtonProbeStartException(
                    false, $"SetFocusPolicy:{exception.GetType().Name}",
                    "The non-exclusive background Guide/Share policy could not be established.", exception);
            }

            return NativeGameInputSystemButtonProbeSession.Create(gameInput, callback);
        }
        catch (DllNotFoundException exception)
        {
            Release(gameInput);
            throw new GameInputSystemButtonProbeStartException(
                true, "GameInputDllMissing", "GameInput is not installed on this system.", exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            Release(gameInput);
            throw new GameInputSystemButtonProbeStartException(
                true, "GameInputEntryPointMissing", "The installed GameInput API does not provide the required system-button callback.", exception);
        }
        catch (GameInputSystemButtonProbeStartException)
        {
            if (focusPolicyApplied)
            {
                try { gameInput?.SetFocusPolicy(GameInputDefaultFocusPolicy); } catch { }
            }
            Release(gameInput);
            throw;
        }
        catch (Exception exception)
        {
            if (focusPolicyApplied)
            {
                try { gameInput?.SetFocusPolicy(GameInputDefaultFocusPolicy); } catch { }
            }
            Release(gameInput);
            throw new GameInputSystemButtonProbeStartException(
                false, $"RegisterSystemButtonCallback:{exception.GetType().Name}",
                "The GameInput system-button callback could not be registered.", exception);
        }
    }

    private static void Release(IGameInput? gameInput)
    {
        if (gameInput is null || !Marshal.IsComObject(gameInput)) return;
        try { Marshal.ReleaseComObject(gameInput); } catch { }
    }

    private sealed class NativeGameInputSystemButtonProbeSession : IGameInputSystemButtonProbeSession
    {
        private readonly IGameInput _gameInput;
        private readonly GameInputSystemButtonCallback _callback;
        private readonly ulong _callbackToken;
        private bool _stopAttempted;
        private bool _stopped;

        private NativeGameInputSystemButtonProbeSession(
            IGameInput gameInput,
            GameInputSystemButtonCallback callback,
            ulong callbackToken)
        {
            _gameInput = gameInput;
            _callback = callback;
            _callbackToken = callbackToken;
        }

        internal static NativeGameInputSystemButtonProbeSession Create(
            IGameInput gameInput,
            Action<GameInputSystemButtonProbeNativeEvent> publish)
        {
            GameInputSystemButtonCallback callback = (_, _, device, timestamp, current, previous) =>
            {
                var identity = CopyIdentity(device);
                publish(new(timestamp, unchecked((uint)current), unchecked((uint)previous),
                    identity.Succeeded, identity.Failure, identity.VendorId, identity.ProductId,
                    identity.DisplayName, identity.PnpPath, identity.ContainerId,
                    identity.DeviceId, identity.DeviceRootId, identity.SupportedInput,
                    identity.SupportedSystemButtons));
            };

            var registerResult = gameInput.RegisterSystemButtonCallback(
                null,
                GameInputSystemButtonGuide | GameInputSystemButtonShare,
                IntPtr.Zero,
                callback,
                out var callbackToken);
            if (registerResult < 0)
                throw new GameInputSystemButtonProbeStartException(
                    false, $"RegisterSystemButtonCallback:0x{unchecked((uint)registerResult):X8}",
                    "The GameInput system-button callback could not be registered.");

            return new NativeGameInputSystemButtonProbeSession(gameInput, callback, callbackToken);
        }

        public bool Stop()
        {
            if (_stopped) return true;
            if (_stopAttempted) return false;
            _stopAttempted = true;

            if (!_gameInput.UnregisterCallback(_callbackToken)) return false;

            try { _gameInput.SetFocusPolicy(GameInputDefaultFocusPolicy); }
            catch (Exception exception)
            {
                AppLog.Warn("GameInput.SystemButton", "Default focus policy could not be restored after callback teardown.",
                    exception, ("Reason", exception.GetType().Name));
            }

            Release(_gameInput);
            GC.KeepAlive(_callback);
            _stopped = true;
            return true;
        }
    }

    private static DeviceIdentity CopyIdentity(IGameInputDevice? device)
    {
        try
        {
            if (device is null)
                return DeviceIdentity.Failed("CallbackDeviceNull");

            var result = device.GetDeviceInfo(out var infoPointer);
            if (result < 0 || infoPointer == IntPtr.Zero)
                return DeviceIdentity.Failed($"GetDeviceInfo:0x{unchecked((uint)result):X8}");

            var info = Marshal.PtrToStructure<NativeGameInputDeviceInfo>(infoPointer);
            return new(true, null, info.VendorId, info.ProductId,
                ReadUtf8(info.DisplayName), ReadUtf8(info.PnpPath),
                info.ContainerId == Guid.Empty ? null : info.ContainerId,
                info.DeviceId, info.DeviceRootId, info.SupportedInput, info.SupportedSystemButtons);
        }
        catch (Exception exception)
        {
            return DeviceIdentity.Failed($"GetDeviceInfo:{exception.GetType().Name}");
        }
    }

    private static string? ReadUtf8(IntPtr value)
    {
        if (value == IntPtr.Zero) return null;
        var text = Marshal.PtrToStringUTF8(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeGameInputDeviceInfo
    {
        public ushort VendorId;
        public ushort ProductId;
        public ushort RevisionNumber;
        public NativeGameInputUsage Usage;
        public NativeGameInputVersion HardwareVersion;
        public NativeGameInputVersion FirmwareVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)] public byte[] DeviceId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)] public byte[] DeviceRootId;
        public int DeviceFamily;
        public int SupportedInput;
        public int SupportedRumbleMotors;
        public int SupportedSystemButtons;
        public Guid ContainerId;
        public IntPtr DisplayName;
        public IntPtr PnpPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeGameInputUsage { public ushort Page; public ushort Id; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeGameInputVersion { public ushort Major; public ushort Minor; public ushort Build; public ushort Revision; }

    private readonly record struct DeviceIdentity(
        bool Succeeded,
        string? Failure,
        ushort? VendorId,
        ushort? ProductId,
        string? DisplayName,
        string? PnpPath,
        Guid? ContainerId,
        byte[]? DeviceId,
        byte[]? DeviceRootId,
        int? SupportedInput,
        int? SupportedSystemButtons)
    {
        internal static DeviceIdentity Failed(string reason) =>
            new(false, reason, null, null, null, null, null, null, null, null, null);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GameInputSystemButtonCallback(
        ulong callbackToken,
        IntPtr context,
        [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device,
        ulong timestamp,
        int currentButtons,
        int previousButtons);

    [ComImport]
    [Guid("20EFC1C7-5D9A-43BA-B26F-B807FA48609C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGameInput
    {
        [PreserveSig] ulong GetCurrentTimestamp();
        [PreserveSig] int GetCurrentReading(int inputKind, [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, out IntPtr reading);
        [PreserveSig] int GetNextReading(IntPtr referenceReading, int inputKind, [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, out IntPtr reading);
        [PreserveSig] int GetPreviousReading(IntPtr referenceReading, int inputKind, [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, out IntPtr reading);
        [PreserveSig] int RegisterReadingCallback([MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, int inputKind, IntPtr context, IntPtr callbackFunc, out ulong callbackToken);
        [PreserveSig] int RegisterDeviceCallback([MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, int inputKind, int statusFilter, int enumerationKind, IntPtr context, IntPtr callbackFunc, out ulong callbackToken);
        [PreserveSig] int RegisterSystemButtonCallback([MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, int buttonFilter, IntPtr context, [MarshalAs(UnmanagedType.FunctionPtr)] GameInputSystemButtonCallback callbackFunc, out ulong callbackToken);
        [PreserveSig] int RegisterKeyboardLayoutCallback([MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, IntPtr context, IntPtr callbackFunc, out ulong callbackToken);
        [PreserveSig] void StopCallback(ulong callbackToken);
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)]
        bool UnregisterCallback(ulong callbackToken);
        [PreserveSig] int CreateDispatcher(out IntPtr dispatcher);
        [PreserveSig] int FindDeviceFromId(IntPtr deviceId, out IntPtr device);
        [PreserveSig] int FindDeviceFromPlatformString(IntPtr value, out IntPtr device);
        [PreserveSig] void SetFocusPolicy(int policy);
    }

    [ComImport]
    [Guid("63E2F38B-A399-4275-8AE7-D4C6E524D12A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGameInputDevice
    {
        [PreserveSig] int GetDeviceInfo(out IntPtr info);
        [PreserveSig] int GetHapticInfo(IntPtr info);
        [PreserveSig] int GetDeviceStatus();
    }

    [DllImport("GameInput.dll", ExactSpelling = true)]
    private static extern int GameInputCreate([MarshalAs(UnmanagedType.Interface)] out IGameInput? gameInput);
}
