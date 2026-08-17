"""
Make the SMA logo's white background transparent, without touching the white inside the
magnifying glass.

The naive version of this job — "set every white-ish pixel to transparent" — destroys the
logo, because the lens is a white disc with SMA written on it. Delete all white and the
letters end up floating over whatever is behind them, which is exactly what we do not want.

What actually distinguishes the two whites is not their colour. It is that the background
white touches the edge of the image and the lens white does not: the grey rim of the
magnifier encloses it completely. So this fills inward from the border and stops wherever
the picture stops being white. The lens is unreachable from outside, so it survives without
needing to be detected, masked or guessed at.

Usage:
    py cut_background.py <input> <output> [--threshold 232] [--chroma 22] [--keep-margin 6]
"""

from __future__ import annotations

import argparse
import sys
from collections import deque

from PIL import Image


def is_background_white(px: tuple[int, int, int, int], threshold: int, chroma: int) -> bool:
    """
    Bright and close to neutral.

    The chroma test matters as much as the brightness one. A photographic subject like this
    has bright specular highlights on the orange plastic and on the chrome rim, and those are
    light enough to pass a brightness test on their own — eating holes in the figure. Requiring
    the channels to be near each other keeps "white" meaning white rather than "very pale
    orange".
    """
    r, g, b, _ = px
    return min(r, g, b) >= threshold and (max(r, g, b) - min(r, g, b)) <= chroma


def cut(path_in: str, path_out: str, threshold: int, chroma: int, keep_margin: int) -> None:
    image = Image.open(path_in).convert("RGBA")
    width, height = image.size
    pixels = image.load()

    removed = bytearray(width * height)
    queue: deque[tuple[int, int]] = deque()

    # Seed from every border pixel. Some sources arrive with a thin non-white frame from JPEG
    # ringing, so a margin of rows/columns is seeded rather than just the outermost line.
    margin = max(1, keep_margin)
    for x in range(width):
        for y in list(range(min(margin, height))) + list(range(max(0, height - margin), height)):
            queue.append((x, y))
    for y in range(height):
        for x in list(range(min(margin, width))) + list(range(max(0, width - margin), width)):
            queue.append((x, y))

    while queue:
        x, y = queue.popleft()

        if not (0 <= x < width and 0 <= y < height):
            continue

        index = y * width + x
        if removed[index]:
            continue

        if not is_background_white(pixels[x, y], threshold, chroma):
            continue

        removed[index] = 1
        queue.append((x + 1, y))
        queue.append((x - 1, y))
        queue.append((x, y + 1))
        queue.append((x, y - 1))

    # Second pass: soften the cut edge.
    #
    # A hard on/off alpha leaves a white fringe, because the pixels right on the boundary are a
    # blend of subject and background and are still light enough to fail the test above. Giving
    # those partial alpha in proportion to how white they are turns a jagged halo into an edge
    # that reads as clean at icon sizes.
    span = max(1, 255 - threshold)
    edge_alpha: dict[tuple[int, int], int] = {}

    for y in range(height):
        for x in range(width):
            if removed[y * width + x]:
                continue

            touches_cut = any(
                0 <= x + dx < width
                and 0 <= y + dy < height
                and removed[(y + dy) * width + (x + dx)]
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
            )

            if not touches_cut:
                continue

            r, g, b, a = pixels[x, y]
            lightest = min(r, g, b)

            if lightest >= threshold:
                continue

            # 0 at pure white, full alpha once the pixel is clearly part of the subject.
            edge_alpha[(x, y)] = min(a, int(255 * min(1.0, (threshold - lightest) / span)))

    for y in range(height):
        for x in range(width):
            if removed[y * width + x]:
                r, g, b, _ = pixels[x, y]
                pixels[x, y] = (r, g, b, 0)

    for (x, y), alpha in edge_alpha.items():
        r, g, b, _ = pixels[x, y]
        pixels[x, y] = (r, g, b, alpha)

    # Trim the fully transparent border so the artwork fills its own canvas. An icon pipeline
    # scales what it is given, and baked-in empty margin comes out as a small logo in a big box.
    bounds = image.getbbox()
    if bounds:
        image = image.crop(bounds)

    image.save(path_out, "PNG")

    kept = sum(1 for v in removed if not v)
    print(f"in  : {path_in} ({width}x{height})")
    print(f"out : {path_out} ({image.size[0]}x{image.size[1]})")
    print(f"cut : {sum(removed):,} background pixels, kept {kept:,}, softened {len(edge_alpha):,} edge pixels")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input")
    parser.add_argument("output")
    parser.add_argument("--threshold", type=int, default=232, help="how bright counts as white (0-255)")
    parser.add_argument("--chroma", type=int, default=22, help="how neutral counts as white")
    parser.add_argument("--keep-margin", type=int, default=2, help="border rows seeded as background")
    args = parser.parse_args()

    cut(args.input, args.output, args.threshold, args.chroma, args.keep_margin)
    return 0


if __name__ == "__main__":
    sys.exit(main())
