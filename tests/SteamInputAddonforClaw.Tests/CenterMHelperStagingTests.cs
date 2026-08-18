using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Deterministic proof that StageFromPublishRoot resolves the exact directory layout
/// CopyCenterMHelperOutput (in SteamInputAddonforClaw.csproj) actually writes -- publishRoot\
/// CenterMHelperSource\CenterMHelper.exe -- without needing a real build or process launch.
/// Regression coverage for the path-contract mismatch between staging and its caller.</summary>
public sealed class CenterMHelperStagingTests
{
    [Fact]
    public void StageFromPublishRoot_resolves_the_exact_layout_CopyCenterMHelperOutput_writes()
    {
        var publishRoot = Path.Combine(Path.GetTempPath(), "CenterMHelperStagingTests-" + Guid.NewGuid());
        var sourceDirectory = Path.Combine(publishRoot, "CenterMHelperSource");
        Directory.CreateDirectory(sourceDirectory);
        var sourceBinary = Path.Combine(sourceDirectory, "CenterMHelper.exe");
        File.WriteAllBytes(sourceBinary, [1, 2, 3, 4]);

        try
        {
            var stagedPath = CenterMHelperStaging.StageFromPublishRoot(publishRoot);

            Assert.NotNull(stagedPath);
            Assert.True(File.Exists(stagedPath));
            Assert.Equal("MSI Center M.exe", Path.GetFileName(stagedPath));
            Assert.Equal(File.ReadAllBytes(sourceBinary), File.ReadAllBytes(stagedPath!));
        }
        finally
        {
            Directory.Delete(publishRoot, recursive: true);
            if (File.Exists(Path.Combine(CenterMHelperStaging.RuntimeDirectory, "MSI Center M.exe")))
                File.Delete(Path.Combine(CenterMHelperStaging.RuntimeDirectory, "MSI Center M.exe"));
        }
    }

    [Fact]
    public void StageFromPublishRoot_publishRootWithoutCenterMHelperSourceSubfolder_failsClosed()
    {
        // Regression: passing the CenterMHelperSource subdirectory itself (or any root that
        // doesn't contain it) instead of the publish/output root must fail closed, not silently
        // resolve to nothing and let a caller proceed as if staging succeeded.
        var wrongRoot = Path.Combine(Path.GetTempPath(), "CenterMHelperStagingTests-wrong-" + Guid.NewGuid());
        Directory.CreateDirectory(wrongRoot);
        File.WriteAllBytes(Path.Combine(wrongRoot, "CenterMHelper.exe"), [1, 2, 3, 4]);

        try
        {
            var stagedPath = CenterMHelperStaging.StageFromPublishRoot(wrongRoot);
            Assert.Null(stagedPath);
        }
        finally
        {
            Directory.Delete(wrongRoot, recursive: true);
        }
    }
}
