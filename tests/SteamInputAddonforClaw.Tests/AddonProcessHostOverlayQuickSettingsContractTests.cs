using System;
using System.IO;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// SF-V2-09 section 30/30.1: AddonProcessHost is a large composition root with no existing
// dedicated unit-test seam for HandleOverlayQuickSettingsMutationAsync/OnFrontendStateInvalidatedForOverlay
// (constructing a fully wired host with a captured, visible Overlay and a fake IAddonFrontendControl
// would require a disproportionate new test-only surface on a class this size). Per the work order's
// own escape hatch, this is the smallest source-contract coverage of the two production guarantees
// that changed here: the historical Device-only Overlay mutation gate is gone (Profile now reaches
// the shared adapter through the exact same path), and the narrow before/after active-AppId
// comparison that requests one backstop Quick Settings refresh when a mutation's own
// StateInvalidated suppression could otherwise leave a stale game context on screen. Behavior
// coverage of the resulting publication path itself lives in OverlayTransportTests
// (RefreshQuickSettingsAsync_*) and OverlayQuickSettingsPageBindingTests (context safety).
public sealed class AddonProcessHostOverlayQuickSettingsContractTests
{
    [Fact]
    public void Overlay_mutation_handler_no_longer_gates_on_a_device_only_page_id()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var handler = ExtractMethod(source, "private async Task<QuickSettingsMutationResult> HandleOverlayQuickSettingsMutationAsync");

        Assert.DoesNotContain("intent.PageId != QuickSettingsPageId.Device", handler);
        Assert.DoesNotContain("Only the Device page is available through the Overlay Quick Settings seam.", handler);
        Assert.DoesNotContain("Profile is not exposed/admitted to the Overlay", handler);

        // The only admission facts left are the ones NamedPipeOverlayServer cannot itself check --
        // shutdown and OQ4 capture ownership -- before dispatching through the one shared adapter
        // seam for whatever PageId the intent carries (Device or Profile).
        Assert.Contains("Volatile.Read(ref _processShutdownStarted) != 0 || !_overlayCaptureActive", handler);
        Assert.Contains("control.MutateQuickSettingAsync(intent, token)", handler);
        Assert.DoesNotContain("GameProfileMutations", handler);
        Assert.DoesNotContain("ProfileStore", handler);
    }

    // Section 10/30.1: the exact before/after AppId comparison the work order specifies -- captured
    // before the mutation, compared after, gated by the same shutdown/capture/visible facts the
    // ordinary StateInvalidated handler uses, requesting the SAME generic two-page refresh.
    [Fact]
    public void Overlay_mutation_handler_requests_one_backstop_refresh_when_the_active_app_id_changed_in_flight()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var handler = ExtractMethod(source, "private async Task<QuickSettingsMutationResult> HandleOverlayQuickSettingsMutationAsync");

        Assert.Contains("var activeAppIdBefore = _runtimeHost?.ActualRunningAppId ?? 0;", handler);
        Assert.Contains("var activeAppIdAfter = _runtimeHost?.ActualRunningAppId ?? 0;", handler);
        Assert.Contains("activeAppIdAfter != activeAppIdBefore", handler);
        Assert.Contains("_overlayController.RefreshQuickSettingsAsync()", handler);

        // No new epoch/state-machine field -- just the local before/after locals inside this method.
        Assert.DoesNotContain("_overlayQuickSettingsEpoch", source);
        Assert.DoesNotContain("OverlayQuickSettingsStateMachine", source);
    }

    [Fact]
    public void Overlay_quick_settings_authority_binds_both_device_and_profile_capture()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");

        Assert.Contains("_overlayController.BindQuickSettingsAuthority(", source);
        Assert.Contains("captureDevicePage: token => _frontendControl!.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Device, appId: null, token)", source);
        Assert.Contains("captureProfilePage: token => CaptureOverlayProfileQuickSettingsPageAsync(token)", source);
    }

    // Section 7.3/7.4: no active game (AppId 0) resolves to an explicit Unavailable page WITHOUT
    // calling into the frontend control at all -- never a fake AppId-0 profile, never a direct
    // ProfileStore/game scan.
    [Fact]
    public void Overlay_profile_capture_resolves_no_active_game_without_calling_the_frontend_control()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var method = ExtractMethod(source, "private Task<QuickSettingsPageSnapshot> CaptureOverlayProfileQuickSettingsPageAsync");

        Assert.Contains("var appId = _runtimeHost?.ActualRunningAppId ?? 0;", method);
        Assert.Contains("if (appId == 0)", method);
        Assert.Contains("QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, appId: null,", method);
        // The appId==0 branch returns before this call is ever reached.
        Assert.Contains("return _frontendControl!.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Profile, appId, token);", method);
    }

    // Section 27/28: the shared product/dispatch authority SF-V2-08 already built stays untouched --
    // SF-V2-09 only consumes it from the Overlay side.
    [Fact]
    public void Shared_quick_settings_product_and_dispatch_files_are_unmodified_by_this_migration()
    {
        Assert.Contains("internal static QuickSettingsPageSnapshot BuildProfile(FrontendGameProfileSnapshot snapshot)",
            ReadSource("src", "SteamInputAddonforClaw", "Frontend", "QuickSettingsPresentation.cs"));
        Assert.Contains("QuickSettingsPageId.Profile => MutateProfileAsync(control, intent, cancellationToken),",
            ReadSource("src", "SteamInputAddonforClaw", "Frontend", "QuickSettingsMutationAdapter.cs"));
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        var index = openBrace;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) break;
        }
        return source[start..(index + 1)];
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
