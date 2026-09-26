"""Prints the shadow alpha profile of a styled window shot (debug helper): python promo/shadow_profile.py shot.png"""
import sys
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGBA")
a = im.split()[3]
x0, y0, x1, y1 = a.point(lambda v: 255 if v > 250 else 0).getbbox()
cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
def prof(name, pts):
    print(name.ljust(7), " ".join(f"{d:>3}:{im.getpixel(p)[3] / 255:.2f}" for d, p in pts))
prof("bottom", [(d, (cx, y1 + d - 1)) for d in (1, 5, 10, 20, 30, 45, 60)])
prof("side", [(d, (x1 + d - 1, cy)) for d in (1, 5, 10, 20, 30, 45)])
prof("top", [(d, (cx, y0 - d)) for d in (1, 5, 10, 20)])
