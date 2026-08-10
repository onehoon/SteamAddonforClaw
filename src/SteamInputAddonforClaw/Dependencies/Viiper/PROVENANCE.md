# libVIIPER.dll provenance

## Canonical corresponding source

- Repository: https://github.com/onehoon/VIIPER
- Immutable tag: [`steam-input-addon-baseline-1`](https://github.com/onehoon/VIIPER/tree/steam-input-addon-baseline-1)
- Commit: `209c882009caea4f3baf322b9b6020c1a921feed`
- Source archive: https://github.com/onehoon/VIIPER/archive/refs/tags/steam-input-addon-baseline-1.tar.gz
- Lineage: `Alia5/VIIPER -> Valkirie/VIIPER -> onehoon/VIIPER`
- License: GPL-3.0; the accompanying `LICENSE.txt` is copied from the pinned source.

## Build attestation

```text
CGO_ENABLED=1
GOOS=windows
GOARCH=amd64
go build -buildmode=c-shared -ldflags="-s -w" -trimpath -o libVIIPER.dll ./clib/
```

- Go: `go1.26.5 windows/amd64`
- GCC/MinGW: `w64devkit 2.9.1`, `gcc.exe (GCC) 16.2.0`
- Output: `libVIIPER.dll` (Windows x64)
- SHA-256: `04FD174EE7DDAA65D17B9C356668A67DBD5CCA3F08CF6051455A863095DD8474`

The build does not use or redistribute Handheld Companion's bundled DLL, and no byte-for-byte equivalence claim is made. Rebuilds are traceable through the exact source, recipe, toolchain, and artifact hash; CI verifies the committed artifact hash rather than demanding a byte-identical rebuild.
