"""
Compare the dimension CHAINS of [50] (reference) and [57] (target).

A chain is the sorted list of measured stations along its axis.  Two chains
match when their stations are equal (within 0.05 mm) and they have the
same number of segments.

Usage: python tests/cmp_chains.py
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

from paths import REFERENCE, require  # noqa: E402

TOL = 0.05


def parse(path):
    """Return a list of (distance, axis, stations) from a dims_*.txt dump."""
    out = []
    cur = None
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        m = re.match(r"\s*--- SET (\d+) Distance=([\d.]+)", line)
        if m:
            cur = {"set": int(m.group(1)), "dist": float(m.group(2)),
                   "dims": []}
            out.append(cur)
            continue
        m = re.match(
            r"\s*DIM start=\(([-\d.]+),([-\d.]+),[-\d.]+\) "
            r"end=\(([-\d.]+),([-\d.]+),[-\d.]+\)", line)
        if m and cur is not None:
            x0, y0, x1, y1 = (float(m.group(i)) for i in range(1, 5))
            cur["dims"].append((x0, y0, x1, y1))
    for cur in out:
        dims = cur["dims"]
        if not dims:
            cur["axis"] = "?"
            cur["stations"] = []
            continue
        horiz = abs(dims[0][1] - dims[0][3]) < abs(dims[0][0] - dims[0][2])
        cur["axis"] = "X" if horiz else "Y"
        vals = set()
        for x0, y0, x1, y1 in dims:
            if horiz:
                vals.add(round(min(x0, x1), 2))
                vals.add(round(max(x0, x1), 2))
            else:
                vals.add(round(min(y0, y1), 2))
                vals.add(round(max(y0, y1), 2))
        cur["stations"] = sorted(vals)
    return out


def same(a, b):
    if a["axis"] != b["axis"]:
        return False
    if len(a["stations"]) != len(b["stations"]):
        return False
    for u, v in zip(a["stations"], b["stations"]):
        if abs(u - v) > TOL:
            return False
    return True


def main():
    ref = parse(require(REFERENCE / "dims_50.txt", "the dimension sets of the reference drawing"))
    tgt = parse(require(REFERENCE / "dims_57.txt", "the dimension sets of the generated drawing"))
    print(f"[50] has {len(ref)} sets, [57] has {len(tgt)} sets\n")

    used = set()
    exact = 0
    print("--- chains of [50] matched in [57] ---")
    for r in ref:
        hit = None
        for i, t in enumerate(tgt):
            if i in used:
                continue
            if same(r, t):
                hit = i
                break
        if hit is not None:
            used.add(hit)
            exact += 1
            print(f"  set {r['set']:2} {r['axis']} dist={r['dist']:8.2f} "
                  f"-> [57] set {tgt[hit]['set']:2}  MATCH")
        else:
            print(f"  set {r['set']:2} {r['axis']} dist={r['dist']:8.2f} "
                  f"stations={r['stations']}   (no match)")

    print(f"\n{exact} of {len(ref)} chains of [50] reproduced exactly in [57]")

    print("\n--- chains of [57] with no counterpart in [50] ---")
    for i, t in enumerate(tgt):
        if i not in used:
            print(f"  [57] set {t['set']:2} {t['axis']} dist={t['dist']:8.2f} "
                  f"stations={t['stations']}")


if __name__ == "__main__":
    sys.exit(main())
