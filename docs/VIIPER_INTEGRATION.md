# VIIPER Integration Contract

This document is the engineering reference for integrating the `onehoon/VIIPER` fork into **Steam Input Addon for Claw**.

It records the pinned VIIPER baseline, fork-added canonical API surface, ownership and teardown contracts, C# interop rules, packaging and CI requirements, and the intended Addon routing architecture that consumes those APIs.

`README.md` remains the source of truth for overall product behavior and routing policy. If this document conflicts with `README.md` about the Addon's product behavior, supported environments, routing eligibility, or recovery policy, `README.md` wins. This document is authoritative for the pinned VIIPER integration contract unless a newer VIIPER baseline is explicitly adopted and this document is updated in the same change.

---

## Document status

| Item | Status |
|---|---|
| VIIPER hardening | **VALIDATED / COMPLETE / FROZEN** |
| Pinned VIIPER repository | `onehoon/VIIPER` |
| Pinned VIIPER merge baseline | `db70bdedbe36846c665c841ea9f6ae9bf01d0d3d` |
| VIIPER canonical embedded ABI | **VALIDATED** |
| Current Addon VIIPER binding | **CANONICAL M4B PAYLOAD / PRODUCTION STEAMOUTPUT** |
| Canonical Addon migration | **M0-M3 VALIDATED / M4A VALIDATED / M4B IN PROGRESS** |
| Game Bar Xbox360 route | **PLANNED** |
| Gyro/IMU routing | **PLANNED / HARDWARE VALIDATION REQUIRED** |
| MSI Claw hardware validation of the canonical DLL path | **REQUIRES ADDON INTEGRATION TESTING** |
| Last architecture review | 2026-08-14 |

Status labels used below:

- **[VALIDATED VIIPER]** — implemented in the pinned VIIPER baseline and covered by VIIPER tests/CI.
- **[CURRENT ADDON]** — implemented in the current Addon repository at the time this document was created.
- **[ADDON CONTRACT]** — required behavior for the Addon migration.
- **[PLANNED]** — intended Addon behavior that is not yet fully implemented.
- **[TBD / HARDWARE]** — must be finalized from integration or physical MSI Claw validation.

Do not silently promote a **PLANNED** or **TBD / HARDWARE** statement to **VALIDATED**. Update this document when implementation and tests establish the behavior.

---

# 1. Why this document exists

The Addon deliberately keeps VIIPER as an embedded transport/device implementation detail rather than allowing the entire application to depend on VIIPER internals.

The integration boundary must remain small and reviewable:

```text
MSI Claw physical input
        ↓
Addon ControllerState
        ↓
Addon routing policy / recovery / isolation
        ↓
small canonical libVIIPER binding
        ↓
Classic Steam Controller / temporary Xbox360
        ↓
Steam / Windows
```

The main reasons for preserving this boundary are:

1. VIIPER device and transport lifetime must not become a second Addon state machine.
2. Steam Input owns remapping, macros, Action Sets/Layers, keyboard/mouse mapping, turbo, long/double press, and per-game layouts.
3. MSI Center M / ClawTweaks compatibility and HidHide recovery are Addon responsibilities, not VIIPER responsibilities.
4. VIIPER's native handles are process-local. Addon recovery must track OS-visible mutations and ownership evidence, not attempt to persist native handles.
5. A pinned native ABI is easier to test, package, hash, and audit than a generic controller-manager integration.

---

# 2. Pinned VIIPER baseline and hardening history

## 2.1 Pinned baseline

**[VALIDATED VIIPER]**

Repository:

```text
https://github.com/onehoon/VIIPER
```

Pinned integration baseline:

```text
db70bdedbe36846c665c841ea9f6ae9bf01d0d3d
```

This is the merge commit containing the completed canonical embedded lifecycle hardening through VIIPER PR #11, the independent L2/R2 Gordon state extension merged by VIIPER PR #13, and the classified Gordon removal API merged by VIIPER PR #14. M0 and the classified-remove dependency are **VALIDATED**.

The Gordon canonical state now has independent `L2` and `R2` fields. Digital full-pull semantics are:

```text
digital full-pull = explicit L2/R2 OR analog saturation
```

Explicit `L2`/`R2` changes only the digital full-pull bit; it must not change the analog trigger magnitude. The canonical `SteamControllerDeviceState` ABI size is **62 bytes**.

The Addon must use a matching DLL, generated header, and C# P/Invoke definition from the same pinned VIIPER commit/build: `db70bdedbe36846c665c841ea9f6ae9bf01d0d3d` and its matching canonical build. The M4B embedded payload is the verified pair recorded in Section 4.1.

Do not silently update the embedded DLL/source baseline beyond this commit. A baseline update requires the process in [Section 22](#22-updating-the-pinned-viiper-baseline).

## 2.2 Hardening sequence

The final integration baseline includes the following planned hardening sequence:

```text
PR8   Gordon runtime state synchronization
PR9   canonical ABI / ownership invariants
PR10  typed callback lifecycle synchronization
PR11  embedded USB/IP transport ownership and drain
```

The important end result is not the PR numbering itself. The important contract is that the pinned baseline now provides:

- typed canonical device handles;
- caller-owned bus lifetime;
- tracked exact localhost USB/IP attachment ownership;
- fail-closed unknown attach/detach outcomes;
- synchronized callback registration/capture/clear;
- callback clearing before teardown;
- exact-device managed USB/IP binding;
- binding/drain linearization;
- late-import rejection;
- accepted-connection tracking;
- async IN-worker drain;
- batching-worker join;
- public typed Remove waits for managed transport to quiesce;
- two-phase `CloseUSBServer` retry semantics;
- no automatic transport resurrection after a device has been drained.

---

# 3. Canonical architecture: use `lib/viiper`, not `clib`

## 3.1 Required integration path

**[ADDON CONTRACT]**

New Addon code must use:

```text
VIIPER/lib/viiper
```

Do not use as the architectural base:

```text
VIIPER/clib
standalone viiper.exe
private Handheld Companion compatibility APIs
```

`clib` remains in the fork for historical/compatibility consumers. It is not the canonical embedded ABI for Steam Input Addon for Claw.

## 3.2 Intended lifetime model

**[VALIDATED VIIPER / ADDON CONTRACT]**

```text
Addon process lifetime
    └─ loaded libVIIPER.dll module

embedded runtime lifetime
    └─ USB server
       └─ caller-owned USB bus
          ├─ typed Classic Steam Controller (Gordon)
          └─ typed Xbox360 device when required

Windows exposure lifetime
    └─ explicit AttachUSBDevice / DetachUSBDevice

report-routing lifetime
    └─ live / neutral Addon state publication
```

The native DLL module should be treated as process-lifetime once loaded. Do not unload a cgo module while delegates/native callbacks may still reference it.

The exact Addon ownership of the USB server/bus may be refined during migration, but the preferred architecture is a long-lived embedded runtime with shorter-lived typed logical devices. Device removal must not implicitly destroy its caller-owned bus.

---

# 4. Current Addon state before canonical migration

This section intentionally documents the mismatch that the next migration work must remove.

## 4.1 Existing embedded payload

**[CURRENT ADDON — LEGACY]**

The Addon currently packages:

```text
src/SteamInputAddonforClaw/Dependencies/Viiper/libVIIPER.dll
src/SteamInputAddonforClaw/Dependencies/Viiper/libVIIPER.h
src/SteamInputAddonforClaw/Dependencies/Viiper/PROVENANCE.md
src/SteamInputAddonforClaw/Dependencies/Viiper/LICENSE.txt
```

The historical pre-M4B payload was identified by the old baseline:

```text
tag:    steam-input-addon-baseline-1
commit: 209c882009caea4f3baf322b9b6020c1a921feed
build:  ./clib/
```

The historical pre-M4B DLL SHA-256 was:

```text
04FD174EE7DDAA65D17B9C356668A67DBD5CCA3F08CF6051455A863095DD8474
```

That provenance/hash belongs to the old DLL and **must not be used as the active M4B payload identity**.

The active M4B payload is built from pinned commit `db70bdedbe36846c665c841ea9f6ae9bf01d0d3d` and has:

```text
generated header SHA-256: 99EC2B08FCC1B168B2AB58BFDDC0B76F74FBC5FFE0D4D2D19D2B25BE1B7CAEF7
DLL SHA-256:              FEBD1D688426144E2973EC3914AEED14DCA35235AD0634E3DB4809101FA0999D
sizeof(remove result):    4
sizeof(Gordon state):     62
L1 offset / LPadX offset: 4 / 24
```

## 4.2 Existing native binding

**[CURRENT ADDON — LEGACY]**

`ViiperNativeApi.cs` currently binds flat `clib` exports such as:

```text
viiper_init
viiper_shutdown
viiper_bus_create
viiper_bus_remove
viiper_device_add_ex
viiper_device_remove
viiper_device_set_input
viiper_device_set_feedback_callback
viiper_list_device_types
viiper_last_error
viiper_free_string
```

`ViiperRuntimeManager` currently owns devices as `busId + deviceId`, sends raw 64-byte Gordon reports through `viiper_device_set_input`, removes the bus when its last device is removed, and shuts down the legacy API when unused.

These are **migration inputs, not the target contract**.

## 4.3 Existing Addon safety behavior that should survive migration

**[CURRENT ADDON / KEEP]**

The existing `SteamOutput` stage already provides useful Addon-side safety that should be retained conceptually when the native ABI changes:

- recovery intent is recorded before virtual-output mutation;
- pre/post Windows PnP snapshots are used to resolve addon-owned Gordon identity;
- ownership remains uncertain until identity is proven;
- HidHide is inspected so the new output is not unexpectedly blocked;
- a neutral state is sent before live publishing begins;
- the live publisher is stopped before virtual-device removal;
- Windows PnP absence is verified before the recovery mutation is completed;
- rollback failure retains evidence so cleanup can be retried.

The canonical migration should replace the native lifecycle primitive without weakening these Addon-side recovery/identity checks.

---

# 5. Canonical C ABI inventory

The Addon should bind only the subset it actually needs.

## 5.1 Server APIs

**[VALIDATED VIIPER]**

```c
bool NewUSBServer(
    USBServerConfig* config,
    USBServerHandle* outHandle,
    VIIPERLogCallback logCallback);

bool CloseUSBServer(USBServerHandle handle);
```

### `USBServerConfig`

Public C layout:

```c
typedef struct {
    char*    addr;
    uint64_t connection_timeout_ms;
    uint64_t device_handler_connect_timeout_ms;
    uint32_t write_batch_flush_interval_ms;
} USBServerConfig;
```

Important:

- `ManagedTransportLifecycle` is **not** a public C field.
- `DisableAutoBusCleanup` is **not** a public C field.
- canonical `NewUSBServer` enables both internal policies itself.
- do not add those Go-only policy fields to the C# interop struct.

A caller may pass an optional log callback. If a callback is supplied, the Addon must retain the managed delegate strongly until after the server is closed.

## 5.2 Bus APIs

**[VALIDATED VIIPER]**

```c
bool CreateUSBBus(USBServerHandle handle, uint32_t* busID);
bool RemoveUSBBus(USBServerHandle handle, uint32_t busID);
```

`CreateUSBBus` accepts a requested ID. A null/zero bus value may allow the server to select the next available bus depending on the exact C call form used by the binding.

**[ADDON CONTRACT]** Prefer explicit Addon ownership of the returned/chosen bus ID and do not infer lifetime from the presence of devices.

## 5.3 Generic typed-device APIs

**[VALIDATED VIIPER]**

```c
bool GetUSBDeviceIdentity(
    uintptr_t handle,
    uint32_t* outBusID,
    uint32_t* outDeviceID);

bool AttachUSBDevice(uintptr_t handle);
bool DetachUSBDevice(uintptr_t handle);
```

`GetUSBDeviceIdentity` returns the **logical VIIPER USB bus/device identity** for a typed native device handle.

It does **not** prove:

- Windows PnP instance identity;
- device-container identity;
- current Windows attachment state;
- addon ownership of a Windows-enumerated USB node.

Addon PnP ownership must continue to use Addon-side PnP/SetupAPI evidence and before/after snapshots.

## 5.4 Classic Steam Controller APIs

**[VALIDATED VIIPER]**

```c
bool CreateSteamControllerDevice(
    USBServerHandle serverHandle,
    SteamControllerDeviceHandle* outDeviceHandle,
    uint32_t busID,
    bool autoAttachLocalhost,
    uint16_t idVendor,
    uint16_t idProduct);

bool SetSteamControllerDeviceState(
    SteamControllerDeviceHandle handle,
    SteamControllerDeviceState state);

bool SetSteamControllerOutputCallback(
    SteamControllerDeviceHandle handle,
    SteamControllerOutputCallback callback);

bool RemoveSteamControllerDevice(
    SteamControllerDeviceHandle handle);

typedef enum {
    VIIPER_REMOVE_SUCCESS = 0,
    VIIPER_REMOVE_RETRYABLE_FAILURE = 1,
    VIIPER_REMOVE_UNSAFE_OUTCOME_UNKNOWN = 2,
    VIIPER_REMOVE_INVALID = 3
} SteamControllerDeviceRemoveResult;

SteamControllerDeviceRemoveResult RemoveSteamControllerDeviceEx(
    SteamControllerDeviceHandle handle);
```

`RemoveSteamControllerDeviceEx` is the classified Gordon removal contract
consumed by the side-by-side M4A Addon session. `SUCCESS` releases the native
handle; `RETRYABLE_FAILURE` retains known ownership for one explicit retry;
`UNSAFE_OUTCOME_UNKNOWN` and `INVALID` fail closed and must not trigger
destructive Remove, Detach, Attach, bus, or server cleanup. The legacy bool
export remains compatible and returns true only for `SUCCESS`.

Zero vendor/product values individually fall back to Gordon defaults. The Addon target remains explicitly:

```text
VID 28DE
PID 1102
```

**[ADDON CONTRACT]** Prefer passing `0x28DE` and `0x1102` explicitly so the v1 device identity is visible at the Addon boundary rather than relying on an implicit default.

### `SteamControllerDeviceState`

Canonical state fields:

```c
typedef struct {
    uint8_t A, X, B, Y;
    uint8_t L1, R1;
    uint8_t L2, R2;
    uint8_t Menu, Steam, Options;
    uint8_t DPadDown, DPadLeft, DPadRight, DPadUp;
    uint8_t L3;
    uint8_t LGrip, RGrip;
    uint8_t LPadTouch, RPadTouch, LPadPress, RPadPress, LPadAndStick;
    int16_t LPadX, LPadY;
    int16_t RPadX, RPadY;
    uint16_t LTrigger, RTrigger;
    int16_t LStickX, LStickY;
    int16_t AccelX, AccelY, AccelZ;
    int16_t GyroX, GyroY, GyroZ;
    int16_t GyroQuatW, GyroQuatX, GyroQuatY, GyroQuatZ;
    uint16_t BatteryMilliVolts;
} SteamControllerDeviceState;
```

The canonical `SteamControllerDeviceState` layout is **62 bytes**. `L2` and `R2` are explicit digital full-pull inputs; Gordon also sets the corresponding digital full-pull bit when the analog trigger reaches saturation. Explicit digital full-pull inputs do not modify `LTrigger` or `RTrigger`.

The native Gordon object owns its own frame progression. The Addon supplies logical state, not a manually owned Gordon frame number.

### Gordon host-output callback

```c
typedef void (*SteamControllerOutputCallback)(
    SteamControllerDeviceHandle handle,
    const uint8_t* data,
    uint32_t length);
```

The canonical Gordon output payload is 64 bytes.

The callback buffer is valid only for the synchronous callback invocation. Copy it immediately if any data must survive callback return.

Passing `NULL` clears callback registration.

## 5.5 Xbox360 APIs

**[VALIDATED VIIPER]**

```c
bool CreateXbox360Device(
    USBServerHandle serverHandle,
    Xbox360DeviceHandle* outDeviceHandle,
    uint32_t busID,
    bool autoAttachLocalhost,
    uint16_t idVendor,
    uint16_t idProduct,
    uint8_t xinputSubType);

bool SetXbox360DeviceState(
    Xbox360DeviceHandle handle,
    Xbox360DeviceState state);

bool SetXbox360RumbleCallback(
    Xbox360DeviceHandle handle,
    Xbox360RumbleCallback callback);

bool RemoveXbox360Device(
    Xbox360DeviceHandle handle);
```

Canonical state:

```c
typedef struct {
    uint32_t Buttons;
    uint8_t  LT;
    uint8_t  RT;
    int16_t  LX;
    int16_t  LY;
    int16_t  RX;
    int16_t  RY;
    uint8_t  Reserved[6];
} Xbox360DeviceState;
```

For the Addon's temporary Game Bar route, default Xbox360 identity/subtype values are preferred unless hardware/software validation establishes a reason to override them.

---

# 6. C# interop rules

## 6.1 Native boolean representation

**[ADDON CONTRACT]**

Do not bind cgo-generated boolean returns/parameters as a Win32 4-byte `BOOL`.

The generated canonical header exposes Go/C boolean values as one-byte values (`GoUint8` in generated declarations). The Addon binding should represent those values as `byte` at the native boundary:

```csharp
private static bool Succeeded(byte value) => value != 0;
```

Use `byte` for `autoAttachLocalhost` as well.

Do not depend on default `[MarshalAs(UnmanagedType.Bool)]` behavior.

## 6.2 Handles

Use native-width values:

```text
USBServerHandle              -> nuint / UIntPtr
SteamControllerDeviceHandle  -> nuint / UIntPtr
Xbox360DeviceHandle          -> nuint / UIntPtr
```

A handle is opaque. The Addon must not reinterpret it as a bus ID, device ID, pointer to a struct, or Windows PnP identity.

## 6.3 Struct layout

Use sequential native layout and native primitive widths:

| C type | C# interop type |
|---|---|
| `uint8_t` | `byte` |
| `uint16_t` | `ushort` |
| `int16_t` | `short` |
| `uint32_t` | `uint` |
| `uint64_t` | `ulong` |
| `uintptr_t` | `nuint` |
| `char*` | unmanaged UTF-8 pointer |

Do not force `Pack=1` unless a generated-header/native layout test proves that is the ABI. Use natural native alignment and add explicit `Marshal.SizeOf` / `Marshal.OffsetOf` regression tests for the structs bound by the Addon.

For Gordon button fields, use `byte`, not C# `bool`.

## 6.4 Delegate lifetime

All native callbacks must have a managed strong reference for at least the entire period in which the native side can call them.

Examples:

- VIIPER log callback: server lifetime;
- Gordon output callback: Gordon registration lifetime through Remove completion;
- Xbox360 rumble callback: Xbox360 registration lifetime through Remove completion.

Never create a temporary delegate, pass it to native code, and allow it to be garbage-collected.

## 6.5 Calling convention

Use the calling convention matching the generated cgo C ABI. The existing Addon native layer uses `CallingConvention.Cdecl`; retain an explicit convention rather than depending on runtime defaults, and verify the Windows x64 DLL through integration tests.

## 6.6 Native module loading

The current Addon already loads VIIPER by absolute path and caches the module for process lifetime. Keep that security/lifetime property during migration:

```text
absolute bundled path
→ NativeLibrary.Load
→ explicit GetExport
→ process-lifetime module cache
```

Do not fall back to unrestricted DLL search-path lookup.

---

# 7. Caller-owned bus contract

**[VALIDATED VIIPER]**

A typed `Remove*Device` operation ends only that logical device lifetime.

It does not automatically remove the USB bus.

```text
CreateUSBBus
    ↓
Create Gordon
    ↓
Remove Gordon
    ↓
Create another typed device if required
    ↓
...
    ↓
RemoveUSBBus or CloseUSBServer
```

Canonical managed servers disable the old automatic empty-bus cleanup behavior.

**[ADDON CONTRACT]**

Do not port the current legacy `ViiperRuntimeManager` behavior that automatically removes the bus simply because `_devices.Count == 0` without first deciding that this is also the end of the Addon-owned bus lifetime.

Bus lifetime must be an explicit Addon ownership decision.

---

# 8. Attachment contract

## 8.1 Normal attach/detach

**[VALIDATED VIIPER]**

```text
Detached
   │ AttachUSBDevice
   ▼
Attached
   │ DetachUSBDevice
   ▼
Detached
```

Each successful attachment stores the exact backend and positive usbip-win2 import port used by that operation. Detach targets that stored token.

Calling Attach on an already attached device is idempotent. Calling Detach on an already detached device is idempotent.

## 8.2 Known failure

A known detach failure preserves the attachment token and attached state so the operation can be retried safely.

A known logical removal failure after a successful detach preserves the surviving logical handle while leaving the transport drained.

## 8.3 Unknown outcome

An attach/detach whose outcome is unknowable is fail-closed:

```text
attachmentOutcomeUnknown
→ owning canonical server becomes close-failed
→ no automatic destructive cleanup for that ambiguous record
→ no blind retry of the old ownership token
```

The Addon should surface this as an unsafe/indeterminate routing condition and preserve its OS-visible recovery evidence.

## 8.4 Drained transport is one-way

**[VALIDATED VIIPER]**

Once the same exact logical registration reaches managed transport `drained`, `AttachUSBDevice` must not resurrect it.

```text
transport drained
→ AttachUSBDevice(same handle)
→ false
→ no new Windows attach backend call
→ no new import binding
```

A completely new typed device registration receives a new transport lifecycle.

---

# 9. Strong typed Remove contract

**[VALIDATED VIIPER]**

Canonical typed removal follows this logical ordering:

```text
lifecycleMu locked
    ↓
validate server + exact concrete device type
    ↓
clear native callback reference
    ↓
exact detach
    ↓
BeginDeviceDrain
    ↓
logical device removal
    ↓
handle finalization if logical removal succeeded
    ↓
retire transport identity if logical removal succeeded
    ↓
release lifecycleMu
    ↓
wait captured managed transport drain
    ↓
public Remove returns
```

The wait occurs outside `lifecycleMu`. This is required because a callback already in flight may synchronously re-enter another canonical API and need `lifecycleMu` before it can finish.

## 9.1 Successful return guarantee

After a successful public typed Remove returns:

- the typed canonical handle is finalized;
- no managed USB/IP connection/worker for the old registration remains active;
- no old callback can continue through VIIPER-managed transport;
- the caller-owned bus remains unless separately removed.

This guarantee does not extend to arbitrary goroutines that application callback code chooses to spawn itself.

## 9.2 Known logical removal failure

If callback clear, detach, and transport quiesce succeed but logical removal fails:

```text
callback     = cleared
attachment   = detached
transport    = drained before public return
logical dev  = retained
canonical handle = retained
server       = active for an ordinary known logical error
```

Retry must not:

- detach a second time;
- reopen the drained transport;
- restore the callback automatically.

Retry performs only the remaining idempotent teardown work and finalizes the handle exactly once when successful.

---

# 10. `RemoveUSBBus` contract

**[VALIDATED VIIPER]**

Bus removal owns all logical devices still registered on that bus.

The key ordering is:

```text
preflight attachment ownership
→ clear callbacks
→ exact detach of surviving devices
→ BeginDeviceDrain for each device
→ remove bus
→ finalize canonical handles
→ release lifecycleMu
→ wait every drain started by the operation
→ return
```

If bus removal returns an error but the bus is already absent, canonical code treats that as effective removal and finalizes the matching ownership records according to the existing partial-success contract.

If a known bus removal error leaves the bus present, surviving ownership remains available for retry; already detached/drained records are not reopened.

---

# 11. `CloseUSBServer` two-phase contract

**[VALIDATED VIIPER]**

Canonical server close intentionally separates logical ownership teardown from transport/server close.

## Phase A — logical teardown

Under `lifecycleMu`:

```text
validate active / retryable close-failed state
→ reject unknown attachment ownership
→ state = closing
→ clear all callback references
→ process buses in stable order
→ detach devices
→ begin transport drains
→ remove logical buses/devices
→ finalize handles
→ closePhase = transportClosePending only when logical ownership is complete
```

Then release `lifecycleMu` and wait all drains begun by Phase A.

If Phase A partially fails, already-finalized records stay finalized. Retry operates only on records still present.

## Phase B — managed transport/server close

Outside `lifecycleMu`:

```text
managed Server.Close
→ stop new accepted-connection registration
→ close listener
→ wait accept loop termination
→ close tracked accepted connections
→ wait connection handlers
→ wait async transfer workers
→ wait batching workers
```

On success:

```text
state = closed
closePhase = closeComplete
server native handle finalized
```

On Phase B failure:

```text
state = close-failed
closePhase remains transportClosePending
server handle retained
```

A retry from `transportClosePending` retries **only Phase B**. It must not replay detach, logical removal, or handle finalization from Phase A.

---

# 12. Managed USB/IP transport guarantees

The following are VIIPER internals, but they explain why the Addon can rely on the public Remove/Close completion boundaries.

## 12.1 Accepted connection ownership

**[VALIDATED VIIPER]**

Managed mode tracks accepted connections and serializes registration against server close under the transport synchronization boundary.

After close transitions to closing, there is no future `WaitGroup.Add` for a newly accepted handler. A connection racing with close is either:

- registered and subsequently drained; or
- rejected and closed without launching a managed handler.

## 12.2 Import binding before success

**[VALIDATED VIIPER]**

Managed import performs:

```text
read requested bus ID from socket
→ transport synchronization boundary
→ exact current device lookup
→ exact binding reservation
→ release transport synchronization
→ only then send successful OP_REP_IMPORT
→ enter URB stream
```

Device lookup and binding reservation are linearized with `BeginDeviceDrain`.

Therefore a drain that wins the race prevents a late import from receiving a successful bind/reply, while a bind that wins the race becomes drain-owned.

## 12.3 Device drain states

Conceptually:

```text
active / accepting
→ quiescing
→ drained
```

`quiescing` is an internal transient state. Any public teardown API that starts the corresponding drain waits until the captured drain boundary completes before returning.

## 12.4 Async non-EP0 IN workers

Managed URB streams track asynchronous non-EP0 IN workers. Stream teardown cancels pending URBs and waits the worker group before the connection handler is considered drained.

## 12.5 Batching writer

Managed stream teardown joins the batching flush worker before the stream is considered drained. A zero flush interval does not wait on a worker that was never started.

---

# 13. usbip-win2 compatibility

## 13.1 VIIPER-pinned baseline

**[VALIDATED VIIPER]**

Supported native attachment baseline:

```text
usbip-win2 v0.9.7.7
commit 7c219953101cc5d0ec9a0bcb3eb87259cf72bedd
```

`v0.9.7.8` and later are not assumed compatible until their ABI/runtime behavior is explicitly reviewed.

## 13.2 Current Addon package

**[CURRENT ADDON]**

The Addon already bundles:

```text
Dependencies/UsbIpWin2/USBip-0.9.7.7-x64.exe
```

with package metadata and installer hash validation.

## 13.3 Required runtime gate

**[ADDON CONTRACT]**

Before the first canonical `AttachUSBDevice`, the Addon must verify the installed usbip-win2 environment is the exact supported baseline or an explicitly approved equivalent.

Do not use a generic "USB/IP interface exists" result as sufficient proof of compatibility.

Do not silently accept a newer installed version merely because it exposes similarly named interfaces.

The exact installed-version gate should be reviewed during canonical migration because the Addon currently has package/provisioning inspection that predates the final VIIPER attachment contract.

---

# 14. Building and embedding canonical `libVIIPER.dll`

## 14.1 Canonical build

**[VALIDATED VIIPER]**

From the pinned VIIPER source:

```bash
just build-libVIIPER Release
```

The Windows canonical build produces the relevant outputs under:

```text
dist/libVIIPER/
    libVIIPER.dll
    libVIIPER.h
    libVIIPER.def
    licenses.txt
```

The Addon consumes the canonical `lib/viiper` shared library, not a DLL built from `./clib/`.

## 14.2 Addon embedded layout

**[CURRENT ADDON / KEEP STRUCTURE]**

The existing project already copies the VIIPER dependency directory to build and publish output:

```text
Dependencies/Viiper/libVIIPER.dll
Dependencies/Viiper/libVIIPER.h
Dependencies/Viiper/PROVENANCE.md
Dependencies/Viiper/LICENSE.txt
```

This layout can be retained during migration.

## 14.3 Required payload replacement steps

**[ADDON CONTRACT]**

When adopting the canonical baseline:

1. build `libVIIPER.dll` from pinned commit `db70bded...` using canonical `lib/viiper`;
2. replace `Dependencies/Viiper/libVIIPER.dll`;
3. replace `Dependencies/Viiper/libVIIPER.h` with the matching generated header;
4. replace/update `Dependencies/Viiper/LICENSE.txt` / notices as required by the built artifact;
5. update `Dependencies/Viiper/PROVENANCE.md` with the new source commit, build recipe, toolchain, and SHA-256;
6. update `ViiperRuntimeInspector.ExpectedPayloadSha256`;
7. update `scripts/verify-publish-assets.ps1` with the same new SHA-256;
8. verify the publish payload contains exactly the intended DLL/header/provenance/license files;
9. do not leave the old `./clib/` provenance text in the repository after the canonical artifact is adopted.

Do not make a byte-for-byte reproducibility claim unless the build pipeline actually guarantees it. The required property is traceable source + exact build recipe/toolchain + committed artifact hash.

## 14.4 Runtime dependency policy

The Addon must not require:

- standalone `viiper.exe`;
- ViGEmBus;
- Handheld Companion;
- ClawTweaks;
- a second separately installed VIIPER runtime.

The intended native runtime dependencies are the embedded canonical DLL plus the supported usbip-win2 environment.

---

# 15. VIIPER CI contract

The pinned VIIPER baseline passed the repository CI that includes:

- Go lint/test;
- canonical focused tests;
- lifecycle race tests;
- Go vet for canonical packages;
- Linux canonical shared-library build;
- Windows canonical shared-library build;
- generated-header validation;
- DLL export validation;
- generated client code;
- C# client pack/build smoke;
- C++ client smoke;
- TypeScript client build/smoke;
- Rust client build/smoke.

The Windows canonical ABI gate explicitly verifies exports including:

```text
GetUSBDeviceIdentity
AttachUSBDevice
DetachUSBDevice
CreateSteamControllerDevice
SetSteamControllerDeviceState
SetSteamControllerOutputCallback
RemoveSteamControllerDevice
RemoveSteamControllerDeviceEx
```

and the generated Gordon callback/create/state/remove declarations.

## 15.1 Required validation for a future VIIPER baseline update

**[ADDON CONTRACT]**

At minimum run/require:

```bash
go test ./...
go test -race ./...
go vet ./...
golangci-lint run
just build-libVIIPER Release
```

and require the canonical Windows DLL/header/export CI to pass.

If the baseline changes any server/bus/typed-device/callback/transport code, repeat the lifecycle review in this document rather than treating a green build alone as sufficient.

---

# 16. Addon CI and regression requirements after migration

**[PLANNED]**

Canonical integration should add or update automated coverage for the following Addon-side boundary.

## 16.1 Interop ABI tests

- expected native symbol names are defined in one binding layer;
- native `byte` boolean conversion is explicit;
- classified Gordon removal uses the validated native C enum width and exact
  result values;
- handle types are native-width;
- `USBServerConfig` field offsets/size match the generated header;
- `SteamControllerDeviceState` offsets/size match the generated header;
- `Xbox360DeviceState` offsets/size match the generated header;
- callbacks use the intended calling convention;
- callback delegates are retained strongly for native lifetime.

## 16.2 Native payload tests

On Windows CI where the bundled DLL is available:

- load the DLL from the exact bundled absolute path;
- resolve every Addon-required canonical export;
- fail if a legacy-only symbol is accidentally required by new production code;
- validate the committed DLL SHA-256 against `PROVENANCE.md`/runtime metadata;
- verify the publish payload contains the matching DLL/header/license/provenance set.

## 16.3 Runtime unit tests

Mockable runtime tests should cover:

- server creation failure;
- bus creation failure;
- typed Gordon create failure;
- explicit attach success/failure;
- neutral state publication before live routing;
- live state rejection fault handling;
- typed Remove success;
- typed Remove false with retained ownership/retry policy;
- bus removal failure;
- Close Phase-B false/retry without replaying Addon logical cleanup;
- callback delegate registration/clear lifetime;
- shutdown/dispose idempotence.

## 16.4 Existing Addon gates

Continue to require:

```text
dotnet test
dotnet build (including Release before PR completion)
git diff --check
publish asset verification
```

and preserve the existing repository rule that implementation work is done on a task branch with tests and a Draft PR, not directly merged by an agent.

---

# 17. Addon routing architecture using canonical VIIPER

This section connects the native contract to the Addon's routing pipeline. Product-policy conflicts are resolved in favor of `README.md`.

## 17.1 Current eligibility model

**[CURRENT ADDON]**

The current Addon intervenes only when its recovery, hardware/environment, prerequisite, and Steam-session gates allow the Stock Center M routing plan.

External physical-controller presence is not a routing gate. Do not reintroduce an external-controller veto as part of VIIPER migration.

The current MVP supports Stock MSI Center M. ClawTweaks production compatibility and Game Bar Xbox360 routing remain planned.

## 17.2 Routing pipeline stages

**[CURRENT ADDON]**

The pipeline model includes:

```text
NativeMode
PhysicalInput
PhysicalIsolation
ThirdPartyIsolation
SteamOutput
XboxOutput
GameBarRouting
```

Stock Center M currently enables the production non-gyro path through `NativeMode`, `PhysicalInput`, `PhysicalIsolation`, and `SteamOutput`. The canonical migration should change the implementation behind the output stage rather than create a competing runtime state machine.

## 17.3 Recommended canonical native ownership layer

**[PLANNED]**

Keep native details behind a narrow subsystem, for example:

```text
VirtualOutput/Viiper/
    ViiperNativeApi             // canonical export binding only
    ViiperRuntimeManager        // USB server/bus ownership
    SteamControllerDevice       // typed Gordon lifetime/state/callback
    Xbox360Device               // typed X360 lifetime/state/callback
    AddonOwnedVirtualDeviceTracker
    PnP identity resolver/diagnostics
```

Names may change, but responsibilities should not leak into the routing policy layer.

---

# 18. Intended Gordon session flow

## 18.1 Creation

**[ADDON CONTRACT / PLANNED MIGRATION]**

For an eligible routing session:

```text
recovery intent recorded
→ snapshot matching PnP state
→ ensure canonical USB server/bus ready
→ CreateSteamControllerDevice(
       autoAttachLocalhost = false,
       VID = 0x28DE,
       PID = 0x1102)
→ optional GetUSBDeviceIdentity for VIIPER logical diagnostics only
→ AttachUSBDevice(Gordon handle)
→ resolve exact new Windows PnP identity using Addon snapshot logic
→ checkpoint addon ownership in RecoveryManager
→ verify output is not blocked by HidHide
→ publish neutral typed state
→ start live ControllerState publication
```

Do not use `autoAttachLocalhost=true` for normal Addon integration. Explicit attach gives the Addon an observable lifecycle boundary and keeps create/attach failure handling separate.

## 18.2 Live state publication

**[PLANNED]**

The existing Addon publisher currently constructs raw 64-byte Gordon input reports and calls the legacy raw `SetInput` API at a nominal 250 Hz.

Canonical migration should instead map the Addon's normalized `ControllerState` into `SteamControllerDeviceState` and call:

```text
SetSteamControllerDeviceState
```

This removes Addon ownership of VIIPER's Gordon frame counter and keeps report serialization inside the Gordon implementation.

The existing `ClassicSteamControllerReportBuilder` may remain temporarily as a regression/protocol reference during migration, but production canonical output should not require the old generic `viiper_device_set_input` path.

Whether the nominal 250 Hz publication cadence remains optimal should be validated on hardware/Steam after the typed migration; do not change cadence merely because the native API changed.

## 18.3 Fixed Claw mapping

**[CURRENT ADDON / KEEP]**

Physical rear controls are independent DirectInput inputs and must remain independent Steam Controller grips.

```text
M2 / physical left rear  → Gordon Left Grip
M1 / physical right rear → Gordon Right Grip
```

Known MSI DirectInput source indices:

```text
M1 = Buttons[15]
M2 = Buttons[16]
```

Do not derive M1/M2 from XInput.

The current right stick remains represented as the Gordon right pad, and current Addon mapping/report semantics should be preserved unless a dedicated mapping change is reviewed separately.

## 18.4 Rollback

**[ADDON CONTRACT]**

```text
stop live publisher first
→ neutral if needed/possible for transition safety
→ RemoveSteamControllerDevice
→ wait for native Remove result (native drains transport before return)
→ verify addon-owned Windows PnP nodes are absent
→ clear Addon ownership uncertainty only after verified absence
→ complete RecoveryManager virtual-output mutation
```

Do not manually remove the bus merely because Gordon was removed unless the Addon is intentionally ending the bus/runtime lifetime.

---

# 19. Game Bar routing contract

## 19.1 Product behavior

**[PLANNED]**

When Game Bar becomes foreground during an active Steam routing session:

```text
same Gordon remains attached
→ Gordon receives neutral states
→ temporary Xbox360 output becomes active
→ live Claw input routes to Xbox360
```

On Game Bar exit:

```text
Xbox360 output is disabled/removed according to the finalized Addon lifetime policy
→ same Gordon handle/device remains
→ live Gordon publication resumes
```

The Gordon device must not be disconnected/recreated merely because Game Bar foreground state changes. Avoiding Gordon hotplug/rebinding is a core UX requirement.

## 19.2 X360 logical-object policy

**[TBD / HARDWARE]**

The behavioral requirement is a temporary X360 route. The exact optimization may be validated during implementation:

- create/attach X360 on Game Bar entry and remove it on exit; or
- retain a logical X360 object for a bounded session and explicitly attach/detach it, if that proves safer/faster and still respects VIIPER's one-way drained-registration rules.

Do not design a path that attempts to reattach a typed registration after it has already been drained by `RemoveXbox360Device`.

## 19.3 Foreground detection

Game Bar detection remains an Addon responsibility and must not depend on ClawTweaks private IPC.

Preferred mechanism remains the Addon-side WinEvent foreground hook + process/package identity validation described by the product architecture.

---

# 20. Output callbacks, rumble, and haptics

## 20.1 Gordon output

**[VALIDATED VIIPER / PLANNED ADDON USE]**

`SetSteamControllerOutputCallback` supplies raw 64-byte Gordon host-output data.

The native callback is synchronized and cleared by typed removal before transport teardown. An already captured/running callback may finish; public Remove waits for managed transport to drain without holding `lifecycleMu`.

Addon callback code should:

- copy native data synchronously if it must retain it;
- avoid throwing exceptions across the unmanaged callback boundary;
- avoid blocking for long operations;
- dispatch any longer processing to controlled managed code;
- keep its delegate alive until Remove/clear is complete.

## 20.2 Xbox360 rumble

`SetXbox360RumbleCallback` exposes left/right motor values.

How those motor requests are converted to MSI Claw physical haptics is an Addon/device-specific concern and should be implemented separately from the ABI migration.

## 20.3 Current scope

The present Addon README still treats rumble/haptics and Game Bar X360 routing as outside the current MVP. Canonical ABI migration must not accidentally enable partially implemented feedback behavior.

---

# 21. Gyro/IMU contract

## 21.1 VIIPER capability

**[VALIDATED VIIPER]**

`SteamControllerDeviceState` already exposes:

```text
AccelX / AccelY / AccelZ
GyroX / GyroY / GyroZ
GyroQuatW / GyroQuatX / GyroQuatY / GyroQuatZ
```

The native ABI therefore has a place to receive sensor data.

## 21.2 Addon status

**[CURRENT ADDON / PLANNED]**

The current production Claw path is explicitly non-gyro. Sensor acquisition, model-specific source differences, calibration, units/scaling, orientation transforms, and hardware validation are not completed merely by migrating to the typed Gordon state.

Keep sensor fields neutral/zero until the Addon's separate gyro work establishes model-specific semantics for supported Claw hardware.

Do not infer sensor values from unrelated controller axes.

---

# 22. Updating the pinned VIIPER baseline

A future VIIPER upgrade is an explicit dependency change, not a casual DLL replacement.

Required procedure:

1. identify the exact new VIIPER commit;
2. review diff from the currently pinned commit;
3. classify changes touching:
   - public ABI;
   - server/bus ownership;
   - typed device lifetime;
   - callback lifetime;
   - attach/detach semantics;
   - managed USB/IP transport;
   - Gordon/Xbox360 state/protocol;
4. run the full VIIPER test/race/vet/lint/build gates;
5. require Windows canonical header/export verification;
6. build the canonical Release DLL;
7. update Addon DLL/header/license/provenance/hash together;
8. run Addon interop/runtime/publish tests;
9. update this document's pinned baseline and any changed contracts in the same Addon PR.

Do not accept "latest main" as a runtime dependency description. Record an immutable commit.

---

# 23. Recovery and crash behavior

## 23.1 Native handles are not persistent recovery state

VIIPER server/device handles are process-local cgo handles. They do not survive process termination and must not be serialized as if they could be recovered in a later process.

## 23.2 Addon recovery responsibility

**[CURRENT ADDON / KEEP]**

Recovery tracks the Addon's forward OS-visible mutations and ownership evidence, including:

- native MSI controller-mode changes;
- HidHide changes owned by the Addon;
- addon-owned virtual-output identity/recovery mutation evidence.

The current Addon startup policy establishes the live Stock MSI baseline from current hardware and retires stale previous-process journal bookkeeping rather than reconstructing old native handles or a previous routing session. Follow `README.md` if this startup policy changes.

## 23.3 Runtime rollback ordering

The routing pipeline remains the owner of the larger rollback dependency order. VIIPER migration must fit inside the existing `SteamOutput` stage rather than override the overall pipeline's dependency ordering.

At the SteamOutput boundary, the important local rule is:

```text
stop publication
→ native typed virtual-output teardown/drain
→ verify PnP absence / ownership release
→ complete that stage's recovery mutation
```

HidHide and MSI native-mode restoration remain coordinated by their own pipeline stages and recovery records according to `README.md`.

---

# 24. PnP identity and HidHide interaction

## 24.1 Logical VIIPER identity is not Windows identity

Never replace Addon PnP ownership logic with `GetUSBDeviceIdentity`.

VIIPER's bus/device IDs are useful for native diagnostics. Windows PnP ownership must continue to be established from Windows enumeration evidence.

This matters because usbip-win2/VIIPER can appear as physical-looking USB topology in Windows.

## 24.2 Existing Addon output identity policy

**[CURRENT ADDON / KEEP]**

The current SteamOutput stage:

- snapshots matching devices before creation;
- waits for the new Gordon PnP identity after creation;
- records the exact instance IDs it owns;
- fails closed if the new output identity cannot be resolved safely;
- checks that HidHide is not unexpectedly blocking the owned output;
- verifies exact absence during rollback before declaring ownership released.

Retain this pattern after canonical migration.

## 24.3 HidHide ownership

VIIPER does not own the Addon's HidHide policy.

The Addon must continue to mutate only its own HidHide deltas, preserve foreign configuration, and restore only what the Addon changed.

---

# 25. MSI Center M / ClawTweaks / HHC boundaries

## 25.1 Stock Center M

**[CURRENT ADDON]**

Stock Center M is the current production mutation environment. Canonical VIIPER migration should preserve the existing current-world baseline, DirectInput acquisition, physical isolation, and recovery behavior.

## 25.2 ClawTweaks

**[PLANNED]**

ClawTweaks compatibility remains optional future behavior.

VIIPER integration must not:

- depend on ClawTweaks private IPC;
- steal/mutate a ClawTweaks-owned virtual device;
- require ClawTweaks source changes;
- disable unrelated TDP/fan/OSD/performance features;
- create duplicate game input alongside a ClawTweaks virtual output.

## 25.3 Handheld Companion

HHC remains a reference implementation/source for hardware/protocol understanding, not a production runtime dependency for the Addon.

The Addon must not switch to HHC's compatibility `clib` path simply because HHC-derived examples use it.

---

# 26. Failure-handling matrix

The exact user-visible reason strings belong to Addon implementation. The lifecycle response should follow this matrix.

| Failure | Required Addon behavior |
|---|---|
| bundled canonical DLL missing | remain passive / prerequisite failure |
| bundled DLL hash mismatch | fail closed; do not load/attach |
| required canonical export missing | fail closed; do not route |
| unsupported usbip-win2 installed version | do not call first Attach |
| `NewUSBServer` fails | no routing mutation; report prerequisite/runtime failure |
| `CreateUSBBus` fails | no typed device creation; clean/close runtime as appropriate |
| Gordon create fails before handle ownership | rollback current SteamOutput intent |
| Gordon create returns surviving ownership after an ambiguous native failure | preserve evidence; fail closed; do not pretend no device exists |
| `AttachUSBDevice` known failure | no live publishing; rollback/retry according to exact state |
| attach/detach unknown outcome | fail closed / unsafe; preserve ownership evidence |
| PnP ownership cannot be proven | native teardown + verify candidate absence; recovery stays unsafe until verified |
| neutral state rejected | do not start live publisher; rollback |
| live state call rejected | stop/fault current SteamOutput session and enter existing pipeline cleanup path |
| typed Remove returns false after drain/logical failure | retain native handle ownership in manager; never reattach drained registration; retry Remove |
| `RemoveUSBBus` known failure | retain bus ownership and retry; do not fake success |
| `CloseUSBServer` Phase B fails | retain server handle; retry Close only; do not reconstruct Phase A resources |
| callback throws in managed code | contain/log in callback boundary; no unmanaged exception escape |
| Addon process crashes | native handles die with process; next startup follows Addon current-world/recovery policy, not handle reconstruction |

---

# 27. Things the Addon must never do

**[ADDON CONTRACT]**

Do not:

- use `clib` for new production integration;
- require standalone `viiper.exe`;
- assume ViGEmBus exists;
- use `autoAttachLocalhost=true` as the normal Addon lifecycle;
- treat VIIPER bus/device IDs as Windows PnP identity;
- reattach a typed registration after native transport drain;
- assume removing Gordon/X360 also removes the caller-owned bus;
- hold an Addon global lifecycle lock while waiting for a native callback/transport drain if callback code may re-enter the same lock;
- unload `libVIIPER.dll` while callbacks/handles can exist;
- destroy/recreate Gordon merely because Game Bar foreground state changed;
- silently accept an unreviewed usbip-win2 upgrade;
- silently replace the pinned VIIPER DLL without updating provenance/hash/header;
- move remapping/macros/profiles into VIIPER or the Addon;
- reintroduce external-controller presence as a routing eligibility rule unless the product architecture explicitly changes again.

---

# 28. Recommended Addon migration sequence

Keep migration PRs deliberately small.

## Phase 0 — integration contract

**This document.**

No runtime behavior change.

## Phase 1 — canonical payload + native ABI binding

Goal:

- build/embed the canonical DLL from `db70bded...`;
- update header/provenance/hash verification;
- replace legacy flat symbol binding with canonical server/bus/device bindings;
- add C# ABI/layout/export tests;
- do not yet redesign routing behavior.

Acceptance:

- Release build succeeds;
- Addon loads only the bundled canonical DLL by absolute path;
- all required symbols resolve;
- old `viiper_*` flat symbols are not required by production code;
- publish asset hash/provenance checks pass.

## Phase 2 — canonical runtime/server/bus ownership

Goal:

- replace `viiper_init/shutdown` semantics with `NewUSBServer/CloseUSBServer`;
- establish explicit caller-owned bus lifetime;
- unit-test failure/retry/dispose behavior.

Do not add Game Bar routing yet.

## Phase 3 — typed Gordon lifecycle parity

Goal:

- `CreateSteamControllerDevice(autoAttach=false, 28DE:1102)`;
- explicit `AttachUSBDevice`;
- retain existing Addon PnP ownership/recovery checks;
- `RemoveSteamControllerDevice` rollback with retained-handle retry semantics;
- no automatic bus destruction from typed Remove.

First hardware validation should happen here.

## Phase 4 — typed Gordon state publication

Goal:

- map normalized Addon `ControllerState` to `SteamControllerDeviceState`;
- replace production raw-report `SetInput` with `SetSteamControllerDeviceState`;
- preserve current non-gyro mapping and nominal publication behavior;
- keep sensor fields neutral until gyro work is separately validated.

## Phase 5 — Gordon host-output callback

Goal:

- bind/retain `SetSteamControllerOutputCallback` delegate;
- safely copy/process output payloads;
- determine physical feedback policy separately.

Do not combine with unrelated gyro work.

## Phase 6 — Game Bar Xbox360 route

Goal:

- typed Xbox360 create/state/rumble/remove lifecycle;
- persistent same Gordon during Game Bar;
- Gordon neutral while X360 is live;
- minimal hotplug behavior;
- no ClawTweaks private IPC.

## Phase 7 — compatibility / recovery hardening

Goal:

- validate power transitions and partial native failure under canonical ABI;
- validate current-process recovery ownership;
- then address ClawTweaks compatibility as a separate concern.

## Phase 8 — gyro/IMU

Separate feature. Do not couple gyro correctness to the canonical ABI migration.

---

# 29. Hardware validation checklist for Addon integration

Physical validation occurs through the Addon rather than a separate VIIPER smoke utility.

At minimum validate on a supported MSI Claw:

1. Addon startup remains passive before an eligible Steam session.
2. canonical DLL loads from bundled path.
3. supported usbip-win2 version gate passes only for the intended environment.
4. eligible route creates exactly one addon-owned Gordon `28DE:1102`.
5. Steam recognizes it as the expected Classic Steam Controller.
6. M2 and M1 appear as independent Left/Right Grip inputs.
7. buttons, D-pad, triggers, left stick, right-pad mapping, and rear buttons behave as before migration.
8. live publisher/state calls remain stable at expected cadence.
9. ending the Steam session removes the addon-owned output without leaving the bus/device/PnP state behind.
10. repeated Steam sessions do not leak handles, buses, usbip connections, or duplicate devices.
11. known attach/detach/remove failure paths can be retried without duplicate detach or transport resurrection.
12. application shutdown after a clean session leaves no addon-owned virtual output.
13. suspend/resume follows current Addon recovery/power policy and does not reuse stale native handles.
14. later Game Bar validation confirms the same Gordon remains present while temporary Xbox360 routing is active.

Record the VIIPER commit, Addon commit, MSI Claw model, MSI Center M version, usbip-win2 version, and relevant logs for hardware sign-off.

---

# 30. Source/reference hierarchy

Use the following priority when resolving implementation questions:

1. **Addon `README.md`** — product behavior, supported environments, routing/recovery policy.
2. **This document** — pinned VIIPER/Add-on integration contract.
3. **Pinned `onehoon/VIIPER` canonical source** — exact ABI/lifecycle implementation.
4. **Generated `libVIIPER.h` from the pinned build** — concrete C ABI layout/signatures.
5. Addon source/tests — current implementation reality.
6. HHC/public Steam Controller protocol references — hardware/protocol reference where the Addon/VIIPER contract intentionally delegates details.
7. DS4Windows/Windows PnP patterns — USB/IP/HidHide/physical-vs-virtual identity reference where applicable.

Do not copy a reference project's overall architecture when a smaller independent implementation is sufficient.

---

# 31. Quick reference for a new development session

If starting a fresh conversation or coding session, establish these facts first:

```text
Project:
  onehoon/SteamInputAddonforClaw

Product:
  MSI Claw physical controller
  → Addon
  → Classic Steam Controller
  → Steam Input

Overall source of truth:
  README.md

VIIPER integration reference:
  docs/VIIPER_INTEGRATION.md

Pinned VIIPER:
  onehoon/VIIPER
  db70bdedbe36846c665c841ea9f6ae9bf01d0d3d

Canonical native path:
  lib/viiper

Never base new Addon code on:
  clib
  standalone viiper.exe
  ViGEmBus assumption

Canonical Gordon:
  CreateSteamControllerDevice(autoAttach=false, 28DE:1102)
  AttachUSBDevice
  SetSteamControllerDeviceState
  SetSteamControllerOutputCallback
  RemoveSteamControllerDevice

Canonical bus/server:
  NewUSBServer
  CreateUSBBus
  RemoveUSBBus
  CloseUSBServer

Canonical Game Bar X360 primitives:
  CreateXbox360Device
  AttachUSBDevice
  SetXbox360DeviceState
  SetXbox360RumbleCallback
  RemoveXbox360Device

usbip-win2 baseline:
  v0.9.7.7
  7c219953101cc5d0ec9a0bcb3eb87259cf72bedd

Current Addon warning:
  repository still contains the old clib-based embedded DLL/binding until
  canonical migration phases replace it.

Core teardown invariant:
  clear callback
  → exact detach
  → transport drain
  → logical ownership cleanup
  → release lifecycle lock
  → wait drain
  → return

Game Bar invariant:
  same Gordon remains; neutral Gordon + temporary live X360.
```

Before implementing a new phase, inspect the current repository because sections marked **PLANNED** may have been completed or changed since this document revision.
