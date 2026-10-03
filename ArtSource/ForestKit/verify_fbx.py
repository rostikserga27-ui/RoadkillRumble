"""Round-trip every individual FBX through Blender; report scale, axes and triangle count."""
import bpy, json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent
out=root.parents[1]/'Assets/_Roadkill/Art/ForestKit'
records=json.loads((root/'manifest.json').read_text(encoding='utf-8'))
report=[]
for rec in records:
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=str(out/rec['category']/(rec['name']+'.fbx')),use_custom_normals=True)
    objects=[ob for ob in bpy.context.scene.objects if ob.type=='MESH']
    verts=[ob.matrix_world@v.co for ob in objects for v in ob.data.vertices]
    lo=Vector([min(v[i] for v in verts) for i in range(3)])
    hi=Vector([max(v[i] for v in verts) for i in range(3)])
    # Blender's FBX importer restores Z-up, metres, authored orientation.
    triangles=0
    for ob in objects:
        ob.data.calc_loop_triangles(); triangles+=len(ob.data.loop_triangles)
    dims=hi-lo; expected=Vector(rec['dimensions_m'])
    passed=(dims-expected).length<.005 and abs(lo.z)<.001 and triangles==rec['triangles']
    if not passed: raise RuntimeError(f'{rec["name"]}: round-trip failed {tuple(dims)}, {triangles}')
    report.append({'name':rec['name'],'pass':True,'triangles':triangles,'dimensions_m':list(dims),'ground_z':lo.z})
(root/'FBX_Roundtrip_QA.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('ROUNDTRIP_PASS',len(report),flush=True)
