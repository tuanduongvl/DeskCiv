"""Reproducible original DeskCiv low-poly candidate asset generator.

Run from the repository root: python assets-candidates/codex/source/generate.py
Requires numpy, Pillow, trimesh and matplotlib.
"""
from pathlib import Path
import math, json
import numpy as np
import trimesh
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[1]
COL={"earth":[119,86,57,255],"clay":[169,105,67,255],"straw":[197,164,91,255],"darkstraw":[151,119,57,255],"wood":[111,76,46,255],"bark":[90,64,42,255],"stone":[126,121,105,255],"leaf":[83,119,58,255],"leaf2":[111,139,66,255],"skin":[191,145,109,255],"linen":[202,183,143,255],"blue":[76,111,105,255],"red":[150,79,55,255],"soil":[97,75,48,255],"gold":[205,172,78,255],"bone":[218,207,176,255]}
def mat(k): return trimesh.visual.material.PBRMaterial(name='palette',baseColorFactor=COL[k],metallicFactor=0,roughnessFactor=1)
def add(meshes, obj, color):
    obj.visual=trimesh.visual.TextureVisuals(material=mat(color)); meshes.append(obj)
def box(meshes,pos,size,color):
    x=trimesh.creation.box(extents=size); x.apply_translation(pos); add(meshes,x,color)
def cyl(meshes,a,b,r,color,n=7):
    a=np.array(a); b=np.array(b); v=b-a; m=trimesh.creation.cylinder(radius=r,height=np.linalg.norm(v),sections=n); m.apply_transform(trimesh.geometry.align_vectors([0,0,1],v)); m.apply_translation((a+b)/2); add(meshes,m,color)
def ico(meshes,pos,scale,color,sub=1):
    m=trimesh.creation.icosphere(subdivisions=sub,radius=1); m.apply_scale(scale); m.apply_translation(pos); add(meshes,m,color)
def cone(meshes,pos,r,h,color,n=9):
    m=trimesh.creation.cone(radius=r,height=h,sections=n); m.apply_transform(trimesh.geometry.align_vectors([0,0,1],[0,1,0])); m.apply_translation(pos); add(meshes,m,color)
def export(path,meshes):
    scene=trimesh.Scene()
    for i,m in enumerate(meshes): scene.add_geometry(m,node_name=f'part{i}')
    scene.export(path,file_type='glb')
def hut():
    m=[]
    # low round wattle wall, deliberate open doorway on +Z
    for i in range(18):
        a=2*math.pi*i/18
        if math.sin(a)>.76: continue
        x,z=2.05*math.cos(a),2.05*math.sin(a)
        box(m,(x,1.12,z),(.78,2.24,.22),'earth')
    for i in range(12):
        a=2*math.pi*i/12; cyl(m,(0,2.05,0),(2.28*math.cos(a),4.72,2.28*math.sin(a)),.075,'wood',5)
    cone(m,(0,3.55,0),2.55,3.9,'straw',12)
    # thatch ribs and doorway lintel
    for i in range(16):
        a=2*math.pi*i/16; cyl(m,(1.8*math.cos(a),1.8,1.8*math.sin(a)),(.08*math.cos(a),5.45,.08*math.sin(a)),.035,'darkstraw',4)
    box(m,(0,2.05,2.08),(1.25,.16,.25),'wood')
    return m
def storehouse():
    m=[]
    for x in [-2.8,2.8]:
      for z in [-2.8,2.8]: cyl(m,(x,0,z),(x,3.2,z),.16,'wood')
    for y in [2.8,3.45]:
      for z in [-2.8,2.8]: box(m,(0,y,z),(6.1,.13,.14),'wood')
      for x in [-2.8,2.8]: box(m,(x,y,0),(.14,.13,5.6),'wood')
    box(m,(0,3.55,0),(6.1,.3,6.1),'wood')
    # simple hipped thatch roof
    for sx in [-1,1]:
      for sz in [-1,1]:
        v=np.array([sx*3.45,3.8,sz*3.45]); apex=np.array([0,6.7,0]); cyl(m,v,apex,.08,'wood',5)
    for y in np.arange(4.0,6.5,.24):
      w=3.35*(6.8-y)/3.0
      for zsgn in [-1,1]: box(m,(0,y,zsgn*w),(2*w,.38,.13),'straw')
      for xsgn in [-1,1]: box(m,(xsgn*w,y,0),(.13,.38,2*w),'darkstraw')
    for x,z,c in [(-1.6,-.8,'straw'),(0,-1,'linen'),(1.5,.5,'earth'),(-.8,1,'linen')]:
      ico(m,(x,4.15,z),(.48,.48,.42),c,1)
    return m
def bakery():
    m=[]
    for i in range(16):
      a=2*math.pi*i/16
      if math.sin(a)>.66: continue
      box(m,(2.15*math.cos(a),1.05,2.15*math.sin(a)),(.82,2.1,.2),'clay')
    cone(m,(0,3.65,0),2.5,3.2,'straw',12)
    # kiln at right, mouth faces +Z; smoke vent at roof ridge
    ico(m,(3.5,1.15,.6),(1.35,1.05,1.35),'clay',2)
    box(m,(3.5,.75,1.72),(.75,.72,.18),'bark')
    box(m,(3.5,1.25,1.77),(1.03,.13,.2),'stone')
    cyl(m,(3.5,2.0,.6),(3.5,3.3,.6),.36,'stone',7)
    for i in range(5): cyl(m,(1.0+i*.24,.25,2.8),(1.75+i*.24,.25,2.8),.095,'wood',5)
    # hearth stones
    for i in range(7):
      a=2*math.pi*i/7; ico(m,(3.5+.8*math.cos(a),.2,1.1+.8*math.sin(a)),(.2,.18,.2),'stone',1)
    return m
def tree():
    m=[]; cyl(m,(0,0,0),(0,4.7,0),.34,'bark',7)
    for i in range(5):
      a=i*2.4; cyl(m,(0,2.0+i*.25,0),(1.6*math.cos(a),4.5+i*.38,1.6*math.sin(a)),.12,'wood',5)
    for p,s,c in [((0,6.2,0),(2.7,2.4,2.5),'leaf'),((-1.1,5.7,.3),(1.8,1.7,1.8),'leaf2'),((1.0,6.0,-.4),(1.8,1.9,1.7),'leaf')]: ico(m,p,s,c,1)
    return m
def crops():
    m=[]; rng=np.random.default_rng(418)
    for row in range(5):
      for col in range(5):
        x=-1.75+col*.39+rng.uniform(-.06,.06); z=-1.65+row*.4+rng.uniform(-.05,.05); h=rng.uniform(.58,1.02)
        # Four slender two-sided triangles per stalk, plus one grain head.
        for j in range(3):
          dx=(-1 if j%2 else 1)*rng.uniform(.09,.18); y=h*(.35+j*.16)
          tri(m,[(x-.012,y,z),(x+.012,y,z),(x+dx,y+.18,z)],'straw')
        tri(m,[(x-.025,h-.02,z),(x,h+.08,z),(x+.025,h-.02,z)],'gold')
    return m
def tool(kind):
    m=[]
    if kind=='axe':
      cyl(m,(0,-.4,0),(0,.28,0),.055,'wood',6); ico(m,(.11,.31,0),(.28,.17,.12),'stone',1)
      ico(m,(.31,.31,0),(.1,.17,.12),'bone',1)
    else:
      # curved flint blade in front of short bone grip
      cyl(m,(0,-.2,0),(0,.12,0),.04,'bone',6)
      for i in range(5):
        a=math.pi*i/16; tri(m,[(.20*math.sin(a),.1+.20*(1-math.cos(a)),.02),(.20*math.sin(a+.22),.1+.20*(1-math.cos(a+.22)),.02),(.20*math.sin(a+.11),.1+.20*(1-math.cos(a+.11)),.02)],'stone')
      box(m,(0,-.18,.025),(.16,.08,.07),'wood')
    return m
def person(role):
    m=[]; shirt='linen' if role=='farmer' else 'blue'; trousers='earth'; hair='bark'
    # cylinders along body and limbs (ground at zero; face +Z)
    cyl(m,(0,.78,0),(0,1.38,0),.22,shirt,8); ico(m,(0,1.58,0),(.16,.2,.15),'skin',1)
    ico(m,(0,1.77,-.01),(.17,.11,.16),hair,1)
    for s in [-1,1]:
      cyl(m,(s*.11,.9,0),(s*.12,.48,0),.09,trousers,6); cyl(m,(s*.12,.48,0),(s*.12,.1,0),.065,trousers,6); box(m,(s*.12,.07,.06),(.14,.1,.26),'wood')
      cyl(m,(s*.2,1.32,0),(s*.28,1.0,.02),.075,shirt,6); cyl(m,(s*.28,1.0,.02),(s*.3,.77,.1),.055,'skin',6); ico(m,(s*.3,.75,.11),(.06,.07,.06),'skin',1)
    box(m,(0,.86,.17),(.35,.32,.025),'gold' if role=='farmer' else 'red')
    # job hat/accessory and held implement
    if role=='farmer':
      cyl(m,(0,1.76,0),(0,1.79,0),.27,'straw',10); cone(m,(0,1.88,0),.14,.2,'straw',8)
    else:
      ico(m,(0,1.77,-.11),(.2,.15,.12),'bark',1)
    # tool held visibly at right hand
    if role=='farmer': cyl(m,(-.3,.76,.14),(-.22,.28,.48),.025,'wood',5)
    else: cyl(m,(-.3,.76,.14),(-.22,.28,.48),.035,'wood',5); ico(m,(-.19,.25,.49),(.18,.11,.07),'stone',1)
    return m
def tri(meshes,points,color):
    p=np.array(points,float); mesh=trimesh.Trimesh(vertices=np.vstack([p,p+[0,0,.008]]),faces=[[0,1,2],[3,5,4]],process=False); add(meshes,mesh,color)
def atlas():
    im=Image.new('RGB',(512,512),(120,100,70)); d=ImageDraw.Draw(im); colors=list(COL.items())
    for i,(n,c) in enumerate(colors):
      x=(i%4)*128; y=(i//4)*128; d.rectangle((x,y,x+127,y+127),fill=tuple(c[:3])); d.text((x+5,y+5),n,fill=(30,25,20))
    im.save(ROOT/'palette.png')
def main():
    atlas()
    assets={"buildings/hut.glb":hut(),"buildings/storehouse.glb":storehouse(),"buildings/bakery.glb":bakery(),"nature/tree.glb":tree(),"nature/wheat_patch.glb":crops(),"tools/axe.glb":tool('axe'),"tools/sickle.glb":tool('sickle'),"characters/farmer.glb":person('farmer'),"characters/woodcutter.glb":person('woodcutter')}
    for name,meshes in assets.items(): export(ROOT/name,meshes)
    render(assets)
def render(assets):
    import matplotlib; matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection
    def draw(ax, meshes, offset=(0,0,0), scale=1):
      off=np.array(offset)
      for ob in meshes:
        f=ob.faces; v=ob.vertices*scale+off
        color=np.array(ob.visual.material.baseColorFactor[:3])/255
        ax.add_collection3d(Poly3DCollection(v[f],facecolor=color,edgecolor=(.12,.1,.07,.14),linewidth=.12))
    def ground(ax,extent):
      X,Y=np.meshgrid([-extent,extent],[-extent,extent]); ax.plot_surface(X,np.zeros_like(X),Y,color='#82915d',shade=False)
      ax.set_axis_off(); ax.view_init(elev=34,azim=-52); ax.set_box_aspect((1,.5,1))
    fig=plt.figure(figsize=(19.2,10.8),dpi=100)
    for i,k in enumerate(['buildings/hut.glb','buildings/storehouse.glb','buildings/bakery.glb']):
      ax=fig.add_subplot(1,3,i+1,projection='3d'); draw(ax,assets[k]); ground(ax,7); ax.set_xlim(-6,6); ax.set_ylim(0,8); ax.set_zlim(-6,6); ax.set_title(k.split('/')[-1],fontsize=16)
    fig.savefig(ROOT/'previews/buildings.png',facecolor='#d9d2b9'); plt.close(fig)
    fig=plt.figure(figsize=(19.2,10.8),dpi=100)
    ax=fig.add_subplot(1,1,1,projection='3d')
    for k,x in [('characters/farmer.glb',-1.5),('characters/woodcutter.glb',1.5)]: draw(ax,assets[k],(x,0,0))
    ground(ax,4); ax.set_xlim(-4,4); ax.set_ylim(0,3); ax.set_zlim(-2,3); ax.set_title('Farmer and woodcutter candidate models',fontsize=18)
    fig.savefig(ROOT/'previews/characters.png',facecolor='#d9d2b9'); plt.close(fig)
    fig=plt.figure(figsize=(19.2,10.8),dpi=100); ax=fig.add_subplot(1,1,1,projection='3d')
    placements=[('buildings/hut.glb',(-12,0,3)),('buildings/storehouse.glb',(0,0,2)),('buildings/bakery.glb',(12,0,3)),('nature/tree.glb',(-17,0,-7)),('nature/tree.glb',(17,0,-7)),('nature/wheat_patch.glb',(0,0,-10)),('characters/farmer.glb',(-5,0,-2)),('characters/woodcutter.glb',(5,0,-2))]
    for k,p in placements: draw(ax,assets[k],p,.55 if k.startswith('buildings') else 1)
    ground(ax,24); ax.set_xlim(-23,23); ax.set_ylim(0,23); ax.set_zlim(-18,18); ax.set_title('Candidate assets from settlement distance',fontsize=18)
    fig.savefig(ROOT/'previews/distance.png',facecolor='#d9d2b9'); plt.close(fig)
if __name__=='__main__': main()
