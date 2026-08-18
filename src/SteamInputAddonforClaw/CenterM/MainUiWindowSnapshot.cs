namespace SteamInputAddonforClaw.CenterM;

internal readonly record struct MainUiWindowSnapshot(bool ProcessAlive, int RecognizedMainWindowCount, int VisibleMainWindowCount);

/// <summary>Pure recognition rule for a candidate MSI Center M MainUI top-level window, based on
/// the exact title/class observed in research (handoff section 11). Deliberately narrow so a tray
/// owner window (WindowsForms10.Window...) is never mistaken for the MainUI window.</summary>
internal static class MainUiWindowRecognition
{
    internal const string ExpectedTitle = "MSI Center M";
    internal const string ExpectedClassPrefix = "HwndWrapper[MSI Center M.exe;";

    internal static bool IsRecognizedMainUiWindow(string? title, string? className) =>
        string.Equals(title, ExpectedTitle, StringComparison.Ordinal)
        || (className?.StartsWith(ExpectedClassPrefix, StringComparison.Ordinal) ?? false);
}

/// <summary>Captures a fresh snapshot of a tracked process's recognized MainUI window state.
/// Returns null when the snapshot itself could not be determined (e.g. window enumeration
/// failure) -- callers must treat that as Uncertain, never default it to a specific lifecycle
/// state.</summary>
internal interface IMainUiWindowSnapshotProvider
{
    MainUiWindowSnapshot? Capture(int processId);
}
