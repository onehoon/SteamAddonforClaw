using System.Runtime.InteropServices;
using SteamInputAddonforClaw.GameBar;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WinGSuppressionGuardTests
{
    [Fact] public void DisarmedWinGPasses() { using var g = Create(); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void ArmedLeftWinGIsSuppressed() => AssertChord(0x5B);
    [Fact] public void ArmedRightWinGIsSuppressed() => AssertChord(0x5C);
    [Fact] public void GWithoutWinPasses() { using var g = Create(); g.Start(); Assert.True(g.IsHookInstalled); Assert.True(g.EnsureArmed()); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void OtherWinShortcutsPass() { using var g = Create(); g.EnsureArmed(); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x52, true)); }
    [Fact] public void ExternalInjectedWinGIsSuppressed() { using var g = Create(); g.EnsureArmed(); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true, 0x10, 123)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true, 0x10, 123)); }
    [Fact] public void OwnMarkerBypassesGuard() { using var g = Create(); g.EnsureArmed(); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true, extraInfo: OwnMarker)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true, extraInfo: OwnMarker)); }
    [Fact] public void CleanupConsumesMatchingWinAndGUp() { using var g = Create(); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, false)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x5B, false)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x52, true)); }
    [Fact] public void PartialCleanupAttemptsDummyUpAndStillSuppresses() { var calls = new List<WinGSuppressionGuard.Input[]>(); using var g = Create(calls, _ => 1); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); Assert.Equal(2, calls.Count); Assert.Equal((ushort)0xFF, calls[1][0].Data.Keyboard.Vk); Assert.Equal(0x0002u, calls[1][0].Data.Keyboard.Flags); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, false)); }
    [Fact] public void RapidConsecutiveChordsAreBothSuppressed() { using var g = Create(); g.EnsureArmed(); for (var i = 0; i < 2; i++) { g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, false)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x5B, false)); } }
    [Fact] public void DisarmLeavesCurrentResidueConsumedThenPassesNewChord() { using var g = Create(); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); g.Disarm(); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, false)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x5B, false)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void StartAndDisposeAreIdempotent() { var installs = 0; var removes = 0; using var g = new WinGSuppressionGuard((_, _, _, _, _) => { installs++; return new(1); }, (_, _, _) => IntPtr.Zero, _ => removes++); g.Start(); g.Start(); g.Dispose(); g.Dispose(); Assert.Equal(1, installs); Assert.Equal(1, removes); }
    [Fact] public void FailedInstallationCannotArm() { using var g = new WinGSuppressionGuard((_, _, _, _, _) => IntPtr.Zero); g.Start(); Assert.False(g.EnsureArmed()); Assert.False(g.IsArmed); }
    [Fact] public void InputMatchesNativeWin32Layout() => Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<WinGSuppressionGuard.Input>());
    [Fact] public void CleanupExceptionStillSuppressesG() { using var g = Create(send: _ => throw new InvalidOperationException()); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void SuccessfulCleanupDoesNotPoisonLaterPlainG() { using var g = Create(); g.EnsureArmed(); g.ProcessKey(0x5B, true); g.ProcessKey(0x47, true); g.ProcessKey(0x47, false); g.ProcessKey(0x5B, false); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true)); }

    private static void AssertChord(int win)
    {
        using var g = Create(); g.EnsureArmed(); g.ProcessKey(win, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true));
    }

    private static WinGSuppressionGuard Create(List<WinGSuppressionGuard.Input[]>? calls = null, Func<WinGSuppressionGuard.Input[], uint>? send = null)
    {
        var guard = new WinGSuppressionGuard((_, _, _, _, _) => new(1), (_, _, _) => IntPtr.Zero, _ => { }, inputs => { calls?.Add(inputs); return send?.Invoke(inputs) ?? (uint)inputs.Length; });
        guard.Start();
        return guard;
    }

    private const long OwnMarker = unchecked((long)0x5349475F57494E47);
}
