using System.Globalization;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class BootSessionTests
{
    [Fact]
    public void Cold_boot_and_restart_events_have_distinct_start_identities()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var marker = Path.Combine(directory, "a2vm-rumble-boot-attempt.txt");
            var firstBoot = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            var firstStartId = BootSession.TryCreateWindowsStartId(100, firstBoot, "0");
            Assert.NotNull(firstStartId);
            Assert.Equal(BootSessionAttemptResult.Claimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, firstStartId));

            var sameBootRestartId = BootSession.TryCreateWindowsStartId(100, firstBoot, "0x0");
            Assert.Equal(BootSessionAttemptResult.AlreadyClaimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, sameBootRestartId));

            var nextBoot = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
            var nextStartId = BootSession.TryCreateWindowsStartId(101, nextBoot, "0");
            Assert.Equal(BootSessionAttemptResult.Claimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, nextStartId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Hibernate_resume_event_does_not_create_a_new_windows_start_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var marker = Path.Combine(directory, "a2vm-rumble-boot-attempt.txt");
            var bootTime = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            var startId = BootSession.TryCreateWindowsStartId(100, bootTime, "0");

            Assert.Null(BootSession.TryCreateWindowsStartId(101, bootTime.AddHours(1), "2"));
            Assert.Equal(BootSessionAttemptResult.Claimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, startId));
            Assert.Equal(BootSessionAttemptResult.AlreadyClaimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, startId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Fast_startup_event_creates_a_new_start_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var marker = Path.Combine(directory, "a2vm-rumble-boot-attempt.txt");
            var coldBootId = BootSession.TryCreateWindowsStartId(
                100,
                new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc),
                "0");
            var fastStartupId = BootSession.TryCreateWindowsStartId(
                101,
                new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
                "0x1");

            Assert.Equal(BootSessionAttemptResult.Claimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, coldBootId));
            Assert.Equal(BootSessionAttemptResult.Claimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, fastStartupId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Unsupported_or_unavailable_windows_start_identity_fails_closed()
    {
        var time = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.Null(BootSession.TryCreateWindowsStartId(100, time, "2"));
        Assert.Null(BootSession.TryCreateWindowsStartId(100, time, "unexpected"));
        Assert.Null(BootSession.TryCreateWindowsStartId(null, time, "0"));
        Assert.Null(BootSession.TryCreateWindowsStartId(100, null, "0"));
    }

    [Fact]
    public void Legacy_attempt_timestamp_is_conservatively_migrated_without_repeating_the_cycle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var marker = Path.Combine(directory, "a2vm-rumble-boot-attempt.txt");
            var legacyAttempt = new DateTimeOffset(2026, 10, 10, 12, 5, 0, TimeSpan.Zero);
            var currentStartId = BootSession.TryCreateWindowsStartId(
                100,
                legacyAttempt.UtcDateTime.AddMinutes(-5),
                "0");
            File.WriteAllText(marker, legacyAttempt.ToString("O", CultureInfo.InvariantCulture));

            Assert.Equal(BootSessionAttemptResult.AlreadyClaimed,
                BootSession.TryClaimA2vmRumbleAttempt(marker, currentStartId));
            Assert.Equal(currentStartId, File.ReadAllText(marker));
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
                BootSession.TryClaimA2vmRumbleAttempt(marker, "v1:100:0:639009072000000000"));
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
                BootSession.TryClaimA2vmRumbleAttempt(marker, "v1:100:0:639009072000000000"));
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
                    "v1:100:0:639009072000000000"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
