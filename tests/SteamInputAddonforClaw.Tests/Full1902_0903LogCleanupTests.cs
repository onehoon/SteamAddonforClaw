using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Full1902 0903 cleanup sections 5 & 6: the two high-volume diagnostic lines observed on
/// real hardware are removed while the surrounding useful evidence is kept.</summary>
[Collection("AppLog")]
public sealed class Full1902_0903LogCleanupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw-0903LogTests", Guid.NewGuid().ToString("N"));

    public Full1902_0903LogCleanupTests()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string ReadLog()
    {
        AppLog.DrainForTests();
        var files = Directory.Exists(_directory) ? Directory.GetFiles(_directory) : [];
        return files.Length == 0 ? "" : LogFileTestHelper.ReadAllText(files[0]);
    }

    // ---- section 5: no per-device PnP ancestry flood ----

    [Fact]
    public void ResolveAncestors_no_longer_logs_one_line_per_call_but_keeps_the_snapshot_summary()
    {
        var root = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.Empty, null, [], "ROOT", [], [], "USB", null, null, null, null, true);
        var controller = new ControllerDeviceInfo("HID\\CTRL", Guid.NewGuid(), null, ["root\\usb\\0000"], "HID", [], [], "HIDClass", null, null, null, null, true);
        var snapshot = new ControllerTopologySnapshot([root, controller]);

        var a = snapshot.ResolveAncestors(controller);
        var b = snapshot.ResolveAncestors(root);

        Assert.Single(a);      // functional resolution unchanged
        Assert.Empty(b);
        var log = ReadLog();
        Assert.DoesNotContain("Controller ancestry resolved", log);
        Assert.Contains("Controller topology snapshot created", log);
    }

    // ---- section 6: no misleading generic MsiInput "M1=False->False M2=False->False" line ----

    [Fact]
    public void Unrelated_controller_state_change_no_longer_emits_the_generic_msiinput_line()
    {
        var previous = new ControllerState(new GamepadButtons(), default, default, default, new AuxiliaryButtonState([false, false]));
        var next = previous with { Buttons = previous.Buttons with { A = true } };

        AppLog.Debug("Test", "marker"); // ensure a log file exists even when LogStateChange emits nothing
        MsiClawInputSource.LogStateChange(7, previous, next);

        var log = ReadLog();
        Assert.Contains("marker", log);
        Assert.DoesNotContain("ControllerState changed.", log);
        Assert.DoesNotContain("M1 state changed.", log);
        Assert.DoesNotContain("M2 state changed.", log);
    }

    [Fact]
    public void Real_m1_transition_still_emits_the_dedicated_msiinput_log()
    {
        var previous = new ControllerState(new GamepadButtons(), default, default, default, new AuxiliaryButtonState([false, false]));
        // Catalog order is [M2, M1], so index 1 is M1.
        var next = previous with { Auxiliary = new AuxiliaryButtonState([false, true]) };

        MsiClawInputSource.LogStateChange(7, previous, next);

        var log = ReadLog();
        Assert.Contains("M1 state changed.", log);
        Assert.DoesNotContain("ControllerState changed.", log);
    }

    // ---- section 7: authority-aware Overlay-unavailable severity ----

    [Fact]
    public void Overlay_unavailable_severity_is_authority_aware_and_the_no_op_early_return_is_unchanged()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        var branch = host.IndexOf("no owned presentation / running PR5 source", StringComparison.Ordinal);
        Assert.True(branch > 0);
        var window = host[(branch - 400)..(branch + 700)];

        // Still a no-op early return; only the severity now depends on the expected authority.
        Assert.Contains("if (ExpectsFull1902ControllerAuthority())", window);
        Assert.Contains("AppLog.Warn(\"OverlayCapture\"", window);
        Assert.Contains("AppLog.Info(\"OverlayCapture\"", window);
        Assert.Contains("return;", window);
        // The branch must not start ownership / a presentation / retry to satisfy the request.
        foreach (var forbidden in new[] { "AcquireAsync", "AttachInitialAsync", "SetEnabledAsync", "Task.Delay" })
            Assert.DoesNotContain(forbidden, window);
    }
}
