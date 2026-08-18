using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class TrackedCenterMMainUiTests
{
    [Fact]
    public void FullRightsAvailable_RetainsHandleAndAllowsTermination()
    {
        var opener = new FakeOpener(fullRightsSucceed: true, observationSucceeds: true);

        using var tracked = TrackedCenterMMainUi.Create(1234, "path", opener);

        Assert.True(tracked.HasRetainedHandle);
        Assert.True(tracked.HasTerminateRights);
        Assert.Equal(1, opener.OpenCallCount);
    }

    [Fact]
    public void TerminateDenied_FallsBackToObservationOnlyHandle_RetainsIdentityWithoutTerminateRights()
    {
        var opener = new FakeOpener(fullRightsSucceed: false, observationSucceeds: true);

        using var tracked = TrackedCenterMMainUi.Create(1234, "path", opener);

        Assert.True(tracked.HasRetainedHandle);
        Assert.False(tracked.HasTerminateRights);
        Assert.Equal(2, opener.OpenCallCount);
    }

    [Fact]
    public void BothOpensFail_NoRetainedHandle_IdentityIsUncertain()
    {
        var opener = new FakeOpener(fullRightsSucceed: false, observationSucceeds: false);

        using var tracked = TrackedCenterMMainUi.Create(1234, "path", opener);

        Assert.False(tracked.HasRetainedHandle);
        Assert.False(tracked.HasTerminateRights);
    }

    private sealed class FakeOpener(bool fullRightsSucceed, bool observationSucceeds) : IProcessHandleOpener
    {
        private const uint PROCESS_TERMINATE = 0x0001;
        internal int OpenCallCount { get; private set; }

        public SafeProcessHandle Open(int processId, uint desiredAccess)
        {
            OpenCallCount++;
            var isFullRightsRequest = (desiredAccess & PROCESS_TERMINATE) != 0;
            var succeeds = isFullRightsRequest ? fullRightsSucceed : observationSucceeds;
            return succeeds ? new SafeProcessHandle(GetCurrentProcessHandle(), false) : new SafeProcessHandle(IntPtr.Zero, false);
        }

        private static IntPtr GetCurrentProcessHandle() => System.Diagnostics.Process.GetCurrentProcess().Handle;
    }
}
