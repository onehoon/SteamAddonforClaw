using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Feedback;

/// <summary>Full1902 production rumble: one presentation-scoped adapter converts the active virtual
/// presentation's already-decoded (Xbox360) or raw (SteamDeck) host output into a semantic
/// <see cref="TwoMotorRumble"/> and writes it to the one shared physical <see cref="IPhysicalRumbleSink"/>.
///
/// These are deliberately NOT authority managers: they own only a disposable native-callback lifetime
/// and do bounded local work on the callback thread. Lifetime arm/disarm ordering belongs to
/// <c>MsiClawAddonPresentation</c>'s existing gate. The managed delegate stays rooted by
/// <see cref="CanonicalViiperNativeApi"/>'s own callback-root map for exactly as long as native may
/// call it, so a failed native clear here does not create an unrooted callback.</summary>
internal sealed class Xbox360RumbleFeedbackBridge : IDisposable
{
    private readonly IPhysicalRumbleSink _sink;
    private readonly Func<Xbox360RumbleCallback?, bool> _setNativeCallback;
    private readonly Xbox360RumbleCallback _callback;
    private int _disposed;

    private Xbox360RumbleFeedbackBridge(IPhysicalRumbleSink sink, Func<Xbox360RumbleCallback?, bool> setNativeCallback)
    {
        _sink = sink;
        _setNativeCallback = setNativeCallback;
        _callback = OnRumble;
    }

    /// <summary>8-bit to 16-bit full-range expansion. Preserves exact 8-bit magnitude through the
    /// physical sink's <c>&gt;&gt; 8</c> conversion: 0-&gt;0, 1-&gt;257 (&gt;&gt;8==1), 255-&gt;65535 (&gt;&gt;8==255).</summary>
    internal static ushort Expand(byte value) => (ushort)(value * 257);

    /// <summary>Registers the native Xbox360 rumble callback. Returns <see langword="null"/> (rumble
    /// unavailable for this presentation) if registration is not confirmed -- the caller keeps the
    /// controller presentation healthy regardless.</summary>
    internal static Xbox360RumbleFeedbackBridge? TryArm(IPhysicalRumbleSink sink, Func<Xbox360RumbleCallback?, bool> setNativeCallback)
    {
        var bridge = new Xbox360RumbleFeedbackBridge(sink, setNativeCallback);
        if (!setNativeCallback(bridge._callback))
        {
            AppLog.Warn("Rumble", "Production rumble callback registration failed.", null,
                ("Event", "ProductionRumbleCallbackArmFailed"), ("Presentation", "Xbox360"));
            return null;
        }
        AppLog.Info("Rumble", "Production rumble callback armed.",
            ("Event", "ProductionRumbleCallbackArmed"), ("Presentation", "Xbox360"));
        return bridge;
    }

    private void OnRumble(nuint handle, byte leftMotor, byte rightMotor)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var result = _sink.SetRumble(new TwoMotorRumble(Expand(leftMotor), Expand(rightMotor)));
            if (result.Status == PhysicalRumbleWriteStatus.Failed)
                AppLog.Debug("Rumble", "Production rumble write failed.",
                    ("Event", "ProductionRumbleWriteFailed"), ("Presentation", "Xbox360"), ("Reason", result.Reason));
        }
        catch
        {
            // Never throw across the native callback boundary.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            if (_setNativeCallback(null))
                AppLog.Info("Rumble", "Production rumble callback disarmed.",
                    ("Event", "ProductionRumbleCallbackDisarmed"), ("Presentation", "Xbox360"));
            else
                AppLog.Warn("Rumble", "Production rumble callback clear was not confirmed; native root retained until device teardown.", null,
                    ("Event", "ProductionRumbleCallbackClearFailed"), ("Presentation", "Xbox360"));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Rumble", "Production rumble callback clear threw.", exception,
                ("Event", "ProductionRumbleCallbackClearFailed"), ("Presentation", "Xbox360"));
        }
    }
}

/// <summary>SteamDeck production rumble: decodes the raw normalized Deck host-output report with the
/// stateless <see cref="SteamDeckRumbleDecoder"/> and writes recognized rumble/haptic packets to the
/// shared physical sink. Not a restoration of the deleted routing-era bridge -- no authority, no
/// session, no arbitration.</summary>
internal sealed class SteamDeckRumbleFeedbackAdapter : IDisposable
{
    private readonly IPhysicalRumbleSink _sink;
    private readonly Func<bool> _clearNativeCallback;
    private readonly SteamDeckOutputCallback _callback;
    private int _disposed;

    private SteamDeckRumbleFeedbackAdapter(IPhysicalRumbleSink sink, Func<bool> clearNativeCallback)
    {
        _sink = sink;
        _clearNativeCallback = clearNativeCallback;
        _callback = OnOutput;
    }

    /// <summary>Registers the native Deck output callback via <paramref name="setNativeCallback"/>.
    /// Returns <see langword="null"/> if registration is not confirmed.</summary>
    internal static SteamDeckRumbleFeedbackAdapter? TryArm(
        IPhysicalRumbleSink sink,
        Func<SteamDeckOutputCallback, bool> setNativeCallback,
        Func<bool> clearNativeCallback)
    {
        var adapter = new SteamDeckRumbleFeedbackAdapter(sink, clearNativeCallback);
        if (!setNativeCallback(adapter._callback))
        {
            AppLog.Warn("Rumble", "Production rumble callback registration failed.", null,
                ("Event", "ProductionRumbleCallbackArmFailed"), ("Presentation", "SteamDeck"));
            return null;
        }
        AppLog.Info("Rumble", "Production rumble callback armed.",
            ("Event", "ProductionRumbleCallbackArmed"), ("Presentation", "SteamDeck"));
        return adapter;
    }

    private void OnOutput(nuint handle, nint data, uint length)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (data == 0 || length == 0) return;
            var report = new byte[(int)Math.Min(length, 512u)];
            Marshal.Copy(data, report, 0, report.Length);
            var decoded = SteamDeckRumbleDecoder.Decode(report);
            if (!decoded.HasPhysicalTranslation) return;
            var result = _sink.SetRumble(decoded.Rumble);
            if (result.Status == PhysicalRumbleWriteStatus.Failed)
                AppLog.Debug("Rumble", "Production rumble write failed.",
                    ("Event", "ProductionRumbleWriteFailed"), ("Presentation", "SteamDeck"), ("Command", decoded.Command), ("Reason", result.Reason));
        }
        catch
        {
            // Never throw across the native callback boundary.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            if (_clearNativeCallback())
                AppLog.Info("Rumble", "Production rumble callback disarmed.",
                    ("Event", "ProductionRumbleCallbackDisarmed"), ("Presentation", "SteamDeck"));
            else
                AppLog.Warn("Rumble", "Production rumble callback clear was not confirmed; native root retained until device teardown.", null,
                    ("Event", "ProductionRumbleCallbackClearFailed"), ("Presentation", "SteamDeck"));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Rumble", "Production rumble callback clear threw.", exception,
                ("Event", "ProductionRumbleCallbackClearFailed"), ("Presentation", "SteamDeck"));
        }
    }
}
