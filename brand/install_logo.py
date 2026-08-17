"""
Install the cut-out SMA logo as the app's icon, splash and welcome mark.

Takes brand/sma-logo.png — the real logo with its background removed — and writes the three
places the app reads artwork from. Re-run it after re-cutting and every surface updates
together, so the launcher icon can never drift away from the screen.

Usage:  py install_logo.py
"""

from __future__ import annotations

import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.join(os.path.dirname(HERE), "Retail25.Shopper", "Resources")

SOURCE = os.path.join(HERE, "sma-logo.png")


def centred(logo: Image.Image, canvas: int, fill: tuple[int, int, int, int] | None, inset: float) -> Image.Image:
    """
    The logo centred on a square, scaled to leave a margin.

    The margin is not decoration. Android masks a launcher icon into a circle or a squircle
    depending on the handset, and anything close to the edge is what gets cut off — usually the
    magnifier handle, which is the part sticking furthest out.
    """
    out = Image.new("RGBA", (canvas, canvas), fill or (0, 0, 0, 0))

    box = int(canvas * inset)
    scaled = logo.copy()
    scaled.thumbnail((box, box), Image.LANCZOS)

    out.paste(
        scaled,
        ((canvas - scaled.width) // 2, (canvas - scaled.height) // 2),
        scaled,
    )
    return out


def main() -> None:
    logo = Image.open(SOURCE).convert("RGBA")

    # Launcher icon: white ground, because the artwork is a photographic render that reads badly
    # over an arbitrary wallpaper, and 68% so the handle survives the platform's mask.
    icon = centred(logo, 1024, (255, 255, 255, 255), 0.68)
    icon.save(os.path.join(APP, "AppIcon", "appicon.png"))

    # Splash: transparent, composited over the brand colour by the platform.
    splash = centred(logo, 512, None, 0.82)
    splash.save(os.path.join(APP, "Splash", "splash.png"))

    # Welcome screen: the logo as-is, transparent, over the gradient.
    welcome = centred(logo, 512, None, 0.94)
    welcome.save(os.path.join(APP, "Images", "sma_logo.png"))

    for path in ("AppIcon/appicon.png", "Splash/splash.png", "Images/sma_logo.png"):
        full = os.path.join(APP, *path.split("/"))
        print(f"wrote {path:<26} {os.path.getsize(full) / 1024:7.1f} KB")


if __name__ == "__main__":
    main()
