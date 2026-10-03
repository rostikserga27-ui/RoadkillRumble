"""Rebuild just the demo from the finished, validated master assets."""
import bpy, runpy, json
from pathlib import Path
root=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(root/'ForestKit.blend'))
material=bpy.data.materials['M_ForestPalette']
ns=runpy.run_path(str(root/'build_forest_kit.py'))
ns['demo'].__globals__['SHARED']=material
ns['SharedPalette'].material.__globals__['SHARED']=material
records=json.loads((root/'manifest.json').read_text())
for r in records:
    r['root']=next(o for o in bpy.data.objects if o.type=='EMPTY' and o.get('asset_name')==r['name'])
    r['objects']=list(r['root'].children)
ns['ASSETS'].extend(records)
ns['demo'](bpy.context.scene.camera)
bpy.ops.wm.save_as_mainfile(filepath=str(root/'ForestDemo.blend'))
