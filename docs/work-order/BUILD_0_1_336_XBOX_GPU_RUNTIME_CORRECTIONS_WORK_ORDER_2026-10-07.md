# Work Order — Build 0.1.336 XBOX / Intel GPU Runtime Corrections

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@03e26ee8cb9b457dd8f1fddea55becb3d51ed20a`  
> **Observed build:** `0.1.336.0`  
> **Field evidence:** Google Drive `Addon/Log/1007`  
> **Product architecture:** standalone Full1902  
> **PR shape:** one PR, three self-contained commits  
> **Primary scope:** correct one real Minimum GPU Clock factory-release verification bug, correct Intel FPS `ctlClose` success classification, and remove XBOX duplicate-generation debug spam without changing the proven session-detection behavior

---

## 1. Read first / authority

Read and preserve the current product contracts before editing:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md

docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md
docs/XBOX_GAME_PROFILE_ARCHITECTURE/work-order/PR7_XBOX_PRODUCTION_GAME_SESSION_RUNTIME_AND_DIAGNOSTIC_RETIREMENT_WORK_ORDER_2026-10-06.md
docs/work-order/PR9_NON_STEAM_GAME_DETECTION_FOUNDATION_WINDOWS_OBSERVATION_PROCESS_LIFETIME_WORK_ORDER_2026-10-06.md

docs/work-order/PR1_INTEL_MINIMUM_GPU_CLOCK_PRODUCTION_CORE_WORK_ORDER_2026-10-07.md
docs/work-order/PR4_GAME_ONLY_MINIMUM_GPU_CLOCK_FACTORY_RELEASE_POLICY_CORRECTION_WORK_ORDER_2026-10-07.md
docs/work-order/PR6_PRODUCTION_MINIMUM_GPU_CLOCK_FILE_ORGANIZATION_WORK_ORDER_2026-10-07.md
docs/work-order/IGCL_GPU_FREQUENCY_DEVELOPER_POC_WORK_ORDER_2026-10-06.md
~~~

Important constraints:

- Full1902 controller ownership is already working and is not part of this PR.
- XBOX active-game detection is already working on field hardware and must not be redesigned.
- Minimum GPU Clock remains a game-only override.
- No active enabled game override means factory minimum policy.
- Factory release still writes `min = -1`; this PR corrects verification of the driver's normalized readback.
- Do not share/refactor the FPS and Minimum GPU Clock IGCL native sessions.
- Do not create a common IGCL manager/owner.
- Do not add retry loops, epochs, debounce, timers, rate-limit state, or new lifecycle synchronization for the issues in this work order.

This is a targeted correctness/log-hygiene PR, not an architecture refactor.

---

## 2. 0.1.336 field evidence

Only `0.1.336.0` logs were used for this work order.

The long field session is:

~~~text
2026-10-07 19:40:05
Version=0.1.336.0
PID=15892
~~~

A second clean 0.1.336 Runtime starts at:

~~~text
2026-10-07 20:14:58
Version=0.1.336.0
PID=6344
~~~

The earlier `19:03` Runtime was `0.1.335.0` and is not evidence for this PR.

### 2.1 XBOX production runtime is functionally correct

The catalog found the two installed XBOX games:

~~~text
store:9NBLGGH2JHXJ
Microsoft.MinecraftUWP

store:9PK8PHLCQDF6
KingsgloryGames.AniimoLegend
~~~

During launch:

~~~text
gamelaunchhelper.exe
→ ExecutableMismatch
→ correctly rejected

Aniimo.exe
→ MicrosoftGame.config exact executable match
→ Positive XBOX active-game identity accepted
→ Key=store:9PK8PHLCQDF6
→ DisplayName="Aniimo Legend"
~~~

The XBOX identity then drove the normal profile path:

~~~text
ProfileTarget=Xbox:store:9PK8PHLCQDF6
→ XBOX profile mutation
→ TDP updates succeed
→ Minimum GPU Clock apply succeeds
~~~

Observed GPU apply:

~~~text
Operation=ApplyMinimum
Reason=XboxProfileMutation
RequestedMinMhz=2267
ReadbackMinMhz=2267
ReadbackMaxMhz=2300
Verified=True
Outcome=Succeeded
~~~

On game exit:

~~~text
Matched XBOX game process exited
→ active identity cleared
→ ProfileTarget=None
→ normal factory/global reconciliation
~~~

Do not change this ownership/session-selection behavior.

### 2.2 XBOX duplicate cache works, but its DEBUG log is excessively noisy

In the 0.1.336 long session:

~~~text
XboxSession lines:                         1197
duplicate-generation suppression lines:    991
initial NoPackage classifications:           63
initial ConfigNegative classifications:      36
positive active-game accepts:                 1
active-game clears:                           1
~~~

The important behavior is already correct:

~~~text
same PID + same creation-time generation
→ do not re-inspect package/config/image path
→ use cached result
~~~

The problem is only this line being emitted on every duplicate callback:

~~~text
Duplicate event for an already classified process generation was suppressed.
~~~

One cached NoPackage generation alone produced hundreds of identical suppression messages.

The original classification already records the meaningful result. Re-logging every duplicate event adds no diagnostic value.

### 2.3 Minimum GPU Clock factory release is physically succeeding but verification reports failure

Repeated 0.1.336 evidence:

~~~text
Operation=ReleaseToFactoryMinimum
RequestedMinMhz=-1
PreWriteMinMhz=2267
PreWriteMaxMhz=2300
NativeSetResult=0x00000000

ReadbackMinMhz=100
ReadbackMaxMhz=2300

Verified=False
Failure=ReadbackMismatch
~~~

The same pattern also occurs from an already-factory state:

~~~text
PreWriteMinMhz=100
RequestedMinMhz=-1
ReadbackMinMhz=100
NativeSetResult=0x00000000
Failure=ReadbackMismatch
~~~

The discovered B390 capability in the same Runtime says:

~~~text
HardwareMinMhz=100
HardwareMaxMhz=2300
CanControl=True
~~~

Therefore the driver accepted `min=-1` and normalized the readback to its real hardware minimum, `100 MHz`.

Current policy incorrectly requires:

~~~text
requested < 0
→ readback must also be < 0
~~~

That is not what the field driver returns.

This is not only a false warning. `PrepareForUninstall()` trusts the same verification result, and `AddonProcessHost.PrepareForUninstallAsync()` blocks uninstall preparation when the release is reported as failed.

So the current false mismatch can turn a physically successful factory release into:

~~~text
GpuMinimumClockReleaseFailed
→ uninstall preparation blocked
~~~

This is a real correctness bug and is the highest-priority fix in this PR.

### 2.4 Intel FPS `ctlClose=1` is a success-with-information result

0.1.336 successfully enabled and disabled FRAME_LIMIT:

~~~text
Enable 116 FPS → SetResult=0x00000000
Disable         → SetResult=0x00000000
~~~

At controlled Runtime shutdown:

~~~text
[WARN] [Profiles.IntelFps]
ctlClose failed.
Result=0x00000001
~~~

The official Intel IGCL result contract defines:

~~~text
CTL_RESULT_SUCCESS = 0x00000000
CTL_RESULT_SUCCESS_STILL_OPEN_BY_ANOTHER_CALLER = 0x00000001
~~~

The repository already documents this exact distinction in:

~~~text
docs/work-order/IGCL_GPU_FREQUENCY_DEVELOPER_POC_WORK_ORDER_2026-10-06.md
docs/work-order/PR1_INTEL_MINIMUM_GPU_CLOCK_PRODUCTION_CORE_WORK_ORDER_2026-10-07.md
~~~

Result `1` is valid for close/shared-caller semantics and must not be logged as a failure.

Important:

> Do not generalize result `1` as success for mutation APIs.

For `SetRange`, FRAME_LIMIT set, enumeration, etc., retain their current result handling unless their own API contract says otherwise.

---

# 3. PR structure — one PR, three commits

Implement this as one PR with exactly three logical commits.

Recommended order:

~~~text
Commit 1
fix(gpu): accept normalized factory minimum readback

Commit 2
fix(igcl): classify shared-caller ctlClose result as success

Commit 3
chore(xbox): remove duplicate-generation debug spam
~~~

Each commit should include its own tests where practical and should leave the tree buildable.

Do not combine all edits into one commit.

Do not create a fourth refactor/cleanup commit.

---

# 4. Commit 1 — Correct Minimum GPU Clock factory-release verification

## 4.1 Files

Expected production files:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClockPolicy.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClockRuntime.cs
~~~

Expected test file:

~~~text
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockTests.cs
~~~

Do not change the native control ABI in:

~~~text
IntelGpuMinimumClockControl.cs
~~~

## 4.2 Preserve the write contract

Keep this behavior exactly:

~~~text
factory release
→ request Min = -1
→ preserve the current Max semantics
→ SetRange
→ read back
→ verify
~~~

Do not replace `-1` with `HardwareMinMhz` in the write request.

The driver must remain free to interpret the factory/unmanaged sentinel.

The bug is the readback verifier, not the request.

## 4.3 Separate normal explicit-minimum verification from factory-release verification

Current generic matching:

~~~csharp
internal static bool MatchesReadback(
    IntelGpuFrequencyRange request,
    IntelGpuFrequencyRange preWrite,
    IntelGpuFrequencyRange readback) =>
    MatchesRangeSide(request.Min, readback.Min)
    && MatchesRangeSide(preWrite.Max >= 0 ? preWrite.Max : -1, readback.Max);

private static bool MatchesRangeSide(double requested, double actual) =>
    double.IsFinite(actual)
    && (requested < 0
        ? actual < 0
        : Math.Abs(requested - actual) <= FrequencyToleranceMhz);
~~~

Keep this strict behavior for normal explicit minimum writes.

Add a narrow factory-release verifier.

Recommended shape:

~~~csharp
internal static bool MatchesFactoryMinimumReleaseReadback(
    IntelGpuFrequencyRange preWrite,
    IntelGpuFrequencyRange readback,
    double hardwareMinimumMhz)
{
    if (!double.IsFinite(hardwareMinimumMhz)
        || !double.IsFinite(readback.Min)
        || !double.IsFinite(readback.Max))
        return false;

    var minimumReleased =
        readback.Min < 0
        || Math.Abs(readback.Min - hardwareMinimumMhz) <= FrequencyToleranceMhz;

    return minimumReleased
        && MatchesRangeSide(
            preWrite.Max >= 0 ? preWrite.Max : -1,
            readback.Max);
}
~~~

Exact naming may vary, but keep the semantics narrow:

~~~text
Factory minimum request was -1

Success if:
    driver returns negative/unmanaged sentinel
OR
    driver normalizes the released minimum to the already-discovered HardwareMinMhz

AND
    the preserved Max still verifies
~~~

Do not accept an arbitrary lower positive value.

For the observed B390 case:

~~~text
HardwareMinMhz=100
ReadbackMinMhz=100
→ success
~~~

For an external floor still left at:

~~~text
ReadbackMinMhz=1500
HardwareMinMhz=100
→ failure
~~~

## 4.4 Use the factory-release verifier only in `ReleaseToFactoryMinimum`

Current:

~~~csharp
readback = _control.GetRange();
var verified = IntelGpuMinimumClockPolicy.MatchesReadback(
    request,
    preWrite.Value,
    readback.Value);
~~~

Change the factory-release path to use the capability already loaded by the Runtime.

Recommended shape:

~~~csharp
readback = _control.GetRange();
var verified =
    IntelGpuMinimumClockPolicy.MatchesFactoryMinimumReleaseReadback(
        preWrite.Value,
        readback.Value,
        capability.HardwareMinMhz);
~~~

Do not change `ApplyMinimum(...)`.

Explicit minimum application must continue requiring the requested explicit minimum on readback.

## 4.5 Keep fail-close uninstall semantics

Do not weaken:

~~~text
SetRange native failure
→ release fails

readback still shows a non-factory external minimum
→ release fails

invalid/non-finite readback
→ release fails

capability unavailable after allowed reinitialize
→ uninstall preparation fails closed
~~~

Only correct the false-negative normalized-factory-minimum case.

## 4.6 Tests

Update the old policy expectation that currently treats a positive normalized minimum as automatically wrong.

Required tests:

~~~text
factory release accepts negative sentinel readback

factory release accepts HardwareMinMhz normalized readback
    request -1
    hardware minimum 100
    readback 100
    preserved max unchanged
    → Verified=True

factory release rejects a positive non-hardware floor
    hardware minimum 100
    readback 1500
    → Verified=False

factory release still rejects preserved-max mismatch
~~~

Add a Runtime-level regression that reproduces the real field behavior.

Recommended test:

~~~csharp
[Fact]
public void Factory_release_accepts_driver_normalized_hardware_minimum()
{
    using var temp = new TemporaryDirectory();

    var fake = new FakeControl(new(2267, 2300))
    {
        Capability = new(
            true,
            null,
            "Intel(R) Arc(TM) B390 GPU",
            0x8086,
            0xB080,
            true,
            100,
            2300,
            SelectableClocks),
        SetRangeReadback = requested =>
            requested with { Min = 100 }
    };

    using var runtime =
        CreateRuntime(temp, fake, () => AcDcPowerSource.AC);

    runtime.InitializeReadOnly();

    var result =
        runtime.ReleaseToFactoryMinimum("UnitTest");

    Assert.True(result.Succeeded);
    Assert.True(result.Verified);
    Assert.Equal(-1, result.RequestedMinMhz);
    Assert.Equal(100, result.ReadbackRange!.Value.Min);
    Assert.Equal(2300, result.ReadbackRange.Value.Max);
}
~~~

Also update the uninstall regression so the same normalized readback is accepted by:

~~~text
PrepareForUninstall()
~~~

This is important because uninstall fail-close is where the false mismatch becomes user-visible functionality.

Do not remove the existing test proving a genuine readback mismatch fails.

---

# 5. Commit 2 — Correct Intel FPS `ctlClose` success classification

## 5.1 Files

Expected production file:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelFrameLimiter.cs
~~~

Expected test file:

~~~text
tests/SteamInputAddonforClaw.Tests/IntelFrameLimiterTests.cs
~~~

## 5.2 Current bug

Current `NativeIgcl.Dispose()` sends every nonzero close result through the generic failure logger:

~~~csharp
public void Dispose()
{
    if (_closed) return;
    _closed = true;

    if (_api != 0)
    {
        var result = _close(_api);
        Log("ctlClose", result);
        _api = 0;
    }

    ...
}
~~~

And the generic logger treats all nonzero results as WARN/failure.

That is wrong specifically for:

~~~text
0x00000001
CTL_RESULT_SUCCESS_STILL_OPEN_BY_ANOTHER_CALLER
~~~

## 5.3 Required change

Keep the close handling local to `NativeIgcl`.

Recommended shape:

~~~csharp
private const uint CtlResultSuccess = 0x00000000;
private const uint CtlResultSuccessStillOpenByAnotherCaller = 0x00000001;

internal static bool IsSuccessfulCloseResultForTests(uint result) =>
    result is CtlResultSuccess
        or CtlResultSuccessStillOpenByAnotherCaller;

private static bool IsSuccessfulCloseResult(uint result) =>
    result is CtlResultSuccess
        or CtlResultSuccessStillOpenByAnotherCaller;
~~~

Or one internal helper may be used directly if that is cleaner.

Dispose:

~~~csharp
var result = _close(_api);

if (result == CtlResultSuccessStillOpenByAnotherCaller)
{
    AppLog.Debug(
        "Profiles.IntelFps",
        "ctlClose completed; IGCL remains open by another caller.",
        ("Operation", "ctlClose"),
        ("Result", $"0x{result:X8}"));
}
else if (!IsSuccessfulCloseResult(result))
{
    Log("ctlClose", result);
}
~~~

A successful zero result does not need a new log.

The success-with-information result may remain DEBUG for field visibility.

## 5.4 Do not change generic IGCL result semantics

Do not change:

~~~csharp
if (result != 0)
~~~

globally in the existing generic logger.

Do not make `0x00000001` a blanket success for:

- FRAME_LIMIT set;
- frequency `SetRange`;
- enumeration;
- property reads;
- capability reads;
- other IGCL mutations.

The repository's existing Minimum GPU Clock work explicitly requires strict mutation success.

This commit is only about `ctlClose`.

## 5.5 Tests

Add a small pure-result test using the existing `NativeIgcl` test seam style.

Recommended:

~~~csharp
[Theory]
[InlineData(0x00000000u, true)]
[InlineData(0x00000001u, true)]
[InlineData(0x40000001u, false)]
[InlineData(0x40000003u, false)]
public void Ctl_close_accepts_both_official_success_results(
    uint result,
    bool expected)
{
    Assert.Equal(
        expected,
        NativeIgcl.IsSuccessfulCloseResultForTests(result));
}
~~~

Do not build a fake `ControlLib.dll`, native shim, or new dependency injection layer merely to test this branch.

---

# 6. Commit 3 — Remove XBOX duplicate-generation DEBUG spam

## 6.1 Files

Expected production file:

~~~text
src/SteamInputAddonforClaw/Xbox/Session/XboxGameSessionRuntime.cs
~~~

Tests should only change if needed to preserve the existing cache contract:

~~~text
tests/SteamInputAddonforClaw.Tests/XboxGameSessionRuntimeTests.cs
~~~

## 6.2 Keep the cache behavior exactly as-is

Current duplicate-generation branch conceptually does:

~~~csharp
if (existing.Generation.Key == generation.Key)
{
    generation.Dispose();

    AppLog.Debug(
        "XboxSession",
        "Duplicate event for an already classified process generation was suppressed.",
        ...);

    if (existing.Inspection?.Match is { } cachedMatch
        && _activeGame is null)
    {
        ...
    }

    return;
}
~~~

Remove only the per-event duplicate DEBUG emission.

Recommended final shape:

~~~csharp
if (existing.Generation.Key == generation.Key)
{
    generation.Dispose();

    if (existing.Inspection?.Match is { } cachedMatch
        && _activeGame is null)
    {
        ...
    }

    return;
}
~~~

Do not change the cached-match reactivation behavior.

Do not change PID reuse handling.

Do not change process-generation identity.

Do not change the initial classification logs.

Do not change active-game acceptance/clear logs.

## 6.3 Why removal is preferable to rate limiting

The useful evidence already exists on first classification:

~~~text
NoPackage
ConfigNegative
ExecutableMismatch
Matched / Positive XBOX active-game identity accepted
~~~

Every later duplicate event carries no new classification information.

Therefore do not add:

- per-PID counters;
- timestamps;
- log windows;
- rate limiter dictionaries;
- periodic summaries;
- debounce;
- another cache layer;
- a global event-suppression service.

The existing process-generation cache is already the correct owner.

This commit should make the hot duplicate path quieter, not more complex.

## 6.4 Tests

Preserve the existing production contract:

~~~text
same PID + same creation time
→ does not re-inspect

PID reuse with a new creation time
→ retires old generation
→ evaluates new generation

duplicate Matched callback
→ does not emit a second active-game change

matched process exit
→ clears exactly once
~~~

If existing tests already prove these behaviors, do not add redundant structural tests solely because one log statement was removed.

If cheap using the existing `AppLog` test support, one regression may assert that repeating an already-classified generation does not produce the removed message.

Do not restructure `XboxGameSessionRuntime` to make a logging assertion injectable.

---

# 7. Explicitly reviewed items that are NOT changes in this PR

The 0.1.336 logs were also reviewed for the following.

## 7.1 XBOX catalog repeated scans

Observed:

~~~text
scan requested: 8
scan completed: 6
accepted identities repeated as UI/catalog refresh occurred
~~~

No incorrect catalog result, stale identity, crash, or profile corruption was observed.

Do not add scan coalescing/debounce/state in this PR merely because multiple UI refreshes can request a catalog scan.

That requires separate evidence of user-visible cost or correctness impact.

## 7.2 VIIPER / USBIP disconnect messages

Observed detach/re-attach transitions completed successfully through the native IOCTL and canonical tracked paths.

Messages such as:

~~~text
URB completion after disconnect
connection was aborted by the software in your host machine
~~~

occur immediately after intentional detach and are followed by successful reconnect/attach.

No VIIPER change is required here.

Do not touch:

~~~text
src/.../VirtualOutput/*
USBIP ownership
attach/detach sequencing
presentation switching
~~~

## 7.3 SteamDeck/Xbox360 presentation switching

0.1.336 field evidence showed normal:

~~~text
Xbox360 → SteamDeck
Steam game becomes active

SteamDeck → Xbox360
Steam game exits
~~~

Publisher heartbeat remained around 250 Hz with:

~~~text
SetStateFailures=0
SkippedDeadlineCount=0
~~~

Do not change presentation routing.

## 7.4 Overlay

The 0.1.336 Overlay ran for the long session without WARN/ERROR and shut down cleanly before the controlled Runtime restart.

No Overlay change is required.

---

# 8. Expected final diff

Expected production scope:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClockPolicy.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClockRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelFrameLimiter.cs
src/SteamInputAddonforClaw/Xbox/Session/XboxGameSessionRuntime.cs
~~~

Expected tests:

~~~text
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockTests.cs
tests/SteamInputAddonforClaw.Tests/IntelFrameLimiterTests.cs

tests/SteamInputAddonforClaw.Tests/XboxGameSessionRuntimeTests.cs
    only if a narrow existing test needs updating/extension
~~~

No expected changes to:

~~~text
Full1902 controller ownership
HidHide
DirectInput
VIIPER / USBIP
ControllerPresentation
SteamOutput publisher
Rumble
Xbox catalog identity format
Profile persistence schema
Frontend protocol
Overlay protocol
TDP
Center M authority
startup task
SafeUninstall sequencing
~~~

If the implementation starts touching those areas, stop and re-check scope.

---

# 9. Validation

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
~~~

Then perform focused hardware validation on the MSI Claw.

## A. Startup with no active GPU-clock profile

Expected:

~~~text
Minimum GPU Clock capability initialized
HardwareMinMhz=100

ReleaseToFactoryMinimum
RequestedMinMhz=-1
ReadbackMinMhz=100
Verified=True
Outcome=Succeeded
~~~

Not expected:

~~~text
Failure=ReadbackMismatch
~~~

## B. Steam game with Minimum GPU Clock enabled

Example:

~~~text
ApplyMinimum
RequestedMinMhz=2267
ReadbackMinMhz=2267
Verified=True
~~~

Exit game:

~~~text
ReleaseToFactoryMinimum
RequestedMinMhz=-1
ReadbackMinMhz=100
Verified=True
~~~

## C. XBOX game

Use the same Aniimo path proven by 0.1.336.

Expected:

~~~text
gamelaunchhelper.exe
→ ExecutableMismatch

Aniimo.exe
→ Positive XBOX active-game identity accepted

ProfileTarget=Xbox:store:9PK8PHLCQDF6
→ profile applies
→ GPU minimum applies when enabled

Aniimo.exe exits
→ active identity cleared
→ factory minimum release verifies successfully
~~~

## D. Controlled Runtime restart

Expected FPS shutdown:

~~~text
ctlClose result 0
→ no warning

OR

ctlClose result 1
→ no warning
→ optional DEBUG success-with-information line
~~~

Not expected:

~~~text
[WARN] ctlClose failed. Result=0x00000001
~~~

## E. XBOX event volume

Normal Windows activity may still generate many WinEvents.

Expected:

~~~text
initial candidate classifications remain visible
positive XBOX match remains visible
active clear remains visible
~~~

Not expected:

~~~text
hundreds of:
Duplicate event for an already classified process generation was suppressed.
~~~

The dedupe behavior itself must remain active.

---

# 10. Acceptance criteria

## Commit 1 — GPU factory release

- [ ] Factory release still writes `Min=-1`.
- [ ] Explicit maximum preservation is unchanged.
- [ ] Explicit `ApplyMinimum` readback verification is unchanged.
- [ ] A negative factory-minimum readback still verifies.
- [ ] A readback equal to discovered `HardwareMinMhz` verifies for factory release.
- [ ] A positive floor above the hardware minimum still fails verification.
- [ ] Native SetRange failures still fail closed.
- [ ] Uninstall no longer blocks merely because the driver normalized `-1` to `HardwareMinMhz`.
- [ ] Genuine uninstall release failures still block uninstall.

## Commit 2 — IGCL close

- [ ] `ctlClose=0` remains success.
- [ ] `ctlClose=1` is treated as success-with-information.
- [ ] `ctlClose=1` no longer emits WARN/failure.
- [ ] Actual IGCL close error values still emit WARN.
- [ ] No mutation API starts accepting `1` as success because of this change.
- [ ] No shared IGCL session/manager is introduced.

## Commit 3 — XBOX logging

- [ ] Same-generation duplicate callbacks still skip re-inspection.
- [ ] Cached matched-generation behavior remains unchanged.
- [ ] PID-reuse behavior remains unchanged.
- [ ] Active XBOX identity selection/clear behavior remains unchanged.
- [ ] The per-duplicate DEBUG message is removed.
- [ ] No rate limiter, timer, counter manager, or additional event state is added.

## Whole PR

- [ ] Full1902 controller lifecycle is unchanged.
- [ ] XBOX game/profile behavior remains functionally identical to the successful 0.1.336 field path.
- [ ] Steam presentation routing remains unchanged.
- [ ] VIIPER/HidHide/DirectInput ownership is unchanged.
- [ ] All tests pass.
- [ ] Final diff stays within the narrow files listed above.

---

# 11. Review guidance

Review this PR against actual field behavior, not theoretical interleavings.

Blocking findings should be limited to realistic regressions such as:

- factory release can still falsely fail on the observed 100 MHz readback;
- uninstall can still be blocked by that false failure;
- explicit GPU minimum writes become under-validated;
- real GPU release failures become accepted;
- `ctlClose` real errors become hidden;
- IGCL result `1` is incorrectly generalized to mutation APIs;
- XBOX duplicate cleanup accidentally changes generation caching, active-game selection, or process-exit ownership;
- controller/VIIPER/Full1902 code changes without necessity.

Do not require new synchronization, retry machinery, shared IGCL ownership, event-rate state, or generalized logging abstractions for this PR.

The intended result is deliberately simple:

~~~text
factory release
→ accept the driver's real normalized factory minimum

ctlClose
→ understand Intel's documented success-with-information result

duplicate XBOX WinEvent
→ suppress work silently using the cache that already exists
~~~
