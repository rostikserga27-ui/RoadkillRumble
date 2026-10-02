"""pose_test.py - deform check: pose the Humanoid rig hard (raised/bent arm, lifted knee, twist, head turn) and render."""
import math, sys
import bpy
argv = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=argv[0])
rig = bpy.data.objects["Fisherman"]
pb = rig.pose.bones
def r(name, x=0, y=0, z=0):
    b = pb[name]; b.rotation_mode = "XYZ"; b.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
r("LeftUpperArm", 0, 0, 0); pb["LeftUpperArm"].rotation_mode = "XYZ"
pb["LeftUpperArm"].rotation_euler = (0, math.radians(-50), 0)
r("LeftLowerArm", 70, 0, 0)
r("RightUpperArm", 60, 0, 0)
r("RightLowerArm", 50, 0, 0)
r("LeftUpperLeg", -55, 0, 0)
r("LeftLowerLeg", 80, 0, 0)
r("LeftFoot", -20, 0, 0)
r("Spine", 0, 12, 0); r("Chest", 8, 8, 0)
r("Head", -10, 25, 0)
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=argv[1])
