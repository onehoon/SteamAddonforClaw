using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Feedback;

internal sealed class SteamDeckRumbleFeedbackBridge
{
    private const uint MaximumCallbackLength = 64;
    private readonly FeedbackAuthority _authority;
    private readonly FeedbackAuthorityToken _token;
    private readonly IPhysicalRumbleSink _sink;

    internal SteamDeckRumbleFeedbackBridge(FeedbackAuthority authority, FeedbackAuthorityToken token, IPhysicalRumbleSink sink)
    {
        _authority = authority;
        _token = token;
        _sink = sink;
        Callback = OnNativeOutput;
    }

    internal SteamDeckOutputCallback Callback { get; }
    internal Action? BeforeLease { get; set; }

    private void OnNativeOutput(nuint handle, nint data, uint length)
    {
        try
        {
            // Diagnostics only: every classification path is logged before its return so a
            // hardware Ping can be matched to exactly what Steam sent, without changing behavior.
            if (length > MaximumCallbackLength || (data == 0 && length != 0))
            {
                AppLog.Debug("Rumble", "SteamDeck feedback RX invalid callback.", ("Length", length));
                return;
            }
            var report = new byte[(int)length];
            if (length != 0) Marshal.Copy(data, report, 0, report.Length);

            var opcode = report.Length > 0 ? report[0] : (byte?)null;
            AppLog.Debug("Rumble", "SteamDeck feedback RX", ("Opcode", opcode is null ? "None" : $"0x{opcode:X2}"), ("Length", length));

            var decoded = SteamDeckRumbleDecoder.Decode(report);
            if (decoded.Command == SteamDeckFeedbackCommand.Rumble)
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command), ("Left", decoded.Rumble.LargeMotor), ("Right", decoded.Rumble.SmallMotor));
            else
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command));

            if (!decoded.IsSupported) return;
            BeforeLease?.Invoke();
            if (!_authority.TryAcquireLease(_token, out var lease) || lease is null)
            {
                AppLog.Debug("Rumble", "SteamDeck feedback DROP", ("Reason", "AuthorityRejected"));
                return;
            }
            using (lease)
            {
                _sink.SetRumble(decoded.Rumble);
            }
        }
        catch (Exception exception)
        {
            try { AppLog.Debug("Rumble", "Steam Deck feedback callback was contained.", ("Reason", exception.GetType().Name)); }
            catch { /* Never allow diagnostics to cross the unmanaged callback boundary. */ }
        }
    }
}
