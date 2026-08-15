# Mandatory VIIPER Implementation References

This document defines the mandatory source-reading and validation rules for any VIIPER work performed for **Steam Input Addon for Claw**.

It is intentionally short and normative. The detailed integration contract remains in [`VIIPER_INTEGRATION.md`](./VIIPER_INTEGRATION.md), while the active implementation backlog remains in [`VIIPER_MIGRATION_TODO.md`](./VIIPER_MIGRATION_TODO.md).

The exact pre-Steam-Deck-transition version is archived under `docs/archive/gordon-baseline-2026-08-15/`.

---

## 1. Mandatory upstream-fork documents

Before designing, implementing, reviewing, or updating any VIIPER change for this Addon, read both of the following files from the **same VIIPER revision being worked on**:

1. `onehoon/VIIPER/FORK_ARCHITECTURE.md`
   - https://github.com/onehoon/VIIPER/blob/main/FORK_ARCHITECTURE.md

2. `onehoon/VIIPER/docs/libviiper/fork-api.md`
   - https://github.com/onehoon/VIIPER/blob/main/docs/libviiper/fork-api.md

These are mandatory.

`FORK_ARCHITECTURE.md` is the fork's architectural source of truth. `docs/libviiper/fork-api.md` is the consumer-facing canonical ABI/lifecycle guide.

For the current Steam Deck SD2 work, the selected immutable VIIPER revision is:

```text
onehoon/VIIPER@ec64282c69e5587466b950332d7983fd53a7d778
```

The former `feature/canonical-steamdeck` branch was merged and removed. Do not use it as a source reference.

---

## 2. Scope of this rule

This rule applies to:

- changes in `onehoon/VIIPER` made for Steam Input Addon for Claw;
- canonical `lib/viiper` typed wrappers;
- Steam Deck typed ABI work;
- Classic Steam Controller / Gordon typed ABI maintenance;
- Xbox360 typed ABI work used by the Addon;
- attachment/detachment ownership changes;
- bus/server lifecycle changes;
- callback or transport-drain changes;
- generated C ABI layout changes;
- usbip-win2 compatibility changes;
- Addon C# P/Invoke definitions for canonical VIIPER;
- replacement of the embedded `libVIIPER.dll` payload;
- updates to the pinned VIIPER baseline;
- review of corrective VIIPER PRs discovered during Addon integration.

SD1 and SD2 are complete. The active Steam Deck migration step is **SD3** in `VIIPER_MIGRATION_TODO.md`.

---

## 3. Required source hierarchy

### 3.1 Architecture

Authority:

```text
onehoon/VIIPER/FORK_ARCHITECTURE.md
```

Use it to determine invariants such as:

- `lib/viiper` is the canonical embedded ABI for new fork development;
- typed device handles are the preferred ownership model;
- required fork devices should be exposed through typed wrappers rather than a new generic controller-manager API;
- new Addon integration must not use `clib` as its architectural base;
- buses are caller-owned;
- Windows attachment is explicit and tracked;
- server close is fail-closed and retry-aware;
- fork-specific changes should remain localized where practical.

### 3.2 Consumer API and lifecycle contract

Authority:

```text
onehoon/VIIPER/docs/libviiper/fork-api.md
```

Use it to determine:

- canonical exported API names;
- typed handle semantics;
- lifecycle order;
- typed Remove semantics;
- `AttachUSBDevice` / `DetachUSBDevice` behavior;
- callback/teardown guarantees;
- build and validation expectations;
- generated-header requirements.

### 3.3 Exact C ABI layout and signatures

Authority:

```text
dist/libVIIPER/libVIIPER.h
```

from the **same VIIPER commit and build as the DLL being embedded**.

Do not use the repository-root legacy `libviiper.h` as the canonical Addon header.

For C# interop, never infer field layout, field order, native boolean width, callback signature, enum width, or opaque-handle width from memory or an older generated header.

### 3.4 Executable implementation and tests

After reading the architecture/API documents, inspect the concrete device implementation and tests affected by the change.

For Steam Deck work, inspect at minimum:

```text
device/steamdeck/
lib/viiper/steamdeck.go
lib/viiper/steamdeck_test.go
lib/viiper/
```

and use the existing Gordon canonical wrapper as a lifecycle/ownership reference where appropriate.

Documentation does not replace code review. If docs and executable behavior disagree, reconcile the mismatch in VIIPER rather than silently choosing one interpretation.

---

## 4. Required pre-change checklist

Before writing VIIPER or Addon interop code:

1. Start from the latest explicitly selected VIIPER immutable revision. For current SD2 this is `ec64282c69e5587466b950332d7983fd53a7d778`.
2. Read `FORK_ARCHITECTURE.md` from that revision.
3. Read `docs/libviiper/fork-api.md` from that revision.
4. Read the Addon `VIIPER_MIGRATION_TODO.md`.
5. Identify the exact canonical `lib/viiper` files affected or consumed.
6. Identify the underlying device implementation affected.
7. Identify generated C ABI impact.
8. Identify focused/lifecycle/race tests protecting the contract.
9. Confirm whether `clib` compatibility must remain unchanged.
10. Confirm whether the change affects the Addon's pinned ABI or embedded payload provenance.

Do not begin from historical HHC `clib` usage and work backward.

---

## 5. Fork invariants that must not be accidentally regressed

Every Addon-driven VIIPER change must preserve these invariants unless the same PR explicitly updates the documented contract:

- canonical new integration uses `lib/viiper`, not `clib`;
- typed handles remain opaque capability tokens;
- stale/zero/wrong-type handles fail safely;
- typed Remove removes the logical device, not the caller-owned bus;
- `RemoveUSBBus` / `CloseUSBServer` own bus teardown;
- Windows attachment is explicit and tracked for typed handles;
- known and unknown attachment outcomes remain distinct;
- unknown attachment/detachment outcomes fail closed;
- exact successful attachment backend/port ownership is retained for detach;
- callback-bearing typed devices satisfy the documented callback clear/capture contract before being exposed publicly;
- callback clearing occurs before destructive teardown;
- public typed Remove/Close does not return before managed transport work required by the selected contract is drained;
- already-completed teardown is not replayed during retry;
- server lifecycle remains `active` / `closing` / `close-failed` / `closed` according to the documented state contract;
- canonical Windows attachment support remains pinned to validated usbip-win2 compatibility, currently `v0.9.7.7` / `7c219953101cc5d0ec9a0bcb3eb87259cf72bedd`, until a later version is explicitly validated;
- canonical DLL and generated header always come from the same commit/build.

---

## 6. Steam Deck-specific rules

The Addon's active runtime composition uses Steam Deck exclusively. Gordon remains retained reference/rollback code, and SD3 real MSI Claw hardware validation is still pending.

### 6.1 Existing device implementation is the protocol authority

VIIPER `main@ec64282...` exposes the existing:

```text
device/steamdeck
```

through the canonical typed `lib/viiper` wrapper.

Do not create a second Steam Deck report builder inside `lib/viiper` or the Addon.

### 6.2 Generic ABI, not Claw-specific ABI

The canonical `SteamDeckDeviceState` represents generic Steam Deck semantic state. Do not remove trackpad or additional rear-button fields merely because the first Addon consumer does not use them.

The Addon may send neutral values for unsupported physical controls.

The selected ABI is pinned at:

```text
SteamDeckDeviceState = 76 bytes
SteamDeckDeviceRemoveResult = 4 bytes
```

Critical field offsets are pinned by VIIPER tests. The generated header from the selected build remains the exact authority for Addon P/Invoke layout.

### 6.3 Output callback remains outside initial SD2 scope

The first Steam Deck input smoke test does not require a host-output callback.

The validated SD1 wrapper intentionally does not expose `SetSteamDeckOutputCallback`, rumble, or haptics ABI because `device/steamdeck` callback registration/dispatch still needs a separate review against the canonical callback synchronization contract.

Do not add a public callback merely to make SD2 feature-complete.

### 6.4 No Steam Deck hardware-validation claim before hardware proof

Do not claim that Steam Deck hardware validation or SD3 is complete until the Addon SD3 hardware gate passes. The active runtime composition may be Deck-only before that hardware evidence exists, and it must fail closed rather than falling back to Gordon.

---

## 7. Documentation is part of a VIIPER API change

If a VIIPER PR changes an architectural or consumer-visible contract, the same PR must update the relevant VIIPER documentation.

Examples:

- new typed device family;
- new state fields;
- changed struct layout;
- new exported function;
- changed lifecycle ordering;
- changed callback ownership;
- changed attach/detach behavior;
- changed caller-owned bus behavior;
- changed supported usbip-win2 version;
- new failure/retry semantics.

At minimum review:

```text
FORK_ARCHITECTURE.md
docs/libviiper/fork-api.md
```

The Steam Deck typed-wrapper documentation requirement was satisfied by VIIPER PR #16. Future changes must keep those documents synchronized with the public ABI.

---

## 8. Required VIIPER validation gate

For canonical `lib/viiper` changes, expected baseline validation is:

```text
go test ./...
go test -race ./internal/server/usb ./lib/viiper
go vet ./...
git diff --check
just build-libVIIPER Release
```

CI must also validate the relevant Windows shared-library/header/export surface and lifecycle/race coverage.

When the public ABI changes, generated-header expectations and ABI layout tests must be updated deliberately.

---

## 9. Addon baseline-adoption rule

After a VIIPER PR merges, the Addon must not simply replace the DLL.

Adoption requires all of the following to refer to the same reviewed VIIPER commit:

```text
VIIPER commit pin
canonical generated header
embedded libVIIPER.dll
C# interop layout/tests
payload provenance/hash
VIIPER_INTEGRATION.md
VIIPER_MIGRATION_TODO.md
```

For current SD2, all new native artifacts and managed ABI definitions must come from:

```text
onehoon/VIIPER@ec64282c69e5587466b950332d7983fd53a7d778
```

Do not mix a DLL from one VIIPER revision with a generated header, managed layout, documentation, or provenance from another revision.

The Addon's current embedded payload is the SD2-validated `ec64282c...` pair. Steam Deck is the sole active runtime output composition; Gordon's typed ABI/behavior remains retained and unchanged.

---

## 10. Immediate application to SD2

SD1 is validated and merged to VIIPER `main`.

Selected native source revision:

```text
onehoon/VIIPER@ec64282c69e5587466b950332d7983fd53a7d778
```

SD2 adopted that minimal typed Steam Deck ABI into the Addon. The active runtime composition now uses Deck exclusively; Gordon remains retained reference/rollback code and is not a runtime fallback.

Before implementing/reviewing SD2, verify:

- `FORK_ARCHITECTURE.md` and `fork-api.md` are read from `ec64282c69e5587466b950332d7983fd53a7d778`;
- matching Release `libVIIPER.dll` and generated `libVIIPER.h` are built from that exact commit;
- `SteamDeckDeviceState` managed layout matches the generated ABI and the 76-byte native pin;
- `SteamDeckDeviceRemoveResult` is represented with the correct 4-byte width and values;
- `CreateSteamDeckDevice`, `SetSteamDeckDeviceState`, `RemoveSteamDeckDevice`, and `RemoveSteamDeckDeviceEx` signatures match the generated header;
- shared identity/attach/detach APIs are reused rather than adding Deck-specific lifecycle calls;
- the existing Gordon path is not deleted or broadly refactored before hardware proof;
- default Steam Deck identity remains `28DE:1205`;
- frame ownership remains internal to `device/steamdeck`;
- trackpad and IMU fields remain neutral initially but stay present in the ABI;
- physical LT/RT full-pull state remains independent from analog travel;
- M1/M2 map to R4/L4 for the first smoke test;
- no public output callback/rumble/haptics ABI is assumed;
- recovery/PnP/HidHide safety ordering remains intact;
- provenance, hashes, tests, integration docs, and TODO are updated atomically with payload adoption.

---

## 11. New-conversation / agent-handoff rule

Every new VIIPER implementation task must provide or require these files:

```text
SteamInputAddonforClaw/README.md
SteamInputAddonforClaw/docs/VIIPER_INTEGRATION.md
SteamInputAddonforClaw/docs/VIIPER_MIGRATION_TODO.md
SteamInputAddonforClaw/docs/VIIPER_IMPLEMENTATION_RULES.md
SteamInputAddonforClaw/docs/Reference Research_Steam Deck VIIPER SteamOutput Input Reports.txt

onehoon/VIIPER/FORK_ARCHITECTURE.md
onehoon/VIIPER/docs/libviiper/fork-api.md
```

For exact ABI work also require the generated `dist/libVIIPER/libVIIPER.h` from the selected build.

For current SD2, the selected VIIPER revision is `ec64282c69e5587466b950332d7983fd53a7d778` on `main`. No VIIPER implementation task for this Addon should proceed from the deleted development branch or from memory alone.
