"""Label the Blender category rows without modifying rendered artwork."""
import json, math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parent
records=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
title=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',26)
for cat in dict.fromkeys(r['category'] for r in records):
    members=[r for r in records if r['category']==cat]
    file=ROOT/'Previews'/f'{cat}_row.png'
    if not file.exists(): continue
    strip=Image.open(file).convert('RGB'); width=strip.width//len(members)
    cols=4; cell_h=strip.height+65; rows=math.ceil(len(members)/cols)
    sheet=Image.new('RGB',(width*cols,rows*cell_h+70),'#292D2B'); draw=ImageDraw.Draw(sheet)
    draw.text((22,16),f'FOREST KIT / {cat.replace("_"," ")} / {len(members)} assets',font=title,fill='#E8DFC9')
    for i,r in enumerate(members):
        x=(i%cols)*width; y=70+(i//cols)*cell_h
        sheet.paste(strip.crop((i*width,0,(i+1)*width,strip.height)),(x,y))
        draw.text((x+12,y+strip.height+4),r['name'],font=font,fill='#E8DFC9')
        dims=r['dimensions_m']
        draw.text((x+12,y+strip.height+28),f'{r["triangles"]} tris | {dims[0]:.2f} x {dims[1]:.2f} x {dims[2]:.2f} m',font=font,fill='#A6A294')
    sheet.save(ROOT/'Previews'/f'{cat}_sheet.png')
    print(cat,len(members),min(r['triangles'] for r in members),max(r['triangles'] for r in members))

# Inspection overview: representative silhouettes in all five measured views.
names=['Tree_Oak_03','Tree_Pine_03','Tree_Birch_02','Log_Hollow_02','Rock_MossyBoulder_02',
       'Plant_Fern_01','Flower_Tulip_Single_01','Mushroom_Large_01','Extra_Bucket_01','Extra_Tent_01']
views=['front','side','back','three_quarter','top']
inspection=ROOT/'Previews/Inspection'
if all((inspection/(r['category']+'_'+v+'.png')).exists() for r in records for v in views):
    sheet=Image.new('RGB',(1280,len(names)*255+55),'#292D2B'); draw=ImageDraw.Draw(sheet)
    for j,v in enumerate(views): draw.text((j*256+15,15),v,font=font,fill='#E8DFC9')
    for row,name in enumerate(names):
        r=next(a for a in records if a['name']==name)
        members=[a for a in records if a['category']==r['category']]; i=members.index(r)
        for j,v in enumerate(views):
            strip=Image.open(inspection/(r['category']+'_'+v+'.png'))
            crop=strip.crop((i*256,0,(i+1)*256,300)).resize((256,225))
            sheet.paste(crop,(j*256,55+row*255))
        draw.text((12,55+row*255+228),name,font=font,fill='#E8DFC9')
    sheet.save(ROOT/'Previews/Inspection_Review.png')
