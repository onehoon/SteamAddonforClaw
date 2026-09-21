using SteamInputAddonforClaw.ClawHud;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClawHudRuntimeLockTests
{
    private const string ValidLock = """
        {
          "schema_version": 1,
          "runtime_version": "1.0.1",
          "tag": "steamaddon-runtime-v1.0.1",
          "asset": "ClawHUDRuntime.zip",
          "source_commit": "717c57ea1812a874cf474f360faa01ed50cf39ea",
          "sha256": "9e9fc07c43db90837d01de6b24388a001eae1b3da222d1ee8dbf1dabcf0383f5"
        }
        """;

    [Fact]
    public void ValidLock_IsAccepted_AndBuildsExactUrl()
    {
        var path = WriteLock(ValidLock);
        try
        {
            var runtimeLock = ClawHudRuntimeLock.Load(path);
            Assert.Equal("1.0.1", runtimeLock.RuntimeVersion);
            Assert.Equal("https://github.com/onehoon/ClawHUD/releases/download/steamaddon-runtime-v1.0.1/ClawHUDRuntime.zip", runtimeLock.DownloadUri.ToString());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("v1.0.1")]
    [InlineData("1.0.1-beta")]
    [InlineData("1.0.1+build")]
    public void InvalidRuntimeVersion_IsRejected(string version) => Assert.Throws<InvalidDataException>(() => ClawHudRuntimeLock.Load(WriteLock(ValidLock.Replace("1.0.1", version, StringComparison.Ordinal))));

    [Fact]
    public void InvalidIdentityFields_AreRejected()
    {
        foreach (var invalid in new[]
        {
            ValidLock.Replace("\"schema_version\": 1", "\"schema_version\": 2", StringComparison.Ordinal),
            ValidLock.Replace("steamaddon-runtime-v1.0.1", "wrong-tag", StringComparison.Ordinal),
            ValidLock.Replace("ClawHUDRuntime.zip", "Other.zip", StringComparison.Ordinal),
            ValidLock.Replace("717c57ea1812a874cf474f360faa01ed50cf39ea", "short", StringComparison.Ordinal),
            ValidLock.Replace("9e9fc07c43db90837d01de6b24388a001eae1b3da222d1ee8dbf1dabcf0383f5", "short", StringComparison.Ordinal),
        })
        {
            var path = WriteLock(invalid);
            try { Assert.Throws<InvalidDataException>(() => ClawHudRuntimeLock.Load(path)); }
            finally { File.Delete(path); }
        }
    }

    private static string WriteLock(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"clawhud-lock-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, contents);
        return path;
    }
}
