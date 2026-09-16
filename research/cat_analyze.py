import sys
from collections import defaultdict

path = "Content/Models/Cat.obj"
vs = []
with open(path, "r", errors="ignore") as f:
    for line in f:
        if line.startswith("v "):
            p = line.split()
            vs.append((float(p[1]), float(p[2]), float(p[3])))

print("verts:", len(vs))
xs = [v[0] for v in vs]; ys = [v[1] for v in vs]; zs = [v[2] for v in vs]
print("X range: %.2f .. %.2f" % (min(xs), max(xs)))
print("Y range: %.2f .. %.2f" % (min(ys), max(ys)))
print("Z range: %.2f .. %.2f" % (min(zs), max(zs)))

# Z histogram (vertical) to find ground and body
zmin, zmax = min(zs), max(zs)
bins = 20
hist = [0]*bins
for z in zs:
    i = min(bins-1, int((z - zmin) / (zmax - zmin) * bins))
    hist[i] += 1
print("\nZ histogram (vertical), low Z = floor:")
for i, c in enumerate(hist):
    lo = zmin + (zmax-zmin)*i/bins
    hi = zmin + (zmax-zmin)*(i+1)/bins
    print("  %6.1f..%6.1f : %6d %s" % (lo, hi, c, "#"*(c*60//max(hist))))

# Low-Z band = paws/legs. Try 15% of height.
for frac in (0.10, 0.15, 0.20, 0.30, 0.40):
    thr = zmin + (zmax-zmin)*frac
    low = [v for v in vs if v[2] <= thr]
    lx = [v[0] for v in low]; ly = [v[1] for v in low]
    print("\nZ<=%.1f (%.0f%%): %d verts, X %.1f..%.1f, Y %.1f..%.1f"
          % (thr, frac*100, len(low), min(lx), max(lx), min(ly), max(ly)))
