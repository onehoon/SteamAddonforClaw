using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMHelperInvariantTests
{
    [Fact]
    public void HelperOnly_IsValid() =>
        Assert.Equal(CenterMHelperInvariantState.Valid,
            CenterMHelperInvariant.Evaluate([new ProcessSnapshotEntry(1, "MSI Center M", null)], ownedHelperProcessId: 1));

    [Fact]
    public void RealOnly_IsForeignProcessDetected() =>
        Assert.Equal(CenterMHelperInvariantState.ForeignProcessDetected,
            CenterMHelperInvariant.Evaluate([new ProcessSnapshotEntry(2, "MSI Center M", null)], ownedHelperProcessId: 1));

    [Fact]
    public void HelperPlusReal_IsMultipleSameNameProcesses() =>
        Assert.Equal(CenterMHelperInvariantState.MultipleSameNameProcesses,
            CenterMHelperInvariant.Evaluate([new ProcessSnapshotEntry(1, "MSI Center M", null), new ProcessSnapshotEntry(2, "MSI Center M", null)], ownedHelperProcessId: 1));

    [Fact]
    public void MultipleForeign_IsMultipleSameNameProcesses() =>
        Assert.Equal(CenterMHelperInvariantState.MultipleSameNameProcesses,
            CenterMHelperInvariant.Evaluate([new ProcessSnapshotEntry(2, "MSI Center M", null), new ProcessSnapshotEntry(3, "MSI Center M", null)], ownedHelperProcessId: 1));

    [Fact]
    public void ZeroProcesses_IsHelperMissing() =>
        Assert.Equal(CenterMHelperInvariantState.HelperMissing,
            CenterMHelperInvariant.Evaluate([], ownedHelperProcessId: 1));

    [Fact]
    public void EnumerationError_IsUncertain() =>
        Assert.Equal(CenterMHelperInvariantState.Uncertain,
            CenterMHelperInvariant.Evaluate(null, ownedHelperProcessId: 1));
}
