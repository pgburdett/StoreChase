path = "Content/Models/Cat.obj"
vs = []
with open(path, "r", errors="ignore") as f:
    for line in f:
        if line.startswith("v "):
            p = line.split()
            vs.append((float(p[1]), float(p[2]), float(p[3])))

zmin = min(v[2] for v in vs); zmax = max(v[2] for v in vs)
H = zmax - zmin
print("zmin=%.2f zmax=%.2f H=%.2f" % (zmin, zmax, H))

# Where does the central body mass (|x|<1) start vertically?
central = [v for v in vs if abs(v[0]) < 1.0]
zs = sorted(v[2] for v in central)
print("central |x|<1 verts:", len(central), "minZ=%.2f" % zs[0])
# cumulative: first z at which there are many central verts
for frac in (0.05, 0.1, 0.25, 0.5):
    print("  central %.0f%% quantile z=%.2f (%.0f%% of H)" % (frac*100, zs[int(len(zs)*frac)], (zs[int(len(zs)*frac)]-zmin)/H*100))

# Classify low-band verts into 4 clusters and profile each
band = [v for v in vs if v[2] < zmin + H*0.20]
def split(v):
    side = "L" if v[0] < 0 else "R"
    front = "F" if v[1] < 0 else "B"
    return front+side
from collections import defaultdict
cl = defaultdict(list)
for v in band: cl[split(v)].append(v)

for k in sorted(cl):
    g = cl[k]
    xs=[v[0] for v in g]; ys=[v[1] for v in g]; zz=[v[2] for v in g]
    print("\nleg %s n=%d" % (k, len(g)))
    print("  X %.2f..%.2f mean %.2f" % (min(xs),max(xs),sum(xs)/len(xs)))
    print("  Y %.2f..%.2f mean %.2f" % (min(ys),max(ys),sum(ys)/len(ys)))
    print("  Z %.2f..%.2f" % (min(zz),max(zz)))

# Column-ness: for front-left region (x<-1.5, y<0), z histogram
def profile(name, cond):
    g=[v for v in vs if cond(v)]
    print("\n%s n=%d" % (name,len(g)))
    if not g: return
    zz=[v[2] for v in g]
    bins=12; z0=zmin; z1=zmin+H*0.5
    hist=[0]*bins
    for z in zz:
        if z1<=z0: break
        i=min(bins-1,int((z-z0)/(z1-z0)*bins))
        if i>=0: hist[i]+=1
    for i,c in enumerate(hist):
        print("   z %5.1f..%5.1f : %5d %s" % (z0+(z1-z0)*i/bins, z0+(z1-z0)*(i+1)/bins, c, "#"*(c*40//max(hist))))

profile("front-left column (x<-1.5,y<0)", lambda v: v[0]<-1.5 and v[1]<0)
profile("back-left column  (x<-1.5,y>0)", lambda v: v[0]<-1.5 and v[1]>0)
