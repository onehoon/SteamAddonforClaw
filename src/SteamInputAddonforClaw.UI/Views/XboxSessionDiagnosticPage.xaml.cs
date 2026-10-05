using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class XboxSessionDiagnosticPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private string? _reportPath;
    private bool _isActive;
    private bool _observerRunning;
    private bool _requestPending;
    private TaskCompletionSource? _requestCompletion;
    private Task? _stopOnLeaveTask;
    private int _capturePending;

    public event EventHandler? BackRequested;

    public XboxSessionDiagnosticPage() => InitializeComponent();

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    internal void Activate()
    {
        _isActive = true;
        _stopOnLeaveTask = null;
        RequestRefresh();
    }

    internal void Deactivate()
    {
        _isActive = false;
        _stopOnLeaveTask ??= StopAfterLeavingPageAsync();
    }

    internal void RequestRefresh()
    {
        if (!_isActive || _requestPending || Interlocked.Exchange(ref _capturePending, 1) != 0)
            return;
        _ = RefreshAsync();
    }

    internal async Task DeactivateAsync()
    {
        _isActive = false;
        if (_frontend is null)
            return;
        _stopOnLeaveTask ??= StopAfterLeavingPageAsync();
        await _stopOnLeaveTask.ConfigureAwait(true);
    }

    private void BackButton_Click(object sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);

    private async void StartButton_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || _requestPending)
            return;

        var request = BeginRequest();
        StatusText.Text = "Starting hooks and checking the foreground process…";
        try
        {
            Render(await _frontend.StartXboxSessionDiagnosticAsync().ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            LogRequestFailure("start", exception);
        }
        finally
        {
            EndRequest(request);
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || _requestPending)
            return;
        await StopAndRenderAsync().ConfigureAwait(true);
    }

    private async void CaptureReportButton_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || _requestPending)
            return;

        var request = BeginRequest();
        StatusText.Text = "Capturing the current diagnostic evidence…";
        try
        {
            var result = await _frontend.GenerateXboxSessionDiagnosticReportAsync().ConfigureAwait(true);
            Render(result.Snapshot);
            _reportPath = result.ReportPath;
            OpenReportButton.IsEnabled = !string.IsNullOrWhiteSpace(_reportPath);
            StatusText.Text = result.Outcome == FrontendXboxSessionDiagnosticReportOutcome.Created
                ? $"{result.Status} {_reportPath}"
                : $"{result.Status} {result.FailureMessage}";
        }
        catch (Exception exception)
        {
            LogRequestFailure("report capture", exception);
        }
        finally
        {
            EndRequest(request);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            if (_frontend is not null && _isActive && !_requestPending)
                Render(await _frontend.CaptureXboxSessionDiagnosticAsync().ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            LogRequestFailure("capture", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _capturePending, 0);
        }
    }

    private async Task StopAfterLeavingPageAsync()
    {
        await WaitForPendingRequestAsync().ConfigureAwait(true);
        await StopAndRenderAsync(forceStop: true).ConfigureAwait(true);
    }

    private async Task StopAndRenderAsync(bool forceStop = false)
    {
        if (_frontend is null)
            return;
        await WaitForPendingRequestAsync().ConfigureAwait(true);
        if (!forceStop && !_observerRunning)
            return;
        var request = BeginRequest();
        try
        {
            Render(await _frontend.StopXboxSessionDiagnosticAsync().ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            LogRequestFailure("stop", exception);
        }
        finally
        {
            EndRequest(request);
        }
    }

    private TaskCompletionSource BeginRequest()
    {
        _requestPending = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _requestCompletion = completion;
        UpdateButtons();
        return completion;
    }

    private void EndRequest(TaskCompletionSource completion)
    {
        _requestPending = false;
        if (ReferenceEquals(_requestCompletion, completion))
            _requestCompletion = null;
        completion.TrySetResult();
        UpdateButtons();
    }

    private Task WaitForPendingRequestAsync() => _requestCompletion?.Task ?? Task.CompletedTask;

    private void Render(FrontendXboxSessionDiagnosticSnapshot snapshot)
    {
        _observerRunning = snapshot.State == FrontendXboxSessionDiagnosticState.Running;
        StatusText.Text =
            $"Observer: {snapshot.State} — {snapshot.Status}\n" +
            $"Hooks: Foreground={snapshot.ForegroundHookInstalled}; Create={snapshot.CreateHookInstalled}; Show={snapshot.ShowHookInstalled}" +
            (snapshot.FailureMessage is { } failure ? $"\nFailure: {failure}" : string.Empty);

        ActiveGameText.Text = FormatGame(snapshot.ActiveGame, "Active XBOX game");
        PackagePathsText.Text = FormatPaths(snapshot.ActiveGame, "Active package path evidence")
            + (snapshot.LastPositiveGame is { } last && snapshot.ActiveGame?.CandidateKey != last.CandidateKey
                ? "\n\n" + FormatPaths(last, $"Last positive game path evidence ({last.CandidateKey})")
                : string.Empty);
        CountersText.Text =
            $"Accepted WinEvents: {snapshot.Counters.AcceptedWinEvents:N0}\n" +
            $"Unique process generations inspected: {snapshot.Counters.UniqueProcessGenerationsInspected:N0}\n" +
            $"Process open failures: {snapshot.Counters.ProcessOpenFailures:N0}\n" +
            $"Process image failures: {snapshot.Counters.ProcessImageFailures:N0}\n" +
            $"No-package candidates: {snapshot.Counters.NoPackageCandidates:N0}\n" +
            $"Package identity failures: {snapshot.Counters.PackageIdentityFailures:N0}\n" +
            $"Config-negative candidates: {snapshot.Counters.ConfigNegativeCandidates:N0}\n" +
            $"Executable mismatches: {snapshot.Counters.ExecutableMismatches:N0}\n" +
            $"Positive matches: {snapshot.Counters.PositiveMatches:N0}\n" +
            $"Process exits: {snapshot.Counters.ProcessExits:N0}";

        var lifecycle = new StringBuilder("Lifecycle evidence:");
        foreach (var entry in snapshot.LifecycleEvents.TakeLast(40))
            lifecycle.AppendLine().Append($"{entry.TimestampUtc:HH:mm:ss}  {entry.Event}  {entry.CandidateKey ?? string.Empty}  {entry.Detail ?? string.Empty}");
        if (snapshot.OmittedLifecycleEventCount > 0)
            lifecycle.AppendLine().Append($"Older events omitted: {snapshot.OmittedLifecycleEventCount:N0}");
        LifecycleText.Text = lifecycle.ToString();
        UpdateButtons();
    }

    private static string FormatGame(FrontendXboxSessionDiagnosticGame? game, string heading)
    {
        if (game is null)
            return $"{heading}: <none>";
        return $"{heading}: {game.IdentityName}\n" +
               $"Candidate key: {game.CandidateKey}\n" +
               $"PID: {game.ProcessId}\n" +
               $"Running process path: {game.RunningProcessPath}\n" +
               $"Running executable: {game.RunningExecutableName}\n" +
               $"Package full name: {game.PackageFullName}\n" +
               $"Package family name: {game.PackageFamilyName}\n" +
               $"AUMID: {game.ApplicationUserModelId ?? "<absent>"}\n" +
               $"StoreId: {game.StoreId ?? "<absent>"}; TitleId: {game.TitleId ?? "<absent>"}\n" +
               $"Identity: {game.IdentityName}; Publisher: {game.IdentityPublisher}; ResourceId: {game.IdentityResourceId ?? "<absent>"}\n" +
               $"Package identity: {game.PackageIdentityName ?? "<absent>"}; PublisherId: {game.PackageIdentityPublisherId ?? "<absent>"}; Architecture: {game.PackageIdentityArchitecture ?? "<absent>"}; Version: {game.PackageIdentityVersion ?? "<absent>"}\n" +
               $"Matched ExecutableList entry: {game.MatchedExecutableName}\n" +
               $"Config path: {game.ConfigPath}";
    }

    private static string FormatPaths(FrontendXboxSessionDiagnosticGame? game, string heading)
    {
        var text = new StringBuilder(heading);
        if (game is null)
            return text.AppendLine().Append("<none>").ToString();
        foreach (var path in game.PackagePaths)
            text.AppendLine().Append($"{path.PathType}: result={path.ResultCode}; path={path.Path ?? "<unavailable>"}");
        return text.ToString();
    }

    private void UpdateButtons()
    {
        StartButton.IsEnabled = !_requestPending && !_observerRunning;
        StopButton.IsEnabled = !_requestPending && _observerRunning;
        CaptureReportButton.IsEnabled = !_requestPending;
    }

    private void OpenReportButton_Click(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(_reportPath))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(_reportPath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("XboxSessionDiagnostic", "Diagnostic report could not be opened.", exception,
                ("Reason", exception.GetType().Name));
            StatusText.Text = "Report could not be opened. Use the path shown above from Explorer.";
        }
    }

    private static void LogRequestFailure(string action, Exception exception)
    {
        AppLog.Warn("XboxSessionDiagnostic", $"Main UI diagnostic {action} request failed.", exception,
            ("Reason", exception.GetType().Name));
    }
}
