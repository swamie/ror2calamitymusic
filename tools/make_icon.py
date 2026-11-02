"""Renders Thunderstore/icon.png (256x256): the mod's music-note glyph over a brimstone backdrop.

The note uses the same signed-distance shapes as UI/TextureFactory.cs so the icon matches the
in-game Now Playing banner. usage: python3 make_icon.py <out.png>
"""
import math
import sys

import numpy as np
from PIL import Image

SIZE = 256
FILL = np.array([0xF4, 0xF6, 0xFF]) / 255
OUTLINE = np.array([0x2A, 0x42, 0xBE]) / 255
GLOW = np.array([0x3C, 0xF0, 0xC8]) / 255


def rot(x, y, deg):
    r = math.radians(deg)
    c, s = math.cos(r), math.sin(r)
    return x * c - y * s, x * s + y * c


def ellipse(px, py, cx, cy, rx, ry, deg):
    qx, qy = rot(px - cx, py - cy, -deg)
    k = np.sqrt((qx / rx) ** 2 + (qy / ry) ** 2)
    return (k - 1) * min(rx, ry)


def box(px, py, cx, cy, hx, hy, deg):
    qx, qy = rot(px - cx, py - cy, -deg)
    dx, dy = np.abs(qx) - hx, np.abs(qy) - hy
    outside = np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dy, 0) ** 2)
    return outside + np.minimum(np.maximum(dx, dy), 0)


def note_distance(px, py):
    d = np.minimum(ellipse(px, py, 0.30, 0.24, 0.135, 0.095, -25), ellipse(px, py, 0.73, 0.32, 0.135, 0.095, -25))
    d = np.minimum(d, box(px, py, 0.405, 0.53, 0.022, 0.29, 0))
    d = np.minimum(d, box(px, py, 0.835, 0.61, 0.022, 0.29, 0))
    return np.minimum(d, box(px, py, 0.62, 0.80, 0.235, 0.052, 10))


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def over(ca, aa, cb, ab):
    out_a = aa + ab * (1 - aa)
    safe = np.maximum(out_a, 1e-6)[..., None]
    col = (ca * aa[..., None] + cb * (ab * (1 - aa))[..., None]) / safe
    return col, out_a


def main(path):
    ys, xs = np.mgrid[0:SIZE, 0:SIZE]
    u = (xs + 0.5) / SIZE
    v = 1 - (ys + 0.5) / SIZE  # y up

    # Background: deep brimstone red to void blue, with a soft vignette and a teal rim.
    r = np.sqrt((u - 0.5) ** 2 + (v - 0.45) ** 2)
    bg = np.zeros((SIZE, SIZE, 3))
    top = np.array([0.10, 0.03, 0.10])
    bottom = np.array([0.32, 0.04, 0.06])
    bg[:] = top * v[..., None] + bottom * (1 - v[..., None])
    bg *= (1.15 - 0.9 * r)[..., None]
    ember = np.exp(-((u - 0.5) ** 2 + (v - 0.18) ** 2) / 0.05)
    bg += np.array([0.45, 0.10, 0.04]) * ember[..., None] * 0.6
    bg = np.clip(bg, 0, 1)

    shrink = 0.74
    qx = (u - 0.5) / shrink + 0.5
    qy = (v - 0.5) / shrink + 0.52
    d = note_distance(qx, qy) * shrink
    aa = 1.5 / SIZE
    ow, gw = 0.045, 0.085
    fill_a = smooth(aa, -aa, d)
    out_a = smooth(aa, -aa, d - ow)
    glow_a = np.exp(-np.maximum(d - ow, 0) / (gw * 0.4)) * 0.95

    col, a = over(np.broadcast_to(OUTLINE, bg.shape), out_a, np.broadcast_to(GLOW, bg.shape), glow_a)
    col, a = over(np.broadcast_to(FILL, bg.shape), fill_a, col, a)
    final = col * a[..., None] + bg * (1 - a[..., None])

    Image.fromarray((np.clip(final, 0, 1) * 255).astype(np.uint8), "RGB").save(path)
    print("wrote", path)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "icon.png")
