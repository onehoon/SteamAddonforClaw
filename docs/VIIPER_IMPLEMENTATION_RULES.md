# Mandatory VIIPER Implementation References

This document defines the mandatory source-reading and validation rules for any VIIPER work performed for **Steam Input Addon for Claw**.

It is intentionally short and normative. The detailed integration contract remains in [`VIIPER_INTEGRATION.md`](./VIIPER_INTEGRATION.md), while the active implementation backlog remains in [`VIIPER_MIGRATION_TODO.md`](./VIIPER_MIGRATION_TODO.md).

---

## 1. Mandatory upstream-fork documents

Before designing, implementing, reviewing, or updating any VIIPER change for this Addon, read both of the following files from the **same VIIPER revision being worked on**:

1. `onehoon/VIIPER/FORK_ARCHITECTURE.md`
   - https://github.com/onehoon/VIIPER/blob/main/FORK_ARCHITECTURE.md

2. `onehoon/VIIPER/docs/libviiper/fork-api.md`
   - https://github.com/onehoon/VIIPER/blob/main/docs/libviiper/fork-api.md

These are not optional background reading.

`FORK_ARCHITECTURE.md` is the fork's architectural source of truth. It defines the canonical embedded architecture, ownership model, caller-owned bus lifetime, typed-handle direction, tracked USB/IP attachment model, server lifecycle, build expectations, and upstream-synchronization constraints.

`docs/libviiper/fork-api.md` is the consumer-facing API/lifecycle guide for applications embedding the generated canonical C ABI. It defines the supported typed APIs, handle rules, return values, lifecycle order, attachment semantics, callback/teardown behavior, build/validation expectations, and the requirement to consume the generated header from the same build as the DLL.

---

## 2. Scope of this rule

This mandatory-reference rule applies to all of the following:

- changes in `onehoon/VIIPER` made for Steam Input Addon for Claw;
- changes to `lib/viiper` canonical wrappers;
- Gordon / Classic Steam Controller typed ABI changes;
- Xbox360 typed ABI changes used by the Addon;
- attachment/detachment ownership changes;
- bus/server lifecycle changes;
- callback or transport-drain changes;
- generated C ABI layout changes;
- usbip-win2 compatibility changes;
- Addon C# P/Invoke definitions for canonical VIIPER;
- replacement of the embedded `libVIIPER.dll` payload;
- updates to the pinned VIIPER baseline;
- review of any corrective VIIPER PR discovered during Addon integration.

This explicitly includes migration step **M0** in `VIIPER_MIGRATION_TODO.md` and every later VIIPER correction discovered while integrating the Addon.

---

## 3. Required source hierarchy

Use the following hierarchy when implementing or reviewing fork behavior.

### 3.1 Architecture

Authority:

```text
onehoon/VIIPER/FORK_ARCHITECTURE.md
```

Use it to determine architectural intent and invariants such as:

- `lib/viiper` is the canonical embedded ABI for new fork development;
- typed device handles are the preferred ownership model;
- new Addon integration must not use `clib` as its architectural base;
- buses are caller-owned and are not implicitly removed by typed device removal;
- tracked Windows attachment is explicit and ownership-aware;
- server close is fail-closed and retry-aware;
- fork-specific changes should remain localized where practical.

### 3.2 Consumer API and lifecycle contract

Authority:

```text
onehoon/VIIPER/docs/libviiper/fork-api.md
```

Use it to determine application-facing behavior such as:

- canonical exported API names;
- typed handle semantics;
- true/false return behavior;
- normal lifecycle ordering;
- permitted server states and mutations;
- typed Remove semantics;
- `AttachUSBDevice` / `DetachUSBDevice` behavior;
- callback and teardown guarantees;
- required build/validation commands;
- generated-header requirements.

### 3.3 Exact C ABI layout and signatures

Authority:

```text
dist/libVIIPER/libVIIPER.h
```

from the **same VIIPER commit and build as the DLL being embedded**.

Do not use the repository-root legacy `libviiper.h` as the canonical Addon header.

For C# interop, never infer field layout, field order, native boolean width, callback signature, or opaque-handle width from memory or an older generated header.

### 3.4 Executable implementation and tests

After reading the architecture/API documents, inspect the concrete production code and tests affected by the change.

Documentation does not eliminate code review. If the docs and executable behavior appear inconsistent, stop and reconcile the mismatch in the VIIPER PR rather than silently choosing one interpretation.

Do not update the Addon against an undocumented accidental behavior.

---

## 4. Required pre-change checklist for VIIPER work

Before writing code in `onehoon/VIIPER`:

1. Start from the latest explicitly selected VIIPER baseline.
2. Read `FORK_ARCHITECTURE.md` from that revision.
3. Read `docs/libviiper/fork-api.md` from that revision.
4. Identify the exact canonical `lib/viiper` files affected.
5. Identify the generated C ABI impact, if any.
6. Identify existing focused/lifecycle/race tests protecting the affected contract.
7. Confirm whether `clib` compatibility must remain unchanged.
8. Confirm whether the change affects the Addon's pinned ABI or embedded DLL provenance.

Do not begin from HHC's historical `clib` usage and work backward. The Addon's new integration begins from the canonical fork architecture.

---

## 5. Fork invariants that must not be accidentally regressed

Every Addon-driven VIIPER change must preserve these existing invariants unless the same PR explicitly and deliberately changes the documented contract:

- canonical new integration uses `lib/viiper`, not `clib`;
- typed handles remain opaque capability tokens;
- stale/zero/wrong-type handles fail safely;
- typed Remove removes the logical device, not the caller-owned bus;
- `RemoveUSBBus` / `CloseUSBServer` own bus teardown;
- Windows attachment is explicit and tracked for typed handles;
- known and unknown attachment outcomes remain distinct;
- unknown attachment/detachment outcomes fail closed;
- exact successful attachment backend/port ownership is retained for detach;
- callback clearing occurs before destructive teardown;
- public typed Remove/Close does not return before managed transport work required by the contract is drained;
- already-completed teardown is not replayed during retry;
- server lifecycle remains `active` / `closing` / `close-failed` / `closed` according to the documented state contract;
- canonical Windows attachment support remains pinned to validated usbip-win2 compatibility, currently `v0.9.7.7` / `7c219953101cc5d0ec9a0bcb3eb87259cf72bedd`, until a later version is explicitly validated;
- canonical DLL and generated header always come from the same commit/build.

---

## 6. Documentation is part of a VIIPER API change

If a VIIPER PR changes an architectural or consumer-visible contract, that PR must update the relevant VIIPER documentation in the same change.

Examples:

- new typed state fields;
- changed struct layout;
- new exported function;
- changed lifecycle ordering;
- changed callback ownership;
- changed attach/detach behavior;
- changed caller-owned bus behavior;
- changed supported usbip-win2 version;
- new failure/retry semantics.

At minimum, review both mandatory documents and update whichever one is affected:

```text
FORK_ARCHITECTURE.md
docs/libviiper/fork-api.md
```

If neither document needs a textual change, the PR review should still state that both were checked and remain accurate.

---

## 7. Required VIIPER validation gate

For canonical `lib/viiper` changes, the expected baseline validation is:

```text
go test ./...
go test -race ./internal/server/usb ./lib/viiper
go vet ./...
git diff --check
just build-libVIIPER Release
```

CI must also validate the relevant canonical Windows shared-library/header/export surface and lifecycle/race coverage.

When the public ABI changes, generated-header expectations and ABI layout tests must be updated deliberately, not merely regenerated without review.

---

## 8. Addon baseline-adoption rule

After a VIIPER corrective PR merges, the Addon must not simply replace the DLL.

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

Do not mix a DLL from one VIIPER revision with a generated header, P/Invoke layout, documentation, or provenance from another revision.

---

## 9. Immediate application to M0

The current first migration prerequisite is the independent Gordon L2/R2 full-pull corrective API described in `VIIPER_MIGRATION_TODO.md`.

Before implementing M0, the implementation model/reviewer must read both mandatory VIIPER documents listed in Section 1.

Because M0 changes the public typed Gordon state layout, the M0 PR must explicitly verify/update:

- `FORK_ARCHITECTURE.md` if the architectural description requires clarification;
- `docs/libviiper/fork-api.md` so the consumer-facing Gordon typed state/API remains accurate;
- canonical state struct/layout tests;
- generated header validation;
- Windows DLL/export/ABI CI;
- Addon pin/provenance documents after merge.

The M0 implementation must preserve the existing fork lifecycle/ownership/transport contracts. It is a narrow Gordon typed-state correction, not an opportunity to redesign `lib/viiper`.

---

## 10. New-conversation rule

When starting a new conversation or handing a VIIPER implementation task to another coding agent, provide or require these files before implementation:

```text
SteamInputAddonforClaw/README.md
SteamInputAddonforClaw/docs/VIIPER_INTEGRATION.md
SteamInputAddonforClaw/docs/VIIPER_MIGRATION_TODO.md
SteamInputAddonforClaw/docs/VIIPER_IMPLEMENTATION_RULES.md

onehoon/VIIPER/FORK_ARCHITECTURE.md
onehoon/VIIPER/docs/libviiper/fork-api.md
```

For exact ABI work also require the generated `dist/libVIIPER/libVIIPER.h` from the selected VIIPER build.

No VIIPER implementation task for this Addon should proceed from memory alone.