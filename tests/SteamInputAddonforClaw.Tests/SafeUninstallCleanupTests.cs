using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SafeUninstallCleanupTests
{
    [Fact]
    public void BoundedDirectoryDeletion_RetriesTransientIoFailureAndVerifiesAbsence()
    {
        var directory = CreateDirectory();
        var attempts = 0;
        var delays = 0;

        var result = BoundedDirectoryDeletion.Delete(directory,
            deleteDirectory: path =>
            {
                attempts++;
                if (attempts < 3) throw new IOException("transient lock");
                Directory.Delete(path, recursive: true);
            },
            delay: _ => delays++);

        Assert.Equal(DirectoryDeletionStatus.Deleted, result.Status);
        Assert.False(Directory.Exists(directory));
        Assert.Equal(3, attempts);
        Assert.Equal(2, delays);
    }

    [Fact]
    public void BoundedDirectoryDeletion_StopsAfterFiveFailedAttempts()
    {
        var directory = CreateDirectory();
        var attempts = 0;
        var delays = 0;

        var result = BoundedDirectoryDeletion.Delete(directory,
            deleteDirectory: _ => { attempts++; throw new UnauthorizedAccessException("locked"); },
            delay: _ => delays++);

        Assert.Equal(DirectoryDeletionStatus.Failed, result.Status);
        Assert.True(Directory.Exists(directory));
        Assert.Equal(BoundedDirectoryDeletion.MaximumAttempts, attempts);
        Assert.Equal(BoundedDirectoryDeletion.MaximumAttempts - 1, delays);
        Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData(false, "uninstall")]
    [InlineData(true, "uninstall --silent")]
    public void FinalDataCleanup_LaunchesOnlyAfterVerifiedDeletionAndSetsApprovalMarker(bool silent, string expectedArguments)
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var updater = Path.Combine(root, "Update.exe");
        Directory.CreateDirectory(root);
        File.WriteAllText(updater, string.Empty);
        var launched = false;
        var shutdown = false;

        var result = SafeUninstall.DeleteDataRootAndLaunchVeloPack(
            Path.Combine(root, "SteamInputAddonforClaw-Data"), updater, root, silent,
            () => shutdown = true,
            path =>
            {
                Assert.True(shutdown);
                Assert.Equal(Path.Combine(root, "SteamInputAddonforClaw-Data"), path);
                return new(DirectoryDeletionStatus.Deleted, "Deleted");
            },
            startInfo =>
            {
                launched = true;
                Assert.Equal(expectedArguments, string.Join(' ', startInfo.ArgumentList));
                Assert.Equal("1", startInfo.Environment[UninstallBootstrap.SafeUninstallApprovedEnvironmentVariable]);
                return true;
            });

        Assert.Equal(FinalUninstallHandoffResult.Launched, result);
        Assert.True(launched);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void FinalDataCleanupFailureNeverLaunchesVeloPack()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var launched = false;
        var result = SafeUninstall.DeleteDataRootAndLaunchVeloPack(
            Path.Combine(root, "SteamInputAddonforClaw-Data"), Path.Combine(root, "Update.exe"), root, false,
            static () => { },
            _ => new(DirectoryDeletionStatus.Failed, "StillExists"),
            _ => { launched = true; return true; });

        Assert.Equal(FinalUninstallHandoffResult.DataRootRemovalFailed, result);
        Assert.False(launched);
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
