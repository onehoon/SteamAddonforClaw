using SteamInputAddonforClaw.GameBar;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiQuickSettingsRuntimeQuiescerTests
{
    private const string ExactPackageFullName =
        "9426MICRO-STARINTERNATION.MSIQuickSettings_1.0.0.0_x64__f34examplepublisher";

    [Fact]
    public void Exact_msi_quick_settings_package_full_name_is_eligible()
    {
        Assert.True(MsiQuickSettingsRuntimeQuiescer.IsExactMsiQuickSettingsPackageFullName(ExactPackageFullName));
    }

    [Theory]
    [InlineData("OtherPublisher.MSIQuickSettings_1.0.0.0_x64__publisher")]
    [InlineData("9426MICRO-STARINTERNATION.MSIQuickSettingsTest_1.0.0.0_x64__publisher")]
    [InlineData("9426MICRO-STARINTERNATION.MSIQuickSettings")]
    [InlineData("prefix9426MICRO-STARINTERNATION.MSIQuickSettings_1.0.0.0_x64__publisher")]
    public void Similar_or_unrelated_package_identity_is_not_eligible(string packageFullName)
    {
        Assert.False(MsiQuickSettingsRuntimeQuiescer.IsExactMsiQuickSettingsPackageFullName(packageFullName));
    }

    [Fact]
    public void Missing_or_unreadable_package_identity_is_not_eligible()
    {
        Assert.False(MsiQuickSettingsRuntimeQuiescer.IsExactMsiQuickSettingsPackageFullName(null));
        Assert.False(MsiQuickSettingsRuntimeQuiescer.IsExactMsiQuickSettingsPackageFullName(string.Empty));
    }

    [Fact]
    public void Multiple_candidate_processes_for_one_package_are_deduplicated()
    {
        var packageFullNames = MsiQuickSettingsRuntimeQuiescer.GetDistinctTargetPackageFullNames(
        [
            ExactPackageFullName,
            ExactPackageFullName.ToUpperInvariant(),
            null,
            "OtherPublisher.MSIQuickSettings_1.0.0.0_x64__publisher"
        ]);

        Assert.Single(packageFullNames);
        Assert.Equal(ExactPackageFullName, packageFullNames[0]);
    }

    [Theory]
    [InlineData(ExactPackageFullName, ExactPackageFullName, true)]
    [InlineData("9426MICRO-STARINTERNATION.MSIQuickSettings_1.0.0.0_X64__F34EXAMPLEPUBLISHER", ExactPackageFullName, true)]
    [InlineData("9426MICRO-STARINTERNATION.MSIQuickSettings_1.0.0.1_x64__f34examplepublisher", ExactPackageFullName, false)]
    [InlineData("OtherPublisher.MSIQuickSettings_1.0.0.0_x64__publisher", ExactPackageFullName, false)]
    public void Process_termination_requires_an_exact_proven_package_full_name_match(
        string? candidatePackageFullName,
        string targetPackageFullName,
        bool expected)
    {
        Assert.Equal(expected,
            MsiQuickSettingsRuntimeQuiescer.IsExactPackageFullNameMatch(candidatePackageFullName, targetPackageFullName));
    }

    [Fact]
    public void Implementation_uses_proven_exact_package_identity_for_process_termination()
    {
        var source = File.ReadAllText(SourcePath("src/SteamInputAddonforClaw/GameBar/MsiQuickSettingsRuntimeQuiescer.cs"));

        Assert.Contains("CandidateProcessName = \"Gamebar_Widget\"", source, StringComparison.Ordinal);
        Assert.Contains("MsiQuickSettingsPackageName = \"9426MICRO-STARINTERNATION.MSIQuickSettings\"", source, StringComparison.Ordinal);
        Assert.Contains("Process.GetProcessesByName(CandidateProcessName)", source, StringComparison.Ordinal);
        Assert.Contains("Process.GetProcesses()", source, StringComparison.Ordinal);
        Assert.Contains("GetPackageFullName", source, StringComparison.Ordinal);
        Assert.Contains("PROCESS_QUERY_LIMITED_INFORMATION", source, StringComparison.Ordinal);
        Assert.Contains("PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION", source, StringComparison.Ordinal);
        Assert.Contains("TerminateProcess", source, StringComparison.Ordinal);
        Assert.Contains("IsExactPackageFullNameMatch", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (!IsExactMsiQuickSettingsPackageFullName(packageFullName))", StringComparison.Ordinal)
            < source.IndexOf("return TerminateExactPackageProcesses(", StringComparison.Ordinal),
            "package identity must be proven before package termination is considered");
        var termination = Method(source, "private static MsiQuickSettingsRuntimeQuiesceResult TerminateExactPackageProcesses(");
        Assert.True(
            termination.IndexOf("IsExactPackageFullNameMatch(terminationHandlePackageFullName, targetPackageFullName)", StringComparison.Ordinal)
            < termination.IndexOf("TerminateProcess(terminationHandle, 0)", StringComparison.Ordinal),
            "package identity must be revalidated on the termination handle before terminating the process");
        Assert.DoesNotContain("RuntimeBroker", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Process.Kill", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminateAllProcesses", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPackageDebugSettings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableDebugging", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-AppxPackage", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StartServicing(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_startup_attempts_quiesce_before_admission_and_continues_on_failure()
    {
        var host = File.ReadAllText(SourcePath("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
        var body = Method(host, "private async Task TryStartDisabledModeControllerAsync(");

        var authorityGate = body.IndexOf("startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled", StringComparison.Ordinal);
        var watcher = body.IndexOf("StartMsiQuickSettingsProcessStartWatcher()", StringComparison.Ordinal);
        var quiesce = body.IndexOf("MsiQuickSettingsRuntimeQuiescer.QuiesceExisting()", StringComparison.Ordinal);
        var owner = body.IndexOf("CreatePhysicalOwnership(startupComposition)", StringComparison.Ordinal);
        var admission = body.IndexOf("startupResult.DisabledBootAdmission?.IsReady != true", StringComparison.Ordinal);

        Assert.True(authorityGate >= 0 && authorityGate < watcher && watcher < quiesce);
        Assert.True(quiesce < owner && owner < admission);

        var cleanupBoundary = body[quiesce..owner];
        Assert.Contains("catch (Exception", cleanupBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("return;", cleanupBoundary, StringComparison.Ordinal);
    }

    private static string SourcePath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relativePath);
    }

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method not found: {signature}");

        var nextPrivate = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        var nextInternal = source.IndexOf("\n    internal ", start + signature.Length, StringComparison.Ordinal);
        if (nextInternal >= 0 && (nextPrivate < 0 || nextInternal < nextPrivate))
            nextPrivate = nextInternal;

        return nextPrivate < 0 ? source[start..] : source[start..nextPrivate];
    }
}
