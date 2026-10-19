"""RugWash Simulator V0.3.0 — motifs des tapis (Python + NumPy + Pillow).

Mêmes dessins que les premières versions (RugWashTextureGenerator, conservé en secours), rendus nœud par nœud :
couleur évaluée au centre de chaque nœud, puis chaque nœud est « peint » avec un léger dôme (laine), une inclinaison
du velours, une variation propre au nœud, de l'abrash (bandes de teinte de la laine d'un même lot) et une usure douce.
Sorties : Assets/RugWash/Textures/Rugs/Rug_<Nom>_Albedo.png et normales de fibres (nœuds, poils longs, soie).

python Tools/Textures/gen_rugs.py
"""
import math
import os

import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "RugWash", "Textures", "Rugs")
os.makedirs(OUT, exist_ok=True)
TAU = math.tau


def hexc(r, g, b):
    return np.array([r, g, b], np.float32)


def rep(v, p):
    return v - p * np.floor(v / p)


def value_noise(h, w, cells_y, cells_x, seed):
    """Bruit de valeur lissé (grille aléatoire, interpolation smoothstep), périodique."""
    rng = np.random.default_rng(seed)
    grid = rng.random((cells_y + 1, cells_x + 1)).astype(np.float32)
    grid[-1, :] = grid[0, :]
    grid[:, -1] = grid[:, 0]
    y = np.linspace(0, cells_y, h, endpoint=False)
    x = np.linspace(0, cells_x, w, endpoint=False)
    y0 = np.floor(y).astype(int)
    x0 = np.floor(x).astype(int)
    fy = (y - y0)[:, None]
    fx = (x - x0)[None, :]
    fy = fy * fy * (3 - 2 * fy)
    fx = fx * fx * (3 - 2 * fx)
    a = grid[y0][:, x0]
    b = grid[y0][:, x0 + 1]
    c = grid[y0 + 1][:, x0]
    d = grid[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(h, w, base, octaves, seed, aspect=1.0):
    total = np.zeros((h, w), np.float32)
    amp, norm = 0.5, 0.0
    for o in range(octaves):
        cells = base * 2 ** o
        total += amp * value_noise(h, w, max(1, int(cells)), max(1, int(cells * aspect)), seed + o * 31)
        norm += amp
        amp *= 0.5
    return total / norm


def select(c, cond, color):
    return np.where(cond[..., None], color, c)


# ---------------------------------------------------------------------------------------------------------------- Oriental

MADDER = hexc(0.56, 0.115, 0.10)
MADDER_DARK = hexc(0.40, 0.07, 0.07)
INDIGO = hexc(0.095, 0.135, 0.28)
IVORY = hexc(0.90, 0.84, 0.70)
GOLD = hexc(0.80, 0.58, 0.22)
TEAL = hexc(0.11, 0.37, 0.39)


def oriental_field(x, y, ax, ay):
    c = np.broadcast_to(MADDER, x.shape + (3,)).copy()
    row = np.floor(y / 0.11)
    gx = rep(x + (rep(row, 2.0)) * 0.055, 0.11) - 0.055
    gy = rep(y, 0.11) - 0.055
    diamond = np.abs(gx) + np.abs(gy)
    c = select(c, diamond < 0.026, INDIGO)
    c = select(c, diamond < 0.011, IVORY)
    c = select(c, (np.abs(gx) > 0.048) & (np.abs(gy) > 0.048), TEAL)
    # Écoinçons.
    fhx, fhy = 1.254, 0.754
    ex, ey = (ax - fhx) / 0.44, (ay - fhy) / 0.32
    rc = np.sqrt(ex * ex + ey * ey)
    tc = np.arctan2(ey, ex)
    corner = 1 + 0.07 * np.cos(18 * tc)
    inside = rc < corner
    c = select(c, inside & (rc > corner - 0.07), GOLD)
    inner = inside & (rc <= corner - 0.07)
    c = select(c, inner, INDIGO)
    fx, fy = rep(x, 0.06) - 0.03, rep(y, 0.06) - 0.03
    flower = np.sqrt(fx * fx + fy * fy)
    c = select(c, inner & (flower < 0.016), MADDER_DARK)
    c = select(c, inner & (flower < 0.011), IVORY)
    # Pendentifs.
    pendant = ax / 0.075 + np.abs(ay - 0.6) / 0.095
    c = select(c, pendant < 1, INDIGO)
    c = select(c, pendant < 0.45, GOLD)
    # Médaillon.
    mx, my = x / 0.72, y / 0.5
    r = np.sqrt(mx * mx + my * my)
    th = np.arctan2(my, mx)
    star = 0.92 + 0.08 * np.cos(16 * th)
    med = r < star
    c = select(c, med & (r > star - 0.05), GOLD)
    ring = med & (r <= star - 0.05) & (r > 0.72)
    c = select(c, ring, INDIGO)
    c = select(c, ring & (np.abs(r - 0.81) < 0.035) & (np.cos(24 * th) > 0.6), IVORY)
    c = select(c, med & (r <= 0.72) & (r > 0.67), GOLD)
    mid = med & (r <= 0.67) & (r > 0.3)
    c = select(c, mid, IVORY)
    petal = 0.3 + 0.3 * np.abs(np.cos(4 * th))
    c = select(c, mid & (r < petal), MADDER)
    c = select(c, mid & (r < petal) & (r > petal - 0.04), MADDER_DARK)
    c = select(c, mid & (np.abs(np.cos(8 * th)) > 0.94) & (r > 0.42) & (r < 0.62), TEAL)
    core = med & (r <= 0.3)
    c = select(c, core, INDIGO)
    c = select(c, core & (r < 0.2 * (0.55 + 0.45 * np.abs(np.cos(4 * th)))), GOLD)
    c = select(c, core & (r < 0.06), MADDER)
    return c


def oriental(x, y):
    hx, hy = 1.5, 1.0
    ax, ay = np.abs(x), np.abs(y)
    d = np.minimum(hx - ax, hy - ay)
    along = np.where(hx - ax < hy - ay, y, x)
    c = oriental_field(x, y, ax, ay)
    # Bandes de bordure (de l'intérieur vers l'extérieur, la plus extérieure l'emporte).
    c = select(c, d < 0.246, INDIGO)
    band = d < 0.236
    zig = np.abs(rep(along, 0.024) - 0.012)
    c = select(c, band, IVORY)
    c = select(c, band & (np.abs((d - 0.214) - zig) < 0.0035), MADDER)
    c = select(c, d < 0.212, GOLD)
    border = d < 0.2
    s = rep(along, 0.16) - 0.08
    t = d - 0.12
    r = np.sqrt(s * s + t * t)
    th = np.arctan2(t, s)
    c = select(c, border, INDIGO)
    vine = t - 0.045 * np.sin(along / 0.16 * TAU)
    c = select(c, border & (np.abs(vine) < 0.0055), IVORY)
    leaf = np.sqrt((s - 0.055) ** 2 + (t + 0.03) ** 2)
    c = select(c, border & (leaf < 0.016), TEAL)
    c = select(c, border & (r < 0.052 * (0.78 + 0.22 * np.cos(8 * th))), GOLD)
    c = select(c, border & (r < 0.03 * (0.8 + 0.2 * np.cos(6 * th))), MADDER)
    c = select(c, border & (r < 0.011), IVORY)
    c = select(c, d < 0.04, MADDER_DARK)
    guard = d < 0.025
    c = select(c, guard, INDIGO)
    c = select(c, guard & (d > 0.008) & (d < 0.018) & (rep(along, 0.022) < 0.011), IVORY)
    return c


# ---------------------------------------------------------------------------------------------------------------- Soie

S_IVORY = hexc(0.93, 0.89, 0.80)
S_NAVY = hexc(0.07, 0.11, 0.30)
S_WINE = hexc(0.45, 0.08, 0.14)
S_ROSE = hexc(0.78, 0.42, 0.45)
S_SAGE = hexc(0.48, 0.58, 0.45)
S_GOLD = hexc(0.88, 0.72, 0.38)
S_SKY = hexc(0.45, 0.62, 0.78)


def luxury_border(along, t):
    c = np.broadcast_to(S_WINE, along.shape + (3,)).copy()
    period = 0.2
    s = rep(along, period) - period * 0.5
    vine = t - 0.055 * np.sin(along / period * TAU)
    c = select(c, np.abs(vine) < 0.0045, S_GOLD)
    odd = rep(np.floor(along / period), 2.0) > 0.5
    pt = t + np.where(odd, 0.035, -0.035)
    pr = np.sqrt(s * s * 1.6 + pt * pt)
    pth = np.arctan2(pt, s)
    petal = 0.045 * (0.7 + 0.3 * np.abs(np.cos(3 * pth)))
    inside = pr < petal
    c = select(c, inside & odd, S_ROSE)
    c = select(c, inside & ~odd, S_SAGE)
    c = select(c, inside & (pr > petal - 0.008), S_GOLD)
    c = select(c, pr < 0.012, S_IVORY)
    fs = rep(along + period * 0.25, period * 0.5) - period * 0.25
    c = select(c, np.sqrt(fs * fs + vine * vine) < 0.009, S_SKY)
    return c


def luxury_field(x, y, ax, ay):
    c = np.broadcast_to(S_IVORY, x.shape + (3,)).copy()
    cell = 0.16
    gx, gy = rep(x, cell) - cell / 2, rep(y, cell) - cell / 2
    lattice = np.abs(np.abs(gx) + np.abs(gy) - cell / 2)
    c = select(c, lattice < 0.004, S_SAGE)
    node = np.sqrt(gx * gx + gy * gy)
    nth = np.arctan2(gy, gx)
    c = select(c, node < 0.024 * (0.75 + 0.25 * np.cos(8 * nth)), S_ROSE)
    c = select(c, node < 0.009, S_GOLD)
    cx, cy = np.abs(gx) - cell / 2, np.abs(gy) - cell / 2
    c = select(c, np.sqrt(cx * cx + cy * cy) < 0.012, S_NAVY)
    fhx, fhy = 1.322, 0.822
    ex, ey = (ax - fhx) / 0.5, (ay - fhy) / 0.36
    rc = np.sqrt(ex * ex + ey * ey)
    eth = np.arctan2(ey, ex)
    corner = 1 + 0.06 * np.cos(20 * eth)
    inside = rc < corner
    c = select(c, inside, S_NAVY)
    c = select(c, inside & (rc > corner - 0.06), S_GOLD)
    c = select(c, inside & (rc <= corner - 0.06) & (np.abs(rc - 0.55) < 0.05) & (np.cos(16 * eth) > 0.5), S_ROSE)
    pendant = ax / 0.06 + np.abs(ay - 0.62) / 0.1
    c = select(c, pendant < 1, S_NAVY)
    c = select(c, pendant < 0.45, S_GOLD)
    mx, my = x / 0.68, y / 0.5
    r = np.sqrt(mx * mx + my * my)
    th = np.arctan2(my, mx)
    star = 0.9 + 0.1 * np.cos(16 * th)
    med = r < star
    c = select(c, med & (r > star - 0.045), S_GOLD)
    ring = med & (r <= star - 0.045) & (r > 0.7)
    c = select(c, ring, S_NAVY)
    c = select(c, ring & (np.abs(r - 0.78) < 0.03) & (np.cos(32 * th) > 0.55), S_SKY)
    c = select(c, med & (r <= 0.7) & (r > 0.66), S_GOLD)
    mid = med & (r <= 0.66) & (r > 0.28)
    c = select(c, mid, S_IVORY)
    petal = 0.28 + 0.32 * np.abs(np.cos(4 * th))
    c = select(c, mid & (r < petal), S_ROSE)
    c = select(c, mid & (r < petal) & (r > petal - 0.035), S_WINE)
    c = select(c, mid & (np.abs(np.cos(8 * th)) > 0.95) & (r > 0.4) & (r < 0.62), S_SAGE)
    core = med & (r <= 0.28)
    c = select(c, core, S_NAVY)
    c = select(c, core & (r < 0.2 * (0.55 + 0.45 * np.abs(np.cos(6 * th)))), S_GOLD)
    c = select(c, core & (r < 0.055), S_WINE)
    return c


def luxury(x, y):
    hx, hy = 1.6, 1.1
    ax, ay = np.abs(x), np.abs(y)
    d = np.minimum(hx - ax, hy - ay)
    along = np.where(hx - ax < hy - ay, y, x)
    c = luxury_field(x, y, ax, ay)
    c = select(c, d < 0.278, S_NAVY)
    band = d < 0.272
    s = rep(along, 0.03) - 0.015
    t = d - 0.259
    r = np.sqrt(s * s + t * t)
    c = select(c, band, S_IVORY)
    c = select(c, band & (r < 0.0105), S_ROSE)
    c = select(c, band & (r < 0.0075), S_NAVY)
    c = select(c, d < 0.246, S_GOLD)
    border = d < 0.24
    c = np.where(border[..., None], luxury_border(along, d - 0.14), c)
    c = select(c, d < 0.04, S_GOLD)
    guard = d < 0.034
    c = select(c, guard, S_NAVY)
    c = select(c, guard & (d > 0.014) & (d < 0.026) & (rep(along, 0.026) < 0.012), S_IVORY)
    c = select(c, d < 0.006, S_GOLD)
    return c


# ---------------------------------------------------------------------------------------------------------------- Classique

SAGE = hexc(0.40, 0.50, 0.43)
CHARCOAL = hexc(0.16, 0.17, 0.18)
CREAM = hexc(0.88, 0.84, 0.74)
TERRA = hexc(0.72, 0.36, 0.22)
MUSTARD = hexc(0.80, 0.62, 0.25)


def classic(x, y):
    hx, hy = 1.2, 0.8
    d = np.minimum(hx - np.abs(x), hy - np.abs(y))
    c = np.broadcast_to(SAGE, x.shape + (3,)).copy()
    a, b = x + y, x - y
    ra, rb = rep(a, 0.22) - 0.11, rep(b, 0.22) - 0.11
    ia, ib = np.floor(a / 0.22), np.floor(b / 0.22)
    tile = np.abs(ra) + np.abs(rb) < 0.05
    even = rep(ia + ib, 2.0) < 0.5
    c = select(c, tile & even, TERRA)
    c = select(c, tile & ~even, MUSTARD)
    c = select(c, (np.abs(ra) < 0.006) | (np.abs(rb) < 0.006), CREAM)
    c = select(c, d < 0.122, CREAM)
    stripe = d < 0.11
    alt = rep(np.where(np.abs(x) > np.abs(y) * 1.5, y, x), 0.05) < 0.025
    c = select(c, stripe, CHARCOAL)
    c = select(c, stripe & alt, TERRA)
    c = select(c, d < 0.085, CREAM)
    c = select(c, d < 0.07, CHARCOAL)
    return c


# ---------------------------------------------------------------------------------------------------------------- Hall (nouveau)

H_CHAR = hexc(0.17, 0.18, 0.19)
H_IVORY = hexc(0.86, 0.82, 0.73)
H_RUST = hexc(0.66, 0.30, 0.17)
H_OCHRE = hexc(0.78, 0.6, 0.3)
H_SLATE = hexc(0.3, 0.38, 0.42)


def hall(x, y):
    """Grand tapis de hall : losanges berbères anthracite sur fond ivoire, frise en dents de scie rouille et ocre."""
    hx, hy = 1.9, 1.3
    ax, ay = np.abs(x), np.abs(y)
    d = np.minimum(hx - ax, hy - ay)
    along = np.where(hx - ax < hy - ay, y, x)
    c = np.broadcast_to(H_IVORY, x.shape + (3,)).copy()
    # Treillis de losanges allongés (lignes doubles anthracite), petit losange rouille au centre de chaque maille.
    cw, ch = 0.36, 0.26
    gx, gy = rep(x, cw) / cw - 0.5, rep(y, ch) / ch - 0.5
    dia = np.abs(gx) + np.abs(gy)
    c = select(c, (np.abs(dia - 0.5) < 0.035) | (np.abs(dia - 0.42) < 0.02), H_CHAR)
    c = select(c, dia < 0.11, H_RUST)
    c = select(c, dia < 0.05, H_OCHRE)
    # Grand losange central.
    big = ax / 0.95 + ay / 0.62
    c = select(c, (big < 1) & (big > 0.93), H_CHAR)
    c = select(c, (big < 0.93) & (np.abs(dia - 0.5) < 0.035), H_SLATE)
    # Frise.
    c = select(c, d < 0.3, H_IVORY)
    tooth = rep(along, 0.09) / 0.09
    saw = np.abs(tooth - 0.5) * 2
    band = (d > 0.12) & (d < 0.28)
    c = select(c, band & ((d - 0.12) / 0.16 < saw), H_RUST)
    c = select(c, band & ((d - 0.12) / 0.16 < saw * 0.5), H_OCHRE)
    c = select(c, (d < 0.12) & (d > 0.09), H_CHAR)
    c = select(c, d < 0.09, H_IVORY)
    c = select(c, d < 0.05, H_CHAR)
    c = select(c, (d < 0.035) & (d > 0.02) & (rep(along, 0.04) < 0.02), H_IVORY)
    return c


# ---------------------------------------------------------------------------------------------------------------- Rendu

def render_knotted(name, design, size_m, knot_m, px_per_knot, seed, abrash=0.05, dome=0.16, silk=False):
    """Évalue le dessin au centre de chaque nœud puis peint les nœuds (dôme, velours, variation, abrash, usure)."""
    kw, kh = int(round(size_m[0] / knot_m)), int(round(size_m[1] / knot_m))
    W, H = kw * px_per_knot, kh * px_per_knot
    kx = (np.arange(kw) + 0.5) * knot_m - size_m[0] / 2
    ky = (np.arange(kh) + 0.5) * knot_m - size_m[1] / 2
    X, Y = np.meshgrid(kx, ky)  # rangées = y croissant (bas -> haut une fois retourné)
    base = design(X, Y).astype(np.float32)
    rng = np.random.default_rng(seed)
    # Variation propre au nœud (valeur et légère teinte).
    base *= (0.94 + 0.1 * rng.random((kh, kw, 1))).astype(np.float32)
    base *= (1 + (rng.random((kh, kw, 3)) - 0.5) * 0.03).astype(np.float32)
    # Abrash : bandes de lots de laine le long de la chaîne (rangées), plus marquées sur les fonds.
    bands = np.zeros(kh, np.float32)
    row = 0
    while row < kh:
        span = int(rng.integers(8, 40))
        bands[row:row + span] = rng.normal(0, 1)
        row += span
    bands = np.convolve(bands, np.ones(5) / 5, mode="same")
    wave = np.sin(ky * 7.3 + fbm(kh, 1, 3, 2, seed + 5)[:, 0] * 4) * 0.5
    base *= (1 + (bands * 0.6 + wave) * abrash)[:, None, None]
    # Usure douce et salissure ancienne (le tapis n'est pas neuf).
    wear = fbm(kh, kw, 4, 4, seed + 9, aspect=size_m[0] / size_m[1])
    base *= (0.95 + 0.08 * wear)[..., None]
    # Pixels : chaque nœud répété, puis ombrage intra-nœud.
    img = np.repeat(np.repeat(base, px_per_knot, 0), px_per_knot, 1)
    f = (np.arange(px_per_knot) + 0.5) / px_per_knot - 0.5
    fy, fx = np.meshgrid(f, f, indexing="ij")
    if silk:
        shade = 1 + 0.03 * np.cos(fx * math.pi) * np.cos(fy * math.pi)
    else:
        shade = (1 - dome) + dome * 1.15 * np.clip(1 - (fx * fx + fy * fy) * 2.0, 0, 1) ** 0.6 + 0.05 * fy
    img *= np.tile(shade, (kh, kw))[..., None]
    # Fibres de laine (grain très fin, non aligné sur les nœuds).
    grain = np.random.default_rng(seed + 13).random((H, W)).astype(np.float32)
    img *= (0.97 + 0.06 * grain)[..., None]
    img = np.clip(img, 0, 1)
    out = Image.fromarray((np.flipud(img) ** (1.0) * 255 + 0.5).astype(np.uint8), "RGB")
    path = os.path.join(OUT, f"Rug_{name}_Albedo.png")
    out.save(path, optimize=True)
    print(name, out.size, f"{kw}x{kh} nœuds")


def render_tufted(name, design, size_m, px, seed):
    """Tapis tufté (machine) : pas de grille de nœuds, touffes irrégulières et légère moucheture."""
    W, H = px
    xs = (np.arange(W) + 0.5) / W * size_m[0] - size_m[0] / 2
    ys = (np.arange(H) + 0.5) / H * size_m[1] - size_m[1] / 2
    X, Y = np.meshgrid(xs, ys)
    img = design(X, Y).astype(np.float32)
    tone = fbm(H, W, 6, 5, seed, aspect=size_m[0] / size_m[1])
    tufts = value_noise(H, W, int(size_m[1] * 220), int(size_m[0] * 220), seed + 3)
    img *= (0.92 + 0.12 * tone + 0.07 * (tufts - 0.5))[..., None]
    img = np.clip(img, 0, 1)
    Image.fromarray((np.flipud(img) * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, f"Rug_{name}_Albedo.png"), optimize=True)
    print(name, (W, H))


def render_shag(size_m, px, seed):
    W, H = px
    cream, warm = hexc(0.85, 0.79, 0.69), hexc(0.74, 0.66, 0.54)
    cloud = fbm(H, W, 3, 5, seed, aspect=1.5)
    streak = fbm(H, W, 10, 4, seed + 7, aspect=6.0)
    t = np.clip((cloud - 0.35) / 0.4, 0, 1)
    t = t * t * (3 - 2 * t)
    img = cream * (1 - t[..., None]) + warm * t[..., None]
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    edge = np.minimum(np.minimum(u, 1 - u)[None, :] * 2.1, np.minimum(v, 1 - v)[:, None] * 1.4)
    img *= (0.84 + 0.16 * np.clip(edge / 0.06, 0, 1))[..., None]
    strands = value_noise(H, W, int(H / 3), int(W / 9), seed + 11)
    img *= (0.86 + 0.14 * streak + 0.1 * (strands - 0.5))[..., None]
    img = np.clip(img, 0, 1)
    Image.fromarray((np.flipud(img) * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, "Rug_Shag_Albedo.png"), optimize=True)
    print("Shag", (W, H))


# ---------------------------------------------------------------------------------------------------------------- Normales

def height_to_normal(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    n = np.stack([-dx * strength, dy * strength, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8), "RGB")


def knot_normal(knots=32, px=16, seed=71):
    """Tuile de 32 × 32 nœuds : dôme par nœud, sillons entre rangées, brins fins."""
    S = knots * px
    rng = np.random.default_rng(seed)
    f = (np.arange(px) + 0.5) / px - 0.5
    fy, fx = np.meshgrid(f, f, indexing="ij")
    dome = np.clip(1 - (fx * fx + fy * fy) * 2.4, 0, 1) ** 0.5
    h = np.tile(dome, (knots, knots)) * np.repeat(np.repeat(0.8 + 0.4 * rng.random((knots, knots)), px, 0), px, 1)
    fibers = value_noise(S, S, S // 3, S // 3, seed + 1)
    h = h * 0.75 + fibers * 0.25
    height_to_normal(np.flipud(h), 2.2).save(os.path.join(OUT, "Rug_Knot_Normal.png"))


def shag_normal(seed=91):
    S = 512
    warp = fbm(S, S, 4, 3, seed) * 0.35
    y, x = np.mgrid[0:S, 0:S] / S
    strands = np.sin((x * 70 + warp * 30 + value_noise(S, S, 24, 24, seed + 2) * 6) * TAU) * 0.5 + 0.5
    clumps = fbm(S, S, 12, 3, seed + 4)
    h = strands ** 1.6 * 0.6 + clumps * 0.6
    height_to_normal(h, 3.6).save(os.path.join(OUT, "Rug_Shag_Normal.png"))


if __name__ == "__main__":
    render_knotted("Oriental", oriental, (3.0, 2.0), 0.006, 4, seed=11)
    render_knotted("Luxury", luxury, (3.2, 2.2), 0.0035, 2, seed=51, abrash=0.015, silk=True)
    render_knotted("Hall", hall, (3.8, 2.6), 0.007, 3, seed=61, abrash=0.04)
    render_tufted("Classic", classic, (2.4, 1.6), (1800, 1200), seed=23)
    render_shag((2.1, 1.4), (1500, 1000), seed=37)
    knot_normal()
    shag_normal()
