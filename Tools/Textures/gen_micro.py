import os
"""Micro-textures tuilables (bruit filtré par FFT, donc périodique) pour les matériaux unis :
détail d'albédo (x2, gris 0,5 = neutre) et détail de normale. Écrit dans Assets/RugWash/Textures/Surfaces."""
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "RugWash", "Textures", "Surfaces")
rng = np.random.default_rng(3030)


def noise(n, lo, hi, aniso=(1.0, 1.0), seed=None):
    """Bruit périodique de bande [lo, hi] (cycles par tuile), normalisé à moyenne 0, écart-type 1."""
    r = np.random.default_rng(seed) if seed is not None else rng
    white = r.standard_normal((n, n))
    f = np.fft.fft2(white)
    fy = np.fft.fftfreq(n)[:, None] * n * aniso[1]
    fx = np.fft.fftfreq(n)[None, :] * n * aniso[0]
    k = np.sqrt(fx ** 2 + fy ** 2)
    band = np.exp(-((np.log(k + 1e-6) - np.log(np.sqrt(lo * hi))) ** 2) / (2 * (np.log(hi / lo) / 2.5) ** 2))
    band[0, 0] = 0
    out = np.real(np.fft.ifft2(f * band))
    return (out - out.mean()) / (out.std() + 1e-9)


def height_to_normal(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(h)
    l = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    rgb = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    return Image.fromarray((rgb * 255 + 0.5).astype(np.uint8), "RGB")


def gray(v):
    return Image.fromarray((np.clip(v, 0, 1) * 255 + 0.5).astype(np.uint8), "L").convert("RGB")


N = 512
# Plastique moulé : grain fin + légères ondulations.
h = noise(N, 60, 200) * 0.6 + noise(N, 6, 20) * 0.4
height_to_normal(h, 0.9).save(f"{OUT}/Micro_Plastic_Normal.png")
# Caoutchouc : picots granuleux plus marqués.
h = np.tanh(noise(N, 90, 180) * 1.5) + noise(N, 8, 24) * 0.3
height_to_normal(h, 1.6).save(f"{OUT}/Micro_Rubber_Normal.png")
# Métal brossé : stries le long de X (anisotrope).
h = noise(N, 40, 400, aniso=(0.04, 1.0)) * 0.8 + noise(N, 80, 200) * 0.2
height_to_normal(h, 0.7).save(f"{OUT}/Micro_Brushed_Normal.png")
# Maille de tee-shirt (jersey) : rangées de mailles.
y, x = np.mgrid[0:N, 0:N] / N
knit = np.sin(x * np.pi * 2 * 64) ** 2 * 0.6 + np.abs(np.sin((y + 0.25 * np.sin(x * np.pi * 2 * 64)) * np.pi * 2 * 48)) * 0.4
height_to_normal(knit + noise(N, 30, 120) * 0.15, 1.2).save(f"{OUT}/Micro_Knit_Normal.png")
# Carton : cannelures + fibres.
h = np.sin(x * np.pi * 2 * 24) * 0.4 + noise(N, 50, 250) * 0.5
height_to_normal(h, 1.0).save(f"{OUT}/Micro_Cardboard_Normal.png")

# Détail d'albédo « patine » (x2 : 0,5 neutre) : taches douces et grain, très léger.
g = 0.5 + noise(N, 2, 8) * 0.035 + noise(N, 20, 80) * 0.025 + noise(N, 120, 250) * 0.015
gray(g).save(f"{OUT}/Micro_Grime_Detail.png")
# Patine plus marquée (sols, machines usées).
g = 0.5 + np.clip(noise(N, 2, 6), -3, 1.2) * 0.06 + noise(N, 30, 120) * 0.04
gray(g).save(f"{OUT}/Micro_Wear_Detail.png")
print("ok")

# Verre armé dépoli des fenêtres (émissif, tuilable) : lumière diffuse, légères variations, treillis de fil métallique.
S = 512
n = noise(S, 3, 12) * 0.05 + noise(S, 40, 160) * 0.025
y, x = np.mgrid[0:S, 0:S] / S
cells = 8
gx = np.abs(((x * cells) % 1.0) - 0.5)
gy = np.abs(((y * cells) % 1.0) - 0.5)
wire = np.clip(1 - np.minimum(gx, gy) * S / cells / 1.6, 0, 1)
v = np.clip(0.9 + n - wire * 0.35, 0, 1)
rgb = np.stack([v * 0.93, v * 0.97, v], -1)
Image.fromarray((rgb * 255 + 0.5).astype(np.uint8), "RGB").save(f"{OUT}/Window_Wired.png")
print("window ok")
