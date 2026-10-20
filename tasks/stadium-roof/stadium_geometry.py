"""
stadium_geometry.py - parametric geometry for a tubular steel stadium roof (Tekla Structures 2025).

Pure Python (no Tekla, no numpy) so it can be tested anywhere. It writes a member list
(CSV) that the generic Tekla macro `MemberImport.cs` inserts into the model.

Shape (read from the reference image):
  - plan: rounded-square "superellipse" outer line, smaller superellipse opening
  - section: facade rises from the ground, bulges outward, rolls over a shoulder and runs
    inward as a gently rising roof to the inner edge, which has a short hanging skirt
  - structure: N radial trussed ribs (outer chord on the skin, inner chord offset inward,
    posts + Warren diagonals), hoop rings at every node level, X-bracing in the skin in
    every k-th bay, heavy compression ring at the inner edge, base ring at the ground.

Units: mm. Global coordinates, origin = stadium centre at ground level (+ ORIGIN offset).

Usage:
    python stadium_geometry.py                      # defaults (N_RIBS=48 = first test)
    python stadium_geometry.py --ribs 144 --out members_144.csv
"""

import argparse
import csv
import json
import math
from collections import Counter

# ============================ PARAMETERS =====================================
P = {
    "ORIGIN": (0.0, 230000.0, 0.0),        # where the stadium centre goes in the model
    "OUTER_A": 135000.0, "OUTER_B": 115000.0, "OUTER_N": 4.0,   # superellipse at ground (semi-axes, exponent)
    "INNER_A": 78000.0, "INNER_B": 58000.0, "INNER_N": 4.5,     # roof opening (inner edge)
    "N_RIBS": 48,                           # radial ribs around (48 = test, 144 = full)
    # section polyline: (h, z). h = 0 at the outer ground line, 1 at the inner edge,
    # negative = outward bulge. z in mm.
    "SECTION": [
        (0.000, 0), (-0.070, 8000), (-0.115, 17000), (-0.130, 26000), (-0.105, 34000),
        (-0.045, 41000), (0.050, 46000), (0.180, 49000), (0.340, 51000), (0.500, 52000),
        (0.660, 52500), (0.820, 52800), (1.000, 53000),
    ],
    "SKIRT_DROP": 9000.0,                   # hanging skirt at the inner edge
    "DEPTH_FACADE": 3000.0, "DEPTH_ROOF": 4500.0, "DEPTH_EDGE": 2500.0,   # rib truss depth
    "INNER_RING_EVERY": 2,                  # inner-chord ring at every 2nd node
    "BRACE_EVERY": 4,                       # skin X-bracing in every 4th bay
    "MATERIAL": "S355J2H",
    "PROFILES": {
        "OUTER_CHORD": "PD457*16", "INNER_CHORD": "PD355.6*12.5", "DIAGONAL": "PD219.1*8",
        "POST": "PD168.3*8", "RING": "PD273*10", "INNER_RING": "PD219.1*8", "BRACE": "PD114.3*6",
        "COMPRESSION_RING": "PD610*20", "BASE_RING": "PD508*16", "SKIRT": "PD273*10",
    },
    "CLASSES": {
        "OUTER_CHORD": 2, "INNER_CHORD": 4, "DIAGONAL": 7, "POST": 4, "RING": 6, "INNER_RING": 6,
        "BRACE": 13, "COMPRESSION_RING": 13, "BASE_RING": 13, "SKIRT": 4,
    },
    "PREFIX": "ST_",
}
# =============================================================================


def superellipse(a, b, n, phi):
    c, s = math.cos(phi), math.sin(phi)
    return (a * math.copysign(abs(c) ** (2.0 / n), c), b * math.copysign(abs(s) ** (2.0 / n), s))


def equal_arc_params(a, b, n, count, dense=20000):
    """Parameters phi that split the superellipse into `count` equal arc lengths."""
    phis = [2 * math.pi * i / dense for i in range(dense + 1)]
    pts = [superellipse(a, b, n, f) for f in phis]
    cum = [0.0]
    for i in range(1, len(pts)):
        cum.append(cum[-1] + math.dist(pts[i - 1], pts[i]))
    total = cum[-1]
    out, j = [], 0
    for k in range(count):
        target = total * k / count
        while cum[j + 1] < target:
            j += 1
        t = (target - cum[j]) / (cum[j + 1] - cum[j])
        out.append(phis[j] + t * (phis[j + 1] - phis[j]))
    return out


def build(p):
    ox, oy, oz = p["ORIGIN"]
    sec = p["SECTION"]
    ns = len(sec)
    phis = equal_arc_params(p["OUTER_A"], p["OUTER_B"], p["OUTER_N"], p["N_RIBS"])

    def depth(i):  # truss depth at section node i
        h, z = sec[i]
        if z < 41000:
            return p["DEPTH_FACADE"]
        if i == ns - 1:
            return p["DEPTH_EDGE"]
        return p["DEPTH_ROOF"]

    outer, inner, skirt = [], [], []   # [rib][node] -> (x, y, z)
    for phi in phis:
        co = superellipse(p["OUTER_A"], p["OUTER_B"], p["OUTER_N"], phi)
        ci = superellipse(p["INNER_A"], p["INNER_B"], p["INNER_N"], phi)
        vx, vy = ci[0] - co[0], ci[1] - co[1]          # horizontal run of this rib
        L = math.hypot(vx, vy)

        def pt(h, z):
            return (ox + co[0] + h * vx, oy + co[1] + h * vy, oz + z)

        o_row, i_row = [], []
        for i, (h, z) in enumerate(sec):
            o_row.append(pt(h, z))
            # inward normal of the section curve in the (h*L, z) plane
            a, b = sec[max(i - 1, 0)], sec[min(i + 1, ns - 1)]
            dh, dz = (b[0] - a[0]) * L, (b[1] - a[1])
            m = math.hypot(dh, dz)
            nh, nz = dz / m, -dh / m
            d = depth(i)
            i_row.append(pt(h + nh * d / L, z + nz * d))
        outer.append(o_row)
        inner.append(i_row)
        h_end, z_end = sec[-1]
        skirt.append(pt(h_end, z_end - p["SKIRT_DROP"]))

    members = []

    def add(kind, a, b):
        members.append({
            "name": p["PREFIX"] + kind,
            "profile": p["PROFILES"][kind],
            "material": p["MATERIAL"],
            "class": p["CLASSES"][kind],
            "p1": a, "p2": b,
        })

    R = p["N_RIBS"]
    for r in range(R):
        o, n = outer[r], inner[r]
        for i in range(ns - 1):
            add("OUTER_CHORD", o[i], o[i + 1])
            if i >= 1:
                add("INNER_CHORD", n[i], n[i + 1])
        for i in range(1, ns):
            add("POST", o[i], n[i])
        for i in range(1, ns - 1):                       # Warren diagonals
            if i % 2:
                add("DIAGONAL", n[i], o[i + 1])
            else:
                add("DIAGONAL", o[i], n[i + 1])
        add("SKIRT", o[-1], skirt[r])

    for r in range(R):                                   # hoops around
        s = (r + 1) % R
        for i in range(ns):
            kind = "BASE_RING" if i == 0 else ("COMPRESSION_RING" if i == ns - 1 else "RING")
            add(kind, outer[r][i], outer[s][i])
        for i in range(1, ns):
            if i % p["INNER_RING_EVERY"] == 0 or i == ns - 1:
                add("INNER_RING", inner[r][i], inner[s][i])
        add("RING", skirt[r], skirt[s])
        if r % p["BRACE_EVERY"] == 0:                    # skin X-bracing
            for i in range(ns - 1):
                add("BRACE", outer[r][i], outer[s][i + 1])
                add("BRACE", outer[s][i], outer[r][i + 1])
    return members


def write_csv(members, path):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["name", "profile", "material", "class", "x1", "y1", "z1", "x2", "y2", "z2"])
        for m in members:
            w.writerow([m["name"], m["profile"], m["material"], m["class"],
                        *("%.1f" % v for v in m["p1"]), *("%.1f" % v for v in m["p2"])])


def check(members, p):
    """Sanity checks that do not need Tekla. Returns a dict and raises on failure."""
    lengths = [math.dist(m["p1"], m["p2"]) for m in members]
    counts = Counter(m["name"] for m in members)
    zs = [c for m in members for c in (m["p1"][2], m["p2"][2])]
    assert min(lengths) > 300, "member shorter than 300 mm: %.0f" % min(lengths)
    assert all(m["profile"] for m in members)
    R, ns = p["N_RIBS"], len(p["SECTION"])
    assert counts[p["PREFIX"] + "COMPRESSION_RING"] == R and counts[p["PREFIX"] + "BASE_RING"] == R
    assert counts[p["PREFIX"] + "OUTER_CHORD"] == R * (ns - 1)
    return {
        "members": len(members),
        "by_type": dict(sorted(counts.items())),
        "min_length_mm": round(min(lengths)),
        "max_length_mm": round(max(lengths)),
        "z_range_mm": [round(min(zs)), round(max(zs))],
        "total_length_m": round(sum(lengths) / 1000),
    }


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--ribs", type=int, default=None)
    ap.add_argument("--out", default=None)
    ap.add_argument("--config", default=None, help="JSON file with parameters (overrides the defaults above)")
    args = ap.parse_args()
    if args.config:
        with open(args.config, encoding="utf-8") as f:
            P.update({k: v for k, v in json.load(f).items() if not k.startswith("_")})
        P["SECTION"] = [tuple(x) for x in P["SECTION"]]
        P["ORIGIN"] = tuple(P["ORIGIN"])
    if args.ribs is not None:
        P["N_RIBS"] = args.ribs
    mem = build(P)
    summary = check(mem, P)
    out = args.out or "members_%d.csv" % P["N_RIBS"]
    write_csv(mem, out)
    print(json.dumps(summary, indent=2))
    print("written:", out)
