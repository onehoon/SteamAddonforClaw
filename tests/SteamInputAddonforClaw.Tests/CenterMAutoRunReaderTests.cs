using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Contracts.Oem1;
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

    [Theory]
    [InlineData((int)CenterMAutoRunState.Disabled, false, true)]
    [InlineData((int)CenterMAutoRunState.Enabled, false, false)]
    [InlineData((int)CenterMAutoRunState.Unknown, true, false)]
    public void PendingAutoRunRestart_ReconcilesBeforeOem1Activation(
        int observed,
        bool expectedPending,
        bool expectedOwned)
    {
        var initial = new AppSettings
        {
            Oem1Mapping = Oem1MappingSettings.Default,
            CenterMAutoRunMutationPending = true,
            CenterMAutoRunOwnedByAddon = false,
            OriginalAutoRun = 1,
            AppliedAutoRun = 0
        };

        var result = CenterMAutoRunReader.ReconcilePendingState(initial, (CenterMAutoRunState)observed);

        Assert.Equal(expectedPending, result.CenterMAutoRunMutationPending);
        Assert.Equal(expectedOwned, result.CenterMAutoRunOwnedByAddon);
    }
}
