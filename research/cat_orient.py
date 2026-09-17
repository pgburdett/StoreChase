path = "/home/pat/source/repos/StoreChase/Content/Models/Cat.obj"
vs = []
with open(path, "r", errors="ignore") as f:
    for line in f:
        if line.startswith("v "):
            p = line.split()
            vs.append((float(p[1]), float(p[2]), float(p[3])))

ys = [v[1] for v in vs]
ymin, ymax = min(ys), max(ys)
span = ymax - ymin
print("verts:", len(vs), "Y %.2f..%.2f" % (ymin, ymax))

for label, lo, hi in (("Y-min end", ymin, ymin + span * 0.25),
                      ("Y-max end", ymax - span * 0.25, ymax)):
    band = [v for v in vs if lo <= v[1] <= hi]
    xs = [v[0] for v in band]
    zs = [v[2] for v in band]
    cx = sum(xs) / len(xs)
    cz = sum(zs) / len(zs)
    print("\n%s  Y %.1f..%.1f : %d verts" % (label, lo, hi, len(band)))
    print("  X %.1f..%.1f (w=%.1f, center=%.1f)" % (min(xs), max(xs), max(xs)-min(xs), cx))
    print("  Z %.1f..%.1f (h=%.1f, center=%.1f)" % (min(zs), max(zs), max(zs)-min(zs), cz))
