# Cymple image transport

How Cymple trackers send camera frames to the Cymple desktop app, and what Cymple Bridge emits
to stand in for one. Little-endian throughout.

## Device roles

| `deviceFlag` | Role |
|---|---|
| 0 | left eye |
| 1 | right eye |
| 2 | mouth |

Cymple Bridge always sends flag `2`.

## Packet: MSG_IMAGE_V2

UDP to the desktop app on port **23303**. One camera frame may span several datagrams; each
datagram carries a slice.

| Offset | Type | Field |
|---|---|---|
| 0 | u16 | type = 5 (`MSG_IMAGE_V2`) |
| 2 | u16 | length = `dataLen + 8` |
| 4 | u16 | `offset` into the frame |
| 6 | u16 | `total` frame length |
| 8 | u16 | `dataLen` bytes of payload in this datagram |
| 10 | u8 | `deviceFlag` |
| 11 | u8 | frame index, incrementing per frame |
| 12 | bytes | JPEG payload |

The desktop app reassembles slices by frame index and offset. Cymple Bridge chunks at 1400 bytes
to stay inside a typical MTU.

## Discovery / heartbeat

A native Cymple device only streams after it receives a heartbeat. Cymple Bridge does not need
one, because it pushes frames directly to the app rather than waiting to be polled. If you extend
this project to impersonate a device that the app discovers, note that the app sends a 4-byte
heartbeat `00 00 00 00` to port **23302** and a device replies with frames to the sender on
**23303**.

## Credits

Packet layout derived from the open Cymple firmware and the community `BrokenEye2Cymple` bridge.
