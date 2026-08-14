# libVIIPER.dll provenance

## Canonical corresponding source

- Repository: https://github.com/onehoon/VIIPER
- Pinned commit: `db70bdedbe36846c665c841ea9f6ae9bf01d0d3d`
- Lineage: `Alia5/VIIPER -> Valkirie/VIIPER -> onehoon/VIIPER`
- License: GPL-3.0; the accompanying `LICENSE.txt` is copied from the pinned source.

## Build attestation

```text
CGO_ENABLED=1
GOOS=windows
GOARCH=amd64
go build -buildmode=c-shared -ldflags="-s -w" -trimpath -o dist/libVIIPER/libVIIPER.dll ./lib/viiper
```

- Go: `go1.26.5 windows/amd64`
- GCC/MinGW: `w64devkit 2.9.1`, `gcc.exe (GCC) 16.2.0`
- Output: `libVIIPER.dll` (Windows x64)
- Generated header SHA-256: `99EC2B08FCC1B168B2AB58BFDDC0B76F74FBC5FFE0D4D2D19D2B25BE1B7CAEF7`
- DLL SHA-256: `FEBD1D688426144E2973EC3914AEED14DCA35235AD0634E3DB4809101FA0999D`
- Canonical entrypoint: `just build-libVIIPER Release`
- Native ABI checks: `sizeof(SteamControllerDeviceRemoveResult) = 4`, `sizeof(SteamControllerDeviceState) = 62`, `L1 = 4`, `LPadX = 24`

The build does not use or redistribute Handheld Companion's bundled DLL, and no byte-for-byte equivalence claim is made. Rebuilds are traceable through the exact source, recipe, toolchain, and artifact hash; CI verifies the committed artifact hash rather than demanding a byte-identical rebuild.
