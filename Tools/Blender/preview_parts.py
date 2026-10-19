"""Aperçu Workbench de pièces modélisées : blender -b --factory-startup --python Tools/Blender/preview_parts.py -- <module> <pièce...>
Écrit %TEMP%/each_<pièce>_<0|1>.png (deux angles)."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
from rw_lib import *
mod = __import__(sys.argv[sys.argv.index("--") + 1])
names = sys.argv[sys.argv.index("--") + 2:]
reset()
col = mod.build()
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = scene.render.resolution_y = 360
scene.display.shading.light = "STUDIO"
scene.display.shading.show_cavity = True
cam = bpy.data.objects.new("C", bpy.data.cameras.new("C")); scene.collection.objects.link(cam); scene.camera = cam
cam.data.lens = 70
for o in col.objects: color(o, (0.75, 0.55, 0.25, 1))
for name in names:
    for o in col.objects: o.hide_render = (o.name != name)
    o = bpy.data.objects[name]
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    c = sum(pts, Vector()) / 8; r = max((p - c).length for p in pts)
    for i, v in enumerate(((1.0, -1.2, 0.8), (-1.1, 0.6, 0.5))):
        cam.location = c + Vector(v).normalized() * r * 4.2
        cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(os.environ["TEMP"], f"each_{name}_{i}.png")
        bpy.ops.render.render(write_still=True)
