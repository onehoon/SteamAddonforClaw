using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Persistent Dual VIIPER Devices work order section 14/20: production now owns a real
/// Xbox360 typed handle. The obsolete automatic Game Bar foreground -&gt; Xbox360 presentation path
/// (<c>GameBarForegroundWatcher</c> -&gt; <c>_gameBarDelivery</c> -&gt;
/// <c>AddonRoutingRuntime.HandleGameBarForegroundChangedAsync</c> -&gt;
/// <c>EnterXbox360PresentationAsync</c>) must NOT be able to attach that handle. It is already
/// disconnected in <c>AddonProcessHost</c> -- the watcher is never started and its
/// <c>StateChanged</c> event is never subscribed -- and this test locks that in so a future edit
/// cannot silently re-arm an X360 attach the moment a production handle exists.</summary>
public sealed class PersistentDualViiperFoundationTests
{
    [Fact]
    public void AddonProcessHost_does_not_arm_the_obsolete_game_bar_xbox360_presentation_path()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        // The watcher must not be started, and its foreground event must not be wired to the
        // presentation delivery. (The '-=' unsubscribe in cleanup is harmless and may remain.)
        Assert.DoesNotContain("_gameBarForegroundWatcher.Start()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StateChanged += OnGameBarForegroundChanged", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
