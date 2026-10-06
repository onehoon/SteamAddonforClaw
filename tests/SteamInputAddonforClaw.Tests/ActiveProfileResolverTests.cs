using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ActiveProfileResolverTests
{
    [Fact]
    public void None_resolves_to_no_profile()
    {
        var resolved = ActiveProfileResolver.Resolve(ActiveProfileTarget.None, DocumentWithProfiles());
        Assert.Null(resolved);
    }

    [Fact]
    public void Steam_resolves_only_the_exact_enabled_app_id()
    {
        var document = DocumentWithProfiles();
        var resolved = ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(123), document);

        Assert.Equal("Steam:123", resolved?.TargetLabel);
        Assert.Equal(CpuBoostMode.Aggressive, resolved?.Performance.CpuBoost?.Ac);
        Assert.Null(ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(124), document));
        Assert.Null(ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(125), document));
    }

    [Fact]
    public void Xbox_resolves_only_the_exact_enabled_canonical_key()
    {
        var document = DocumentWithProfiles();
        var resolved = ActiveProfileResolver.Resolve(ActiveProfileTarget.ForXbox("store:game-a"), document);

        Assert.Equal("Xbox:store:game-a", resolved?.TargetLabel);
        Assert.Equal(CpuBoostMode.Aggressive, resolved?.Performance.CpuBoost?.Ac);
        Assert.Null(ActiveProfileResolver.Resolve(ActiveProfileTarget.ForXbox("store:game-b"), document));
        Assert.Null(ActiveProfileResolver.Resolve(ActiveProfileTarget.ForXbox("store:game-disabled"), document));
    }

    [Fact]
    public void Steam_wins_while_running_and_Xbox_resumes_after_Steam_exits()
    {
        var document = DocumentWithProfiles();
        uint appId = 123;
        string? xboxKey = "store:game-a";
        var selector = new Func<ProfileDocument, ResolvedActiveProfile?>(doc =>
        {
            var target = appId != 0
                ? ActiveProfileTarget.ForSteam(appId)
                : xboxKey is null ? ActiveProfileTarget.None : ActiveProfileTarget.ForXbox(xboxKey);
            return ActiveProfileResolver.Resolve(target, doc);
        });

        Assert.Equal("Steam:123", selector(document)?.TargetLabel);
        appId = 0;
        Assert.Equal("Xbox:store:game-a", selector(document)?.TargetLabel);
        xboxKey = null;
        Assert.Null(selector(document));
    }

    [Fact]
    public void Steam_and_Xbox_project_identical_override_shapes()
    {
        var document = DocumentWithProfiles();
        var steam = ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(123), document)!.Value;
        var xbox = ActiveProfileResolver.Resolve(ActiveProfileTarget.ForXbox("store:game-a"), document)!.Value;

        Assert.Equal(steam.Performance, xbox.Performance);
        Assert.Equal(steam.Display, xbox.Display);
    }

    private static ProfileDocument DocumentWithProfiles()
    {
        var performance = new GamePerformanceOverrides
        {
            CpuBoost = new GameCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.EfficientEnabled },
            Tdp = new GameTdpSettings { Enabled = true, Ac = new() { Pl1Watts = 21, Pl2Watts = 31 }, Dc = new() { Pl1Watts = 11, Pl2Watts = 21 } }
        };
        var display = new GameDisplayOverrides { Resolution = new GameDisplayResolution { Width = 1920, Height = 1200 } };
        return new ProfileDocument
        {
            Games = new()
            {
                ["123"] = new GameProfile { Enabled = true, Performance = performance, Display = display },
                ["125"] = new GameProfile { Enabled = false, Performance = performance, Display = display }
            },
            XboxGames = new()
            {
                ["store:game-a"] = new XboxGameProfile { Enabled = true, Performance = performance, Display = display },
                ["store:game-disabled"] = new XboxGameProfile { Enabled = false, Performance = performance, Display = display }
            }
        };
    }
}
