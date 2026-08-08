using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Steam;
using System.Reflection;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {GetDisplayVersion()}";
    }

    public void UpdateSteamSessionState(SteamSessionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = state.IsActive ? "Steam session active" : "Steam inactive";
            RunningAppIdText.Text = $"RunningAppID: {state.RunningAppId}";
        });
    }

    public void UpdateExternalControllerAssessment(ExternalControllerAssessment assessment)
    {
        ExternalControllerText.Text = assessment.Status switch
        {
            ExternalControllerAssessmentStatus.Clear => "External controller: None",
            ExternalControllerAssessmentStatus.ExternalPresent => "External controller: Detected",
            _ => "External controller: Indeterminate"
        };
    }

    private static string GetDisplayVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
    }
}
