"""RugWash Simulator — outillage Blender (V0.3.0).

Modélisation scriptée des modèles du jeu (outils, gants, pièces du personnage) et export vers Unity au format
Assets/RugWash/Models/<Nom>.json (lu par RugWash.Editor.RugWashModelLibrary). Chaque objet de la collection exportée
devient une pièce nommée (maillage triangulé, normales lissées/éclatées, UV en mètres par projection cubique).

Repère : on modélise dans le repère Unity via U(x, y, z) (x droite, y haut, z avant), converti en Blender (x, z, y) ;
l'export refait la conversion inverse (et inverse l'ordre des triangles, les deux repères n'ayant pas la même chiralité).

Lancement : blender -b --factory-startup --python Tools/Blender/build_models.py -- [noms...]
"""
import json
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODELS = os.path.join(PROJECT, "Assets", "RugWash", "Models")


def U(x, y=None, z=None):
    """Point du repère Unity -> Vector Blender."""
    if y is None:
        x, y, z = x
    return Vector((x, z, y))


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def collection(name):
    col = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if col.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(col)
    return col


def link(obj, col):
    for c in obj.users_collection:
        c.objects.unlink(obj)
    col.objects.link(obj)
    return obj


def mesh_object(name, bm, col):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    col.objects.link(obj)
    return obj


def smooth(obj, angle=40.0):
    """Lissage avec arêtes vives au-delà de l'angle (équivalent de l'ancien auto-smooth)."""
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.set_sharp_from_angle(angle=math.radians(angle))
    return obj


def bevel(obj, width, segments=3, angle=35.0, profile=0.5):
    m = obj.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle)
    m.profile = profile
    m.harden_normals = False
    return m


def subsurf(obj, levels=2):
    m = obj.modifiers.new("Subsurf", "SUBSURF")
    m.levels = levels
    m.render_levels = levels
    return m


def weighted_normals(obj):
    m = obj.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
    m.keep_sharp = True
    return m


def apply_all(obj):
    bpy.context.view_layer.objects.active = obj
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def remesh_smooth(obj, voxel=0.002, smooth_iters=6, decimate=None):
    """Fond plusieurs volumes en une surface organique (gant, poignées), puis lisse et allège."""
    m = obj.modifiers.new("Remesh", "REMESH")
    m.mode = "VOXEL"
    m.voxel_size = voxel
    m.use_smooth_shade = True
    if smooth_iters:
        s = obj.modifiers.new("Smooth", "SMOOTH")
        s.iterations = smooth_iters
        s.factor = 0.6
    if decimate:
        d = obj.modifiers.new("Decimate", "DECIMATE")
        d.ratio = decimate
    apply_all(obj)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def decimate_to(obj, target_tris):
    """Allège un maillage jusqu'à environ <target_tris> triangles (rien si déjà en dessous)."""
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris <= target_tris:
        return obj
    d = obj.modifiers.new("Decimate", "DECIMATE")
    d.ratio = target_tris / tris
    d.use_collapse_triangulate = True
    apply_all(obj)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    objs[0].name = name
    objs[0].data.name = name
    return objs[0]


# ---------------------------------------------------------------------------------------------------------------- Primitives

def box(name, col, size, center=(0, 0, 0), bevel_w=0.0, segments=3):
    """Boîte (taille et centre dans le repère Unity)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    sx, sy, sz = size
    bmesh.ops.scale(bm, vec=Vector((sx, sz, sy)), verts=bm.verts)
    bmesh.ops.translate(bm, vec=U(center), verts=bm.verts)
    obj = mesh_object(name, bm, col)
    if bevel_w > 0:
        bevel(obj, bevel_w, segments)
    return obj


def cylinder(name, col, radius, depth, center=(0, 0, 0), axis="y", segments=24, bevel_w=0.0, cap_segments=2):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius, radius2=radius, depth=depth)
    # create_cone : axe Z Blender = axe Y Unity.
    rot = {"y": Matrix.Identity(4), "z": Matrix.Rotation(math.radians(90), 4, "X"), "x": Matrix.Rotation(math.radians(90), 4, "Y")}[axis]
    bmesh.ops.transform(bm, matrix=rot, verts=bm.verts)
    bmesh.ops.translate(bm, vec=U(center), verts=bm.verts)
    obj = mesh_object(name, bm, col)
    if bevel_w > 0:
        bevel(obj, bevel_w, cap_segments, angle=50)
    return obj


def lathe(name, col, profile, segments=32, axis_center=(0, 0, 0), close_bottom=True, close_top=True):
    """Révolution autour de l'axe Y Unity : profil [(rayon, hauteur)] du bas vers le haut."""
    bm = bmesh.new()
    rings = []
    for (r, h) in profile:
        ring = []
        for s in range(segments):
            a = s / segments * math.tau
            ring.append(bm.verts.new(U(axis_center[0] + math.cos(a) * r, axis_center[1] + h, axis_center[2] + math.sin(a) * r)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for s in range(segments):
            a, b = rings[k][s], rings[k][(s + 1) % segments]
            c, d = rings[k + 1][(s + 1) % segments], rings[k + 1][s]
            bm.faces.new((a, d, c, b))
    if close_bottom and profile[0][0] > 1e-5:
        bm.faces.new(list(rings[0]))
    if close_top and profile[-1][0] > 1e-5:
        bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_object(name, bm, col)


def loft(name, col, sections, segments=40, exponent=2.6):
    """Volume par sections superelliptiques empilées : [(y, demi-largeur x, demi-profondeur z, décalage z)] (repère Unity)."""
    bm = bmesh.new()
    rings = []
    for (y, wx, wz, dz) in sections:
        ring = []
        for s in range(segments):
            a = s / segments * math.tau
            c, si = math.cos(a), math.sin(a)
            x = wx * math.copysign(abs(c) ** (2 / exponent), c)
            z = wz * math.copysign(abs(si) ** (2 / exponent), si) + dz
            ring.append(bm.verts.new(U(x, y, z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for s in range(segments):
            a, b = rings[k][s], rings[k][(s + 1) % segments]
            c, d = rings[k + 1][(s + 1) % segments], rings[k + 1][s]
            bm.faces.new((a, d, c, b))
    bm.faces.new(list(rings[0]))
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_object(name, bm, col)


def tube(name, col, points, radius, segments=12, caps=True, radii=None):
    """Tube le long d'une polyligne (points dans le repère Unity), rayon éventuellement variable."""
    pts = [U(p) for p in points]
    bm = bmesh.new()
    rings = []
    normal = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if normal is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            normal = t.cross(ref).normalized()
        normal = (normal - t * normal.dot(t)).normalized()
        binormal = t.cross(normal)
        r = radii[i] if radii else radius
        ring = [bm.verts.new(p + (normal * math.cos(a) + binormal * math.sin(a)) * r)
                for a in (s / segments * math.tau for s in range(segments))]
        rings.append(ring)
    for k in range(len(rings) - 1):
        for s in range(segments):
            bm.faces.new((rings[k][s], rings[k][(s + 1) % segments], rings[k + 1][(s + 1) % segments], rings[k + 1][s]))
    if caps:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(list(rings[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_object(name, bm, col)


def capsule(name, col, a, b, radius, segments=16):
    """Segment arrondi de a à b (repère Unity) : tube + deux sphères."""
    objs = [tube(name + "_t", col, [a, b], radius, segments, caps=True)]
    for i, p in enumerate((a, b)):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=max(6, segments // 2), radius=radius)
        bmesh.ops.translate(bm, vec=U(p), verts=bm.verts)
        objs.append(mesh_object(f"{name}_s{i}", bm, col))
    return join(objs, name)


def sphere(name, col, radius, center=(0, 0, 0), scale=(1, 1, 1), segments=24):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=segments // 2, radius=radius)
    bmesh.ops.scale(bm, vec=Vector((scale[0], scale[2], scale[1])), verts=bm.verts)
    bmesh.ops.translate(bm, vec=U(center), verts=bm.verts)
    return mesh_object(name, bm, col)


def arc_points(center, radius, a0, a1, steps, plane="xz", y=0.0):
    """Points d'un arc (degrés) dans le plan XZ (Unity) à la hauteur y."""
    out = []
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        if plane == "xz":
            out.append((center[0] + math.cos(a) * radius, y, center[2] + math.sin(a) * radius))
        else:  # plan YZ
            out.append((center[0], center[1] + math.cos(a) * radius, center[2] + math.sin(a) * radius))
    return out


def boolean(target, cutter, op="DIFFERENCE"):
    m = target.modifiers.new("Bool", "BOOLEAN")
    m.operation = op
    m.object = cutter
    m.solver = "EXACT"
    apply_all(target)
    bpy.data.objects.remove(cutter, do_unlink=True)
    return target


# ---------------------------------------------------------------------------------------------------------------- Export

def _evaluated_mesh(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return ev, me


def export(col_name, file_name, uv_scale=1.0, uv_mode="box", objects=None, extra=None, folder=None):
    """Écrit Assets/RugWash/Models/<file_name>.json : une pièce par objet de la collection (repère Unity).
    uv_mode « box » : projection cubique en mètres (micro-détails) ; « mesh » : UV du modèle (textures propres)."""
    col = bpy.data.collections[col_name] if col_name else None
    parts = []
    objs = objects if objects is not None else sorted(col.objects, key=lambda o: o.name)
    for obj in objs:
        if obj.type != "MESH":
            continue
        ev, me = _evaluated_mesh(obj)
        uv_layer = me.uv_layers.active.data if (uv_mode == "mesh" and me.uv_layers.active) else None
        world = obj.matrix_world
        normal_matrix = world.to_3x3().inverted().transposed()
        corner_normals = [c.vector for c in me.corner_normals] if hasattr(me, "corner_normals") else None
        verts, normals, uvs, tris = [], [], [], []
        index = {}
        for poly in me.polygons:
            fn = (normal_matrix @ poly.normal).normalized()
            # Projection cubique en mètres selon l'axe dominant de la face (repère Unity).
            ux, uy, uz = fn.x, fn.z, fn.y
            ax = max((abs(ux), 0), (abs(uy), 1), (abs(uz), 2))[1]
            face = []
            for li in poly.loop_indices:
                loop = me.loops[li]
                p = world @ me.vertices[loop.vertex_index].co
                n = (normal_matrix @ (corner_normals[li] if corner_normals else me.vertices[loop.vertex_index].normal)).normalized()
                px, py, pz = p.x, p.z, p.y
                nx, ny, nz = n.x, n.z, n.y
                if uv_layer is not None:
                    uv = tuple(uv_layer[li].uv)
                elif ax == 0:
                    uv = (pz * uv_scale, py * uv_scale)
                elif ax == 1:
                    uv = (px * uv_scale, pz * uv_scale)
                else:
                    uv = (px * uv_scale, py * uv_scale)
                key = (round(px, 5), round(py, 5), round(pz, 5), round(nx, 3), round(ny, 3), round(nz, 3), round(uv[0], 4), round(uv[1], 4))
                i = index.get(key)
                if i is None:
                    i = len(verts) // 3
                    index[key] = i
                    verts += [px, py, pz]
                    normals += [nx, ny, nz]
                    uvs += [uv[0], uv[1]]
                face.append(i)
            # Chiralité inversée (Blender main droite -> Unity main gauche) : ordre des sommets retourné.
            tris += [face[0], face[2], face[1]]
        ev.to_mesh_clear()
        part = {"name": obj.name, "v": [round(x, 6) for x in verts], "n": [round(x, 4) for x in normals],
                "uv": [round(x, 5) for x in uvs], "t": tris}
        if extra and obj.name in extra:
            part.update(extra[obj.name])
        parts.append(part)
        print(f"[rw] {file_name}/{obj.name}: {len(verts) // 3} sommets, {len(tris) // 3} triangles")
    out_dir = folder or MODELS
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, file_name + ".json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"source": "Tools/Blender (V0.3.0)", "parts": parts}, f, separators=(",", ":"))
    print(f"[rw] écrit {path}")
    return path


def render_preview(col_name, path, views=((1.2, -1.0, 0.7),), size=640, distance=None):
    """Aperçu Workbench (contrôle visuel du modèle, hors jeu)."""
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    col = bpy.data.collections[col_name]
    objs = [o for o in col.objects if o.type == "MESH"]
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    center = (lo + hi) / 2
    radius = (hi - lo).length / 2
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 60
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    outs = []
    for i, v in enumerate(views):
        d = Vector(v).normalized() * (distance or radius * 3.4)
        cam.location = center + d
        cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
        out = path.replace(".png", f"_{i}.png")
        scene.render.filepath = out
        bpy.ops.render.render(write_still=True)
        outs.append(out)
    bpy.data.objects.remove(cam, do_unlink=True)
    return outs


def color(obj, rgba):
    obj.color = rgba
    return obj
