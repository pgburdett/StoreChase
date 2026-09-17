path = "/home/pat/source/repos/StoreChase/Content/Models/Cat.obj"
vs = []
with open(path, "r", errors="ignore") as f:
    for line in f:
        if line.startswith("v "):
            p = line.split()
            vs.append((float(p[1]), float(p[2]), float(p[3])))

ys = [v[1] for v in vs]
ymin, ymax = min(ys), max(ys)
bins = 24
print("Y %.2f..%.2f  bins=%d" % (ymin, ymax, bins))
print("%8s %6s %6s %6s %6s %6s" % ("Y-range", "count", "Xw", "Zmin", "Zmax", "Zh"))
step = (ymax - ymin) / bins
for i in range(bins):
    lo = ymin + i * step
    hi = lo + step
    band = [v for v in vs if lo <= v[1] < hi or (i == bins - 1 and v[1] == ymax)]
    if not band:
        continue
    xs = [v[0] for v in band]
    zs = [v[2] for v in band]
    print("%7.1f..%6.1f %6d %6.1f %6.1f %6.1f %6.1f" % (
        lo, hi, len(band), max(xs) - min(xs), min(zs), max(zs), max(zs) - min(zs)))
