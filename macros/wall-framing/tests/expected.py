"""
Oracle + comparison harness for the wall-layout dimensioning rules.

`expected_sets()` reads the 18 dimension sets of GA drawing [50] and normalises
them into a comparable form:
    axis        "X" or "Y"  (dominant direction of the dimension line)
    perp        the fixed perpendicular coordinate
    values      sorted list of measured coordinates
    paperDist   Distance / scale  (paper mm)
    up          the up direction vector

`compare()` diffs a generated rule output against the oracle.

Usage: python tests/expected.py
"""
from __future__ import annotations

import re
from pathlib import Path

from paths import REFERENCE, require  # noqa: E402

DUMP = REFERENCE / "full_50.txt"
SCALE = 17.0
TOL = 0.35


def expected_sets(path=DUMP):
    require(Path(path), "the dump of the reference drawing")
    sets, cur = [], None
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        ms = re.match(r"\s+--- SET (\d+) Distance=([-\d.]+)", line)
        if ms:
            cur = {"n": int(ms.group(1)), "dist": float(ms.group(2)), "dims": []}
            sets.append(cur)
            continue
        md = re.match(
            r"\s+DIM start=\(([-\d.]+),([-\d.]+),[-\d.]+\) "
            r"end=\(([-\d.]+),([-\d.]+),[-\d.]+\) "
            r"up=\(([-\d.]+),([-\d.]+),[-\d.]+\) len=([\d.]+)", line)
        if md and cur is not None:
            cur["dims"].append({
                "s": (float(md.group(1)), float(md.group(2))),
                "e": (float(md.group(3)), float(md.group(4))),
                "up": (float(md.group(5)), float(md.group(6))),
            })

    out = []
    for s in sets:
        dims = s["dims"]
        # axis: dominant direction of the first dimension line
        dx = abs(dims[0]["e"][0] - dims[0]["s"][0])
        dy = abs(dims[0]["e"][1] - dims[0]["s"][1])
        axis = "X" if dx >= dy else "Y"
        if axis == "X":
            vals = sorted([round(d["s"][0], 2) for d in dims] +
                          [round(d["e"][0], 2) for d in dims])
            perps = [round(d["s"][1], 2) for d in dims] + \
                    [round(d["e"][1], 2) for d in dims]
        else:
            vals = sorted([round(d["s"][1], 2) for d in dims] +
                          [round(d["e"][1], 2) for d in dims])
            perps = [round(d["s"][0], 2) for d in dims] + \
                    [round(d["e"][0], 2) for d in dims]
        # unique, sorted
        seen, uniq = set(), []
        for v in vals:
            if v not in seen:
                seen.add(v)
                uniq.append(v)
        out.append({
            "n": s["n"],
            "axis": axis,
            "perp": round(sum(perps) / len(perps), 2),
            "values": uniq,
            "paperDist": s["dist"] / SCALE,
            "up": (dims[0]["up"][0], dims[0]["up"][1]),
            "ndims": len(dims),
        })
    return out


def compare(expected, generated):
    """Return a list of human-readable diffs (empty = perfect match)."""
    diffs = []
    if len(expected) != len(generated):
        diffs.append(f"COUNT: expected {len(expected)} sets, got {len(generated)}")
    for i, exp in enumerate(expected):
        if i >= len(generated):
            diffs.append(f"SET {i+1}: missing")
            continue
        got = generated[i]
        if exp["axis"] != got["axis"]:
            diffs.append(f"SET {i+1}: axis expected {exp['axis']} got {got['axis']}")
        if abs(exp["paperDist"] - got["paperDist"]) > 0.05:
            diffs.append(
                f"SET {i+1}: paperDist expected {exp['paperDist']:.3f} "
                f"got {got['paperDist']:.3f}")
        ev, gv = exp["values"], got["values"]
        if len(ev) != len(gv):
            diffs.append(
                f"SET {i+1}: {len(ev)} expected values vs {len(gv)}: "
                f"exp={ev} got={gv}")
        else:
            for a, b in zip(ev, gv):
                if abs(a - b) > TOL:
                    diffs.append(
                        f"SET {i+1}: value expected {a:.2f} got {b:.2f} "
                        f"(d={a-b:+.2f})  full exp={ev} got={gv}")
    return diffs


if __name__ == "__main__":
    sets = expected_sets()
    for s in sets:
        print(f"SET {s['n']:>2} axis={s['axis']} perp={s['perp']:>9.2f} "
              f"paperDist={s['paperDist']:>7.2f} up={s['up']} n={len(s['values'])}")
        print(f"        values={s['values']}")
