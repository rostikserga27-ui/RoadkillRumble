"""Write stable Unity GUIDs, the shared material, and the human-readable kit manifest."""
import json, uuid, html
from pathlib import Path
ROOT=Path(__file__).resolve().parent
PROJECT=ROOT.parents[1]
OUT=PROJECT/'Assets/_Roadkill/Art/ForestKit'
records=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
ns=uuid.UUID('f3e4128c-7847-4bce-9f3f-cc3277a09a61')
def guid(path): return uuid.uuid5(ns,path.relative_to(PROJECT).as_posix()).hex
def meta(path,body=''):
    file=Path(str(path)+'.meta')
    if not file.exists(): file.write_text('fileFormatVersion: 2\nguid: '+guid(path)+'\n'+body,encoding='utf-8')
texture=OUT/'ForestPalette.png'; material=OUT/'M_ForestPalette.mat'
meta(texture,'TextureImporter:\n  serializedVersion: 13\n  sRGBTexture: 1\n  mipmaps:\n    enableMipMap: 0\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 0\n    wrapU: 1\n    wrapV: 1\n  textureCompression: 0\n  maxTextureSize: 256\n')
material.write_text('''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: M_ForestPalette
  m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}
  m_Parent: {fileID: 0}
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {}
  disabledShaderPasses: []
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _MainTex:
        m_Texture: {fileID: 2800000, guid: TEXTURE_GUID, type: 3}
        m_Scale: {x: 1, y: 1}
        m_Offset: {x: 0, y: 0}
    m_Ints: []
    m_Floats:
    - _Glossiness: 0
    - _Metallic: 0
    - _Mode: 0
    - _SrcBlend: 1
    - _DstBlend: 0
    - _ZWrite: 1
    - _SpecularHighlights: 0
    - _GlossyReflections: 0
    m_Colors:
    - _Color: {r: 1, g: 1, b: 1, a: 1}
    - _EmissionColor: {r: 0, g: 0, b: 0, a: 1}
  m_BuildTextureStacks: []
'''.replace('TEXTURE_GUID',guid(texture)),encoding='utf-8')
meta(material,'NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
for path in [OUT,*OUT.rglob('*')]:
    if path.suffix=='.meta': continue
    if path.is_dir(): meta(path,'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
    elif path.suffix.lower()=='.fbx':
        meta(path,'''ModelImporter:
  serializedVersion: 242
  internalIDToNameTable: []
  externalObjects:
  - first:
      type: UnityEngine:Material
      assembly: UnityEngine.CoreModule
      name: M_ForestPalette
    second: {fileID: 2100000, guid: MATERIAL_GUID, type: 2}
  materials:
    materialImportMode: 1
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    importAnimation: 0
  meshes:
    globalScale: 1
    useFileScale: 1
    addColliders: 0
    isReadable: 0
    importBlendShapes: 0
  tangentSpace:
    normalImportMode: 0
    tangentImportMode: 2
  animationType: 0
  userData:
  assetBundleName:
  assetBundleVariant:
'''.replace('MATERIAL_GUID',guid(material)))
script=PROJECT/'Assets/_Roadkill/Editor/ForestKitImport.cs'
meta(script,'MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
lines=['# Forest Environment Kit','',f'{len(records)} original models built in Blender 5.2.2 LTS using blender-lpm-skill.','',
 '**Material:** `M_ForestPalette`, matte, non-metallic. One shared 256×256 colour-swatch atlas; no image detail, normal maps or PBR maps.','',
 '**Scale:** metres; Blender Z up / front −Y; FBX exported −Z forward / Y up, FBX Units Scale, Apply Transform enabled. Each exported asset root is at (0,0,0), with its origin at bottom centre. Meshes have applied transforms. The master .blend lays assets out in category rows at real scale.','',
 '**Wind:** trunks and foliage are distinct child meshes, with the same origin. Ivy foliage is separate too. Wind shaders are not included. Closed foliage attachment shells meet stems; these interfaces are intentionally kept separate for wind rather than welding foliage into wood.','',
 '**QA:** every asset is triangulated and flat shaded. Geometry checks include non-manifold edges, duplicate vertices, zero-area faces and UVs. All 109 individual FBXs were round-trip imported into Blender and checked for triangle count, ground pivot, metres and orientation. See `manifest.json` and `FBX_Roundtrip_QA.json`.','',
 '**Unity validation limitation:** Unity 6000.6.4f1 currently refuses to start because its Editor licence is unavailable. FBX validation in Unity has not been claimed. Once activated, run Roadkill → Forest Kit → Validate Imported Assets.','',
 '## Files','',
 '- `ForestKit.blend`: editable master, named collections by category.','- `ForestDemo.blend`: small assembled forest, pond, campsite and dock.','- `../../Assets/_Roadkill/Art/ForestKit/<category>/*.fbx`: individual assets.','- `../../Assets/_Roadkill/Art/ForestKit/ForestKit_All.fbx`: all 109 assets arranged in category rows as a browsable library; use individual FBXs for placement.','- `../../Assets/_Roadkill/Art/ForestKit/Demo/ForestDemo.fbx`: the arranged demonstration, with demo-only ground and pond meshes.','- `Previews/*_row.png`: each category in a three-quarter row, sizes normalized for readability.','- `Previews/*_sheet.png`: the same renders with names, true dimensions and triangle counts.','- `Asset_Summary.csv`: full asset table.','- `build_forest_kit.py`: reproducible asset recipe.','',
 '## Unity setup','',
 '1. Open the project after activating the Unity Editor licence. The import helper preserves authored normals, disables unnecessary animation data and assigns the one shared material.','2. Drag individual FBXs from `Assets/_Roadkill/Art/ForestKit` into your scene at scale (1,1,1).','3. Use a CapsuleCollider on tree trunks, a BoxCollider on planks/crates, and simple primitive colliders for rocks and logs. Plants, flowers, ivy and mushrooms normally have no collider.','4. Keep scenery static. For a movable log or crate, add a Rigidbody and primitive or convex colliders.','5. For wind, modify only children named `_Foliage`; keep the same shared material unless a foliage shader is needed.','6. To view the demo in Unity, drag `Demo/ForestDemo.fbx` into an empty scene, add a camera and directional light.','',
 '**Not included:** optional LOD1 meshes. Trees already use 146–466 triangles and boulders use 56. No gameplay, networking or level scripts were replaced.','',
 '## Category previews','']
categories=list(dict.fromkeys(r['category'] for r in records))
for cat in categories:
    subset=[r for r in records if r['category']==cat]
    lines += [f'### {cat} — {len(subset)} assets', '', f'![{cat}](Previews/{cat}_sheet.png)','']
lines += ['## Full summary table','','| Asset name | Triangles | Material | Dimensions X × Y × Z (m) |','|---|---:|---|---|']
for r in records:
    lines.append(f'| {r["name"]} | {r["triangles"]} | {r["material"]} | '+ ' × '.join(f'{n:.3f}' for n in r['dimensions_m'])+' |')
(ROOT/'README.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
rows=''.join(f'<tr><td>{r["name"]}</td><td>{r["triangles"]}</td><td>{r["material"]}</td></tr>' for r in records)
cards=''.join(f'<h2>{cat}</h2><img src="Previews/{cat}_sheet.png">' for cat in categories)
(ROOT/'Gallery.html').write_text('<!doctype html><meta charset="utf-8"><title>Forest Kit</title><style>body{background:#292d2b;color:#e8dfc9;font:16px system-ui;max-width:1200px;margin:40px auto;padding:20px}img{width:100%;height:auto}td,th{padding:8px;text-align:left;border-bottom:1px solid #4f5146}table{width:100%;border-collapse:collapse}h1,h2{color:#c6a056}</style><h1>Forest Environment Kit — 109 assets</h1><img src="Previews/Palette.png"><img src="Previews/Demo_Forest.png">'+cards+'<h2>Summary</h2><table><tr><th>Asset</th><th>Triangles</th><th>Shared material</th></tr>'+rows+'</table>',encoding='utf-8')
print('Packaged',len(records),'assets; shared material and stable Unity GUIDs.')
