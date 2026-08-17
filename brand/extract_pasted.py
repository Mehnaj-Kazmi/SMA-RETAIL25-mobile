"""
Pull every image that was pasted into this conversation out of the session transcript.

Claude Code writes the transcript as JSON Lines, and a pasted picture is stored inside it as
base64. That means the logo has been on disk the whole time — just not as a file anyone could
open. This walks the transcript, finds every image block, and writes each one out so the
background cutter has something to work with.
"""

from __future__ import annotations

import base64
import json
import os
import sys

TRANSCRIPT = sys.argv[1]
OUT_DIR = sys.argv[2]

EXT = {"image/png": ".png", "image/jpeg": ".jpg", "image/webp": ".webp", "image/gif": ".gif"}


def walk(node, found):
    """Depth-first, because the shape of a message block has changed between versions."""
    if isinstance(node, dict):
        source = node.get("source")
        if isinstance(source, dict) and source.get("type") == "base64" and source.get("data"):
            found.append((source.get("media_type", "image/png"), source["data"]))
        for value in node.values():
            walk(value, found)
    elif isinstance(node, list):
        for value in node:
            walk(value, found)


def main() -> int:
    os.makedirs(OUT_DIR, exist_ok=True)
    found: list[tuple[str, str]] = []

    with open(TRANSCRIPT, "r", encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                walk(json.loads(line), found)
            except json.JSONDecodeError:
                continue

    seen: set[str] = set()
    written = 0

    for index, (media_type, data) in enumerate(found):
        # The same picture pasted twice is the same bytes twice; keep one of each.
        digest = data[:200]
        if digest in seen:
            continue
        seen.add(digest)

        try:
            raw = base64.b64decode(data)
        except Exception:
            continue

        path = os.path.join(OUT_DIR, f"pasted-{written:02d}{EXT.get(media_type, '.png')}")
        with open(path, "wb") as out:
            out.write(raw)

        print(f"{os.path.basename(path):<20} {media_type:<12} {len(raw) / 1024:8.1f} KB")
        written += 1

    print(f"\n{written} unique image(s) recovered from {len(found)} block(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
