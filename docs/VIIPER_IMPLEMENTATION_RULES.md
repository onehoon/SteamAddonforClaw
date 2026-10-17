# Mandatory VIIPER Implementation References

These rules apply to every Addon change that implements, modifies, reviews, or
validates the VIIPER integration.

## 1. Required source documents

Read these documents before changing code or contracts:

1. `onehoon/VIIPER/FORK_ARCHITECTURE.md`
2. `onehoon/VIIPER/docs/libviiper/fork-api.md`
3. `docs/VIIPER_INTEGRATION.md`
4. `docs/VIIPER_MIGRATION_TODO.md`

For exact native signatures and layouts, use the generated
`dist/libVIIPER/libVIIPER.h` from the same VIIPER build. Do not use the legacy
repository-root compatibility header as the Addon ABI source.

## 2. Current Full1902 Addon presentations

The standalone Full1902 Addon creates one canonical VIIPER runtime, one
caller-owned USB bus, and three persistent typed logical devices during runtime
initialization. **All three start detached** (`autoAttachLocalhost: false`):

- Xbox360 — regular Windows presentation;
- Steam Deck — VID=0x28DE, PID=0x1205, default Steam/BPM presentation;
- DualSense Edge — experimental Settings-selected Steam/BPM presentation.

DualSense Edge logical-device creation is required during shared-runtime
initialization; **only its attachment/publisher activation is deferred** until
the presentation is selected. Exactly one of these virtual presentations is
attached/live at a stable point. Game Bar foreground does not select or switch
the presentation.

Do not introduce a second simultaneously attached virtual controller, an
unrequested selector, or a silent fallback after an attach/native/teardown
failure. Keep the Deck-specific exact PnP identity and ownership checks for the
Deck path; do not infer Edge readiness from the Deck identity.

## 3. Source and ABI rules

- New Addon integration uses `lib/viiper`, not `clib`.
- The DLL, generated header, managed P/Invoke definitions, provenance, tests,
  and pinned VIIPER commit must refer to one revision.
- Preserve the typed opaque-handle model and caller-owned bus lifetime.
- Track `AttachUSBDevice` / `DetachUSBDevice` ownership explicitly.
- Unknown attachment, removal, or lifecycle outcomes fail closed.
- Clear callbacks before teardown and wait for managed transport drain.
- Keep public teardown waits outside the native lifecycle lock.
- Do not silently upgrade usbip-win2 compatibility.

## 4. Addon safety rules

- Resolve the exact Deck PnP identity; VID/PID or friendly-name heuristics are
  insufficient.
- Compare instance identities case-insensitively and preserve exact ownership
  evidence.
- Ambiguous, missing, or unstable identity fails closed.
- Preserve unrelated and pre-existing device/HidHide entries.
- On fresh startup and resume, live current-world state is authoritative.
- Routing state commits must be gated by the authoritative epoch result.
- Publisher, native, HidHide, recovery, and teardown failures stop routing;
  they must not continue through an alternate output.

## 5. Hardware-validation claims

MSI Claw EX basic non-gyro controller input is validated. This does not claim
completion of lifecycle, recovery, suspend/resume, teardown, rumble, haptics,
gyro, or IMU validation. Keep those claims separate and evidence-backed.

## 6. Documentation and tests

An ABI, callback, struct, ownership, attachment, transport, or lifecycle
change requires reviewing the upstream architecture/API documents, Addon
integration contract, provenance, generated header, managed interop, and
focused tests together.

Logging-only changes must remain behavior-neutral and must not emit raw
high-rate I/O. Safety-critical behavior requires deterministic regression
tests. Do not hide failures with skips, weakened assertions, retries, or
`continue-on-error`.

## 7. Validation gate

Before completion, run the relevant Release build and tests, inspect the exact
generated ABI inputs, run `git diff --check`, and report hardware validation
separately from automated validation. Do not merge without explicit approval.
