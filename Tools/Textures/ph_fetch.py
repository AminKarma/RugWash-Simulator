"""Télécharge des textures CC0 Poly Haven et produit les cartes prêtes pour URP Lit.

Sorties (Assets/RugWash/Textures/Surfaces) :
  <Alias>_Albedo.jpg  (sRGB)
  <Alias>_Normal.jpg  (OpenGL, importé comme normal map)
  <Alias>_Mask.png    (R métal, G occlusion, B 0, A lissé = 1 - rugosité)  -> _MetallicGlossMap ET _OcclusionMap
Options par entrée : desaturate (pour les tissus/peintures teintés par _BaseColor), brighten (gain d'albédo),
rough_mul / rough_add (remap rugosité), metal (force métal).
"""
import io, json, os, sys, urllib.request
import numpy as np
from PIL import Image

UA = {"User-Agent": "RugWashSim-dev"}
RAW = os.path.join(os.environ["TEMP"], "ph", "raw")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "RugWash", "Textures", "Surfaces")
os.makedirs(RAW, exist_ok=True)
os.makedirs(OUT, exist_ok=True)

ENTRIES = [
    # alias, id, res albedo/normal, res mask, options
    ("Floor", "concrete_floor_worn_001", "2k", "1k", {"desaturate": 0.5, "normalize": (0.56, 1.15)}),
    ("WashPad", "brushed_concrete", "2k", "1k", {"desaturate": 0.6, "normalize": (0.64, 1.0)}),
    ("Plaster", "painted_plaster_wall", "2k", "1k", {"desaturate": 1.0, "normalize": (0.88, 1.0)}),
    ("CeilingSheet", "box_profile_metal_sheet", "1k", "1k", {"desaturate": 1.0, "normalize": (0.85, 0.8)}),
    ("Wood", "fine_grained_wood", "1k", "1k", {}),
    ("WoodPlanks", "oak_wood_planks", "1k", "1k", {}),
    ("Plywood", "plywood", "1k", "1k", {}),
    ("MetalPlate", "metal_plate", "1k", "1k", {"desaturate": 1.0, "normalize": (0.72, 0.9)}),
    ("PaintedMetal", "blue_metal_plate", "1k", "1k", {"desaturate": 1.0, "normalize": (0.86, 0.55), "metal": 0.0}),
    ("Leather", "brown_leather", "1k", "1k", {}),
        ("Twill", "poly_wool_herringbone", "1k", "1k", {"desaturate": 1.0, "normalize": (0.84, 0.9)}),
    ("Brick", "brick_wall_001", "1k", "1k", {}),
    ("Concrete", "concrete_wall_004", "1k", "1k", {}),
]


def get(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return path
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=120) as r, open(path, "wb") as f:
        f.write(r.read())
    return path


def files(asset):
    req = urllib.request.Request(f"https://api.polyhaven.com/files/{asset}", headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def info(asset):
    req = urllib.request.Request(f"https://api.polyhaven.com/info/{asset}", headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def fetch_map(f, key, res, asset):
    folder = os.path.join(RAW, asset)
    os.makedirs(folder, exist_ok=True)
    url = f[key][res]["jpg"]["url"]
    return get(url, os.path.join(folder, f"{key}_{res}.jpg"))


def main(only=None):
    meta = {}
    for alias, asset, res, mres, opt in ENTRIES:
        if only and alias not in only:
            continue
        f = files(asset)
        i = info(asset)
        dims = i.get("dimensions")  # mm [w, h]
        albedo = Image.open(fetch_map(f, "Diffuse", res, asset)).convert("RGB")
        normal = Image.open(fetch_map(f, "nor_gl", res, asset)).convert("RGB")
        arm = Image.open(fetch_map(f, "arm", mres, asset)).convert("RGB")

        a = np.asarray(albedo).astype(np.float32) / 255.0
        if opt.get("desaturate"):
            lum = (a * np.array([0.299, 0.587, 0.114])).sum(-1, keepdims=True)
            a = a + (lum - a) * opt["desaturate"]
        if "normalize" in opt:
            # Albédo teintable : luminance moyenne ramenée à la cible, contraste réduit (la couleur vient de _BaseColor).
            target, contrast = opt["normalize"]
            lum = (a * np.array([0.299, 0.587, 0.114])).sum(-1, keepdims=True)
            mean = float(lum.mean())
            a = np.clip((a - mean) * contrast + target, 0, 1)
        if opt.get("brighten"):
            # gain en linéaire, borné
            lin = a ** 2.2 * opt["brighten"]
            a = np.clip(lin, 0, 1) ** (1 / 2.2)
        Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).save(f"{OUT}/{alias}_Albedo.jpg", quality=90)
        normal.save(f"{OUT}/{alias}_Normal.jpg", quality=95)

        m = np.asarray(arm).astype(np.float32) / 255.0
        ao, rough, metal = m[..., 0], m[..., 1], m[..., 2]
        rough = np.clip(rough * opt.get("rough_mul", 1.0) + opt.get("rough_add", 0.0), 0, 1)
        if "metal" in opt:
            metal = np.full_like(metal, opt["metal"])
        mask = np.stack([metal, ao, np.zeros_like(ao), 1.0 - rough], -1)
        Image.fromarray((mask * 255 + 0.5).astype(np.uint8), "RGBA").save(f"{OUT}/{alias}_Mask.png", optimize=True)
        meta[alias] = {"asset": asset, "dimensions_mm": dims, "res": res}
        print(alias, asset, dims, albedo.size, flush=True)
    path = f"{OUT}/SOURCES.json"
    old = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
    old.update(meta)
    json.dump(old, open(path, "w", encoding="utf-8"), indent=2)


if __name__ == "__main__":
    main(sys.argv[1:] or None)
