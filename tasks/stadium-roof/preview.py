"""Render a 3D preview of the member list (for checking the shape before Tekla)."""
import sys, csv
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Line3DCollection

path = sys.argv[1]; out = sys.argv[2] if len(sys.argv) > 2 else "preview.png"
colors = {"OUTER_CHORD": "#d62728", "INNER_CHORD": "#1f3fbf", "DIAGONAL": "#8e44ad", "POST": "#1f3fbf",
          "RING": "#9acd32", "INNER_RING": "#e67e22", "BRACE": "#f39c12", "COMPRESSION_RING": "#d35400",
          "BASE_RING": "#d35400", "SKIRT": "#1f3fbf"}
segs, cols = [], []
with open(path) as f:
    for r in csv.DictReader(f):
        k = r["name"].split("_", 1)[1]
        segs.append([(float(r["x1"]), float(r["y1"]), float(r["z1"])), (float(r["x2"]), float(r["y2"]), float(r["z2"]))])
        cols.append(colors.get(k, "k"))
fig = plt.figure(figsize=(14, 8), dpi=110)
ax = fig.add_subplot(111, projection="3d")
ax.add_collection3d(Line3DCollection(segs, colors=cols, linewidths=0.6))
xs = [p[0] for s in segs for p in s]; ys = [p[1] for s in segs for p in s]; zs = [p[2] for s in segs for p in s]
ax.set_xlim(min(xs), max(xs)); ax.set_ylim(min(ys), max(ys)); ax.set_zlim(0, 90000)
ax.set_box_aspect((max(xs) - min(xs), max(ys) - min(ys), 90000))
ax.view_init(elev=28, azim=-60); ax.set_axis_off()
plt.tight_layout(); plt.savefig(out, bbox_inches="tight", facecolor="white")
print("saved", out)
