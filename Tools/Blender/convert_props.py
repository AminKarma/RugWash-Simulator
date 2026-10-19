"""Textures des accessoires Poly Haven -> Assets/RugWash/Textures/Props/<Alias>_<matériau>_{Albedo.jpg,Normal.jpg,Mask.png}
(masque : R métal, G occlusion, A lissé = 1 - rugosité, comme les surfaces). Python système avec Pillow et NumPy."""
import json, os, re, sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from props_list import PROPS

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MANIFEST = os.path.join(PROJECT, "Assets", "RugWash", "Models", "Props", "manifest.json")
OUT = os.path.join(PROJECT, "Assets", "RugWash", "Textures", "Props")
SRC = os.path.join(os.environ["TEMP"], "ph", "models")
os.makedirs(OUT, exist_ok=True)
manifest = json.load(open(MANIFEST, encoding="utf-8"))
sizes = {alias: size for alias, _, _, size in PROPS}


def safe(name):
    return re.sub(r"[^A-Za-z0-9]+", "", name.title())


def load(folder, rel, size, mode):
    if not rel:
        return None
    im = Image.open(os.path.join(folder, rel)).convert(mode)
    if max(im.size) > size:
        im = im.resize((size, size), Image.LANCZOS)
    return im


for alias, entry in manifest.items():
    folder = os.path.join(SRC, entry["asset"])
    size = sizes.get(alias, 1024)
    for mat, m in entry["materials"].items():
        stem = os.path.join(OUT, f"{alias}_{safe(mat)}")
        alpha_cut = m["alpha"] != "OPAQUE" and bool(m["albedo"]) and m["albedo"].lower().endswith(".png")
        m["cutout"] = alpha_cut
        albedo = load(folder, m["albedo"], size, "RGBA" if alpha_cut else "RGB")
        if albedo is not None:
            if alpha_cut:
                albedo.save(stem + "_Albedo.png", optimize=True)
            else:
                albedo.save(stem + "_Albedo.jpg", quality=90)
        normal = load(folder, m["normal"], size, "RGB")
        if normal is not None:
            normal.save(stem + "_Normal.jpg", quality=92)
        orm = load(folder, m["orm"], size, "RGB")
        occ = load(folder, m["occlusion"], size, "RGB")
        if orm is not None or occ is not None:
            n = (orm or occ).size
            o = np.asarray(orm).astype(np.float32) / 255 if orm is not None else None
            ao = (np.asarray(occ.resize(n)).astype(np.float32) / 255)[..., 0] if occ is not None else np.ones(n[::-1], np.float32)
            rough = o[..., 1] * m["roughness"] if o is not None else np.full(n[::-1], m["roughness"], np.float32)
            metal = o[..., 2] * m["metallic"] if o is not None else np.full(n[::-1], m["metallic"], np.float32)
            mask = np.stack([metal, ao, np.zeros_like(ao), 1 - rough], -1)
            Image.fromarray((np.clip(mask, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(stem + "_Mask.png", optimize=True)
        m["stem"] = os.path.basename(stem)
        print(alias, mat, "->", m["stem"], "cutout" if alpha_cut else "")
json.dump(manifest, open(MANIFEST, "w", encoding="utf-8"), indent=1)
