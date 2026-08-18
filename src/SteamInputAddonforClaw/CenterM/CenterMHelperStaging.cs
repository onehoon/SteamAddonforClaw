using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>Stages the dormant helper binary into an Addon-owned runtime directory under the
/// exact filename MSI's own identity checks look for ("MSI Center M.exe"). The source binary
/// (built as CenterMHelper.exe alongside the main app) is copied there rather than run directly
/// from the Velopack install/update payload, so staging failures cannot be attributed to an
/// in-place update in progress.</summary>
internal static class CenterMHelperStaging
{
    private const string SourceBinaryName = "CenterMHelper.exe";
    private const string StagedBinaryName = "MSI Center M.exe";

    internal static string RuntimeDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonForClaw", "Runtime", "CenterM");

    /// <summary>Copies the helper next to <paramref name="sourceDirectory"/>'s built binary into
    /// the runtime directory. Returns null (staging failed) rather than throwing -- callers must
    /// treat that as "helper start not permitted", not attempt to run it anyway.</summary>
    internal static string? Stage(string sourceDirectory)
    {
        try
        {
            var sourcePath = Path.Combine(sourceDirectory, SourceBinaryName);
            if (!File.Exists(sourcePath))
            {
                AppLog.Warn("CenterM.Helper", "Helper source binary not found.", null, ("SourcePath", sourcePath));
                return null;
            }

            Directory.CreateDirectory(RuntimeDirectory);
            var stagedPath = Path.Combine(RuntimeDirectory, StagedBinaryName);
            File.Copy(sourcePath, stagedPath, overwrite: true);
            return stagedPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Warn("CenterM.Helper", "Helper staging failed.", ex);
            return null;
        }
    }
}
