using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class BootSessionTests
{
    [Fact]
    public void A2vm_rumble_attempt_is_claimed_once_per_boot_and_again_after_a_new_boot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var marker = Path.Combine(directory, "a2vm-rumble-boot-attempt.txt");
            var firstAttempt = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal(BootSessionAttemptResult.Claimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, firstAttempt, tickCount64: 60 * 60 * 1000));

            var sameBootRestart = firstAttempt.AddMinutes(10);
            Assert.Equal(BootSessionAttemptResult.AlreadyClaimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, sameBootRestart, tickCount64: 70 * 60 * 1000));

            var nextBoot = firstAttempt.AddHours(2);
            Assert.Equal(BootSessionAttemptResult.Claimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, nextBoot.AddMinutes(5), tickCount64: 5 * 60 * 1000));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_marker_fails_closed_without_overwriting_or_claiming_a_new_attempt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var marker = Path.Combine(directory, "a2vm-rumble-boot-attempt.txt");
            File.WriteAllText(marker, "not-a-timestamp");

            Assert.Equal(BootSessionAttemptResult.Unavailable,
                BootSession.TryClaimA2vmRumbleAttempt(marker, DateTimeOffset.UtcNow, tickCount64: 1));
            Assert.Equal("not-a-timestamp", File.ReadAllText(marker));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Oversized_marker_is_rejected_without_reading_or_overwriting_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var marker = Path.Combine(directory, "a2vm-rumble-boot-attempt.txt");
            var oversizedContents = new string('x', 129);
            File.WriteAllText(marker, oversizedContents);

            Assert.Equal(BootSessionAttemptResult.Unavailable,
                BootSession.TryClaimA2vmRumbleAttempt(marker, DateTimeOffset.UtcNow, tickCount64: 1));
            Assert.Equal(oversizedContents.Length, new FileInfo(marker).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Marker_io_failure_is_reported_as_unavailable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var occupiedPath = Path.Combine(directory, "occupied");
            File.WriteAllText(occupiedPath, "file, not a directory");

            Assert.Equal(BootSessionAttemptResult.Unavailable,
                BootSession.TryClaimA2vmRumbleAttempt(
                    Path.Combine(occupiedPath, "a2vm-rumble-boot-attempt.txt"),
                    DateTimeOffset.UtcNow,
                    tickCount64: 1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
