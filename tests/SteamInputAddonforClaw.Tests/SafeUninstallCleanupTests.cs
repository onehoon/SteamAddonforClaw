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
    public void FinalHandoff_removes_owned_runtime_but_preserves_user_data_logs_and_sets_approval_marker(bool silent, string expectedArguments)
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "SteamInputAddonforClaw-Data");
        var clawHudRuntime = Path.Combine(dataRoot, "Runtime", "ClawHUD");
        var updater = Path.Combine(root, "Update.exe");
        Directory.CreateDirectory(Path.Combine(dataRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(clawHudRuntime, "1.0.1"));
        File.WriteAllText(updater, string.Empty);
        File.WriteAllText(Path.Combine(dataRoot, "settings.json"), "settings");
        File.WriteAllText(Path.Combine(dataRoot, "profiles.json"), "profiles");
        File.WriteAllText(Path.Combine(dataRoot, "shortcuts.json"), "shortcuts");
        File.WriteAllText(Path.Combine(dataRoot, "logs", "launch.log"), "final uninstall log");
        File.WriteAllText(Path.Combine(clawHudRuntime, "1.0.1", "runtime.dll"), "owned runtime");
        var launched = false;
        var shutdown = false;
        var preparationLogged = false;

        var result = SafeUninstall.PreserveUserDataAndLaunchVeloPack(
            dataRoot, clawHudRuntime, updater, root, silent,
            (path, cleanup) =>
            {
                Assert.Equal(dataRoot, path);
                Assert.Equal(DirectoryDeletionStatus.Deleted, cleanup.Status);
                Assert.False(Directory.Exists(clawHudRuntime));
                preparationLogged = true;
            },
            () => { Assert.True(preparationLogged); shutdown = true; },
            path => BoundedDirectoryDeletion.Delete(path),
            startInfo =>
            {
                launched = true;
                Assert.Equal(expectedArguments, string.Join(' ', startInfo.ArgumentList));
                Assert.Equal("1", startInfo.Environment[UninstallBootstrap.SafeUninstallApprovedEnvironmentVariable]);
                return true;
            });

        Assert.Equal(FinalUninstallHandoffResult.Launched, result);
        Assert.True(launched);
        Assert.True(shutdown);
        Assert.True(File.Exists(Path.Combine(dataRoot, "settings.json")));
        Assert.True(File.Exists(Path.Combine(dataRoot, "profiles.json")));
        Assert.True(File.Exists(Path.Combine(dataRoot, "shortcuts.json")));
        Assert.True(File.Exists(Path.Combine(dataRoot, "logs", "launch.log")));
        Assert.False(Directory.Exists(clawHudRuntime));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void VeloPack_launch_failure_leaves_persistent_user_data_and_logs_intact()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "SteamInputAddonforClaw-Data");
        var logs = Path.Combine(dataRoot, "logs");
        var runtime = Path.Combine(dataRoot, "Runtime", "ClawHUD");
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(runtime);
        File.WriteAllText(Path.Combine(dataRoot, "settings.json"), "settings");
        File.WriteAllText(Path.Combine(dataRoot, "profiles.json"), "profiles");
        File.WriteAllText(Path.Combine(dataRoot, "shortcuts.json"), "shortcuts");
        File.WriteAllText(Path.Combine(logs, "launch.log"), "final uninstall log");
        File.WriteAllText(Path.Combine(root, "Update.exe"), string.Empty);
        var launched = false;
        var result = SafeUninstall.PreserveUserDataAndLaunchVeloPack(
            dataRoot, runtime, Path.Combine(root, "Update.exe"), root, false,
            static (_, _) => { },
            static () => { },
            path => BoundedDirectoryDeletion.Delete(path),
            _ => { launched = true; return false; });

        Assert.Equal(FinalUninstallHandoffResult.UpdaterLaunchFailed, result);
        Assert.True(launched);
        Assert.True(File.Exists(Path.Combine(dataRoot, "settings.json")));
        Assert.True(File.Exists(Path.Combine(dataRoot, "profiles.json")));
        Assert.True(File.Exists(Path.Combine(dataRoot, "shortcuts.json")));
        Assert.True(File.Exists(Path.Combine(logs, "launch.log")));
        Assert.False(Directory.Exists(runtime));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Owned_runtime_cleanup_failure_blocks_handoff_without_touching_user_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "SteamInputAddonforClaw-Data");
        Directory.CreateDirectory(dataRoot);
        var settings = Path.Combine(dataRoot, "settings.json");
        File.WriteAllText(settings, "settings");
        var launched = false;

        var result = SafeUninstall.PreserveUserDataAndLaunchVeloPack(
            dataRoot, Path.Combine(dataRoot, "Runtime", "ClawHUD"), "Update.exe", root, false,
            static (_, _) => { }, static () => { },
            _ => new(DirectoryDeletionStatus.Failed, "StillExists"),
            _ => { launched = true; return true; });

        Assert.Equal(FinalUninstallHandoffResult.OwnedRuntimeCleanupFailed, result);
        Assert.False(launched);
        Assert.True(File.Exists(settings));
        Directory.Delete(root, recursive: true);
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
