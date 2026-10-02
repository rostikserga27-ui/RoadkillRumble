"""forest_render.py - contrast check: the character on grass in front of low-poly pines, mid-distance game camera.
  bl.py --script forest_render.py -- --input SK_Fisherman.blend --out forest.png [--dist 4.2] [--az -30]"""
import math
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d: argv[argv.index(k) + 1] if k in argv else d
bpy.ops.wm.open_mainfile(filepath=arg("--input", ""))
OUT, DIST, AZ = arg("--out", "forest.png"), float(arg("--dist", "4.2")), float(arg("--az", "-30"))
sc = bpy.context.scene


def mat(name, hexc):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    h = hexc.lstrip("#"); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    bsdf.inputs["Base Color"].default_value = [(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c] + [1]
    bsdf.inputs["Roughness"].default_value = 0.95
    return m


def add(ob, m):
    sc.collection.objects.link(ob); ob.data.materials.append(m)
    for p in ob.data.polygons: p.use_smooth = False


grass, pine, pine2, trunk = mat("grass", "#5e7d3a"), mat("pine", "#2f5233"), mat("pine2", "#3d6b3c"), mat("trunk", "#5b4030")
bpy.ops.mesh.primitive_plane_add(size=60); g = bpy.context.active_object; g.data.materials.append(grass)
import random
random.seed(4)
for i in range(26):
    a = random.uniform(-2.4, 0.9); r = random.uniform(3.5, 11)
    x, y = r * math.cos(a + math.pi / 2), r * math.sin(a + math.pi / 2) + 1.5
    h = random.uniform(3.5, 7)
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.18, depth=h * 0.4, location=(x, y, h * 0.2))
    t = bpy.context.active_object; t.data.materials.append(trunk)
    for k in range(3):
        bpy.ops.mesh.primitive_cone_add(vertices=7, radius1=h * (0.30 - 0.06 * k), depth=h * 0.45, location=(x, y, h * (0.42 + 0.2 * k)))
        c = bpy.context.active_object; c.data.materials.append(pine if (i + k) % 2 else pine2)
w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs["Color"].default_value = (0.55, 0.72, 0.85, 1); bg.inputs["Strength"].default_value = 0.9
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 3.2; sun.data.angle = math.radians(4); sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
look = Vector((0, 0, 0.95))
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam)
cam.data.lens = 40
d = Vector((math.cos(math.radians(AZ - 90)), math.sin(math.radians(AZ - 90)), 0.32)).normalized()
cam.location = look + d * DIST
cam.rotation_euler = (look - cam.location).to_track_quat("-Z", "Y").to_euler()
sc.camera = cam
try:
    sc.render.engine = "BLENDER_EEVEE"
except TypeError:
    pass
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x, sc.render.resolution_y = 1280, 900
sc.render.film_transparent = False
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("##JSON##{\"out\": \"%s\"}" % OUT)
