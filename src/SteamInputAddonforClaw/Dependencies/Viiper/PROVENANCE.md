# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     ec64282c69e5587466b950332d7983fd53a7d778
Branch:     main
Entrypoint: just build-libVIIPER Release
```

This revision provides the typed Steam Deck ABI used by the active Addon
runtime. The active virtual output identity is `VID=0x28DE`, `PID=0x1205`.

## Build attestation

The artifact was built from an isolated clean checkout with the literal
official entrypoint:

```text
just build-libVIIPER Release
```

Toolchain:

```text
Go:             go1.26.5 windows/amd64
GCC/MinGW:      gcc.exe (MinGW-W64 x86_64-ucrt-posix-seh) 16.1.0
```

Artifact hashes:

```text
Generated header SHA-256: 9e01d6e51b95e4914508e6961e9f867883190be3d0191caa75868076e8ddd5ed
DLL SHA-256:              f8d2651b185d39544f53151d8c857b53b70cf6006de77bbd2574089c7317256b
```

The generated header confirms the pinned Deck ABI layout, including:

```text
sizeof(SteamDeckDeviceState) = 76
sizeof(SteamDeckDeviceRemoveResult) = 4
L2Digital = 6, R2Digital = 7, R3 = 22, QuickAccess = 27
LPadX = 28, AccelX = 36, LTrigger = 56, LStickX = 60, RStickX = 64
```

The build recipe may emit non-terminating version-resource warnings for the
commit-prefix version. The recorded DLL/header pair and exported ABI remain
the attested artifacts, and CI verifies their committed hashes.

## Addon integration alignment

The following Addon files must remain aligned with this artifact:

- `CanonicalViiperNativeTypes.cs`
- `CanonicalViiperNativeApi.cs`
- `CanonicalViiperNativeAbiTests.cs`
- `docs/VIIPER_INTEGRATION.md`
- `docs/VIIPER_MIGRATION_TODO.md`
- this provenance record

The active Addon composition uses the Steam Deck session, mapper, publisher,
identity resolver, and output stage. MSI Claw EX basic non-gyro controller
input is validated; lifecycle, recovery, rumble, haptics, gyro, and IMU
claims require separate evidence.
