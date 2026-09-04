using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamSessionRuntimeTests
{
    // Full1902 Cleanup I: the production Steam runtime no longer owns DeveloperTestModeState,
    // EffectiveSteamSessionSource, or DiagnosticSessionTracker. See EffectiveSteamSessionSourceTests
    // for the parked helper's own spec.
    [Fact]
    public void ProductionRuntime_HasNoSyntheticEffectiveSessionDependency()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs"));
        Assert.DoesNotContain("EffectiveSteamSessionSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeveloperTestModeState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticSessionTracker", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var runtime = new SteamSessionRuntime();

        runtime.Dispose();
        runtime.Dispose();
    }

    [Fact]
    public void ActualObservation_tracksAppId()
    {
        var source = new FakeRunningAppIdSource();
        using var runtime = new SteamSessionRuntime(source);
        var observed = new List<uint>();
        runtime.ActualRunningAppIdChanged += appId => observed.Add(appId);

        runtime.StartActualObservation();
        source.SetRunningAppId(123);

        Assert.Equal([123u], observed);
        Assert.Equal(123u, runtime.ActualRunningAppId);
        Assert.Equal(123u, runtime.CapturePresentationSnapshot().RunningAppId);
    }

    private sealed class FakeRunningAppIdSource : IRunningAppIdSource
    {
        private uint _appId;
        public event EventHandler? Changed;
        public uint GetRunningAppId() => _appId;
        public void SetRunningAppId(uint appId)
        {
            _appId = appId;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
