using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.BackButtons;

namespace SteamInputAddonforClaw.Views;

internal static class BackButtonMappingUiOptions
{
    internal static void AddTargets(ComboBox comboBox)
    {
        foreach (var target in Enum.GetValues<Xbox360BackButtonTarget>())
            comboBox.Items.Add(new ComboBoxItem { Content = Describe(target), Tag = target });
    }

    internal static bool TryGetSelectedTarget(ComboBox comboBox, out Xbox360BackButtonTarget target)
    {
        if (comboBox.SelectedItem is ComboBoxItem { Tag: Xbox360BackButtonTarget selected })
        {
            target = selected;
            return true;
        }

        target = default;
        return false;
    }

    internal static void SelectTarget(ComboBox comboBox, Xbox360BackButtonTarget target)
    {
        comboBox.SelectedItem = null;
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem { Tag: Xbox360BackButtonTarget candidate } && candidate == target)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    internal static string Describe(Xbox360BackButtonTarget target) => BackButtonMappingLabels.GetDisplayName(target);
}
