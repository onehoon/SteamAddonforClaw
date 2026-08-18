namespace SteamInputAddonforClaw.FrontendTransport;

/// <summary>
/// Applies the product-wide retention policy for Runtime and external UI log files.
/// The dedicated Addon log directory keeps only the current local calendar day and
/// the immediately preceding local calendar day.
/// </summary>
public static class AddonLogRetention
{
    public const int RetentionDays = 2;

    public static void PruneDirectory(string directory) => PruneDirectory(directory, DateTime.Now.Date);

    internal static void PruneDirectory(string directory, DateTime today)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var oldestRetainedDate = today.Date.AddDays(-(RetentionDays - 1));
            foreach (var path in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTime(path).Date < oldestRetainedDate)
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                    // Best effort only. A log may still be held by another process.
                }
                catch (UnauthorizedAccessException)
                {
                    // Logging maintenance must never prevent application startup.
                }
            }
        }
        catch (IOException)
        {
            // Logging maintenance must never prevent application startup.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging maintenance must never prevent application startup.
        }
    }
}
