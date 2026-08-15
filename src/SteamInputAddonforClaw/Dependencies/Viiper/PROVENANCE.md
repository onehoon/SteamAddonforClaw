# libVIIPER.dll provenance

## ⚠ TEMPORARY DIAGNOSTIC BUILD — not a baseline promotion

This payload is built from an **unmerged Draft** branch (`feature/dpad-runtime-boundary-diagnostic`
on `onehoon/VIIPER`, PR [#15](https://github.com/onehoon/VIIPER/pull/15)), not from the validated
`main` baseline recorded in `docs/VIIPER_INTEGRATION.md` / `docs/VIIPER_MIGRATION_TODO.md`. It adds
only Debug-only, transition-gated D-pad runtime diagnostics (native ABI-decode stage + final Gordon
report stage) on top of that baseline — no mapping, serialization, cadence, or protocol behavior
change. It exists solely so real MSI Claw hardware testing can exercise those diagnostics; it is not
intended to be merged or promoted as the new pinned VIIPER baseline.

`docs/VIIPER_INTEGRATION.md` and `docs/VIIPER_MIGRATION_TODO.md` intentionally still record
`db70bdedbe36846c665c841ea9f6ae9bf01d0d3d` as the pinned baseline — see "Reverting" below.

## Canonical corresponding source

- Repository: https://github.com/onehoon/VIIPER
- Diagnostic commit: `da78d0fc77034afa48485def31dc1ba54960a04e` (branch `feature/dpad-runtime-boundary-diagnostic`, PR #15 — Draft, unmerged)
- Built on top of pinned baseline: `db70bdedbe36846c665c841ea9f6ae9bf01d0d3d`
- Lineage: `Alia5/VIIPER -> Valkirie/VIIPER -> onehoon/VIIPER`
- License: GPL-3.0; the accompanying `LICENSE.txt` is copied from the pinned source.

## Build attestation

Built with the literal official entrypoint, `just build-libVIIPER Release`, against a clean checkout
of `da78d0fc77034afa48485def31dc1ba54960a04e` (PowerShell 7/`pwsh` installed for this purpose):

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
- Generated header SHA-256: `69D46A77E1E1FF925E986AC5E4A7B50362EB672350040C74AE0F33C3F72ED740`
- DLL SHA-256: `F469C23871EE528BDB390AF953E73A435B8D2DB2BBD68BCB4F17FA7362180F19`
- Canonical entrypoint: `just build-libVIIPER Release` (used verbatim for this artifact; not approximated by a raw `go build`)
- Native ABI checks: `sizeof(SteamControllerDeviceRemoveResult) = 4`, `sizeof(SteamControllerDeviceState) = 62`, `L1 = 4`, `LPadX = 24` (unchanged from the pinned baseline — the diagnostic commit does not touch the ABI struct; confirmed via VIIPER's own ABI/offset tests before this build)

The build does not use or redistribute Handheld Companion's bundled DLL, and no byte-for-byte equivalence claim is made. Rebuilds are traceable through the exact source, recipe, toolchain, and artifact hash; CI verifies the committed artifact hash rather than demanding a byte-identical rebuild.

## Reverting after hardware testing

Once the D-pad root cause is identified from hardware logs, this diagnostic payload must be
replaced again with a build from the normal, non-diagnostic VIIPER baseline (either the current
`db70bdedbe36846c665c841ea9f6ae9bf01d0d3d`, or whatever baseline is current at that time) before
this branch is merged, or this branch should not be merged at all and the diagnostic instrumentation
removed from VIIPER PR #15 / Addon PR #157 instead. Do not let this temporary payload become the
permanent embedded DLL.
