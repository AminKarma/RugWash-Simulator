"""Pièces de la silhouette des joueurs (V0.3.0) dans le repère local de chaque pièce de
RugWashNetworkBuilder.BuildAvatarVisuals : mêmes pivots (hanches, tête, épaules), donc mêmes animations, mêmes points
d'attache et même synchronisation réseau ; seules les formes changent.
Export : Assets/RugWash/Models/Character.json."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rw_lib import *
from models_tools import organic, rbox


def torso(col):
    """Torse (origine au centre de l'ancienne boîte 0,40 × 0,46 × 0,24 m) : épaules arrondies, poitrine, taille affinée, col."""
    # Sections de la taille aux épaules : taille fine, poitrine pleine (avant bombé), carrure.
    body = loft("body", col, [(-0.23, 0.165, 0.105, 0.0), (-0.12, 0.158, 0.1, 0.004), (0.0, 0.17, 0.112, 0.008),
                              (0.1, 0.182, 0.118, 0.01), (0.17, 0.178, 0.108, 0.004), (0.205, 0.14, 0.09, 0.0), (0.225, 0.07, 0.06, 0.0)])
    solids = [body,
              sphere("shL", col, 0.07, (-0.165, 0.16, 0.0), scale=(1.0, 0.85, 1.0)),
              sphere("shR", col, 0.07, (0.165, 0.16, 0.0), scale=(1.0, 0.85, 1.0)),
              cylinder("collar", col, 0.062, 0.05, center=(0, 0.22, 0.005), axis="y", segments=28)]
    return organic("Avatar_Torso", col, solids, voxel=0.004, smooth_iters=6, tris=3000)


def bib_front(col):
    """Bavette avant de la salopette : plastron, poche de poitrine surpiquée, deux boutons de bretelles."""
    solids = [rbox("plate", col, (0.28, 0.28, 0.026), (0, 0, 0), 0.01),
              rbox("pocket", col, (0.12, 0.1, 0.012), (0.02, 0.03, 0.016), 0.004)]
    for x in (-0.1, 0.1):
        solids.append(cylinder(f"btn{x}", col, 0.012, 0.012, center=(x, 0.115, 0.018), axis="z", segments=16))
    return organic("Avatar_BibFront", col, solids, voxel=0.0018, smooth_iters=2, tris=1600)


def trouser(col):
    """Jambe de pantalon (hanche à l'origine, cheville à -0,84 m) : genouillère, poche cargo côté extérieur (+X), ourlet."""
    leg = lathe("leg", col, [(0.064, -0.84), (0.066, -0.8), (0.064, -0.78), (0.067, -0.45), (0.074, -0.2), (0.079, -0.08), (0.07, 0.0)], segments=24)
    solids = [leg,
              rbox("knee", col, (0.1, 0.12, 0.03), (0.0, -0.47, 0.058), 0.012),
              rbox("cargo", col, (0.03, 0.15, 0.12), (0.068, -0.3, 0.0), 0.01),
              rbox("flap", col, (0.036, 0.03, 0.124), (0.072, -0.225, 0.0), 0.008)]
    return organic("Avatar_Trouser", col, solids, voxel=0.0035, smooth_iters=3, tris=2400)


def boot(col):
    """Chaussure de sécurité (origine au centre de l'ancienne boîte 0,12 × 0,11 × 0,26 m) : coque de bout, tige, talon."""
    solids = [rbox("body", col, (0.11, 0.07, 0.24), (0, -0.01, 0.0), 0.03),
              sphere("toe", col, 0.06, (0, -0.012, 0.075), scale=(0.95, 0.65, 1.0)),
              cylinder("shaft", col, 0.05, 0.07, center=(0, 0.03, -0.05), axis="y", segments=24),
              rbox("tongue", col, (0.06, 0.05, 0.08), (0, 0.025, 0.02), 0.02)]
    upper = organic("Avatar_Boot", col, solids, voxel=0.003, smooth_iters=4, tris=1800)
    sole = rbox("Avatar_BootSole", col, (0.122, 0.022, 0.262), (0, -0.044, 0.002), 0.008)
    cutters = [box(f"tread{i}", col, (0.14, 0.008, 0.012), (0, -0.056, -0.11 + i * 0.03)) for i in range(8)]
    for c in cutters:
        boolean(sole, c)
    smooth(sole, 40)
    return [upper, sole]


def face(col):
    """Tête (origine = centre de l'ancienne boîte du visage) : crâne, mâchoire, pommettes, nez, arcades, oreilles, orbites."""
    solids = [sphere("cranium", col, 1.0, (0, 0.012, -0.004), scale=(0.088, 0.108, 0.098), segments=40),
              sphere("jaw", col, 1.0, (0, -0.05, 0.016), scale=(0.072, 0.062, 0.078), segments=32),
              sphere("cheekL", col, 0.032, (-0.045, -0.018, 0.06)),
              sphere("cheekR", col, 0.032, (0.045, -0.018, 0.06)),
              capsule("nose", col, (0, 0.012, 0.088), (0, -0.024, 0.104), 0.013),
              sphere("noseTip", col, 0.015, (0, -0.024, 0.102)),
              capsule("brow", col, (-0.042, 0.036, 0.072), (0.042, 0.036, 0.072), 0.011),
              sphere("chin", col, 0.03, (0, -0.088, 0.062), scale=(1.1, 0.8, 0.9)),
              sphere("earL", col, 1.0, (-0.093, 0.0, -0.005), scale=(0.013, 0.03, 0.021)),
              sphere("earR", col, 1.0, (0.093, 0.0, -0.005), scale=(0.013, 0.03, 0.021)),
              cylinder("neck", col, 0.045, 0.06, center=(0, -0.1, -0.012), axis="y", segments=20)]
    cutters = [sphere(f"socket{x}", col, 0.017, (x, 0.018, 0.098)) for x in (-0.038, 0.038)]
    return organic("Avatar_Face", col, solids, cutters, voxel=0.0025, smooth_iters=5, tris=3200)


def eye(col):
    """Œil (blanc et iris sombre confondus : petite lentille sombre brillante), dans le repère de l'ancien œil."""
    e = sphere("Avatar_Eye", col, 1.0, (0, 0, -0.004), scale=(0.0115, 0.0105, 0.008), segments=20)
    smooth(e, 80)
    return e


def hair(col):
    """Cheveux courts (repère du visage) : casque de cheveux au-dessus de la ligne du front, nuque et tempes."""
    shell = sphere("h", col, 1.0, (0, 0.022, -0.01), scale=(0.093, 0.11, 0.103), segments=40)
    cutters = [box("front", col, (0.3, 0.2, 0.2), (0, -0.045, 0.13)),
               box("low", col, (0.3, 0.2, 0.3), (0, -0.115, 0.0)),
               box("brow", col, (0.3, 0.06, 0.12), (0, 0.025, 0.12))]
    for x in (-0.095, 0.095):
        cutters.append(sphere(f"ear{x}", col, 0.028, (x, -0.005, -0.004)))
    return organic("Avatar_Hair", col, [shell], cutters, voxel=0.0025, smooth_iters=3, tris=1800)


def cap(col):
    """Casquette (repère de l'ancienne calotte, base à y = 0) : six panneaux, coutures, bouton, et visière bombée."""
    profile = [(0.114, 0.0), (0.112, 0.03), (0.1, 0.062), (0.075, 0.088), (0.04, 0.1), (0.0, 0.104)]
    crown = lathe("crown", col, profile, segments=48, close_bottom=False)
    shell = crown.modifiers.new("Shell", "SOLIDIFY")
    shell.thickness = 0.004
    apply_all(crown)
    solids = [crown, sphere("button", col, 0.009, (0, 0.104, 0))]
    # Coutures des six panneaux : passepoils en léger relief le long du profil.
    for i in range(6):
        a = i / 6 * math.tau
        pts = [(math.cos(a) * (r + 0.0012), y + 0.0008, math.sin(a) * (r + 0.0012)) for (r, y) in profile[:-1]] + [(0, 0.105, 0)]
        solids.append(tube(f"seam{i}", col, pts, 0.0022, segments=8))
    body = organic("Avatar_Cap", col, solids, voxel=0.0016, smooth_iters=2, tris=2600)
    body.scale = (1.0, 1.08, 1.0)  # un peu plus longue d'avant en arrière (Blender Y = Unity Z)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    # Visière : plaque bombée devant le front, légèrement inclinée.
    bm = bmesh.new()
    rows, cols = 6, 16
    verts = []
    for r in range(rows + 1):
        row = []
        for c in range(cols + 1):
            u = c / cols * 2 - 1
            t = r / rows
            x = u * 0.085 * (1 - 0.25 * t)
            z = 0.1 + t * 0.085 * (1 - 0.35 * u * u)
            y = 0.012 - 0.018 * u * u - 0.01 * t
            row.append(bm.verts.new(U(x, y, z)))
        verts.append(row)
    for r in range(rows):
        for c in range(cols):
            bm.faces.new((verts[r][c], verts[r][c + 1], verts[r + 1][c + 1], verts[r + 1][c]))
    visor = mesh_object("Avatar_Visor", bm, col)
    solid = visor.modifiers.new("Solid", "SOLIDIFY")
    solid.thickness = 0.008
    bevel(visor, 0.002, segments=2, angle=40)
    apply_all(visor)
    smooth(visor, 50)
    return [body, visor]


def build():
    col = collection("Character")
    keep = [torso(col), bib_front(col), trouser(col), face(col), eye(col), hair(col)] + boot(col) + cap(col)
    for o in list(col.objects):
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    for o in keep:
        color(o, (0.7, 0.6, 0.5, 1))
    return col


if __name__ == "__main__":
    reset()
    build()
    export("Character", "Character")
