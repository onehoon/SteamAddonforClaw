# Classic Steam Controller report contract

Authoritative source: [`onehoon/VIIPER` tag `steam-input-addon-baseline-1`](https://github.com/onehoon/VIIPER/tree/steam-input-addon-baseline-1), `device/steamcontroller/inputstate.go`.

This document is deliberately independent from the C# report builder. It fixes the Developer-only PoC contract used with `viiper_device_set_input`.

| Field | Offset | Encoding |
| --- | ---: | --- |
| Report length | all | 64 bytes |
| Transport header | 0..3 | `01 00 01 3C` |
| Frame counter | 4..7 | `uint32`, little-endian |
| Left Grip | 9 | bit 7 (`0x80`) |
| Right Grip | 10 | bit 0 (`0x01`) |
| Neutral quaternion W | 40..41 | `0x4000`, little-endian, when all quaternion fields are zero |
| Default battery | 62..63 | 3000 mV (`B8 0B`) little-endian |

There is no checksum. The only per-report sequence value is the little-endian frame counter at bytes 4–7.

Golden vectors below use frame `0`.

| Vector | Changed bytes from neutral |
| --- | --- |
| Neutral | `00..3F: 01 00 01 3C 00 00 00 00`, `28..29: 00 40`, `3E..3F: B8 0B` |
| Left Grip | byte `09 = 80` |
| Right Grip | byte `0A = 01` |
| Both Grip | byte `09 = 80`, byte `0A = 01` |

The tests assert the header, default values, frame byte order, and independent Left/Right Grip changes. This PoC does not send any physical controller state.
