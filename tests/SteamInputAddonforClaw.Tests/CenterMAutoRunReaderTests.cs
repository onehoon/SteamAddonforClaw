using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMAutoRunReaderTests
{
    [Fact]
    public void Zero_IsDisabled() =>
        Assert.Equal(CenterMAutoRunState.Disabled, CenterMAutoRunReader.Classify(0));

    [Fact]
    public void One_IsEnabled() =>
        Assert.Equal(CenterMAutoRunState.Enabled, CenterMAutoRunReader.Classify(1));

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData("1")]
    public void OtherValues_AreUnknown_NeverGuessedAsDisabled(object? rawValue) =>
        Assert.Equal(CenterMAutoRunState.Unknown, CenterMAutoRunReader.Classify(rawValue));
}
