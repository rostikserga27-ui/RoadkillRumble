"""Five orthographic views for every asset, tiled by category for geometry inspection."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
records=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ForestKit.blend'))
scene=bpy.context.scene; cam=scene.camera
scene.cycles.samples=8; scene.cycles.use_denoising=True
cats=list(dict.fromkeys(r['category'] for r in records))
for cat in cats: bpy.data.collections[cat].hide_render=True
review=bpy.data.collections['_Review']
views=[('front',0),('side',-90),('back',180),('three_quarter',-25),('top',0)]
for cat in cats:
    members=[r for r in records if r['category']==cat]; copies=[]
    for i,r in enumerate(members):
        root=next(o for o in bpy.data.objects if o.type=='EMPTY' and o.get('asset_name')==r['name']); scale=2.6/max(r['dimensions_m'])
        for orig in root.children:
            ob=orig.copy(); ob.data=orig.data; ob.parent=None; review.objects.link(ob)
            ob.location=(i*3.8,0,0); ob.scale=(scale,)*3; ob.hide_render=False; copies.append(ob)
    scene.render.resolution_x=len(members)*256; scene.render.resolution_y=300; scene.render.resolution_percentage=100
    cam.data.ortho_scale=len(members)*3.8
    for view,angle in views:
        for ob in copies: ob.rotation_euler.z=math.radians(angle)
        target=Vector(((len(members)-1)*3.8/2,0,0 if view=='top' else 1.3))
        offset=Vector((0,0,12)) if view=='top' else Vector((0,-12,7 if view=='three_quarter' else 0))
        cam.location=target+offset; cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
        folder=ROOT/'Previews/Inspection'; folder.mkdir(exist_ok=True)
        scene.render.filepath=str(folder/(cat+'_'+view+'.png')); bpy.ops.render.render(write_still=True)
    for ob in copies: bpy.data.objects.remove(ob,do_unlink=True)
    print('INSPECTION_DONE',cat,flush=True)
