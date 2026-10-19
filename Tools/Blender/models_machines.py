"""Machines, robot et outils restants (V0.3.0, modélisés via le MCP Blender), dans le repère de chaque pièce des
générateurs (RugWashMachineModels, RugWashWorkshopModels, RugWashToolModels) : mêmes encombrements, pivots et points
d'attache (le moyeu du souffleur tourne toujours, la LED du robot reste une pièce à part). Export : Models/Machines.json."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rw_lib import *
from models_tools import organic, rbox


# ---------------------------------------------------------------------------------------------------------------- Souffleur

def blower_housing(col):
    """Carter du souffleur (anneau de 0,68 m, axe Z) : virole profonde avec lèvre avant et nervures arrière."""
    shell = lathe("s", col, [(0.31, -0.06), (0.335, -0.06), (0.345, -0.04), (0.345, 0.04), (0.36, 0.055), (0.36, 0.065),
                             (0.325, 0.065), (0.315, 0.04), (0.315, -0.04), (0.31, -0.06)], segments=64, close_bottom=False, close_top=False)
    shell.rotation_euler = (math.radians(90), 0, 0)  # axe Y du tour -> Z Unity (vers l'avant)
    bpy.context.view_layer.objects.active = shell
    bpy.ops.object.select_all(action="DESELECT")
    shell.select_set(True)
    bpy.ops.object.transform_apply(rotation=True)
    solids = [shell]
    for i in range(8):
        a = i / 8 * math.tau
        rib = box(f"rib{i}", col, (0.02, 0.05, 0.11), (math.cos(a) * 0.33, math.sin(a) * 0.33, -0.005))
        rib.rotation_euler = (0, math.radians(90) * 0, 0)
        solids.append(rib)
    return organic("Blower_Housing", col, solids, voxel=0.004, smooth_iters=2, tris=3500)


def blower_motor(col):
    """Moteur (cylindre d'axe Y avant rotation de 90° dans le générateur) : carcasse à ailettes de refroidissement, capot."""
    body = cylinder("b", col, 0.085, 0.14, axis="y", segments=32)
    solids = [body, cylinder("cap", col, 0.07, 0.03, center=(0, -0.08, 0), axis="y", segments=32)]
    for i in range(16):
        a = i / 16 * math.tau
        fin = box(f"f{i}", col, (0.012, 0.12, 0.012), (math.cos(a) * 0.088, 0, math.sin(a) * 0.088))
        fin.rotation_euler = (0, 0, -a)
        solids.append(fin)
    return organic("Blower_Motor", col, solids, voxel=0.0025, smooth_iters=2, tris=3000)


def blower_blade(col):
    """Pale (repère de l'ancienne boîte 0,1 × 0,27 × 0,012) : profil en goutte élargi vers l'extérieur, vrillée."""
    bm = bmesh.new()
    rows, cols = 10, 8
    grid = []
    for r in range(rows + 1):
        t = r / rows
        y = -0.135 + t * 0.27
        w = 0.035 + 0.035 * math.sin(t * math.pi * 0.85)
        twist = math.radians(-14 + 22 * t)
        row = []
        for c in range(cols + 1):
            u = c / cols * 2 - 1
            x = u * w
            z = 0.004 * (1 - u * u) + x * math.sin(twist)
            row.append(bm.verts.new(U(x * math.cos(twist), y, z)))
        grid.append(row)
    for r in range(rows):
        for c in range(cols):
            bm.faces.new((grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c]))
    blade = mesh_object("Blower_Blade", bm, col)
    s = blade.modifiers.new("S", "SOLIDIFY")
    s.thickness = 0.006
    subsurf(blade, 1)
    apply_all(blade)
    smooth(blade, 60)
    return blade


def blower_base(col):
    """Socle 0,62 × 0,06 × 0,46 m : patins caoutchouc, dessus chanfreiné."""
    solids = [rbox("plate", col, (0.6, 0.04, 0.44), (0, 0.006, 0), 0.015)]
    for x in (-0.26, 0.26):
        for z in (-0.18, 0.18):
            solids.append(cylinder(f"foot{x}{z}", col, 0.03, 0.02, center=(x, -0.02, z), axis="y", segments=20))
    return organic("Blower_Base", col, solids, voxel=0.003, smooth_iters=2, tris=1800)


# ---------------------------------------------------------------------------------------------------------------- Pupitre

def pedestal_column(col):
    """Colonne du pupitre 0,30 × 0,95 × 0,26 m : socle élargi, panneau d'accès vissé, grille d'aération."""
    solids = [rbox("col", col, (0.28, 0.9, 0.24), (0, 0.02, 0), 0.02),
              rbox("foot", col, (0.34, 0.06, 0.3), (0, -0.445, 0), 0.015),
              rbox("door", col, (0.2, 0.42, 0.012), (0, 0.05, -0.122), 0.006)]
    for i in range(4):
        solids.append(cylinder(f"screw{i}", col, 0.006, 0.006, center=((-0.085, 0.085)[i % 2], 0.24 - (i // 2) * 0.38, -0.129), axis="z", segments=12))
    cutters = [box(f"vent{i}", col, (0.16, 0.008, 0.02), (0, -0.28 - i * 0.02, -0.12)) for i in range(5)]
    return organic("Machine_PedestalColumn", col, solids, cutters, voxel=0.003, smooth_iters=1, tris=2400)


def pedestal_panel(col):
    """Pupitre incliné 0,36 × 0,04 × 0,30 m : cadre en relief et bouton d'arrêt (pièce à part) au centre."""
    solids = [rbox("p", col, (0.36, 0.034, 0.3), (0, 0, 0), 0.012), rbox("rim", col, (0.34, 0.012, 0.28), (0, 0.02, 0), 0.006)]
    cutters = [box("inset", col, (0.3, 0.02, 0.24), (0, 0.028, 0))]
    return organic("Machine_PedestalPanel", col, solids, cutters, voxel=0.0025, smooth_iters=1, tris=1600)


def button(col):
    """Coup-de-poing rouge (Ø 100 mm) sur collerette."""
    solids = [cylinder("c", col, 0.05, 0.012, center=(0, -0.01, 0), axis="y", segments=32),
              sphere("dome", col, 1.0, (0, 0.004, 0), scale=(0.042, 0.018, 0.042), segments=32)]
    return organic("Machine_Button", col, solids, voxel=0.0015, smooth_iters=2, tris=1200)


# ---------------------------------------------------------------------------------------------------------------- Robot

def robot_body(col):
    """Coque du robot (Ø 0,34 m) : dessus bombé, rainure de capot, fente de pare-chocs, bouton et grille d'évacuation."""
    shell = lathe("b", col, [(0.0, 0.012), (0.16, 0.012), (0.17, 0.022), (0.171, 0.056), (0.162, 0.072), (0.13, 0.078), (0.0, 0.078)],
                  segments=64)
    # Rainure du capot (anneau fermé sur la pente) et grille d'évacuation à l'arrière.
    ring = lathe("groove", col, [(0.146, 0.06), (0.15, 0.06), (0.15, 0.08), (0.146, 0.08), (0.146, 0.06)], segments=64,
                 close_bottom=False, close_top=False)
    cutters = [ring]
    for i in range(7):
        cutters.append(box(f"g{i}", col, (0.006, 0.03, 0.04), (-0.03 + i * 0.01, 0.06, -0.155)))
    body = organic("Robot_Body", col, [shell], cutters, voxel=0.0025, smooth_iters=1, tris=3500)
    return body


def dock_tower(col):
    """Borne de la base 0,34 × 0,13 × 0,07 m : façade inclinée, fente lumineuse (LED à part), arrondis."""
    solids = [rbox("t", col, (0.34, 0.12, 0.065), (0, -0.005, 0), 0.022),
              rbox("top", col, (0.3, 0.02, 0.05), (0, 0.06, -0.005), 0.01)]
    cutters = [box("slot", col, (0.2, 0.006, 0.02), (0, 0.035, 0.034))]
    return organic("Dock_Tower", col, solids, cutters, voxel=0.0025, smooth_iters=2, tris=1800)


# ---------------------------------------------------------------------------------------------------------------- Outils restants

def bucket_body(col):
    """Seau (repère de l'outil : anse à l'origine, fond à -0,36 m) : paroi conique nervurée, rebord roulé, graduations."""
    prof = [(0.0, -0.36), (0.1, -0.36), (0.105, -0.35), (0.13, -0.13), (0.134, -0.12)]
    outer = lathe("o", col, prof, segments=48, close_top=False)
    s = outer.modifiers.new("S", "SOLIDIFY")
    s.thickness = 0.004
    s.offset = 1.0
    apply_all(outer)
    solids = [outer]
    for i in range(3):  # cerclages de renfort
        y = -0.3 + i * 0.07
        r = 0.1 + (y + 0.36) / 0.24 * 0.03 + 0.003
        solids.append(lathe(f"band{i}", col, [(r - 0.004, y - 0.006), (r + 0.002, y - 0.004), (r + 0.002, y + 0.004), (r - 0.004, y + 0.006)],
                            segments=48, close_bottom=False, close_top=False))
    for side in (-1, 1):  # oreilles d'anse
        solids.append(rbox(f"ear{side}", col, (0.012, 0.03, 0.03), (side * 0.134, -0.135, 0), 0.006))
    return organic("Tool_BucketBody", col, solids, voxel=0.002, smooth_iters=1, tris=3000)


def squeegee_holder(col):
    """Support de raclette 0,55 × 0,035 × 0,045 m : profilé en U, douille du manche au centre."""
    solids = [rbox("bar", col, (0.55, 0.026, 0.042), (0, -0.004, 0), 0.008),
              cylinder("socket", col, 0.018, 0.04, center=(0, 0.02, 0), axis="y", segments=24),
              rbox("cheek", col, (0.12, 0.03, 0.044), (0, 0.006, 0), 0.01)]
    cutters = [box("groove", col, (0.56, 0.01, 0.014), (0, -0.017, 0))]
    return organic("Tool_SqueegeeHolder", col, solids, cutters, voxel=0.0018, smooth_iters=1, tris=2200)


def rotary_motor(col):
    """Moteur de monobrosse (révolution de 0,24 m) : capot bombé, ailettes de refroidissement, poignée de transport."""
    shell = lathe("m", col, [(0.0, 0.17), (0.05, 0.165), (0.1, 0.13), (0.12, 0.06), (0.122, 0.0)], segments=56, close_bottom=False)
    sh = shell.modifiers.new("S", "SOLIDIFY")
    sh.thickness = 0.005
    apply_all(shell)
    solids = [shell, tube("handle", col, [(-0.06, 0.12, 0.06), (-0.04, 0.2, 0.03), (0.04, 0.2, 0.03), (0.06, 0.12, 0.06)], 0.012, segments=12)]
    for i in range(20):
        a = i / 20 * math.tau
        fin = box(f"fin{i}", col, (0.006, 0.05, 0.02), (math.cos(a) * 0.115, 0.05, math.sin(a) * 0.115))
        fin.rotation_euler = (0, 0, -a)
        solids.append(fin)
    return organic("Tool_RotaryMotor", col, solids, voxel=0.0025, smooth_iters=1, tris=3200)


def build():
    col = collection("Machines")
    keep = [blower_housing(col), blower_motor(col), blower_blade(col), blower_base(col), pedestal_column(col), pedestal_panel(col),
            button(col), robot_body(col), dock_tower(col), bucket_body(col), squeegee_holder(col), rotary_motor(col)]
    for o in list(col.objects):
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    for o in keep:
        color(o, (0.75, 0.6, 0.3, 1))
    return col


if __name__ == "__main__":
    reset()
    build()
    export("Machines", "Machines")
