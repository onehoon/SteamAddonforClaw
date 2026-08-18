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
            if (length > MaximumCallbackLength || (data == 0 && length != 0)) return;
            var report = new byte[(int)length];
            if (length != 0) Marshal.Copy(data, report, 0, report.Length);
            var decoded = SteamDeckRumbleDecoder.Decode(report);
            if (!decoded.IsSupported) return;
            BeforeLease?.Invoke();
            if (!_authority.TryAcquireLease(_token, out var lease) || lease is null) return;
            using (lease)
            {
                _sink.SetRumble(decoded.Rumble);
            }
        }
        catch (Exception exception)
        {
            AppLog.Debug("Rumble", "Steam Deck feedback callback was contained.", ("Reason", exception.GetType().Name));
        }
    }
}
