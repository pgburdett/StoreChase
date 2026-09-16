import math
path = "Content/Models/Cat.obj"
vs = []
with open(path, "r", errors="ignore") as f:
    for line in f:
        if line.startswith("v "):
            p = line.split()
            vs.append((float(p[1]), float(p[2]), float(p[3])))

zmin = min(v[2] for v in vs); zmax = max(v[2] for v in vs)
H = zmax - zmin
floorZ = zmin
hipZ = floorZ + H*0.45
blend = H*0.12
bandTop = floorZ + H*0.20
ySplit = (min(v[1] for v in vs if v[2] <= bandTop) + max(v[1] for v in vs if v[2] <= bandTop)) / 2

def legidx(x, y):
    right = x >= 0
    back = y >= ySplit
    return (3 if right else 2) if back else (1 if right else 0)

# centroids + maxdist per leg
sums = [[0,0,0]]*4; cnt=[0]*4
for x,y,z in vs:
    if z<=bandTop:
        i=legidx(x,y); s=sums[i]; sums[i]=[s[0]+x,s[1]+y,s[2]+z]; cnt[i]+=1
cent=[(s[0]/c, s[1]/c) for s,c in zip(sums,cnt)]
maxd=[]
for i in range(4):
    d=max(math.hypot(x-cent[i][0], y-cent[i][1]) for x,y,z in vs if z<=bandTop and legidx(x,y)==i)
    maxd.append(d)
print("centroids", [("%.2f"%c[0], "%.2f"%c[1]) for c in cent])
print("maxdist", ["%.2f"%d for d in maxd])

def smoothstep(t):
    t=max(0.0,min(1.0,t)); return t*t*(3-2*t)

# weights
stats={"body_core_max":0.0,"high_max":0.0}
nz=0; wsum=0; maxw=0
for x,y,z in vs:
    zw = smoothstep((hipZ - z)/blend) if z<hipZ else 0.0
    i=legidx(x,y)
    d=math.hypot(x-cent[i][0], y-cent[i][1])
    rInner=0.55*maxd[i]; rOuter=1.15*maxd[i]
    rw = 1.0 - smoothstep((d-rInner)/(rOuter-rInner)) if rOuter>rInner else 1.0
    w=zw*rw
    if w>0: nz+=1; wsum+=w; maxw=max(maxw,w)
    if abs(x)<1.5 and w>stats["body_core_max"]: stats["body_core_max"]=w
    if z>hipZ and w>stats["high_max"]: stats["high_max"]=w
print("weighted verts:", nz, "of", len(vs), "maxw=%.3f meanw=%.3f"%(maxw, wsum/max(1,nz)))
print("max weight among body core |x|<1.5:", "%.3f"%stats["body_core_max"])
print("max weight above hipZ:", "%.3f"%stats["high_max"])

# where do the strongly-weighted verts live?
import collections
strong=[(x,y,z) for x,y,z in vs if (lambda w: w>0.5)(smoothstep((hipZ - z)/blend)* (1.0 - smoothstep((math.hypot(x-cent[legidx(x,y)][0], y-cent[legidx(x,y)][1])-0.55*maxd[legidx(x,y)])/(1.15*maxd[legidx(x,y)]-0.55*maxd[legidx(x,y)])))) ]
for i in range(4):
    g=[(x,y,z) for x,y,z in strong if legidx(x,y)==i]
    if g: print("leg%d strong n=%d z %.1f..%.1f x %.1f..%.1f y %.1f..%.1f"%(i,len(g),min(v[2] for v in g),max(v[2] for v in g),min(v[0] for v in g),max(v[0] for v in g),min(v[1] for v in g),max(v[1] for v in g)))
