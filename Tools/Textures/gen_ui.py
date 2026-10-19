"""Sprites de la charte UI V0.3.0 (blanc sur transparent, teintés par Image.color dans Unity), sur-échantillonnés ×4.
Sortie : scratchpad/v30/ui/ (copiés ensuite dans Assets/RugWash/Textures/UI)."""
import math, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "RugWash", "Textures", "UI")
os.makedirs(OUT, exist_ok=True)
SS = 4
ROBOTO = None
for root, _, files in os.walk("E:/Dev unity/RugWash Simulator/Library/PackageCache"):
    if "Roboto-Black.ttf" in files:
        ROBOTO = os.path.join(root, "Roboto-Black.ttf")
        break


def canvas(w, h):
    return Image.new("L", (w * SS, h * SS), 0)


def finish(mask, name, w, h):
    mask = mask.resize((w, h), Image.LANCZOS)
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    img.putalpha(mask)
    img.save(os.path.join(OUT, name + ".png"))


def rounded(w, h, r, name, ring=0):
    m = canvas(w, h)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([0, 0, w * SS - 1, h * SS - 1], r * SS, fill=255)
    if ring:
        d.rounded_rectangle([ring * SS, ring * SS, (w - ring) * SS - 1, (h - ring) * SS - 1], max(1, (r - ring)) * SS, fill=0)
    finish(m, name, w, h)


# Panneaux et boutons (9-slice).
rounded(64, 64, 14, "UI_Panel")
rounded(64, 64, 14, "UI_PanelOutline", ring=2)
rounded(64, 64, 32, "UI_Pill")
rounded(64, 64, 32, "UI_PillOutline", ring=2)
rounded(32, 32, 6, "UI_Bar")

# Ombre portée douce (9-slice, bord 48).
S = 160
m = Image.new("L", (S, S), 0)
ImageDraw.Draw(m).rounded_rectangle([40, 40, S - 41, S - 41], 18, fill=255)
m = m.filter(ImageFilter.GaussianBlur(14))
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
img.putalpha(m)
img.save(os.path.join(OUT, "UI_Shadow.png"))

# Dégradé vertical (haut opaque -> bas transparent) et halo radial.
g = Image.new("RGBA", (4, 128), (255, 255, 255, 0))
for y in range(128):
    a = int(255 * (1 - y / 127) ** 1.6)
    for x in range(4):
        g.putpixel((x, y), (255, 255, 255, a))
g.save(os.path.join(OUT, "UI_GradientV.png"))
r = Image.new("RGBA", (128, 128), (255, 255, 255, 0))
for y in range(128):
    for x in range(128):
        d = math.hypot(x - 63.5, y - 63.5) / 63.5
        r.putpixel((x, y), (255, 255, 255, int(255 * max(0, 1 - d) ** 2)))
r.save(os.path.join(OUT, "UI_Glow.png"))


# Icônes 96 px.
def icon(name, draw):
    W = 96
    m = canvas(W, W)
    d = ImageDraw.Draw(m)
    draw(d, W * SS)
    finish(m, "Icon_" + name, W, W)


def coin(d, s):
    d.ellipse([s * .06, s * .06, s * .94, s * .94], fill=255)
    d.ellipse([s * .16, s * .16, s * .84, s * .84], fill=0)
    if ROBOTO:
        f = ImageFont.truetype(ROBOTO, int(s * .5))
        d.text((s * .5, s * .52), "€", font=f, fill=255, anchor="mm")


def star(d, s):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rr = s * (.46 if i % 2 == 0 else .2)
        pts.append((s * .5 + rr * math.cos(a), s * .53 + rr * math.sin(a)))
    d.polygon(pts, fill=255)


def drop(d, s):
    d.ellipse([s * .2, s * .38, s * .8, s * .94], fill=255)
    d.polygon([(s * .5, s * .04), (s * .215, s * .6), (s * .785, s * .6)], fill=255)
    d.ellipse([s * .34, s * .58, s * .46, s * .8], fill=0)


def clock(d, s):
    d.ellipse([s * .06, s * .06, s * .94, s * .94], fill=255)
    d.ellipse([s * .15, s * .15, s * .85, s * .85], fill=0)
    d.line([(s * .5, s * .5), (s * .5, s * .24)], fill=255, width=int(s * .08))
    d.line([(s * .5, s * .5), (s * .7, s * .6)], fill=255, width=int(s * .08))
    d.ellipse([s * .44, s * .44, s * .56, s * .56], fill=255)


def check(d, s):
    d.line([(s * .16, s * .52), (s * .4, s * .76), (s * .86, s * .26)], fill=255, width=int(s * .14), joint="curve")


def lock(d, s):
    d.rounded_rectangle([s * .18, s * .44, s * .82, s * .92], s * .08, fill=255)
    d.arc([s * .3, s * .1, s * .7, s * .62], 180, 360, fill=255, width=int(s * .1))
    d.line([(s * .3, s * .36), (s * .3, s * .46)], fill=255, width=int(s * .1))
    d.line([(s * .7, s * .36), (s * .7, s * .46)], fill=255, width=int(s * .1))
    d.ellipse([s * .44, s * .58, s * .56, s * .7], fill=0)


def spray(d, s):
    d.rounded_rectangle([s * .3, s * .38, s * .7, s * .94], s * .1, fill=255)
    d.rectangle([s * .4, s * .26, s * .6, s * .4], fill=255)
    d.rounded_rectangle([s * .3, s * .1, s * .78, s * .28], s * .05, fill=255)
    for i, (x, y) in enumerate([(.86, .12), (.92, .22), (.86, .32)]):
        d.ellipse([s * (x - .035), s * (y - .035), s * (x + .035), s * (y + .035)], fill=255)


def person(d, s):
    d.ellipse([s * .32, s * .08, s * .68, s * .44], fill=255)
    d.pieslice([s * .14, s * .5, s * .86, s * 1.2], 180, 360, fill=255)


def cart(d, s):
    d.line([(s * .06, s * .18), (s * .2, s * .18), (s * .3, s * .66), (s * .8, s * .66)], fill=255, width=int(s * .08), joint="curve")
    d.polygon([(s * .24, s * .28), (s * .9, s * .28), (s * .82, s * .56), (s * .3, s * .56)], fill=255)
    for x in (.36, .74):
        d.ellipse([s * (x - .07), s * .74, s * (x + .07), s * .88], fill=255)


def gear(d, s):
    c = s * .5
    pts = []
    for i in range(16):
        a = i * math.pi / 8
        rr = s * (.46 if i % 2 == 0 else .36)
        for da in (-0.13, 0.13):
            pts.append((c + rr * math.cos(a + da), c + rr * math.sin(a + da)))
    d.polygon(pts, fill=255)
    d.ellipse([s * .34, s * .34, s * .66, s * .66], fill=0)


def broom(d, s):
    d.line([(s * .78, s * .06), (s * .44, s * .56)], fill=255, width=int(s * .08))
    d.polygon([(s * .3, s * .5), (s * .58, s * .66), (s * .44, s * .96), (s * .06, s * .76)], fill=255)


def sparkle(d, s):
    for (cx, cy, rr) in [(.42, .52, .4), (.78, .22, .16)]:
        pts = []
        for i in range(8):
            a = -math.pi / 2 + i * math.pi / 4
            q = rr if i % 2 == 0 else rr * .28
            pts.append((s * cx + s * q * math.cos(a), s * cy + s * q * math.sin(a)))
        d.polygon(pts, fill=255)


def rug(d, s):
    d.rounded_rectangle([s * .14, s * .2, s * .86, s * .8], s * .04, fill=255)
    d.rounded_rectangle([s * .22, s * .28, s * .78, s * .72], s * .02, fill=0)
    d.polygon([(s * .5, s * .34), (s * .68, s * .5), (s * .5, s * .66), (s * .32, s * .5)], fill=255)
    for y in (.2, .8):
        for i in range(7):
            x = .18 + i * .107
            d.line([(s * x, s * y), (s * x, s * (y + (.1 if y > .5 else -.1)))], fill=255, width=int(s * .035))


def chat(d, s):
    d.rounded_rectangle([s * .08, s * .14, s * .92, s * .7], s * .14, fill=255)
    d.polygon([(s * .24, s * .66), (s * .44, s * .66), (s * .2, s * .9)], fill=255)


for n, f in [("Coin", coin), ("Star", star), ("Drop", drop), ("Clock", clock), ("Check", check), ("Lock", lock), ("Spray", spray),
             ("Person", person), ("Cart", cart), ("Gear", gear), ("Broom", broom), ("Sparkle", sparkle), ("Rug", rug), ("Chat", chat)]:
    icon(n, f)
print("ok", ROBOTO)

# Dégradé horizontal (gauche opaque -> droite transparente) : voile du menu principal.
h = Image.new("RGBA", (256, 4), (255, 255, 255, 0))
for x in range(256):
    for y in range(4):
        h.putpixel((x, y), (255, 255, 255, int(255 * (1 - x / 255) ** 1.4)))
h.save(os.path.join(OUT, "UI_GradientH.png"))
