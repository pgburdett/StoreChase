from collections import defaultdict

path = "Content/Models/Cat.obj"
vs = []
with open(path, "r", errors="ignore") as f:
    for line in f:
        if line.startswith("v "):
            p = line.split()
            vs.append((float(p[1]), float(p[2]), float(p[3])))

zmin, zmax = min(v[2] for v in vs), max(v[2] for v in vs)
xmin, xmax = min(v[0] for v in vs), max(v[0] for v in vs)
ymin, ymax = min(v[1] for v in vs), max(v[1] for v in vs)

def show(zt):
    print(f"\n=== low band Z<={zt:.1f} : X x Y occupancy (each cell count) ===")
    nx, ny = 13, 24
    grid = [[0]*ny for _ in range(nx)]
    for x, y, z in vs:
        if z > zt: continue
        ix = min(nx-1, int((x-xmin)/(xmax-xmin)*nx))
        iy = min(ny-1, int((y-ymin)/(ymax-ymin)*ny))
        grid[ix][iy] += 1
    # print as rows = Y (nose at top), cols = X
    print("        " + "".join("%5d" % i for i in range(nx)))
    for j in range(ny-1, -1, -1):
        ylo = ymin + (ymax-ymin)*j/ny
        row = "".join(("%5d" % grid[i][j]) if grid[i][j] else "    ." for i in range(nx))
        print("Y%6.1f %s" % (ylo, row))
    print("X axis: " + " ".join("%5.1f" % (xmin+(xmax-xmin)*i/nx) for i in range(nx)))

show(6.0)
show(10.0)
