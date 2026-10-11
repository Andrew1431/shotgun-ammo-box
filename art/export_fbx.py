# Headless: blender -b art/ammo_box.blend --python art/export_fbx.py
# Exports the AmmoBox mesh into the Unity project and renders thunderstore/icon.png.
import bpy, os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
FBX = os.path.join(ROOT, "unity", "ShotgunAmmoBoxAssets", "Assets", "AmmoBox", "AmmoBox.fbx")
ICON = os.path.join(ROOT, "thunderstore", "icon.png")

box = bpy.data.objects["AmmoBox"]
for o in bpy.context.view_layer.objects:
    o.select_set(o == box)
bpy.context.view_layer.objects.active = box
os.makedirs(os.path.dirname(FBX), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=FBX, use_selection=True, object_types={"MESH"},
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    mesh_smooth_type="FACE", use_mesh_modifiers=True)
d = box.dimensions
print(f"[box] exported {FBX} ({d.x:.3f} x {d.y:.3f} x {d.z:.3f} m)")

# Icon: three-quarter orthographic view on a transparent background.
scene = bpy.context.scene
lo = Vector(map(min, *[box.matrix_world @ Vector(c) for c in box.bound_box]))
hi = Vector(map(max, *[box.matrix_world @ Vector(c) for c in box.bound_box]))
centre = (lo + hi) / 2
cam_data = bpy.data.cameras.new("IconCam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = max(hi - lo) * 1.35
cam = bpy.data.objects.new("IconCam", cam_data)
scene.collection.objects.link(cam)
cam.location = centre + Vector((0.9, -1.3, 0.8)).normalized() * 2
cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.camera = cam
for name, loc, power in (("Key", (1.5, -2, 2.5), 250), ("Fill", (-2, -1, 1), 80), ("Rim", (0, 2, 2), 150)):
    l = bpy.data.lights.new(name, "AREA"); l.energy = power; l.size = 1.5
    o = bpy.data.objects.new(name, l); scene.collection.objects.link(o)
    o.location = centre + Vector(loc)
    o.rotation_euler = (centre - o.location).to_track_quat("-Z", "Y").to_euler()
scene.render.resolution_x = scene.render.resolution_y = 256
scene.render.film_transparent = True
scene.render.filepath = ICON
bpy.ops.render.render(write_still=True)
print("[box] rendered", ICON)
