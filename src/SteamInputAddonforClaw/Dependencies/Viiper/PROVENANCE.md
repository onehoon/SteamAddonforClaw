# libVIIPER.dll provenance

## SD2: Steam Deck typed ABI adopted (VIIPER main@ec64282c...)

This payload atomically adopts the canonical Steam Deck typed ABI selected in
`docs/VIIPER_MIGRATION_TODO.md` SD2, replacing the previous Gordon-era `db70bdedbe36846c665c841ea9f6ae9bf01d0d3d`-based diagnostic payload. It supersedes the prior
`da78d0fc77034afa48485def31dc1ba54960a04e` (VIIPER PR #15, Draft/unmerged D-pad diagnostic) payload
recorded in this file's previous revision -- that diagnostic branch is no longer embedded.

This build adds the minimal typed Steam Deck ABI (`SteamDeckDeviceHandle`,
`SteamDeckDeviceState`, `SteamDeckDeviceRemoveResult`, `CreateSteamDeckDevice`,
`SetSteamDeckDeviceState`, `RemoveSteamDeckDevice`, `RemoveSteamDeckDeviceEx`) alongside the
existing Gordon ABI, unchanged. No Gordon or `clib` behavior was removed.

## Canonical corresponding source

- Repository: https://github.com/onehoon/VIIPER
- Commit: `ec64282c69e5587466b950332d7983fd53a7d778` (`main`, merged PR
  [#16](https://github.com/onehoon/VIIPER/pull/16) — "Expose Steam Deck through canonical
  libVIIPER API")
- Lineage: `Alia5/VIIPER -> Valkirie/VIIPER -> onehoon/VIIPER`
- License: GPL-3.0; the accompanying `LICENSE.txt` is copied from the pinned source.

## Build attestation

Built with the literal official entrypoint, `just build-libVIIPER Release`, against a clean
checkout of `ec64282c69e5587466b950332d7983fd53a7d778` in an isolated temporary clone (not the
`D:\repo\VIIPER` working tree, which stayed on its own branch/PR #16-review state throughout and
was not checked out to a different commit or otherwise mutated by this adoption):

```text
just build-libVIIPER Release
  -> New-Item dist/libVIIPER
  -> go install github.com/josephspurrier/goversioninfo/cmd/goversioninfo@latest
  -> pwsh -NoProfile -NonInteractive -File scripts/inject-version.ps1 ...
  -> goversioninfo -64 -o lib/viiper/resource.syso ...
  -> CGO_ENABLED=1 go build -buildmode=c-shared -trimpath -ldflags "-s -w" -o dist/libVIIPER/libVIIPER.dll ./lib/viiper
  -> gendef - dist/libVIIPER/libVIIPER.dll | Set-Content ... libVIIPER.def
  -> go run ./lib/viiper/postbuild
  -> just licenses-libVIIPER
```

- Go: `go1.26.5 windows/amd64`
- GCC/MinGW: `gcc.exe (MinGW-W64 x86_64-ucrt-posix-seh) 16.1.0`
- Output: `libVIIPER.dll` (Windows x64)
- Generated header SHA-256: `9e01d6e51b95e4914508e6961e9f867883190be3d0191caa75868076e8ddd5ed`
- DLL SHA-256: `f8d2651b185d39544f53151d8c857b53b70cf6006de77bbd2574089c7317256b`
- Canonical entrypoint: `just build-libVIIPER Release` (used verbatim for this artifact)
- Native ABI checks confirmed from the generated header itself (`dist/libVIIPER/libVIIPER.h`):
  `sizeof(SteamDeckDeviceState) = 76`, `sizeof(SteamDeckDeviceRemoveResult) = 4`,
  `L2Digital = 6`, `R2Digital = 7`, `R3 = 22`, `QuickAccess = 27`, `LPadX = 28`, `AccelX = 36`,
  `LTrigger = 56`, `LStickX = 60`, `RStickX = 64` (Gordon's `sizeof(SteamControllerDeviceState) =
  62` / `sizeof(SteamControllerDeviceRemoveResult) = 4` are unchanged by this commit).

Reproduced independently (isolated temp clone of `ec64282c69e5587466b950332d7983fd53a7d778`, not
the `D:\repo\VIIPER` working tree, which was not checked out to a different commit or otherwise
mutated): `just build-libVIIPER Release` completed with overall exit code `0`, and the resulting
`dist/libVIIPER/libVIIPER.dll` hashed to the same SHA-256 recorded above
(`f8d2651b185d39544f53151d8c857b53b70cf6006de77bbd2574089c7317256b`). Within that successful run,
`scripts/inject-version.ps1` emitted a non-terminating version-resource warning on this non-numeric
commit-prefix version string (`ec64282`): `Cannot convert value "ec64282" to type "System.Int32"`,
followed by two related `goversioninfo` "could not be parsed" warnings. These only affect the DLL's
embedded Win32 version resource (`FileVersion`/`ProductVersion` metadata), not the built ABI or
exported symbols -- the recipe continues past them and the subsequent `go build`, `gendef`,
`go run ./lib/viiper/postbuild`, and `just licenses-libVIIPER` steps all completed normally and
produced this DLL/header pair and `licenses.txt`.

The build does not use or redistribute Handheld Companion's bundled DLL, and no byte-for-byte
equivalence claim is made. Rebuilds are traceable through the exact source, recipe, toolchain, and
artifact hash; CI verifies the committed artifact hash rather than demanding a byte-identical
rebuild.

## Addon adoption status

This is the SD2 atomic adoption: the DLL, generated header, C# P/Invoke ABI (`CanonicalViiperNativeTypes.cs`,
`CanonicalViiperNativeApi.cs`), ABI tests (`CanonicalViiperNativeAbiTests.cs`), `docs/VIIPER_INTEGRATION.md`,
and `docs/VIIPER_MIGRATION_TODO.md` are all updated together in the same change, and all describe
VIIPER commit `ec64282c69e5587466b950332d7983fd53a7d778`.

The Steam Deck path (`CanonicalSteamDeckSession` / `SteamDeckDeviceStateMapper` /
`CanonicalSteamDeckInputPublisher` / `CanonicalSteamDeckOutputStage`) exists side-by-side with the
unmodified Gordon production path. The default production Steam routing output remains Gordon;
Steam Deck is reachable only through the `STEAMINPUT_ADDON_DEV_STEAMDECK_OUTPUT=1` developer/test
environment variable seam in `App.xaml.cs`, pending the SD3 real-hardware smoke test and SD4
production cutover review (see `docs/VIIPER_MIGRATION_TODO.md`).
