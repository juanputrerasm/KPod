#!/bin/sh
# Regenerates KPod.ico from the two source SVGs. Run after editing either.
#
# A Windows .ico holds a separate image per size. The full artwork keeps its
# detail at 48px and above; below that the slabs collapse into a smudge, so the
# small entries use the simplified K-only variant instead.
#
# Uses only stock macOS tools (sips) plus python3 for the .ico container.
set -eu

cd "$(dirname "$0")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

for size in 16 24 32; do
    sips -s format png -Z "$size" KPod-small.svg --out "$work/$size.png" >/dev/null
done

for size in 48 64 128 256; do
    sips -s format png -Z "$size" KPod.svg --out "$work/$size.png" >/dev/null
done

python3 - "$work" KPod.ico <<'PY'
import struct, sys, pathlib

work, out = pathlib.Path(sys.argv[1]), sys.argv[2]
sizes = [16, 24, 32, 48, 64, 128, 256]
images = [(size, (work / f"{size}.png").read_bytes()) for size in sizes]

# ICONDIR, then one 16-byte ICONDIRENTRY per image, then the PNG payloads.
# Windows has accepted PNG-compressed entries since Vista.
offset = 6 + 16 * len(images)
entries = b""
for size, data in images:
    dimension = 0 if size >= 256 else size   # 0 means 256 in the directory
    entries += struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(data), offset)
    offset += len(data)

pathlib.Path(out).write_bytes(
    struct.pack("<HHH", 0, 1, len(images)) + entries + b"".join(d for _, d in images))
print(f"{out}: {len(images)} sizes ({', '.join(str(s) for s in sizes)})")
PY
