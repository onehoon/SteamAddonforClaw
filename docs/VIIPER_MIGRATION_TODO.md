# Steam Deck Runtime Roadmap

## Current state

The Addon has one active Steam virtual-output architecture: the canonical
Steam Deck typed VIIPER path with identity `28DE:1205`.

The embedded VIIPER revision is:

```text
onehoon/VIIPER@3283d3a7bef190000cca583dd94375ab383c8c8f
```

The Addon-side session, mapper, publisher, PnP identity resolver, and safety
stage are implemented. MSI Claw EX hardware has validated the basic non-gyro
controller input path. That result does not by itself validate every lifecycle,
recovery, native teardown, or failure-path condition.

Phase 2B1 tooling status: `scripts/update-viiper.ps1` can fetch and
independently verify the canonical Windows libVIIPER artifact for an exact,
already-built `onehoon/VIIPER` commit into a disposable staging directory.

Phase 2B2 adopted the canonical artifact above (from
`ec64282c69e5587466b950332d7983fd53a7d778`) and aligned the managed Steam
Deck callback ABI: the native revision adds exactly one export,
`SetSteamDeckOutputCallback`, and the managed `ICanonicalViiperNativeApi`
surface, `RequiredExports`, and callback-lifetime rooting were updated in the
same change. This is dependency/ABI adoption only -- it does not implement
rumble/haptics, does not change Steam routing/Game Bar/X360/lifecycle policy,
and does not claim any additional hardware validation. Basic non-gyro
hardware validation from before this adoption remains the only established
Steam Deck input hardware claim; SD3 lifecycle/recovery validation below
remains unstarted.

Phase 3 dependency automation: Addon-side receiver/adoption automation is
implemented. `.github/workflows/viiper-dependency-update.yml` can open a
Draft PR after independently re-verifying a requested canonical VIIPER
commit, using `scripts/adopt-viiper.ps1` for mechanical adoption. It never
infers managed ABI compatibility and never merges; every automated PR
requires human review, and if the ABI changed, the required managed changes
are added to that same PR before it can merge. Full cross-repository Phase 3
completion remains pending the VIIPER-side sender workflow, which triggers
this receiver after a verified canonical build -- until that exists, this
workflow only runs via manual `workflow_dispatch`. This is
dependency-automation tooling, not runtime/hardware validation -- it does
not advance or substitute for SD3 below.

## Active roadmap

### SD3 — lifecycle and recovery hardware validation

Status: **NEXT**

Complete real MSI Claw EX validation for:

- native-mode entry and restoration;
- exact `28DE:1205` PnP identity and ownership;
- publisher startup, heartbeat, and clean stop;
- Steam-session routing transitions;
- suspend/resume reconciliation;
- HidHide and recovery cleanup;
- Deck creation, native failure, and teardown fail-closed behavior.

Basic non-gyro controller input is already validated. Do not use this item to
claim rumble, haptics, gyro, or IMU support.

### SD4 — production readiness review

Status: **BLOCKED ON SD3 EVIDENCE**

Review the complete hardware evidence, release packaging, diagnostics, and
recovery behavior before calling the active Deck path production-ready.

Required properties:

- the active output remains exactly `28DE:1205`;
- ambiguous PnP or ownership state fails closed;
- native, publisher, HidHide, and teardown failures do not continue routing;
- shutdown and resume leave the physical device in a safe stock state;
- diagnostics identify the selected target and failure operation clearly.

### SD5 — OEM1 and Quick Access

Status: **PLANNED**

Map the validated OEM1 control to the Steam Deck Quick Access field after the
basic lifecycle gate is complete.

### SD6 — gyro and accelerometer

Status: **SEPARATE FEATURE TRACK**

Add Windows sensor acquisition, capability checks, calibration, and Steam Deck
motion mapping only after the dedicated hardware and lifecycle design is
approved.

### SD7 — Game Bar and typed Xbox360 route

Status: **PLANNED**

Define the Game Bar transition and typed Xbox360 composition without weakening
the active Deck lifecycle or recovery invariants.

## Separate feature tracks

Rumble, haptics, gyro, and accelerometer behavior are not implied by the basic
non-gyro input validation. Each requires its own protocol, mapping, lifecycle,
and hardware evidence.

## Non-negotiable rules

- Steam Deck `28DE:1205` is the sole active Steam output target.
- Keep the exact VIIPER source, DLL, generated header, managed ABI, hashes, and
  provenance aligned.
- Use `lib/viiper` and the typed ABI for new integration work.
- Preserve caller-owned bus lifetime and explicit attachment ownership.
- Unknown attachment, removal, PnP, HidHide, and recovery outcomes fail closed.
- Do not claim hardware validation that was not performed.
- Do not add output selection or silent fallback to another implementation.

## Required references

Before any VIIPER implementation or review, read:

1. `docs/VIIPER_INTEGRATION.md`
2. `docs/VIIPER_IMPLEMENTATION_RULES.md`
3. `onehoon/VIIPER/FORK_ARCHITECTURE.md`
4. `onehoon/VIIPER/docs/libviiper/fork-api.md`

If the native ABI, ownership, callback, or lifecycle contract changes, update
the relevant documents and provenance in the same change.
