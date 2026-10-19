"""Blender (fond) : importe les accessoires Poly Haven téléchargés, les allège au budget, sépare par matériau, place le pivot
au centre de la base et exporte Assets/RugWash/Models/Props/<Alias>.json (UV du modèle) + un manifeste des textures.
blender -b --factory-startup --python Tools/Blender/props_polyhaven.py -- [alias...]"""
import json
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from props_list import PROPS
import rw_lib

SRC = os.path.join(os.environ["TEMP"], "ph", "models")
OUT = os.path.join(rw_lib.MODELS, "Props")


def gltf_materials(path):
    """Matériaux glTF : textures (fichiers) et facteurs."""
    data = json.load(open(path, encoding="utf-8"))
    images = [im.get("uri") for im in data.get("images", [])]
    textures = [images[t["source"]] if "source" in t else None for t in data.get("textures", [])]
    out = {}
    for m in data.get("materials", []):
        pbr = m.get("pbrMetallicRoughness", {})
        tex = lambda d: textures[d["index"]] if d else None
        out[m.get("name", "material")] = {
            "albedo": tex(pbr.get("baseColorTexture")),
            "normal": tex(m.get("normalTexture")),
            "orm": tex(pbr.get("metallicRoughnessTexture")),
            "occlusion": tex(m.get("occlusionTexture")),
            "color": pbr.get("baseColorFactor", [1, 1, 1, 1]),
            "metallic": pbr.get("metallicFactor", 1.0),
            "roughness": pbr.get("roughnessFactor", 1.0),
            "alpha": m.get("alphaMode", "OPAQUE"),
            "doubleSided": m.get("doubleSided", False),
        }
    return out


def process(alias, asset, budget):
    rw_lib.reset()
    folder = os.path.join(SRC, asset)
    gltf = next(f for f in os.listdir(folder) if f.endswith(".gltf"))
    bpy.ops.import_scene.gltf(filepath=os.path.join(folder, gltf))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    # Transformations appliquées (parents vides du glTF supprimés).
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj = rw_lib.join(meshes, alias) if len(meshes) > 1 else meshes[0]
    obj.name = alias
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)

    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris > budget:
        d = obj.modifiers.new("Decimate", "DECIMATE")
        d.ratio = budget / tris
        d.use_collapse_triangulate = True
        rw_lib.apply_all(obj)

    # Pivot : centre de la base (Unity : y = 0 au sol, x et z centrés).
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for v in obj.data.vertices:
        lo = Vector(map(min, lo, v.co))
        hi = Vector(map(max, hi, v.co))
    offset = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
    for v in obj.data.vertices:
        v.co += offset

    # Une pièce par matériau.
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    if len(obj.data.materials) > 1:
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.separate(type="MATERIAL")
        bpy.ops.object.mode_set(mode="OBJECT")
    parts = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    materials = gltf_materials(os.path.join(folder, gltf))
    extra = {}
    used = []
    for i, part in enumerate(parts):
        mat = part.data.materials[0].name if part.data.materials else "material"
        base = mat.split(".")[0]
        key = base if base in materials else next(iter(materials), base)
        part.name = f"{alias}_{i}"
        extra[part.name] = {"material": key}
        used.append(key)
    size = hi - lo
    rw_lib.export(None, alias, uv_mode="mesh", objects=parts, extra=extra, folder=OUT)
    return {"asset": asset, "size": [size.x, size.z, size.y],
            "materials": {k: materials[k] for k in set(used) if k in materials}}


if __name__ == "__main__":
    wanted = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    manifest_path = os.path.join(OUT, "manifest.json")
    manifest = json.load(open(manifest_path, encoding="utf-8")) if os.path.exists(manifest_path) else {}
    for alias, asset, budget, _ in PROPS:
        if wanted and alias not in wanted:
            continue
        try:
            manifest[alias] = process(alias, asset, budget)
        except Exception as e:  # un accessoire en échec n'empêche pas les autres
            print(f"[rw] ÉCHEC {alias}: {e}")
    os.makedirs(OUT, exist_ok=True)
    json.dump(manifest, open(manifest_path, "w", encoding="utf-8"), indent=1)
    print("[rw] manifeste", manifest_path)
