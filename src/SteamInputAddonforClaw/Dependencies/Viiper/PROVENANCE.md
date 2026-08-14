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
- Generated header SHA-256: `7F7965DA90EBE69AB57BBC23389BBFEAF8036224BEE3D4D05E0036B60C557E57`
- DLL SHA-256: `2B02EE966F23AAE8D3CDAE0ECB96CBECB54C9EF37C3B7D653AD0B2DC379CDD93`
- Native ABI checks: `sizeof(SteamControllerDeviceRemoveResult) = 4`, `sizeof(SteamControllerDeviceState) = 62`, `L1 = 4`, `LPadX = 24`

The build does not use or redistribute Handheld Companion's bundled DLL, and no byte-for-byte equivalence claim is made. Rebuilds are traceable through the exact source, recipe, toolchain, and artifact hash; CI verifies the committed artifact hash rather than demanding a byte-identical rebuild.
