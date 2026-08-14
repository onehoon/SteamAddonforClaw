using Xunit;

// AppLog (Diagnostics/AppLog.cs) is one process-wide singleton: DirectoryOverride, MinimumLevelOverride,
// and the single background writer/queue are all shared global state. [Collection("AppLog", ...)] only
// serializes tests within that one named collection against each other -- it does nothing to stop an
// unrelated, untagged test class (e.g. one that exercises production code calling AppLog.Info/Warn
// indirectly, like EffectiveSteamSessionSourceTests) from running fully in parallel in a different
// collection and racing the same writer's one open file handle. That cross-collection race was the actual
// root cause of an intermittent CI failure (see PR #153): under full-suite parallel execution, a test's
// own AppLog.DrainForTests() could close the writer's file just before an unrelated, concurrently-running
// test's log entry reopened it against a different directory, so a File.ReadAllText immediately after
// drain would occasionally race a handle that no longer pointed where it should.
//
// Disabling parallelization for the whole assembly is the complete fix: it guarantees no two tests, in
// any collection, ever touch AppLog concurrently. Retrying the flaky read only reduced the odds of
// hitting the window; this removes the window itself.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
