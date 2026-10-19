"""Pièces d'outils tenus en main (V0.3.0), modélisées dans le repère local de chaque pièce de RugWashToolModels
(même encombrement que l'ancienne boîte arrondie, centrée sur l'origine) : poignées ergonomiques à empreintes de doigts,
corps de pistolet profilé, tête de pulvérisateur à gâchette, bloc moteur ventilé de l'aspirateur, tête d'aspiration,
bloc de brosse à saisie latérale. Export : Assets/RugWash/Models/Tools.json (une pièce par clé de maillage)."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rw_lib import *


def organic(name, col, solids, cutters=(), voxel=0.0011, smooth_iters=4, decimate=0.25, sharp=70, tris=2400):
    """Union de volumes (remaillage voxel), soustraction d'outils de découpe, puis lissage et allègement."""
    body = join(solids, name) if len(solids) > 1 else solids[0]
    body.name = name
    m = body.modifiers.new("Union", "REMESH")
    m.mode = "VOXEL"
    m.voxel_size = voxel
    apply_all(body)
    for cutter in cutters:
        boolean(body, cutter)
    remesh_smooth(body, voxel=voxel, smooth_iters=smooth_iters)
    decimate_to(body, tris)
    smooth(body, sharp)
    return body


def rbox(name, col, size, center=(0, 0, 0), radius=0.006):
    b = box(name, col, size, center)
    bevel(b, radius, segments=4, angle=30)
    apply_all(b)
    return b


def cyl_x(name, col, radius, length, center):
    return cylinder(name, col, radius, length, center=center, axis="x", segments=20)


def cyl_z(name, col, radius, length, center):
    return cylinder(name, col, radius, length, center=center, axis="z", segments=20)


# ---------------------------------------------------------------------------------------------------------------- Pistolet

def gun_grip(col):
    """Poignée 36 × 120 × 52 mm : galbe, trois empreintes de doigts à l'avant, talon évasé en bas."""
    solids = [rbox("g", col, (0.034, 0.112, 0.046), (0, 0.002, 0), 0.012),
              rbox("heel", col, (0.038, 0.016, 0.054), (0, -0.054, -0.002), 0.007),
              sphere("palm", col, 0.02, (0, 0.012, -0.012), scale=(0.85, 1.9, 1.0))]
    cutters = [cyl_x(f"f{i}", col, 0.0085, 0.06, (0, y, 0.0285)) for i, y in enumerate((0.032, 0.006, -0.02))]
    return organic("Tool_GunGrip", col, solids, cutters, decimate=0.22)


def gun_body(col):
    """Corps 56 × 68 × 170 mm : nez effilé vers le collier de la lance, dos arrondi, nervure supérieure, flancs creusés."""
    solids = [rbox("b", col, (0.052, 0.058, 0.13), (0, -0.002, -0.012), 0.018),
              rbox("nose", col, (0.04, 0.044, 0.05), (0, 0.004, 0.06), 0.014),
              rbox("rib", col, (0.014, 0.012, 0.15), (0, 0.03, 0.0), 0.005),
              sphere("rear", col, 0.03, (0, -0.002, -0.07), scale=(0.9, 0.95, 0.6))]
    cutters = [box(f"side{s}", col, (0.01, 0.026, 0.07), (s * 0.029, -0.004, -0.01)) for s in (-1, 1)]
    return organic("Tool_GunBody", col, solids, cutters, decimate=0.2)


def cannon_body(col):
    """Corps du canon à mousse 60 × 70 × 200 mm : même famille, plus long, bouton de réglage sur le dessus."""
    solids = [rbox("b", col, (0.056, 0.06, 0.16), (0, -0.003, -0.012), 0.02),
              rbox("nose", col, (0.042, 0.046, 0.05), (0, 0.004, 0.075), 0.015),
              cylinder("knob", col, 0.014, 0.02, center=(0, 0.034, -0.03), axis="y", segments=20)]
    cutters = [box(f"side{s}", col, (0.01, 0.026, 0.09), (s * 0.031, -0.005, -0.015)) for s in (-1, 1)]
    return organic("Tool_CannonBody", col, solids, cutters, decimate=0.2)


def trigger(col, name, height, depth):
    """Gâchette incurvée (arc vers l'avant), largeur 12 mm."""
    pts = []
    for i in range(9):
        t = i / 8
        pts.append((0, height * (0.5 - t), depth * 0.5 * math.sin(t * math.pi) * 0.9 - depth * 0.15))
    solids = [tube("t", col, pts, 0.0055, segments=12)]
    for o in solids:
        o.scale = (1.0, 1.0, 1.0)
    return organic(name, col, solids, voxel=0.0006, smooth_iters=3, decimate=0.3)


# ---------------------------------------------------------------------------------------------------------------- Pulvérisateur

def soap_head(col):
    """Tête de pulvérisateur 34 × 46 × 110 mm : col arrière sur le bouchon, capot plat, museau de buse à l'avant."""
    solids = [cylinder("neck", col, 0.0155, 0.04, center=(0, -0.006, -0.03), axis="y", segments=24),
              rbox("hood", col, (0.032, 0.026, 0.1), (0, 0.009, 0.002), 0.01),
              rbox("muzzle", col, (0.024, 0.022, 0.03), (0, 0.008, 0.046), 0.008),
              rbox("spine", col, (0.02, 0.012, 0.08), (0, -0.006, 0.01), 0.005)]
    cutters = [box("trigger_slot", col, (0.022, 0.03, 0.05), (0, -0.026, 0.03))]
    return organic("Tool_SoapHead", col, solids, cutters, decimate=0.22)


def soap_cap(col):
    """Bouchon cranté (lathe + crans) remplaçant le cylindre lisse : rayon 20 mm, hauteur 20 mm."""
    cap = lathe("c", col, [(0.0, -0.01), (0.0195, -0.01), (0.0205, -0.008), (0.0205, 0.008), (0.018, 0.01), (0.0, 0.01)], segments=40)
    ribs = []
    for i in range(24):
        a = i / 24 * math.tau
        ribs.append(box(f"r{i}", col, (0.003, 0.016, 0.003), (math.cos(a) * 0.0205, 0, math.sin(a) * 0.0205)))
        ribs[-1].rotation_euler = (0, 0, -a)
    return organic("Tool_SoapCap", col, [cap] + ribs, voxel=0.0006, smooth_iters=2, decimate=0.3)


# ---------------------------------------------------------------------------------------------------------------- Aspirateur

def vac_body(col):
    """Bloc moteur 75 × 85 × 150 mm : coque arrondie, ouïes d'aération sur les flancs, couvercle de filtre à l'arrière."""
    solids = [rbox("b", col, (0.07, 0.078, 0.14), (0, 0, 0), 0.026),
              cylinder("filter", col, 0.03, 0.02, center=(0, 0.002, -0.07), axis="z", segments=28)]
    cutters = []
    for s in (-1, 1):
        for i in range(5):
            cutters.append(box(f"v{s}{i}", col, (0.012, 0.006, 0.05), (s * 0.036, 0.012 - i * 0.011, 0.02)))
    cutters.append(cylinder("seam", col, 0.032, 0.0025, center=(0, 0.002, -0.055), axis="z", segments=28))
    return organic("Tool_VacBody", col, solids, cutters, decimate=0.2)


def vac_grip(col):
    solids = [rbox("g", col, (0.038, 0.114, 0.052), (0, 0, 0), 0.014)]
    cutters = [cyl_x(f"f{i}", col, 0.0085, 0.06, (0, y, 0.0295)) for i, y in enumerate((0.03, 0.004, -0.022))]
    return organic("Tool_VacGrip", col, solids, cutters, decimate=0.22)


def vac_head(col):
    """Tête d'aspiration 250 × 38 × 75 mm : profil en coin (avant bas), arêtes arrondies, deux roulettes à l'arrière."""
    solids = [rbox("h", col, (0.246, 0.03, 0.07), (0, 0.002, 0.0), 0.012),
              rbox("top", col, (0.12, 0.014, 0.05), (0, 0.016, -0.006), 0.006)]
    for x in (-0.1, 0.1):
        solids.append(cyl_x(f"w{x}", col, 0.013, 0.014, (x, -0.006, -0.03)))
    cutters = [box("mouth", col, (0.2, 0.012, 0.02), (0, -0.016, 0.012))]
    return organic("Tool_VacHead", col, solids, cutters, voxel=0.0012, decimate=0.2)


# ---------------------------------------------------------------------------------------------------------------- Brosse

def brush_body(col):
    """Bloc de brosse 230 × 45 × 90 mm : dessus bombé, chanfreins, gorges de saisie sur les côtés longs."""
    solids = [rbox("b", col, (0.228, 0.03, 0.088), (0, -0.007, 0), 0.01),
              rbox("crown", col, (0.2, 0.02, 0.06), (0, 0.012, 0), 0.01)]
    cutters = [cylinder(f"g{s}", col, 0.006, 0.26, center=(0, 0.004, s * 0.044), axis="x", segments=16) for s in (-1, 1)]
    return organic("Tool_BrushBody", col, solids, cutters, voxel=0.0012, decimate=0.22)


def detail_body(col):
    solids = [rbox("b", col, (0.048, 0.026, 0.158), (0, -0.002, 0), 0.01),
              rbox("crown", col, (0.036, 0.012, 0.12), (0, 0.01, -0.01), 0.006)]
    return organic("Tool_DetailBody", col, solids, voxel=0.0008, decimate=0.25)


def build():
    col = collection("Tools")
    parts = [gun_grip(col), gun_body(col), cannon_body(col), trigger(col, "Tool_GunTrigger", 0.048, 0.022),
             trigger(col, "Tool_SoapTrigger", 0.055, 0.02), soap_head(col), soap_cap(col), vac_body(col), vac_grip(col), vac_head(col),
             brush_body(col), detail_body(col)]
    for o in list(col.objects):
        if o not in parts:
            bpy.data.objects.remove(o, do_unlink=True)
    return col


if __name__ == "__main__":
    reset()
    build()
    export("Tools", "Tools")
