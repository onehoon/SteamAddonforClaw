# Classic Steam Controller report contract

> [!NOTE]
> **Historical Gordon reference.** The Addon's new primary virtual-output target is being migrated to Steam Deck (`28DE:1205`). The current production baseline still uses Gordon until Steam Deck hardware validation and production cutover are complete.
>
> The exact pre-transition version of this document is archived at `docs/archive/gordon-baseline-2026-08-15/protocol/classic-steam-controller-report.md`.

This document remains useful for understanding the preserved Gordon implementation and its tests. Do not use it as the report contract for new Steam Deck work.

For the active Steam Deck reference, use:

- `../Reference Research_Steam Deck VIIPER SteamOutput Input Reports.txt`
- `../VIIPER_MIGRATION_TODO.md`
- `../VIIPER_INTEGRATION.md`

## Historical Gordon report facts

The Gordon path uses a 64-byte Classic Steam Controller input report.

Important historical fields retained by the current implementation include:

| Field | Historical Gordon location / behavior |
| --- | --- |
| Report length | 64 bytes |
| Transport header | bytes 0..3 |
| Frame counter | bytes 4..7, little-endian |
| Left Grip | byte 9, bit 7 (`0x80`) |
| Right Grip | byte 10, bit 0 (`0x01`) |
| Analog trigger state | Gordon-specific raw units |
| Explicit L2/R2 full-pull | canonical typed state added independently from analog magnitude |

The current Gordon Addon mapping historically uses:

```text
M2 / left rear  -> Left Grip
M1 / right rear -> Right Grip
right stick     -> Gordon right pad
R3              -> Gordon right-pad press
```

These substitutions are **not** part of the new Steam Deck mapper.

## Steam Deck replacement direction

Steam Deck provides native fields for:

```text
LStickX/LStickY
RStickX/RStickY
L3/R3
L4/R4/L5/R5
QuickAccess
L2Digital/R2Digital
LTrigger/RTrigger
raw motion fields
```

The Addon should use those native semantics directly and leave Steam Deck trackpad fields neutral on MSI Claw.

Do not copy Gordon byte offsets or Gordon pad/grip substitutions into the Steam Deck implementation.

## Preservation rule

Keep this historical contract and the Gordon tests until the Steam Deck production path is proven and the Addon Gordon path is deliberately retired. VIIPER's Gordon implementation itself does not need to be removed when the Addon stops using it.
