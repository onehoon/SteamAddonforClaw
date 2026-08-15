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

The active Steam Deck migration begins with **SD1** in `VIIPER_MIGRATION_TODO.md`.

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
lib/viiper/
```

and use the existing Gordon canonical wrapper as a lifecycle/ownership reference where appropriate.

Documentation does not replace code review. If docs and executable behavior disagree, reconcile the mismatch in VIIPER rather than silently choosing one interpretation.

---

## 4. Required pre-change checklist

Before writing VIIPER code:

1. Start from the latest explicitly selected VIIPER baseline/branch.
2. Read `FORK_ARCHITECTURE.md` from that revision.
3. Read `docs/libviiper/fork-api.md` from that revision.
4. Read the Addon `VIIPER_MIGRATION_TODO.md`.
5. Identify the exact canonical `lib/viiper` files affected.
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

The Addon's new primary target architecture is Steam Deck, but the current production Addon remains Gordon until hardware cutover is validated.

### 6.1 Existing device implementation is the protocol authority

The first Steam Deck canonical work should expose the existing:

```text
device/steamdeck
```

through a typed `lib/viiper` wrapper.

Do not create a second Steam Deck report builder inside `lib/viiper` or the Addon.

### 6.2 Generic ABI, not Claw-specific ABI

A canonical `SteamDeckDeviceState` must represent generic Steam Deck semantic state. Do not remove trackpad or additional rear-button fields merely because the first Addon consumer does not use them.

The Addon may send neutral values for unsupported physical controls.

### 6.3 Minimal SD1 callback scope

The first Steam Deck input smoke test does not require a host-output callback.

If `device/steamdeck` callback registration/dispatch does not yet satisfy the canonical callback synchronization contract, **do not expose a public canonical Steam Deck output callback just to make the first wrapper look feature-complete**.

Instead:

- expose the minimal typed input/lifecycle surface;
- validate it;
- harden callback ownership separately before adding callback/rumble/haptics ABI.

### 6.4 No Steam Deck production claim before hardware proof

Do not update product documentation or code comments to say Steam Deck is the production output until the Addon SD3 hardware gate passes and SD4 cutover is reviewed.

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

For the Steam Deck typed wrapper, both documents should stop claiming that `device/steamdeck` lacks a typed canonical wrapper only after the wrapper actually exists on the PR branch.

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

Do not mix a DLL from one VIIPER revision with a generated header, managed layout, documentation, or provenance from another revision.

---

## 10. Immediate application to SD1

Current active VIIPER branch:

```text
onehoon/VIIPER:feature/canonical-steamdeck
```

Selected base:

```text
db70bdedbe36846c665c841ea9f6ae9bf01d0d3d
```

SD1 exists to expose the existing Steam Deck implementation through the canonical typed ABI with minimum input/lifecycle surface.

Before implementing/reviewing SD1, verify:

- `FORK_ARCHITECTURE.md` and `fork-api.md` were read from the branch/base being changed;
- the existing Gordon wrapper is used only as a lifecycle/ownership pattern, not as a reason to copy Gordon-specific state semantics;
- default Steam Deck identity remains `28DE:1205`;
- frame ownership remains internal to `device/steamdeck`;
- typed state includes the generic Steam Deck fields needed by external consumers;
- shared identity/attach/detach APIs accept the typed handle;
- typed removal preserves caller-owned bus lifetime;
- classified removal follows the canonical result model;
- no public output callback is added until callback synchronization is proven;
- generated header/export/layout tests are updated;
- Gordon and `clib` compatibility remain intact.

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

No VIIPER implementation task for this Addon should proceed from memory alone.
