using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SettingsPageEnterBiosTests
{
    [Fact]
    public void Settings_card_contains_the_required_icon_and_is_between_power_source_and_components()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");

        Assert.Contains("x:Name=\"EnterBiosCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Enter BIOS\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Description=\"Restart the device and open BIOS settings.\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Enter BIOS\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsCard.HeaderIcon>", xaml, StringComparison.Ordinal);
        Assert.Contains("<SymbolIcon Symbol=\"Setting\" />", xaml, StringComparison.Ordinal);

        var powerSource = xaml.IndexOf("Show only current power source", StringComparison.Ordinal);
        var enterBios = xaml.IndexOf("x:Name=\"EnterBiosCard\"", StringComparison.Ordinal);
        var requiredComponents = xaml.IndexOf("Header=\"Required Components\"", StringComparison.Ordinal);
        Assert.True(powerSource >= 0 && enterBios > powerSource && requiredComponents > enterBios);
    }

    [Fact]
    public void Confirmation_precedes_the_single_enter_bios_rpc_and_uses_cancel_as_default()
    {
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs");
        var handlerStart = code.IndexOf("private async void EnterBiosButton_Click", StringComparison.Ordinal);
        var handlerEnd = code.IndexOf("\n    private async Task ShowEnterBiosFailureAsync", handlerStart, StringComparison.Ordinal);
        var handler = code[handlerStart..handlerEnd];

        Assert.Contains("Title = \"Enter BIOS?\"", handler, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Restart and Enter BIOS\"", handler, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = \"Cancel\"", handler, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", handler, StringComparison.Ordinal);
        Assert.Contains("The device will restart directly into BIOS settings.", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("MSI BIOS mode", handler, StringComparison.Ordinal);

        var confirmation = handler.IndexOf("await confirmation.ShowAsync()", StringComparison.Ordinal);
        var rpc = handler.IndexOf("RequestEnterBiosAsync", StringComparison.Ordinal);
        Assert.True(confirmation >= 0 && rpc > confirmation);
        Assert.Contains("!= ContentDialogResult.Primary", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("shutdown.exe", handler, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enter_bios_operation_guard_rejects_duplicate_submission_and_can_be_released()
    {
        var operationInProgress = 0;

        Assert.True(SettingsPage.TryBeginEnterBiosOperation(ref operationInProgress));
        Assert.False(SettingsPage.TryBeginEnterBiosOperation(ref operationInProgress));

        Volatile.Write(ref operationInProgress, 0);
        Assert.True(SettingsPage.TryBeginEnterBiosOperation(ref operationInProgress));
    }

    [Fact]
    public void Settings_page_does_not_start_shutdown_directly()
    {
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs");

        Assert.DoesNotContain("Process.Start", code, StringComparison.Ordinal);
        Assert.Contains("RequestEnterBiosAsync", code, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
