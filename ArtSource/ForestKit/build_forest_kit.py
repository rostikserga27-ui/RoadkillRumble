"""Reproducible, metre-scaled forest kit. Run inside Blender 5.2 with --background.
Uses blender-lpm-skill primitives, face palette and finishing. No generated art.
All geometry is triangulated; intersecting rigid parts are united before export.
"""
import bpy, bmesh, math, random, sys, json, csv
from pathlib import Path
from mathutils import Vector, Matrix

SKILL = Path.home() / '.codex/skills/blender-lpm-skill/scripts'
sys.path.insert(0, str(SKILL))
import lpm

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
OUT = PROJECT / 'Assets/_Roadkill/Art/ForestKit'
PREVIEW = ROOT / 'Previews'
for p in (OUT, PREVIEW): p.mkdir(parents=True, exist_ok=True)
random.seed(20261003)
ENTRIES = [
 ('Bark','#75513D'), ('BarkDark','#4F3930'), ('CutWood','#BD9566'), ('WoodLight','#D1B184'),
 ('Moss','#788455'), ('MossLight','#A0A369'), ('LeafOlive','#74824D'), ('LeafSage','#91A16B'),
 ('LeafDeep','#465E45'), ('LeafWarm','#A6A261'), ('Pine','#405C50'), ('PineLight','#627866'),
 ('Teal','#365A5B'), ('Charcoal','#343937'), ('Birch','#D2C9AD'), ('BirchMark','#57534A'),
 ('Rock','#85847A'), ('RockLight','#A6A294'), ('RockDark','#626C68'), ('Ochre','#C6A056'),
 ('Terracotta','#B86D50'), ('Rust','#8E5141'), ('Cream','#E5D5AD'), ('PetalWhite','#E8DFC9'),
 ('PetalPink','#C58C85'), ('PetalPurple','#9690A5'), ('PetalBlue','#7D9CA5'), ('Berry','#A65856'),
 ('Water','#567E82'), ('Canvas','#BCAB81'), ('Metal','#667478'), ('Ember','#DD9A54')]

class SharedPalette(lpm.Palette):
    def material(self, name):
        return SHARED

# Use the skill palette convention, but only a BaseColor swatch image: no PBR maps.
# Padding to 8x8 cells gives a single 256x256 atlas shared by every asset.
pad = ENTRIES + [('Unused%02d'%i, '#343937') for i in range(32)]
P = SharedPalette([(n, h, 0, 0.95) for n,h in pad], cell=32)
SHARED = lpm.Palette.material(P, 'ForestPalette')
SHARED.name = 'M_ForestPalette'
nt = SHARED.node_tree
bs = nt.nodes.get('Principled BSDF')
for sock in ('Metallic','Roughness'):
    for link in list(bs.inputs[sock].links): nt.links.remove(link)
bs.inputs['Metallic'].default_value = 0
bs.inputs['Roughness'].default_value = 0.95
for node in list(nt.nodes):
    if node.type not in ('OUTPUT_MATERIAL','BSDF_PRINCIPLED','TEX_IMAGE'): nt.nodes.remove(node)
    elif node.type == 'TEX_IMAGE' and node.image.name.endswith('MaskMap'): nt.nodes.remove(node)
palette_image = bpy.data.images[SHARED['lpm_base_image']]
palette_image.filepath_raw = str(OUT / 'ForestPalette.png')
palette_image.file_format = 'PNG'; palette_image.save()
SHARED['palette_definitions'] = json.dumps(dict(ENTRIES))
ASSETS=[]; CATEGORIES=['Trees','Fallen_Wood','Rocks','Plants','Flowers','Mushrooms','Extras']

def collection(name):
    c=bpy.data.collections.get(name)
    if not c:
        c=bpy.data.collections.new(name); bpy.context.scene.collection.children.link(c)
    return c

def mesh(name, verts, faces, color):
    return lpm._object(name, lpm._bm_from(verts, faces), P[color])

def ellipsoid(name, pos, size, color, sides=7, rings=3, seed=0):
    """Closed faceted ellipsoid: poles and staggered rings, no subdivided sphere."""
    rng=random.Random(seed)
    x,y,z=pos; sx,sy,sz=size; verts=[(x,y,z-sz)]
    for j in range(1,rings+1):
        a=math.pi*j/(rings+1)
        for i in range(sides):
            t=2*math.pi*i/sides + (j%2)*0.12
            f=1+rng.uniform(-0.075,0.075)
            verts.append((x+math.sin(a)*sx*math.cos(t)*f,y+math.sin(a)*sy*math.sin(t)*f,z-math.cos(a)*sz))
    top=len(verts); verts.append((x,y,z+sz)); faces=[]
    for i in range(sides): faces.append((0,1+(i+1)%sides,1+i))
    for j in range(rings-1):
        a=1+j*sides; b=a+sides
        for i in range(sides): faces.append((a+i,a+(i+1)%sides,b+(i+1)%sides,b+i))
    for i in range(sides): faces.append((top,1+(rings-1)*sides+i,1+(rings-1)*sides+(i+1)%sides))
    ob=mesh(name,verts,faces,color)
    # Sparse face colours add facets without texture detail.
    choices={'LeafOlive':'LeafSage','LeafSage':'LeafWarm','Pine':'PineLight','Rock':'RockLight'}
    if color in choices:
        attr=ob.data.attributes['lpm_color']
        for poly in ob.data.polygons:
            if poly.normal.z>0.25 and rng.random()<0.23: attr.data[poly.index].value=P[choices[color]]
    return ob

def tube(name,a,b,r,color='Bark',sides=6,r2=None):
    a,b=Vector(a),Vector(b); d=b-a
    ob=lpm.prism(name,sides,r,d.length,color=P[color],radius_top=r if r2 is None else r2)
    ob.data.transform(Matrix.Translation(a) @ d.to_track_quat('Z','Y').to_matrix().to_4x4())
    return ob

def box(name,size,pos,color): return lpm.box(name,size,at=pos,color=P[color])

def leaf(name,a,b,w,color='LeafSage'):
    """Solid folded diamond, 8 triangles: never a zero-thickness double-sided plane."""
    a,b=Vector(a),Vector(b); d=(b-a); cross=d.cross(Vector((0,0,1)))
    if cross.length<0.001: cross=Vector((1,0,0))
    cross.normalize(); mid=(a+b)*0.5; thickness=min(w*0.12,0.025)
    v=[a,mid+cross*w,b,mid-cross*w,mid+Vector((0,0,thickness)),mid-Vector((0,0,thickness))]
    f=[(4,i,(i+1)%4) for i in range(4)]+[(5,(i+1)%4,i) for i in range(4)]
    return mesh(name,v,f,color)

def union(parts):
    """Remove unseen interior surfaces, retaining lpm_color face IDs."""
    ob=parts[0]
    for other in parts[1:]:
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
        mod=ob.modifiers.new('UnionClean','BOOLEAN'); mod.operation='UNION'; mod.solver='MANIFOLD'; mod.object=other
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(other,do_unlink=True)
    return ob

def clean(ob):
    bm=bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm,verts=bm.verts,dist=0.00001)
    bmesh.ops.dissolve_degenerate(bm,edges=bm.edges,dist=0.0000001)
    bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    for p in ob.data.polygons: p.use_smooth=False

def finish(name,groups,category,budget):
    """One root at ground centre; tree foliage remains a separate child mesh."""
    objects=[]
    for suffix,parts,fuse in groups:
        if fuse and len(parts)>1: parts=[union(parts)]
        ob=lpm.finish(name+(suffix or '_Mesh'),parts,P,ground=False,center=False)
        clean(ob); objects.append(ob)
    verts=[v.co for ob in objects for v in ob.data.vertices]
    offset=Vector(((min(v.x for v in verts)+max(v.x for v in verts))/2,
                   (min(v.y for v in verts)+max(v.y for v in verts))/2,min(v.z for v in verts)))
    for ob in objects: ob.data.transform(Matrix.Translation(-offset)); ob.data.update()
    root=bpy.data.objects.new(name,None); collection(category).objects.link(root)
    for ob in objects:
        for c in list(ob.users_collection): c.objects.unlink(ob)
        collection(category).objects.link(ob); ob.parent=root
    root['asset_name']=name; root['category']=category; root['budget']=budget; root['units']='metres'; root['front']='-Y'
    tris=sum(lpm.tri_count(ob) for ob in objects)
    if tris>budget: raise RuntimeError(f'{name}: {tris}>{budget}; reduce construction complexity')
    dims=[max(v[i] for v in verts)-min(v[i] for v in verts) for i in range(3)]
    rec={'name':name,'category':category,'triangles':tris,'budget':budget,'dimensions_m':[round(n,3) for n in dims],
         'material':SHARED.name,'root':root,'objects':objects}
    ASSETS.append(rec)
    print('ASSET '+json.dumps({k:v for k,v in rec.items() if k not in ('root','objects')}),flush=True)
    return rec

def tree(kind,v):
    rng=random.Random(100*v+len(kind)); s=[0.8,1,1.2,0.92][v-1]
    parts=[]; crown=[]
    if kind=='Oak':
        h=5.8*s; r=0.28*s
        parts=[tube('trunk',(0,0,0),(0.13*s,0,h*.7),r,r2=r*.45,sides=8)]
        for j in range(4):
            a=j*1.65+v*.3; end=(math.cos(a)*1.05*s,math.sin(a)*1.05*s,h*.67)
            parts.append(tube('bough',(0.06,0,h*.34),end,r*.42,r2=r*.13))
        crown=[ellipsoid('crown',(0,0,h*.73),(1.45*s,1.20*s,1.55*s),'LeafOlive',9,5,v)]
        for j in range(2 if v!=3 else 3):
            a=j*2.7+v*.9
            crown.append(ellipsoid('lobe',(math.cos(a)*.95*s,math.sin(a)*.65*s,h*(.66+.03*j)),(.95*s,.85*s,1.0*s),'LeafOlive',7,3,v*10+j))
        # Branch crowns subtly change shape, not only scale.
        for vt in crown[0].data.vertices:
            vt.co.x += math.sin(vt.co.z*1.2+v)*0.18*s
    elif kind=='Pine':
        h=[6,7.5,9.2,5][v-1]; parts=[tube('trunk',(0,0,0),(0,0,h*.95),.21*s,r2=.045,sides=8)]
        profile=[(0,h*.12),(1.35*s,h*.12),(.52*s,h*.32),(1.05*s,h*.32),(.34*s,h*.51),(.78*s,h*.51),(.20*s,h*.69),(.53*s,h*.69),(0,h)]
        crown=[lpm.lathe('foliage',profile,segments=9,color=P['Pine'])]
        lpm.paint(crown[0],P['PineLight'],where=lambda c,n,i:n.z>0.35 and i%4==0)
    elif kind=='Birch':
        h=[5,6.5,7.6,4.3][v-1]; parts=[tube('trunk',(0,0,0),(.35*s,0,h*.88),.135*s,'Birch',8,.055)]
        for j in range(3):
            a=j*2.1+v*.2
            parts.append(tube('branch',(.2,0,h*.45),(math.cos(a)*.85*s,math.sin(a)*.7*s,h*.75),.065*s,'Birch',5,.025))
        crown=[ellipsoid('foliage',(.25,0,h*.78),(.97*s,.80*s,h*.24),'LeafSage',9,7,v)]
        for ob in parts:
            attr=ob.data.attributes['lpm_color']
            for p in ob.data.polygons:
                if p.index%4==1: attr.data[p.index].value=P['BirchMark']
    elif kind=='Dead':
        h=[4,5.3,6.8,3.7][v-1]; parts=[tube('trunk',(0,0,0),(.3,0,h),.28*s,'BarkDark',7,.075)]
        for j in range(5):
            a=j*2.4; start=(.14,0,h*(.26+.1*j)); end=(math.cos(a)*(1+j*.05)*s,math.sin(a)*.8*s,start[2]+.65*s)
            parts.append(tube('branch',start,end,.12*s,'BarkDark',5,.035))
            parts.append(tube('twig',end,(end[0]+.2*s,end[1],end[2]+.6*s),.037*s,'BarkDark',5,.01))
    elif kind=='Sapling':
        h=[3,3.6,4,3.3][v-1]; parts=[tube('trunk',(0,0,0),(.1,0,h*.80),.08,'Bark',6,.025)]
        crown=[ellipsoid('foliage',(0,0,h*.73),(.65*s,.55*s,h*.30),'LeafSage',9,7,v)]
    groups=[('_Trunk',parts,True)]
    if crown: groups.append(('_Foliage',crown,True))
    return finish(f'Tree_{kind}_{v:02}',groups,'Trees',1200)

def stump(v):
    r=[.32,.42,.5,.28][v-1]; h=[.45,.65,.72,.35][v-1]
    body=lpm.prism('stump',8,r,h,color=P['Bark'],radius_top=r*.82)
    lpm.paint(body,P['CutWood'],where=lambda c,n,i:n.z>.8)
    parts=[body]
    for j in range(4):
        a=j*math.pi/2+.3; parts.append(tube('root',(0,0,.18),(math.cos(a)*r*1.6,math.sin(a)*r*1.6,.08),.13,'Bark',5,.04))
    return finish(f'Tree_Stump_{v:02}',[('',parts,True)],'Trees',300)

def log(kind,v):
    length=[2.4,3.2,1.8][v-1]; r=.22+.04*v
    if kind=='Hollow':
        # Closed annular tube: visible bore, connected rim, no hidden end caps.
        verts=[]; faces=[]; n=8
        for x,rad in [(-length/2,r),(length/2,r),(-length/2,r*.65),(length/2,r*.65)]:
            verts.extend((x,math.cos(i*2*math.pi/n)*rad,r+math.sin(i*2*math.pi/n)*rad) for i in range(n))
        for i in range(n):
            j=(i+1)%n
            faces += [(i,j,n+j,n+i),(2*n+i,3*n+i,3*n+j,2*n+j),(i,2*n+i,2*n+j,j),(n+i,n+j,3*n+j,3*n+i)]
        ob=mesh('hollow',verts,faces,'Bark'); attr=ob.data.attributes['lpm_color']
        for p in ob.data.polygons:
            if abs(p.normal.x)>.7: attr.data[p.index].value=P['CutWood']
        parts=[ob]
    else:
        ob=tube('log',(-length/2,0,r),(length/2,0,r),r,'Bark',8)
        attr=ob.data.attributes['lpm_color']
        for p in ob.data.polygons:
            if abs(p.normal.x)>.7: attr.data[p.index].value=P['CutWood']
            elif kind=='Mossy' and p.normal.z>.5: attr.data[p.index].value=P['Moss']
        if kind=='Broken':
            for vert in ob.data.vertices:
                if vert.co.x>length*.4: vert.co.x+=random.uniform(-.23,.16)
        parts=[ob,tube('branch',(.1,0,r),(0,.48,r+.23),.085,'Bark',5,.03)]
    return finish(f'Log_{kind}_{v:02}',[('',parts,True)],'Fallen_Wood',300)

def branches(kind,v):
    parts=[]; n=1 if kind in ('Stick','Branch') else 4
    for j in range(n):
        a=j*1.7+v*.4; length=.55+.20*v+j*.06; y=(j-(n-1)/2)*.11
        ob=tube('stick',(-length/2,y,.035),(length/2,y+.1*math.sin(a),.07),.035,'BarkDark',5,.022)
        lpm.rotate(ob,math.degrees(a)*.3,axis='Z'); parts.append(ob)
        if kind=='Branch': parts.append(tube('fork',(.035,y,.062),(.16,y+.24,.12),.026,'BarkDark',5,.011))
    return finish(f'Wood_{kind}_{v:02}',[('',parts,True)],'Fallen_Wood',300)

def uprooted():
    parts=[tube('trunk',(-1.7,0,.4),(1.4,0,.6),.32,'Bark',8,.21)]
    for j in range(6):
        a=j*math.tau/6
        parts.append(tube('root',(-1.6,0,.4),(-1.85,math.cos(a)*.7,.45+math.sin(a)*.5),.13,'BarkDark',5,.02))
    return finish('Wood_Uprooted_01',[('',parts,True)],'Fallen_Wood',300)

def rock(kind,v,moss=False):
    sizes={'Pebble':(.17,.14,.12),'Medium':(.55,.43,.42),'Boulder':(1.2,.95,1.05),'Flat':(.75,.58,.16),'Cluster':(.38,.30,.33)}
    sx,sy,sz=sizes[kind]; fac=[.8,1,1.18][v-1]; parts=[]
    n=3 if kind=='Cluster' else 1
    for j in range(n):
        parts.append(ellipsoid('stone',(j*sx*.95,math.sin(j)*sy*.5,sz*fac), (sx*fac,sy*fac,sz*fac), 'Rock',7,4 if kind=='Boulder' else 3,100*v+j))
        if moss: lpm.paint(parts[-1],P['Moss'],where=lambda c,n,i:n.z>.30)
    return finish(f'Rock_{"Mossy" if moss else ""}{kind}_{v:02}',[('',parts,True)],'Rocks',400 if kind!='Pebble' else 150)

def bush(kind,v):
    s=[.8,1,1.18][v-1]; parts=[ellipsoid('bush',(0,0,.52*s),(.6*s,.5*s,.52*s),'LeafOlive',8,4,v)]
    if kind=='Leafy':
        for j in range(4):
            a=j*1.5; parts.append(leaf('leaf',(.1*math.cos(a),.1*math.sin(a),.4*s),(.65*s*math.cos(a),.65*s*math.sin(a),.82*s),.16*s))
    if kind=='Berry':
        for j in range(6):
            a=j*2.2; parts.append(ellipsoid('berry',(.42*math.cos(a)*s,.4*math.sin(a)*s,(.55+.1*(j%2))*s),(.065,.065,.065),'Berry',5,1,j))
    return finish(f'Bush_{kind}_{v:02}',[('',parts,True)],'Plants',300)

def fern(v):
    parts=[]; s=.7+.15*v
    for j in range(3):
        a=j*2.1+.4
        tip=Vector((.5*math.cos(a)*s,.5*math.sin(a)*s,.58*s)); start=Vector((0,0,0))
        parts.append(tube('frond',start,tip,.015,'LeafDeep',4,.004))
        for k in range(1,5):
            base=tip*k/5; cross=Vector((-math.sin(a),math.cos(a),.15))
            for side in (-1,1):
                parts.append(leaf('pinna',base,base+cross*side*(.18-k*.024)*s,.033*s,'LeafSage'))
    return finish(f'Plant_Fern_{v:02}',[('',parts,False)],'Plants',300)

def grass(kind,v):
    parts=[]; rng=random.Random(400+v); n=14 if kind=='ShortGrass' else 12
    for j in range(n):
        x=rng.uniform(-.28,.28); y=rng.uniform(-.24,.24); h=rng.uniform(.18,.38) if kind=='ShortGrass' else rng.uniform(.55,1.1)
        parts.append(leaf('blade',(x,y,0),(x+rng.uniform(-.15,.15),y+.05,h),.028 if kind=='ShortGrass' else .04,'LeafSage' if j%3 else 'Ochre'))
    return finish(f'Plant_{kind}_{v:02}',[('',parts,False)],'Plants',300)

def reeds(v):
    parts=[]
    for j in range(4):
        x=(j-1.5)*.12; y=.10*math.sin(j); h=.8+j*.12+v*.08
        parts.append(tube('stem',(x,y,0),(x+.04,y,h),.016,'LeafDeep',5))
        parts.append(tube('cattail',(x+.035,y,h-.19),(x+.04,y,h+.05),.042,'BarkDark',6))
        parts.append(leaf('reed',(x,y,.15),(x+.24,y-.04,h*.72),.035,'LeafSage'))
    return finish(f'Plant_Cattail_{v:02}',[('',parts,True)],'Plants',300)

def ivy(v):
    parts=[tube('vine',(0,0,0),(.06,0,1.3),.018,'LeafDeep',5)]
    for j in range(7):
        z=.07+j*.18; side=(-1)**j
        parts.append(leaf('ivy',(-.04*side,.003,z),(.20*side,-.055,z+.10),.065,'LeafOlive'))
    # Leaf blades remain a separate wind-ready surface, like the tree crowns.
    return finish(f'Plant_Ivy_{v:02}',[('_Stem',[parts[0]],False),('_Foliage',parts[1:],False)],'Plants',300)

def flower(kind,cluster=False):
    colors={'Daisy':'PetalWhite','Tulip':'Terracotta','WildPink':'PetalPink','Bluebell':'PetalBlue','Buttercup':'Ochre'}
    parts=[]; count=2 if cluster else 1
    for j in range(count):
        x=(j-(count-1)/2)*.14; y=.08*math.sin(j*3); h=.28+j*.04
        if kind in ('Tulip','Bluebell'):
            profile=[(0,0),(.009,0),(.009,h-.02),(.06,h-.015),(.065,h+.06),(.047,h+.08),(0,h+.025)]
            cap=lpm.lathe('flower',profile,segments=5 if cluster else 7,at=(x,y,0),color=P[colors[kind]])
            lpm.paint(cap,P['LeafDeep'],where=lambda c,n,i:c.z<h-.025)
            parts.append(cap)
        else:
            # One watertight stem + scalloped bloom, no intersecting petal fans.
            n=8 if cluster else 10; verts=[]; faces=[]
            for ring,(rad,z) in enumerate(((.009,0),(.009,h-.015),(.09,h),(.024,h+.014))):
                for k in range(n):
                    a=k*math.tau/n; rr=rad*(.38 if ring==2 and k%2 else 1)
                    verts.append((x+math.cos(a)*rr,y+math.sin(a)*rr,z))
            faces.append(tuple(range(n-1,-1,-1)))
            for ring in range(3):
                for k in range(n): faces.append((ring*n+k,ring*n+(k+1)%n,(ring+1)*n+(k+1)%n,(ring+1)*n+k))
            faces.append(tuple(range(3*n,4*n)))
            ob=mesh('bloom',verts,faces,colors[kind]); attr=ob.data.attributes['lpm_color']
            for p in ob.data.polygons:
                if p.index<=n: attr.data[p.index].value=P['LeafDeep']
                elif p.index==len(ob.data.polygons)-1: attr.data[p.index].value=P['Ochre']
            parts.append(ob)
    # Clusters have fewer facets, keeping even three stems at <=150 tris.
    return finish(f'Flower_{kind}_{"Cluster" if cluster else "Single"}_01',[('',parts,True)],'Flowers',150)

def mushroom(kind,v):
    parts=[]; n=3 if kind=='Cluster' else 1; r=.065 if kind=='Small' else .20
    if kind=='Cluster': r=.075
    for j in range(n):
        x=(j-(n-1)/2)*r*2.5; y=.09*math.sin(j*2); h=r*(1.7+j*.15)
        parts.append(lpm.prism('stem',3 if kind=='Cluster' else 5,r*.30,h,at=(x,y,0),color=P['Cream'],radius_top=r*.22))
        # The underside has a stem hole, so there are no hidden faces inside the cap.
        profile=[(r*.22,h*.77),(r,h*.9),(r*.65,h*1.30),(0,h*1.46)]
        parts.append(lpm.lathe('cap',profile,segments=5 if kind=='Cluster' else 6,at=(x,y,0),color=P[['Terracotta','Ochre','Teal'][v-1]]))
    return finish(f'Mushroom_{kind}_{v:02}',[('',parts,True)],'Mushrooms',150)

def extra(kind,v=1):
    parts=[]
    if kind=='LilyPad':
        # Connected wedge-shaped disk with a triangular notch; solid thickness.
        a=[math.radians(28)+j*math.radians(304)/8 for j in range(9)]
        outline=[(0,0)]+[(math.cos(t)*.28,math.sin(t)*.28) for t in a]
        verts=[(x,y,z) for z in (0,.022) for x,y in outline]; n=len(outline)
        faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        parts=[mesh('pad',verts,faces,'LeafOlive')]
    elif kind=='LeafPile':
        for j in range(12):
            a=j*2.4; r=.05+j*.023
            parts.append(leaf('fallen',(math.cos(a)*r,math.sin(a)*r,.012), (math.cos(a)*r+.15*math.cos(a+.8),math.sin(a)*r+.15*math.sin(a+.8),.028),.055,['Ochre','Terracotta','LeafWarm'][j%3]))
    elif kind in ('Bridge','Dock'):
        width=1.25 if kind=='Bridge' else 1.65; length=3 if kind=='Bridge' else 2.7
        for j in range(9):
            parts.append(box('plank',(width,.29,.11),(0,-length/2+(j+.5)*length/9,.38),'CutWood' if j%3 else 'WoodLight'))
        for x in (-width*.36,width*.36):
            parts.append(box('beam',(.15,length,.16),(x,0,.22),'Bark'))
            for y in (-length*.42,length*.42): parts.append(tube('post',(x,y,0),(x,y,.48),.07,'Bark',6))
    elif kind=='Planks':
        for j in range(3):
            ob=box('plank',(1.3,.20,.08),(0,(j-1)*.24,0),'CutWood'); lpm.rotate(ob,[-6,4,-2][j]); parts.append(ob)
    elif kind=='Fence':
        for x in (-.85,.85): parts.append(box('post',(.14,.14,1.1),(x,0,0),'Bark'))
        for z in (.35,.80): parts.append(box('rail',(1.95,.10,.13),(0,-.12,z),'CutWood'))
    elif kind=='Signpost':
        parts=[box('post',(.14,.14,1.6),(0,0,0),'Bark'),box('sign',(1,.14,.32),(0,-.10,1.15),'CutWood')]
        parts.append(leaf('arrow',(-.25,-.185,1.25),(.30,-.185,1.25),.10,'Charcoal'))
    elif kind=='Campfire':
        for j in range(8):
            a=j*math.tau/8; parts.append(ellipsoid('stone',(.46*math.cos(a),.46*math.sin(a),.10),(.14,.13,.1),'Rock',5,2,j))
        for j in range(3):
            ob=tube('wood',(-.3,0,.08),(.3,0,.08),.075,'Bark',6); lpm.rotate(ob,j*60); parts.append(ob)
    elif kind=='Tent':
        # Solid pitched canvas shell with an open doorway, 5 mm wall thickness.
        # Front supports coloured separately; no sealed hidden tent-volume.
        parts=[mesh('canvas',[(-.9,-1.1,0),(0,-1.1,1.25),(.9,-1.1,0),(-.9,1.1,0),(0,1.1,1.25),(.9,1.1,0)],[(0,3,4,1),(1,4,5,2),(3,5,4)],'Canvas')]
        ob=parts[0]; bpy.context.view_layer.objects.active=ob; ob.select_set(True)
        mod=ob.modifiers.new('CanvasThickness','SOLIDIFY'); mod.thickness=.012; bpy.ops.object.modifier_apply(modifier=mod.name)
        parts.extend([tube('pole',(0,-1.12,0),(0,-1.12,1.26),.023,'Bark',5),tube('ridge',(0,-1.1,1.25),(0,1.1,1.25),.026,'Bark',5)])
    elif kind=='Buoy':
        parts=[ellipsoid('body',(0,0,.22),(.16,.16,.22),'Terracotta',7,3),tube('stem',(0,0,.33),(0,0,.65),.02,'Charcoal',5)]
        lpm.paint(parts[0],P['Cream'],where=lambda c,n,i:c.z>.20)
    elif kind=='Crate':
        parts=[box('crate',(.58,.52,.5),(0,0,0),'CutWood')]
        # Mark plank divisions with face colour, avoiding hidden board layers.
        lpm.paint(parts[0],P['Bark'],where=lambda c,n,i:n.z<-.9)
        for y in (-.27,.27):
            ob=box('brace',(.52,.035,.06),(0,y,.23),'Bark'); lpm.rotate(ob,32,axis='Y',about=(0,y,.26)); parts.append(ob)
    elif kind=='Bucket':
        # Watertight open bucket shell with inner cavity and bottom.
        parts=[lpm.lathe('bucket',[(0,0),(.15,0),(.21,.33),(.185,.33),(.13,.035),(0,.035)],segments=8,color=P['Teal'])]
        for j in range(6):
            a=math.pi*j/6; b=math.pi*(j+1)/6
            parts.append(tube('handle',(.18*math.cos(a),0,.32+.20*math.sin(a)),(.18*math.cos(b),0,.32+.20*math.sin(b)),.012,'Metal',4))
    return finish(f'Extra_{kind}_{v:02}',[('',parts,kind not in ('LeafPile','Campfire','Bridge','Dock','Planks'))],'Extras',400)

def export(rec):
    folder=OUT/rec['category']; folder.mkdir(exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT'); rec['root'].select_set(True)
    for ob in rec['objects']: ob.select_set(True)
    bpy.context.view_layer.objects.active=rec['root']
    bpy.ops.export_scene.fbx(filepath=str(folder/(rec['name']+'.fbx')),use_selection=True,
        object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=True,
        mesh_smooth_type='FACE',use_mesh_modifiers=True,use_tspace=False,bake_anim=False,
        path_mode='RELATIVE',embed_textures=False)

def validate(rec):
    failures=[]; boundary=0; nonmanifold=0; degenerate=0; duplicates=0
    for ob in rec['objects']:
        bm=bmesh.new(); bm.from_mesh(ob.data)
        boundary+=sum(e.is_boundary for e in bm.edges)
        nonmanifold+=sum(not e.is_manifold for e in bm.edges)
        degenerate+=sum(f.calc_area()<1e-10 for f in bm.faces)
        keys=[tuple(round(c,6) for c in v.co) for v in bm.verts]
        duplicates+=len(keys)-len(set(keys)); bm.free()
        if any(len(p.vertices)!=3 for p in ob.data.polygons): failures.append('nontriangle')
        if not ob.data.uv_layers: failures.append('no UV')
        if ob.location.length>1e-6 or any(abs(v-1)>1e-6 for v in ob.scale): failures.append('transform')
    if nonmanifold or degenerate or duplicates: failures.append('geometry')
    rec['qa']={'pass':not failures,'boundary_edges':boundary,'nonmanifold_edges':nonmanifold,'degenerate_faces':degenerate,'duplicate_vertices':duplicates,'failures':failures}
    return not failures

def setup_render():
    scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=16
    scene.cycles.use_denoising=True; scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='Standard'; scene.view_settings.look='None'
    scene.world=bpy.data.worlds.new('ForestStudio'); scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.12,.12,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
    cam=bpy.data.objects.new('ReviewCamera',bpy.data.cameras.new('ReviewCamera')); collection('_Review').objects.link(cam)
    scene.camera=cam; cam.data.type='ORTHO'; cam.data.lens=45
    for name,loc,power,size in [('Key',(-4,-8,12),1400,9),('Fill',(8,-2,6),550,8)]:
        data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
        ob=bpy.data.objects.new(name,data); collection('_Review').objects.link(ob); ob.location=loc
        ob.rotation_euler=(-ob.location).to_track_quat('-Z','Y').to_euler()
    sun=bpy.data.lights.new('UniformSoftSun','SUN'); sun.energy=2.2; sun.angle=.3
    ob=bpy.data.objects.new('UniformSoftSun',sun); collection('_Review').objects.link(ob)
    ob.rotation_euler=(math.radians(25),math.radians(-30),math.radians(-25))
    return cam

def preview_category(category,cam):
    records=[a for a in ASSETS if a['category']==category]
    for a in ASSETS: a['root'].hide_render=True
    copies=[]
    # Every thumbnail uses the same view and normalized height; labels state real dimensions.
    for i,a in enumerate(records):
        height=max(a['dimensions_m']); sc=2.6/max(height,.001)
        for orig in a['objects']:
            ob=orig.copy(); ob.data=orig.data; ob.parent=None
            collection('_Review').objects.link(ob); ob.location=(i*3.8,0,0); ob.scale=(sc,sc,sc); ob.hide_render=False; copies.append(ob)
    for c in CATEGORIES: collection(c).hide_render=True
    target=Vector(((len(records)-1)*3.8/2,0,1.3)); cam.location=target+Vector((0,-12,7))
    # rotate objects themselves by 25 degrees, keeping camera aligned for clean strip crops.
    for ob in copies: ob.rotation_euler.z=math.radians(-25)
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    scene=bpy.context.scene; scene.render.resolution_x=len(records)*360; scene.render.resolution_y=420; scene.render.resolution_percentage=100
    cam.data.ortho_scale=len(records)*3.8; scene.render.filepath=str(PREVIEW/(category+'_row.png'))
    bpy.ops.render.render(write_still=True)
    for ob in copies: bpy.data.objects.remove(ob,do_unlink=True)
    for c in CATEGORIES: collection(c).hide_render=False
    for a in ASSETS: a['root'].hide_render=False
    print('CATEGORY_DONE '+category,flush=True)

def build(category):
    if category=='Trees':
        for kind in ('Oak','Pine','Birch','Dead','Sapling'):
            for v in range(1,5): tree(kind,v)
        for v in range(1,5): stump(v)
    elif category=='Fallen_Wood':
        for kind in ('Whole','Broken','Hollow','Mossy'):
            for v in range(1,4): log(kind,v)
        for kind in ('Stick','Branch','Pile'):
            for v in range(1,3): branches(kind,v)
        uprooted()
    elif category=='Rocks':
        for kind in ('Pebble','Medium','Boulder','Flat','Cluster'):
            for v in range(1,4): rock(kind,v)
        for kind in ('Medium','Boulder','Flat','Cluster'): rock(kind,2,True)
    elif category=='Plants':
        for kind in ('Round','Leafy','Berry'):
            for v in range(1,3): bush(kind,v)
        for v in range(1,3):
            fern(v); grass('ShortGrass',v); grass('TallGrass',v); reeds(v); ivy(v)
    elif category=='Flowers':
        for kind in ('Daisy','Tulip','WildPink','Bluebell','Buttercup'):
            flower(kind); flower(kind,True)
    elif category=='Mushrooms':
        for kind in ('Small','Large','Cluster'):
            for v in range(1,4): mushroom(kind,v)
    elif category=='Extras':
        for kind in ('LilyPad','LeafPile','Bridge','Dock','Planks','Fence','Signpost','Campfire','Tent','Buoy','Crate','Bucket'): extra(kind)

def demo(cam):
    scene=bpy.data.scenes.new('Demo_Forest'); bpy.context.window.scene=scene
    for c in CATEGORIES: scene.collection.children.link(collection(c))
    for a in ASSETS: a['root'].hide_render=True
    col=bpy.data.collections.new('Demo_Set'); scene.collection.children.link(col)
    rng=random.Random(83)
    def put(name,pos,scale=1,angle=0):
        rec=next(a for a in ASSETS if a['name']==name)
        root=bpy.data.objects.new(name+'_Placed',None); col.objects.link(root); root.location=pos; root.rotation_euler.z=angle; root.scale=(scale,)*3
        for orig in rec['objects']:
            ob=orig.copy(); ob.data=orig.data; ob.parent=root; col.objects.link(ob); ob.hide_render=False
        return root
    for i in range(30):
        a=i*2.4; rad=rng.uniform(7.5,13); x=math.cos(a)*rad; y=math.sin(a)*rad
        put(f'Tree_{["Oak","Pine","Birch","Sapling"][i%4]}_{(i//4)%4+1:02}',(x,y,0),rng.uniform(.85,1.1),rng.uniform(0,6.2))
    put('Extra_Dock_01',(2.6,-2,0)); put('Extra_Tent_01',(-3,1,0)); put('Extra_Campfire_01',(-1,-1,0))
    put('Extra_Crate_01',(1.2,-2,0)); put('Extra_Bucket_01',(1.9,-2.1,0)); put('Extra_Signpost_01',(4.6,1,0),1,.3)
    put('Wood_Uprooted_01',(-5,4,0)); put('Log_Mossy_02',(-3,-4,0),1,.8); put('Tree_Stump_02',(-1.4,2,0))
    small=[a['name'] for a in ASSETS if a['category'] in ('Plants','Flowers','Mushrooms','Rocks')]
    for i in range(100):
        x,y=rng.uniform(-11,11),rng.uniform(-9,11)
        if 1<x<6 and -6<y<1: continue
        if abs(x)<2 and abs(y)<3: continue
        put(rng.choice(small),(x,y,0),rng.uniform(.7,1.1),rng.uniform(0,6.2))
    # Terrain and pond are demo-only, excluded from the exported kit.
    terrain=lpm.prism('Demo_Ground',12,16,.5,at=(0,0,-.5),color=P['LeafDeep'])
    lpm.scale(terrain,sy=.85)
    pond=ellipsoid('Demo_Pond',(4,-4,.008),(3.4,2.7,.012),'Water',12,1)
    for ob in (terrain,pond):
        for c in list(ob.users_collection): c.objects.unlink(ob)
        col.objects.link(ob)
        ob=lpm.finish(ob.name,[ob],P,ground=False,center=False); clean(ob)
    for i in range(6): put('Extra_LilyPad_01',(3+(i%3)*.8,-4+(i//3)*.8,.035),1,i)
    for c in CATEGORIES: scene.collection.children.unlink(collection(c))
    scene.collection.children.link(collection('_Review')); scene.camera=cam
    scene.world=bpy.data.worlds['ForestStudio'].copy(); scene.world.name='ForestCozyWorld'
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.22,.22,.22,1)
    scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.use_denoising=True
    scene.view_settings.view_transform='Standard'; scene.view_settings.look='None'
    cam.location=(20,-30,22); target=Vector((0,1,2)); cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.ortho_scale=31
    scene.render.resolution_x=1920; scene.render.resolution_y=1440; scene.render.resolution_percentage=100
    scene.render.filepath=str(PREVIEW/'Demo_Forest.png'); bpy.ops.render.render(write_still=True)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in col.objects: ob.select_set(True)
    folder=OUT/'Demo'; folder.mkdir(exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(folder/'ForestDemo.fbx'),use_selection=True,
        object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=True,bake_anim=False,
        mesh_smooth_type='FACE',path_mode='RELATIVE')
    return scene

def main():
    # Separate headless process: live Blender user data is never reset.
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob,do_unlink=True)
    scene=bpy.context.scene; scene.name='Forest_Kit'; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    cam=setup_render()
    for cat in CATEGORIES:
        build(cat)
        for rec in [a for a in ASSETS if a['category']==cat]:
            if not validate(rec): raise RuntimeError(rec['name']+' failed geometry QA: '+str(rec['qa']))
            export(rec)
        partial=[{k:v for k,v in a.items() if k not in ('root','objects')} for a in ASSETS]
        (ROOT/'manifest.json').write_text(json.dumps(partial,indent=2),encoding='utf-8')
        preview_category(cat,cam)
    # The combined FBX is a browsable library; individual FBXs above retain zeroed roots.
    for row,cat in enumerate(CATEGORIES):
        x=0
        for a in [r for r in ASSETS if r['category']==cat]:
            x+=a['dimensions_m'][0]/2; a['root'].location=(x,row*13,0); x+=a['dimensions_m'][0]/2+1.5
    bpy.ops.object.select_all(action='DESELECT')
    for a in ASSETS:
        a['root'].select_set(True)
        for ob in a['objects']: ob.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(OUT/'ForestKit_All.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=True,bake_anim=False,mesh_smooth_type='FACE',path_mode='RELATIVE')
    report=[{k:v for k,v in a.items() if k not in ('root','objects')} for a in ASSETS]
    (ROOT/'manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    with (ROOT/'Asset_Summary.csv').open('w',newline='',encoding='utf-8') as f:
        w=csv.writer(f); w.writerow(['asset','category','triangles','budget','dimensions_m','material','qa_pass'])
        for r in report: w.writerow([r['name'],r['category'],r['triangles'],r['budget'],' x '.join(map(str,r['dimensions_m'])),r['material'],r['qa']['pass']])
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ForestKit.blend'))
    demo(cam)
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ForestDemo.blend'))
    print('KIT_COMPLETE '+str(len(ASSETS)),flush=True)

if __name__=='__main__': main()
