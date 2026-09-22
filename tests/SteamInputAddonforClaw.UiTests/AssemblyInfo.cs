using Xunit;

// UI and Overlay tests share Windows App SDK and process-wide diagnostic state.
// Keep this test assembly serial so native surface initialization and shared test
// fixtures are never overlapped by the runner.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
