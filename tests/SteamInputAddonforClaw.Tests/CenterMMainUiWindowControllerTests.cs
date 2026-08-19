using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMMainUiWindowControllerTests
{
    private const int Pid = 4242;
    private static readonly IntPtr MainUiHwnd = new(1001);
    private static readonly IntPtr ForeignHwnd = new(2002);
    private static readonly IntPtr TrayOwnerHwnd = new(3003);

    [Fact]
    public void Unique_recognized_visible_window_that_survives_revalidation_is_minimized_exactly_once()
    {
        var api = new FakeWindowApi();
        api.Windows[MainUiHwnd] = new WindowInfo(Pid, Visible: true, Title: "MSI Center M", ClassName: null);
        var controller = new Win32CenterMMainUiWindowController(api);

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () => CenterMWindowMutationAuthority.Match);

        Assert.Equal(CenterMMainUiMinimizeResult.Requested, result);
        Assert.Equal([MainUiHwnd], api.PostMinimizeCalls);
    }

    [Fact]
    public void Recognized_by_class_prefix_is_also_a_valid_target()
    {
        var api = new FakeWindowApi();
        api.Windows[MainUiHwnd] = new WindowInfo(Pid, Visible: true, Title: null, ClassName: "HwndWrapper[MSI Center M.exe;;abc]");
        var controller = new Win32CenterMMainUiWindowController(api);

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () => CenterMWindowMutationAuthority.Match);

        Assert.Equal(CenterMMainUiMinimizeResult.Requested, result);
    }

    [Fact]
    public void No_recognized_visible_window_is_reported_without_posting_anything()
    {
        var api = new FakeWindowApi();
        api.Windows[TrayOwnerHwnd] = new WindowInfo(Pid, Visible: false, Title: "WindowsForms10.Window.8", ClassName: null);
        var controller = new Win32CenterMMainUiWindowController(api);

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () => CenterMWindowMutationAuthority.Match);

        Assert.Equal(CenterMMainUiMinimizeResult.NoRecognizedVisibleWindow, result);
        Assert.Empty(api.PostMinimizeCalls);
    }

    [Fact]
    public void Foreign_owned_window_is_never_a_candidate_even_if_it_matches_title()
    {
        var api = new FakeWindowApi();
        api.Windows[ForeignHwnd] = new WindowInfo(Pid + 1, Visible: true, Title: "MSI Center M", ClassName: null);
        var controller = new Win32CenterMMainUiWindowController(api);

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () => CenterMWindowMutationAuthority.Match);

        Assert.Equal(CenterMMainUiMinimizeResult.NoRecognizedVisibleWindow, result);
        Assert.Empty(api.PostMinimizeCalls);
    }

    [Fact]
    public void Ambiguous_multiple_recognized_visible_windows_fails_closed_without_posting()
    {
        var api = new FakeWindowApi();
        api.Windows[MainUiHwnd] = new WindowInfo(Pid, Visible: true, Title: "MSI Center M", ClassName: null);
        api.Windows[new IntPtr(1002)] = new WindowInfo(Pid, Visible: true, Title: "MSI Center M", ClassName: null);
        var controller = new Win32CenterMMainUiWindowController(api);

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () => CenterMWindowMutationAuthority.Match);

        Assert.Equal(CenterMMainUiMinimizeResult.AmbiguousVisibleWindows, result);
        Assert.Empty(api.PostMinimizeCalls);
    }

    [Fact]
    public void Enumeration_failure_is_reported_as_failed()
    {
        var api = new FakeWindowApi { EnumerationSucceeds = false };
        var controller = new Win32CenterMMainUiWindowController(api);

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () => CenterMWindowMutationAuthority.Match);

        Assert.Equal(CenterMMainUiMinimizeResult.Failed, result);
        Assert.Empty(api.PostMinimizeCalls);
    }

    [Fact]
    public void Target_destroyed_between_discovery_and_revalidation_is_never_posted_to()
    {
        var api = new FakeWindowApi();
        api.Windows[MainUiHwnd] = new WindowInfo(Pid, Visible: true, Title: "MSI Center M", ClassName: null);
        // The second enumeration (immediately before posting) no longer sees it -- destroyed
        // between the two calls.
        api.RemoveOnNthEnumeration = 2;
        var controller = new Win32CenterMMainUiWindowController(api);

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () => CenterMWindowMutationAuthority.Match);

        Assert.Equal(CenterMMainUiMinimizeResult.TargetChanged, result);
        Assert.Empty(api.PostMinimizeCalls);
    }

    [Fact]
    public void Target_hwnd_reused_by_a_different_recognized_window_between_enumerations_is_never_posted_to()
    {
        // Simulates the exact hazard the fix targets: the numeric HWND from the first enumeration
        // is still "present" and still recognized on the second pass, but as a DIFFERENT window
        // object (destroyed original + reused handle) is not directly observable through this fake
        // -- so the more realistic simulation is a second candidate appearing at revalidation time,
        // which must equally block the post.
        var api = new FakeWindowApi();
        api.Windows[MainUiHwnd] = new WindowInfo(Pid, Visible: true, Title: "MSI Center M", ClassName: null);
        api.AddOnNthEnumeration = (2, new IntPtr(9999), new WindowInfo(Pid, Visible: true, Title: "MSI Center M", ClassName: null));
        var controller = new Win32CenterMMainUiWindowController(api);

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () => CenterMWindowMutationAuthority.Match);

        Assert.Equal(CenterMMainUiMinimizeResult.TargetChanged, result);
        Assert.Empty(api.PostMinimizeCalls);
    }

    [Fact]
    public void Retained_handle_authority_check_failing_immediately_before_post_blocks_the_mutation()
    {
        // Both HWND enumeration passes remain stable (numeric PID match), but the caller-supplied
        // retained-handle authority check fails right before PostMessageW -- e.g. the PID was
        // reused by a different process after the original one exited. The numeric agreement alone
        // must never be trusted as process-object identity.
        var api = new FakeWindowApi();
        api.Windows[MainUiHwnd] = new WindowInfo(Pid, Visible: true, Title: "MSI Center M", ClassName: null);
        var controller = new Win32CenterMMainUiWindowController(api);
        var authorityCallCount = 0;

        var result = controller.TryMinimizeRecognizedMainUi(Pid, () =>
        {
            authorityCallCount++;
            return CenterMWindowMutationAuthority.Mismatch;
        });

        Assert.Equal(CenterMMainUiMinimizeResult.TargetChanged, result);
        Assert.Equal(1, authorityCallCount);
        Assert.Empty(api.PostMinimizeCalls);
    }

    private readonly record struct WindowInfo(int OwnerProcessId, bool Visible, string? Title, string? ClassName);

    private sealed class FakeWindowApi : IWin32MainUiWindowApi
    {
        internal Dictionary<IntPtr, WindowInfo> Windows { get; } = [];
        internal bool EnumerationSucceeds { get; init; } = true;
        internal List<IntPtr> PostMinimizeCalls { get; } = [];
        internal int? RemoveOnNthEnumeration { get; set; }
        internal (int Nth, IntPtr Hwnd, WindowInfo Info)? AddOnNthEnumeration { get; set; }
        private int _enumerationCount;

        public IReadOnlyList<IntPtr>? EnumerateTopLevelWindows()
        {
            _enumerationCount++;
            if (!EnumerationSucceeds) return null;

            var snapshot = new Dictionary<IntPtr, WindowInfo>(Windows);
            if (RemoveOnNthEnumeration == _enumerationCount) snapshot.Remove(MainUiHwnd);
            if (AddOnNthEnumeration is { } addition && addition.Nth == _enumerationCount)
                snapshot[addition.Hwnd] = addition.Info;

            return [.. snapshot.Keys];
        }

        private WindowInfo Lookup(IntPtr hWnd) => Windows.TryGetValue(hWnd, out var info) ? info
            : AddOnNthEnumeration is { } addition && addition.Hwnd == hWnd ? addition.Info
            : default;

        public uint GetOwnerProcessId(IntPtr hWnd) => (uint)Lookup(hWnd).OwnerProcessId;
        public bool IsWindowVisible(IntPtr hWnd) => Lookup(hWnd).Visible;
        public string? GetClassName(IntPtr hWnd) => Lookup(hWnd).ClassName;
        public string? GetWindowText(IntPtr hWnd) => Lookup(hWnd).Title;

        public bool PostMinimize(IntPtr hWnd)
        {
            PostMinimizeCalls.Add(hWnd);
            return true;
        }
    }
}
