using Microsoft.UI.Xaml.Controls;

namespace SteamInputAddonforClaw.Views;

/// <summary>Intentionally an empty shell in PR277 (work order section 13): establishes the top-level
/// navigation destination only. No game list, RunningAppID, or per-game profile behavior belongs
/// here yet.</summary>
public sealed partial class ProfilePage : UserControl
{
    public ProfilePage() => InitializeComponent();
}
