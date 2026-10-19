"""Gant de travail refermé sur une poignée (V0.3.0), dans le repère du gant de RugWashToolModels.Glove :
Y = axe de la poignée qui traverse le poing, Z = des poignets vers les jointures (avant), X = dos de la main (main droite).
La poignée (rayon ~16 mm) passe par l'origine ; le poignet est derrière (Z < 0), dans la manchette."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rw_lib import *


def finger(col, name, y, radius, length_scale=1.0):
    # Jointure au coin avant du dos de la main, puis enroulement autour de la poignée (devant, puis côté paume).
    knuckle = (0.031, y, 0.02)
    pts = [knuckle, (0.026, y, 0.03)]
    r = 0.0265
    for a in range(55, int(55 + 175 * length_scale) + 1, 25):
        pts.append((math.cos(math.radians(a)) * r, y, math.sin(math.radians(a)) * r))
    radii = [radius * (1.0 - 0.18 * i / (len(pts) - 1)) for i in range(len(pts))]
    t = tube(name, col, pts, radius, segments=14, caps=True, radii=radii)
    tip = sphere(name + "_tip", col, radii[-1], pts[-1], segments=14)
    knob = sphere(name + "_knuckle", col, radius * 1.08, knuckle, segments=14)
    return [t, tip, knob]


def build_glove():
    col = collection("Glove")
    parts = []
    # Dos de la main et paume : dalle arrondie côté +X, de la manchette aux jointures.
    # Dos de la main bombé (sections superelliptiques le long de la poignée), plus épais au centre.
    palm = loft("Palm", col, [(-0.043, 0.009, 0.027, -0.012), (-0.032, 0.0135, 0.033, -0.011), (0.0, 0.0155, 0.036, -0.01),
                              (0.03, 0.014, 0.035, -0.01), (0.043, 0.0095, 0.029, -0.011)], segments=36, exponent=2.3)
    for v in palm.data.vertices:
        v.co.x += 0.024
    parts.append(palm)
    # Talon de la main (sous la paume, contre la poignée) et éminence du pouce.
    parts.append(sphere("Heel", col, 0.022, (0.012, -0.012, -0.03), scale=(1.0, 1.6, 1.0)))
    parts.append(sphere("Thenar", col, 0.02, (0.008, 0.022, -0.034), scale=(1.1, 1.2, 1.0)))
    for name, y, r, length in (("Index", 0.031, 0.0108, 1.0), ("Middle", 0.0105, 0.0113, 1.0), ("Ring", -0.0105, 0.0108, 0.96),
                               ("Pinky", -0.0295, 0.0094, 0.88)):
        parts += finger(col, name, y, r, length)
    # Pouce : de la base (dos, arrière, haut) par-dessus la poignée, côté opposé, posé sur l'index.
    thumb_pts = [(0.024, 0.03, -0.036), (0.006, 0.045, -0.033), (-0.014, 0.047, -0.02), (-0.026, 0.043, -0.002), (-0.027, 0.038, 0.014)]
    parts.append(tube("Thumb", col, thumb_pts, 0.012, segments=14, radii=[0.0135, 0.0125, 0.0118, 0.0112, 0.0105]))
    parts.append(sphere("ThumbTip", col, 0.0105, thumb_pts[-1], segments=14))
    # Poignet (rejoint la manchette).
    parts.append(cylinder("Wrist", col, 0.027, 0.05, center=(0.006, 0.0, -0.058), axis="z", segments=20))
    fist = join(parts, "Fist")
    remesh_smooth(fist, voxel=0.0016, smooth_iters=8, decimate=0.075)
    smooth(fist, 60)

    # Manchette en tissu épais : léger évasement et bourrelet au bord.
    cuff = lathe("Cuff", col, [(0.034, 0.0), (0.037, 0.012), (0.039, 0.036), (0.0415, 0.05), (0.04, 0.056), (0.036, 0.057)], segments=28,
                 close_bottom=False, close_top=False)
    # Lathe autour de Y -> axe Z vers l'arrière (comme l'ancienne manchette, tournée de -90° autour de X).
    cuff.rotation_euler = (math.radians(90), 0, 0)
    cuff.location = U(0.0, 0.0, -0.03)
    bpy.context.view_layer.objects.active = cuff
    cuff.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    solid = cuff.modifiers.new("Solid", "SOLIDIFY")
    solid.thickness = 0.003
    apply_all(cuff)
    smooth(cuff, 50)
    color(fist, (0.9, 0.55, 0.2, 1))
    color(cuff, (0.15, 0.2, 0.3, 1))
    return col


if __name__ == "__main__":
    reset()
    build_glove()
    export("Glove", "Glove")
