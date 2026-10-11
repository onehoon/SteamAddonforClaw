# Work Order — A2VM 0.1.361 Windows Session-End: Eliminate Cold Physical Rumble STOP Endpoint Discovery Latency

**Date:** 2026-10-11  
**Repository:** \`onehoon/SteamAddonforClaw\`  
**Target:** latest \`main\` after PR #752 (build 0.1.361.0)  
**Priority:** P1, actual Windows shutdown reliability  
**Implementation:** local Codex, one focused PR  
**Product:** standalone Full PID1902 application. **No CTW integration.**

## 1. Decision

Implement **one narrow, safe optimization**: on the already-owned PID1902 DirectInput session, prefer resolving the physical-rumble STOP endpoint through the **existing exact, strongly verified DirectInput HID interface path**, rather than performing a cold, machine-wide HID-interface discovery during genuine Windows session-end shutdown.

Keep the existing full catalog resolver as a fallback when the exact shortcut cannot be proven. Preserve the native write / retry / STOP confirmation contracts. Do **not** lengthen the global Windows shutdown deadline as a first response and do not change the A2VM boot-rumble prime policy.

This is about reducing **shutdown time**. It is **not** a proposal to remove \`1902 -> 1901 -> 1902\` boot priming. The 361 logs do not establish whether a one-leg \`1901 -> 1902\` boot always rearms the firmware motor: rumble re-arm is not necessarily required on every boot, and these runs recorded only zero-amplitude STOP writes, not a validated nonzero motor response. Do not make a re-arm policy decision based on the absence of a symptom.

## 2. Mandatory architecture and source reading

Read the active documents, in the precedence specified in \`docs/Full 1902 Implementation/README.md\`:

- \`docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md\`
- \`docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md\`
- \`docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md\`
- \`docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md\`
- \`docs/work-order/2026-10-11_A2VM_BOOT_ACQUISITION_AND_SESSION_END_SIMPLIFICATION_TWO_COMMIT_WORK_ORDER.md\` and its merged implementation PR #752.
- \`docs/work-order/FULL1902_PRODUCTION_RUMBLE_FEEDBACK_WORK_ORDER.md\`
- \`docs/work-order/FULL1902_XBOX360_RUMBLE_LATCH_SAFETY_STOP_HOTFIX_WORK_ORDER.md\`
- \`docs/work-order/FULL1902_PHYSICAL_RUMBLE_STOP_RETRY_HARDENING_WORK_ORDER_2026-09-25.md\`

The current controller architecture continues to have **one** physical owner, **one** presentation owner, **one** native VIIPER authority, **one** owned DirectInput generation, and the established HidHide baseline. The narrow Windows session-end mode write is still only a best-effort, no-PnP-wait exception after safely proven retirement.

### Primary production code to inspect

1. \`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumbleSink.cs\`
   - \`SetRumbleCore\`, cached endpoint and physical session generation, \`BeginPhysicalSessionRetirement\`, \`BeginPhysicalSession\`, \`InvalidatePhysicalSession\`, STOP retry and confirmed-write semantics.
2. \`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumbleEndpointResolver.cs\`
   - \`MsiClawRumbleEndpointResolver.Resolve\`, \`MsiClawRumbleEndpointResolution\` and the existing injected catalog test seam.
3. \`src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRumbleEndpointCatalog.cs\`
   - \`Find\` currently calls \`DeviceInformation.FindAllAsync\` over the whole HID interface class and then \`EnumeratePresentDevices()\`, followed by per-candidate HID capability/open validation.
   - \`TryReadHidCapabilities\` already implements compatible \`GENERIC_READ|GENERIC_WRITE\` -> \`GENERIC_WRITE\` fallback and reads the **actual** report lengths and HID usage.
4. \`src/SteamInputAddonforClaw/Input/DirectInput/VorticeDirectInputDeviceEnumerator.cs\`
   - \`inspectionDevice.Properties.InterfacePath\` becomes the selected \`DirectInputDeviceDescriptor.DevicePath\`.
5. \`src/SteamInputAddonforClaw/Input/DirectInput/DirectInputDeviceTopologyResolver.cs\`
   - the selected interface path is already bound to an exact PnP instance, MSI PID1902, gamepad usage and a physical USB root.
6. \`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs\`
   - \`MsiClawPhysicalInputIdentity(InstanceGuid, DevicePath, PnpInstanceId, PhysicalIdentity)\`.
7. \`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs\`
   - \`PublishLivePhysicalSession\` already publishes the verified descriptor identity and increments the generation only for a new real DirectInput session.
   - \`PrepareForWindowsSessionEndAsync\` proves current exact physical ownership, stops DirectInput and only then resolves the PID1902 command HID and writes XInput.
8. \`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs\`
   - \`RetireActivePresentationCoreAsync\`, \`RetireAsync("WindowsSessionEnd")\`, feedback disarm/drain, typed VIIPER detach, \`Closed\`, physical STOP.
9. \`src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs\`
   - \`HandleWindowsSessionEnd\` and its **shared 2-second** preparation deadline.

Follow actual code and type contracts on latest \`main\`; line numbers in earlier PR documents can be stale.

## 3. Field evidence — A2VM 8, 0.1.361.0

**Folder:** \`GoogleDrive/Addon/Log/AV2M/1011-4\`  
[Drive folder](https://drive.google.com/drive/folders/18xwAjde2s_OwxPVIFiq5A21m2HJRiZQc)

Seven 361 Runtime launches all reached verified PID1902 ownership, exact HidHide compliance and live Xbox360 presentation. Of five genuine Windows-session-end attempts, **three** wrote the PID1901 command, **one** stopped safely on \`Xbox360DetachUnsafeOutcomeUnknown\`, and **one** exhausted the 2-second deadline *after* proving VIIPER \`Closed\` and physical STOP.

| Launch | Session-end | Outcome | Evidence |
|---|---|---|---|
| 17:46, P7944 | 18:51 | \`Skipped: PresentationRetirementUnproven\` | \`libVIIPER.log\`: USB/IP client disconnect 18:51:47.563, *before* native detach at 18:51:48.290 -> unsafe outcome. Correct fail-close; no PID1901 command. |
| 18:52, P10136 | 19:32 | \`CommandWritten\`, 1,507 ms | VIIPER \`Closed\` 28 ms; physical STOP 413 ms; final mode command written. |
| 19:33, P9076 | 19:34 | \`CommandWritten\`, 1,891 ms | VIIPER \`Closed\` 117 ms; physical STOP 538 ms; final mode command written with little deadline margin. |
| 19:35, P9020 | 19:36 | \`CommandWritten\`, 1,801 ms | VIIPER \`Closed\` 29 ms; physical STOP 526 ms; final mode command written. |
| 19:38, P8404 | 19:38 | \`Skipped: DeadlineExceeded;CommandWriteResultUnconfirmed\`, 2,009 ms | Typed detach succeeded in 8 ms; VIIPER \`Closed\` after 162 ms; physical STOP confirmed after **761 ms**; physical primary identity preflight finished, but no DirectInput cleanup-completed event and no mode-command dispatch event before deadline. |

Log links:

- [P7944 Runtime](https://drive.google.com/file/d/1NmyTJFGAfExVGzay8d-tun5Ks8B2MA_S/view)
- [P10136 Runtime](https://drive.google.com/file/d/1JcrTidRxfLRQ2TvbE55UF3riWSXkNm_k/view)
- [P9076 Runtime](https://drive.google.com/file/d/1whpigBSj2D6JpBkWSCvWSkfZD3DKk_yi/view)
- [P9020 Runtime](https://drive.google.com/file/d/1kq7xReA8DyzzjWbfj9-AZsG4y5qpAcTa/view)
- [P8404 Runtime](https://drive.google.com/file/d/1fISOyhA2Od4MMeyY4_-VS86fz6TbI-lE/view)
- [Native VIIPER](https://drive.google.com/file/d/1KSTdtlYXhvquoAMq3UUUDbRw32bZNnIV/view)

On P8404 the final physical STOP wrote a 0/0 rumble packet in only **~6.7 ms**, yet the complete STOP stage consumed **761 ms**. The intervening Runtime log includes a machine-wide sequence of \`Rumble endpoint candidate\` / rejected-topology messages, which is consistent with cold endpoint discovery dominating the stage. The exact split between WinRT enumeration, SetupAPI and opening endpoints was **not separately measured**; do not claim that all ~754 ms came from a single API.

The shutdown path moved typed detach before physical STOP in PR #752. The new order was effective: **4/5** actual session-end attempts reached successful typed detach; the remaining failure was already disconnected before the managed detach opportunity. Preserve this order and the strict fail-close result.

The 19:39 -> 19:40 **application restart** was correctly handled without any PID1901 write; do not treat it as another Windows shutdown sample.

## 4. Implementation — use the already-verified exact physical HID path first

The existing physical-session identity contains:

\`\`\`csharp
internal sealed record MsiClawPhysicalInputIdentity(
    Guid InstanceGuid,
    string DevicePath,        // Selected DirectInput InterfacePath
    string PnpInstanceId,     // Exact HID\\VID_0DB0&PID_1902&MI_00&COL01\\...
    string PhysicalIdentity); // Strong physical root
\`\`\`

\`VorticeDirectInputDeviceEnumerator.CreateDescriptor\` and \`DirectInputDeviceTopologyResolver.Resolve\` have already correlated this **same selected path** with the current MSI PID1902 gamepad collection and physical root; \`PublishLivePhysicalSession\` retains it only for the current owned generation. It is therefore the correct **first candidate** for rumble HID output resolution, without re-enumerating *every* system HID interface at the point of shutdown.

Implement a small direct-path preference in the **existing** resolver/catalog code:

1. In the production default \`MsiClawRumbleEndpointResolver\`, try the verified \`identity.DevicePath\` **before** invoking the full \`WindowsMsiClawRumbleEndpointCatalog.Find\`. Preserve explicitly injected catalog behavior in tests and other callers.
2. Validate at least all of the following before using the direct path:
   - nonempty path with the expected HID device-interface syntax;
   - the instance ID extracted from that path equals **exactly** \`identity.PnpInstanceId\` (case-insensitive), and is the supported \`PID1902 MI_00 COL01\` gamepad primary collection;
   - the committed identity still names the correct strongly owned MSI physical root (no other controller or old generation);
   - an actual open of that path succeeds, using the existing read/write then write-only fallback;
   - \`TryReadHidCapabilities\` succeeds and reports Generic Desktop Gamepad (or currently supported joystick usage), input report length **64**, and a valid positive output report length **at least the 11-byte rumble packet**.
   - use the **returned actual output report length** for the transport; never assume hard-coded 32 or 64 bytes.
3. Make only the smallest necessary code visibility/refactoring change to reuse existing HID-path-to-PnP parsing in \`DirectInputDeviceTopologyResolver\`; do not duplicate a divergent parser or introduce an interface/capability service. Reuse \`WindowsMsiClawRumbleEndpointCatalog.TryReadHidCapabilities\` and \`MsiClawHardware\` matching helpers, not raw native calls newly copied into the resolver.
4. Return the existing \`MsiClawRumbleEndpointResolution\` on a successful direct-path proof, so \`MsiClawRumbleSink\` reuses its current cached endpoint, generation check, same-identity recheck, retry behavior, transport, and STOP result.
5. If the direct-path proof is invalid or unavailable, **do not guess**. Fall back to the existing full catalog/unique gamepad endpoint resolution rather than invent a path, widening the permitted physical targets, or claiming STOP succeeded. Maintain the existing ambiguity/failure policy.
6. Do not keep a separate stale endpoint authority: the existing \`MsiClawRumbleSink._cachedEndpoint\` / \`_endpointGeneration\` is enough. On \`BeginPhysicalSession\`, genuine PnP re-enumeration/recovery, teardown and invalidation, the already-implemented cache/generation boundaries remain the only authority.
7. Avoid a new startup background worker, persistent prewarm cache, timer, service, or a second device-enumeration subsystem. An exact path proof **on the first actual rumble write** should be cheap; any later writes, including terminal STOP, naturally hit the already-existing generation cache. If the first write is only shutdown STOP, it should still avoid global interface discovery when exact proof passes.

**Implementation shape (illustrative, not a new architecture):**

\`\`\`csharp
public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity)
{
    // Only on the production resolver with the known live DI path.
    // TryResolveExactOwnedInterface must use existing path/PnP parsing,
    // HID open + capability verification and strong session identity.
    var exact = _ownedPathCatalog?.TryResolveExactOwnedInterface(identity);
    if (exact is { IsAvailable: true })
        return exact.Value;

    // Existing unique-endpoint discovery and fail-closed semantics.
    return ResolveFromExistingCatalog(identity);
}
\`\`\`

Choose actual local naming and types to avoid introducing a new unnecessary abstraction. This is one fast-path validation feeding the *same existing* resolver and cached result. Do not bypass the existing \`MsiClawRumbleSink\` physical-generation validation.

**Important correctness detail:** matching string shape or VID/PID alone is insufficient. The selected DirectInput path, exact PnP collection and same owned physical root are already established as a unit at acquisition; preserve that binding and freshly prove HID capabilities/openability before the first rumble output. If source inspection reveals \`InterfacePath\` is *not* usable by the same native HID report transport, **do not fake this optimization**. In that case use the smallest verified existing-ownership-based alternative and document the evidence; never substitute an unchecked shortcut.

## 5. Windows session-end boundaries — no behavioral relaxation

Do not otherwise reorder or weaken the PR #752 Windows session-end contract:

\`\`\`WM_ENDSESSION(TRUE), not logoff:
  existing admission and shutdown gate
  -> publisher stop/join
  -> feedback callback disarm/drain
  -> canonical typed VIIPER detach (must prove success)
  -> canonical VIIPER runtime Closed
  -> physical PID1902 rumble STOP (must prove success; expedited endpoint resolution)
  -> DirectInput StopAndConfirmCleanupAsync
  -> fresh unique PID1902 command HID resolution
  -> at most one write-only PID1901 command, subject to remaining 2-second deadline
\`\`\`

Keep the best-effort STOP attempt on native teardown failure/cancellation added after review of PR #752. The shutdown time budget stays **2 seconds**, counting admission and every stage. No new callback/blocking mechanism, extra host thread, service preshutdown registration or Windows timeout hack.

If USB/IP has disconnected **before** the managed shutdown callback, the typed native detach may legitimately return \`UnsafeOutcomeUnknown\`. Continue to **skip PID1901** in that situation. Do not accept TCP disconnect as proof of safe VIIPER retirement, forcibly close another native controller, or add retries with no realistic remaining native endpoint. This is a separate limitation, not evidence that the physical STOP optimization failed.

Preserve:
- Center M Disabled as durable PID1902 authority;
- A2VM once-per-Windows-boot rumble re-arm marker and all \`1902->1901->1902\` / \`1901->1902\` decisions;
- HidHide exactly verified before virtual output admission;
- virtual Xbox360, SteamDeck and DualSense Edge presentation handling;
- physical rumble STOP on normal exit, suspend, PnP loss, detach failure, and app restart; no latched-motor regression;
- PID1901 command **only** during actual eligible Windows session-end (and separate explicit authority release), never mere Runtime exit/restart/update, WM_QUERYENDSESSION, aborted session end, sleep/hibernate/resume or logoff.

Avoid changing the total deadline, moving physical STOP back before VIIPER detach, skipping physical STOP when no nonzero rumble was observed, or eliminating final command HID identity verification.

## 6. Automated tests

Extend existing \`tests/SteamInputAddonforClaw.Tests/MsiClawRumbleTests.cs\` and only the relevant session-end/presentation tests where needed.

1. **Fast-path success:** fake a verified live \`MI_00&COL01\` DirectInput \`DevicePath\`/PnP/root and report capabilities. Assert the production-style resolver returns this path and the global catalog is **not called** (spy/throwing global catalog). Final STOP writes the actual-width output packet.
2. **Safety validation:** wrong PnP ID, different USB root, non-primary collection, unsupported usage, input length !=64, insufficient/zero output length, failed HID open and failed capabilities -> no direct-path promotion; existing full unique resolution is used when safe, otherwise rumble write fails closed. Do not use raw string prefix only to establish identity.
3. **Generation/identity:** within the same live physical generation, the existing endpoint cache is reused; on physical loss/recovery, invalidation or changed identity/generation, the prior path is not reused. No old-generation write after session retirement.
4. **Physical STOP even when no gameplay rumble occurred:** simulate the initial STOP as the *first* rumble call in the session; the direct path can be proven without machine-wide discovery, and the existing STOP result remains mandatory.
5. **Windows shutdown ordering and failure gating:** publisher -> feedback disarm/drain -> typed detach -> VIIPER Closed -> STOP confirmed -> DirectInput cleanup -> one PID1901 write. STOP or native detach failure prevents the physical mode write. Do not use timing-based assertions susceptible to machine load.
6. **User-scoped, not hardware-CI:** no physical-controller/real-boot test requirement on local Codex and **no missing hardware test PR blocker**. The user will perform actual-device follow-up after code merge.

Use the already existing fake HID API, resolver injection seam, fake transport, \`MsiClawRumbleSink\` tests, and \`AddonProcessHostWindowsSessionEndTests\`. Do not create a parallel testing scaffold.

## 7. PR acceptance and follow-up logs

One focused PR including source + appropriate automated tests, with only the minimal current-policy documentation adjustment if truly required. Prioritize eliminating reachable shutdown latency and retaining cleanup safety over LOC reduction or generalized defensive infrastructure.

**Code-level acceptance:**

- direct first STOP can use a positively verified exact live DI HID interface;
- global \`DeviceInformation.FindAllAsync\` / full PnP scan is bypassed on that proven path;
- output report length, usage, same-root/collection and current generation are still verified;
- unsafe or unknown VIIPER native detach remains blocking for PID1901;
- confirmed STOP and DirectInput cleanup stay mandatory;
- the 2-second hard session-end budget and one XInput command remain unchanged;
- startup prime, suspend/resume, process restart and stock authority-release semantics are unchanged.

**Later user-owned A2VM validation:** repeat Windows shutdown/reboot sessions, with BPM ON/OFF as useful. Compare \`A2vmWindowsSessionEndPhysicalStopCompleted.StageElapsedMs\` against the 361 baseline of 413/538/526/**761 ms**, and check \`A2vmWindowsSessionEndPid1901Prepare\` \`CommandWritten\` versus \`DeadlineExceeded\`. Distinguish \`UnsafeOutcomeUnknown\` caused by USB/IP already gone from genuine local STOP delay. Successful PID1901 command is still **write-only**, not an in-handler PID1901 PnP proof. No claim of firmware rumble re-arm correctness is supported without separate physical motor evidence.

## 8. Explicit exclusions

- No new startup rumble prime decision or assumptions about when motor re-arming is necessary.
- No CTW integration or controller-service redesign.
- No P2 BPM reconcile changes based on these logs.
- No Windows shutdown timer inflation, forced mode write, extra native controller, fallback detach that ignores typed ownership, or broad VIIPER ABI changes.
- No second authority, epoch, lock, cache owner, manager or speculative event-order race protections.
- No exhaustive theoretical race tests or real-device checks as local Codex PR blockers.

The aim is **one current-owned HID endpoint, one existing STOP owner, one proven native teardown, and one bounded optional PID1901 command**.
