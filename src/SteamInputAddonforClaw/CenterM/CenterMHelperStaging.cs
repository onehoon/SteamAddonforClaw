using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>Stages the dormant helper binary into an Addon-owned runtime directory under the
/// exact filename MSI's own identity checks look for ("MSI Center M.exe"). The source binary
/// (built as CenterMHelper.exe alongside the main app) is copied there rather than run directly
/// from the Velopack install/update payload, so staging failures cannot be attributed to an
/// in-place update in progress.</summary>
internal static class CenterMHelperStaging
{
    /// <summary>The exact subdirectory + filename CopyCenterMHelperOutput (in
    /// SteamInputAddonforClaw.csproj) publishes the helper to, alongside this app's own output/
    /// publish directory. This is the one place that layout contract is spelled out -- callers
    /// pass the publish/output root, never this subpath, so production and any test/smoke tooling
    /// can never drift out of sync with each other.</summary>
    private static readonly string SourceRelativePath = Path.Combine("CenterMHelperSource", "CenterMHelper.exe");
    private const string StagedBinaryName = "MSI Center M.exe";

    internal static string RuntimeDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonForClaw", "Runtime", "CenterM");

    /// <summary>Copies the helper from <paramref name="publishRoot"/> (this app's own output or
    /// publish directory -- the same root CopyCenterMHelperOutput writes
    /// CenterMHelperSource\CenterMHelper.exe into) into the runtime directory. Returns null
    /// (staging failed) rather than throwing -- callers must treat that as "helper start not
    /// permitted", not attempt to run it anyway.</summary>
    internal static string? StageFromPublishRoot(string publishRoot)
        => StageFromPublishRoot(publishRoot, RuntimeDirectory);

    internal static string? StageFromPublishRoot(string publishRoot, string runtimeDirectory)
    {
        try
        {
            var sourcePath = Path.Combine(publishRoot, SourceRelativePath);
            if (!File.Exists(sourcePath))
            {
                AppLog.Warn("CenterM.Helper", "Helper source binary not found.", null, ("SourcePath", sourcePath));
                return null;
            }

            lock (typeof(CenterMHelperStaging))
            {
                Directory.CreateDirectory(runtimeDirectory);
                var stagedPath = Path.Combine(runtimeDirectory, StagedBinaryName);

                if (File.Exists(stagedPath) && FilesHaveIdenticalContent(sourcePath, stagedPath))
                {
                    AppLog.Debug("CenterM.Helper", "Existing staged helper matches current source; reusing staged binary.");
                    return stagedPath;
                }

                File.Copy(sourcePath, stagedPath, overwrite: true);

                // A freshly written self-contained single-file executable, when created suspended
                // immediately afterward, was observed to intermittently fail its own bundle
                // self-extraction (falling back to an invalid framework-dependent-style DLL lookup) --
                // reproducibly fixed by forcing one full read of the file here, before the caller ever
                // creates it suspended, so any on-access scan/cache settles while the process is not
                // yet held stationary. See PR review discussion for the reproduction.
                _ = File.ReadAllBytes(stagedPath);

                return stagedPath;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Warn("CenterM.Helper", "Helper staging failed.", ex);
            return null;
        }
    }

    private static bool FilesHaveIdenticalContent(string firstPath, string secondPath)
    {
        var first = File.ReadAllBytes(firstPath);
        var second = File.ReadAllBytes(secondPath);
        return first.AsSpan().SequenceEqual(second);
    }
}
