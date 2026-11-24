"""Read back every candidate GLB and report measurable structural checks."""
from pathlib import Path
import trimesh
ROOT=Path(__file__).resolve().parents[1]
LIMITS={"buildings/hut.glb":((7.6,7.6,5.5),2500),"buildings/storehouse.glb":((11.5,11.5,7),4000),"buildings/bakery.glb":((11.5,11.5,6.5),4000),"nature/tree.glb":((7,7,10),900),"nature/wheat_patch.glb":((4,4,1.1),700),"tools/axe.glb":((.9,.9,.9),120),"tools/sickle.glb":((.6,.6,.6),120),"characters/farmer.glb":((1.2,1.2,1.9),2500),"characters/woodcutter.glb":((1.2,1.2,1.9),2500)}
for rel,(size,tri_limit) in LIMITS.items():
    scene=trimesh.load(ROOT/rel,force='scene'); meshes=list(scene.geometry.values())
    faces=sum(len(m.faces) for m in meshes)
    bounds=scene.bounds; ext=(bounds[1]-bounds[0]).tolist() if bounds is not None else [0,0,0]
    materials=len({getattr(getattr(m.visual,'material',None),'name',None) for m in meshes})
    print(f"{rel}|triangles={faces}|bounds={','.join(f'{x:.3f}' for x in ext)}|materials={materials}|file={ (ROOT/rel).stat().st_size}")
