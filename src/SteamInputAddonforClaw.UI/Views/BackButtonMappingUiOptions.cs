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

    internal static string Describe(Xbox360BackButtonTarget target) => target switch
    {
        Xbox360BackButtonTarget.Disabled => "Disabled",
        Xbox360BackButtonTarget.A => "A",
        Xbox360BackButtonTarget.B => "B",
        Xbox360BackButtonTarget.X => "X",
        Xbox360BackButtonTarget.Y => "Y",
        Xbox360BackButtonTarget.DPadUp => "D-Pad Up",
        Xbox360BackButtonTarget.DPadRight => "D-Pad Right",
        Xbox360BackButtonTarget.DPadDown => "D-Pad Down",
        Xbox360BackButtonTarget.DPadLeft => "D-Pad Left",
        Xbox360BackButtonTarget.LeftBumper => "Left Bumper (LB)",
        Xbox360BackButtonTarget.RightBumper => "Right Bumper (RB)",
        Xbox360BackButtonTarget.LeftTrigger => "Left Trigger (LT)",
        Xbox360BackButtonTarget.RightTrigger => "Right Trigger (RT)",
        Xbox360BackButtonTarget.LeftStickClick => "Left Stick Click (L3)",
        Xbox360BackButtonTarget.RightStickClick => "Right Stick Click (R3)",
        Xbox360BackButtonTarget.View => "View",
        Xbox360BackButtonTarget.Menu => "Menu",
        Xbox360BackButtonTarget.XboxGuide => "Xbox Guide",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
    };
}
