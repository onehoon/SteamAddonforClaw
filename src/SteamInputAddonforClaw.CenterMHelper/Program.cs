// Dormant OEM1 native-launch-suppression helper. Its actual running copy is staged and renamed to
// "MSI Center M.exe" by the Addon (see CenterMHelperStaging) -- its own project/assembly name
// stays CenterMHelper for build clarity. It performs no MSI/WMI/registry/network calls, creates no
// window/tray/pipe/child process, and simply blocks forever: lifetime is controlled entirely by
// its owner (the Addon) via a retained process handle inside a private Job Object with
// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, so an Addon crash reliably takes this process down too.
using var waitForever = new ManualResetEventSlim(false);
waitForever.Wait();
