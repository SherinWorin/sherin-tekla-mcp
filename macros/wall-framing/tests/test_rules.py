"""
Unit test of the wall-framing annotation rules (source/WF_Rules.cs).

The rules assembly has no Tekla dependency, so it can be tested without a
running Tekla Structures.  The test feeds the rules the real model parts of
P142-COREL REEF_BP4_MOCKUP-R00 (cached in tests/reference/asm_parts.txt) and compares
the generated dimension chains against the dimension sets that GA drawing
[50] actually contains (cached in tests/reference/full_50.txt).

Usage: python tests/test_rules.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN, REFERENCE, PRO_RULES_DLL, require  # noqa: E402
from expected import expected_sets  # noqa: E402

import clr  # noqa: E402
from System.Reflection import Assembly  # noqa: E402
import System  # noqa: E402
from System.Collections import ArrayList  # noqa: E402

TOL = 0.5
PANEL_PREFIX = "BP4-WP"

RULES_DLL = BIN / "WF_Rules.dll"
PARTS = REFERENCE / "asm_parts.txt"


def load_parts():
    out = []
    require(PARTS, "the dump of the wall parts")
    for line in PARTS.read_text(encoding="utf-8").splitlines():
        f = line.split("|")
        if len(f) < 8:
            continue
        name, _cls, pid, x0, y0, x1, y1, apos = f[:8]
        out.append({
            "name": name, "id": int(pid),
            "x0": float(x0), "y0": float(y0),
            "x1": float(x1), "y1": float(y1),
            "apos": apos,
        })
    return out


def static_call(rules, name, args):
    """Call a static method of the rules type through reflection."""
    return rules.GetMethod(name).Invoke(None, args)


def build(asm):
    rules = asm.GetType("P142.WallFraming.WfRules")
    part_t = asm.GetType("P142.WallFraming.WfPart")

    parts = System.Collections.Generic.List[part_t]()
    for p in load_parts():
        if not p["apos"].startswith(PANEL_PREFIX):
            continue
        o = System.Activator.CreateInstance(part_t)
        o.Name = p["name"]
        o.Id = p["id"]
        o.AssemblyPos = p["apos"]
        o.X0 = p["x0"]
        o.Y0 = p["y0"]
        o.X1 = p["x1"]
        o.Y1 = p["y1"]
        parts.Add(o)

    panels = static_call(rules, "BuildPanels", [parts, PANEL_PREFIX, TOL])
    return rules, panels


def main():
    if not RULES_DLL.exists():
        print(f"FATAL: {RULES_DLL} missing - build it first")
        return 2
    asm = Assembly.LoadFrom(str(RULES_DLL))
    rules, panels = build(asm)

    print(f"panels: {panels.Count}")
    exp = expected_sets()

    generated = []
    for i in range(panels.Count):
        p = panels[i]
        chain = static_call(rules, "ChainStations", [p, TOL])
        vals = [round(float(v), 2) for v in chain]
        axis = "X" if p.Horiz else "Y"
        generated.append({"axis": axis, "values": vals, "panel": p.Name})
        print(f"  {p.Name:10} axis={axis} n={len(vals):2} {vals}")

    # --- the band chains -----------------------------------------------------
    # the layout bounding box, from the absolute extents of every part
    allp = load_parts()
    bx0 = min(p["x0"] for p in allp)
    by0 = min(p["y0"] for p in allp)
    bx1 = max(p["x1"] for p in allp)
    by1 = max(p["y1"] for p in allp)
    print(f"\nlayout bounding box: X {bx0:.2f}..{bx1:.2f}  Y {by0:.2f}..{by1:.2f}")

    band_edge = asm.GetType("P142.WallFraming.WfBandEdge")
    band_names = {"Bottom": 0, "Top": 1, "Left": 2, "Right": 3}
    bands = []
    # the TOP edge of the layout is the top outline, not a band chain
    for nm in ("Bottom", "Left", "Right"):
        ev = System.Enum.ToObject(band_edge, band_names[nm])
        vals = static_call(rules, "BandStations",
                           [panels, ev, bx0, by0, bx1, by1, TOL])
        vals = [round(float(v), 2) for v in vals]
        axis = "X" if nm in ("Bottom", "Top") else "Y"
        bands.append({"axis": axis, "values": vals, "name": "band " + nm})
        print(f"  band {nm:6} axis={axis} n={len(vals):2} {vals}")

    # the top outline of the layout (a staircase of the upper boundary)
    #
    # The Pro rules are not in the public repository.  When the private
    # sources have been built, WF_Rules_Pro.dll holds them and the Pro
    # chains are checked here as well.
    pro = None
    if PRO_RULES_DLL.exists():
        pro_asm = Assembly.LoadFrom(str(PRO_RULES_DLL))
        pro = pro_asm.GetType("P142.WallFraming.WfRulesPro")
    if pro is None:
        print("\n  (the Pro rules are not built here - only the free chains "
              "are checked)")

    if pro is not None:
        top = [round(float(v), 2)
               for v in static_call(pro, "TopOutlineStations", [panels, 2.0, TOL])]
        bands.append({"axis": "X", "values": top, "name": "top outline"})
        print(f"  top outline axis=X n={len(top):2} {top}")

        poly_sys = static_call(pro, "TopOutlinePolyline", [panels, 2.0, TOL])
        poly = [round(float(v), 2) for v in poly_sys]
        print(f"  top outline polyline: {list(zip(poly[0::2], poly[1::2]))}")

        # the north run: the run of the northern wall, on its own level
        run = static_call(pro, "NorthRun", [panels, 2.0, TOL])
        if run is None:
            print("  north run: none")
        else:
            vals = [round(float(v), 2) for v in run.Stations]
            print(f"  north run axis=X level={run.Level:.2f} n={len(vals)} {vals}")
            bands.append({"axis": "X", "values": vals, "name": "north run"})

        # the two overall dimensions of the layout
        ov = static_call(pro, "Overall", [bx0, by0, bx1, by1, poly_sys])
        hvals = [round(float(ov.Hx0), 2), round(float(ov.Hy0), 2),
                 round(float(ov.Hx1), 2), round(float(ov.Hy1), 2)]
        bands.append({"axis": "Y",
                      "values": sorted([hvals[1], hvals[3]]),
                      "name": "overall height"})
        print(f"  overall height axis=Y {sorted([hvals[1], hvals[3]])}")
        wvals = [round(float(ov.Wx0), 2), round(float(ov.Wy0), 2),
                 round(float(ov.Wx1), 2), round(float(ov.Wy1), 2)]
        bands.append({"axis": "X",
                      "values": sorted([wvals[0], wvals[2]]),
                      "name": "overall width"})
        print(f"  overall width  axis=X {sorted([wvals[0], wvals[2]])}")

    # --- compare against the measured sets -----------------------------------
    print("\n=== matching generated chains against the measured sets of [50] ===")
    used = set()
    exact = 0
    for g in generated + bands:
        best = None
        for idx, e in enumerate(exp):
            if idx in used:
                continue
            if e["axis"] != g["axis"]:
                continue
            ev = e["values"]
            if len(ev) != len(g["values"]):
                continue
            err = max(abs(a - b) for a, b in zip(ev, g["values"]))
            if err < 0.4:
                if best is None or err < best[2]:
                    best = (idx, e, err)
        if best:
            idx, e, err = best
            used.add(idx)
            exact += 1
            print(f"  {g.get('panel', g.get('name')):10} -> SET {e['n']:>2}  EXACT  "
                  f"(paperDist {e['paperDist']:.2f})")
        else:
            print(f"  {g.get('panel', g.get('name')):10} -> no measured set matches "
                  f"{g['values']}")

    print(f"\nexact chain matches: {exact} / {len(generated) + len(bands)}")
    print(f"measured sets not explained: "
          f"{sorted(e['n'] for i, e in enumerate(exp) if i not in used)}")

    # --- the automatic panel detection ---------------------------------------
    #
    # On another computer the model may use another panel prefix.  When the
    # configured prefix matches nothing, the panels are found automatically:
    # a wall assembly is a group of parts with a bottom track, a top track,
    # a stud and a gypsum board.  The ceiling panels of the model must NOT
    # be taken for wall panels.
    print("\n=== the automatic panel detection ===")
    part_t = asm.GetType("P142.WallFraming.WfPart")
    all_parts = System.Collections.Generic.List[part_t]()
    for p in load_parts():
        o = System.Activator.CreateInstance(part_t)
        o.Name = p["name"]
        o.Id = p["id"]
        o.AssemblyPos = p["apos"]
        o.X0 = p["x0"]
        o.Y0 = p["y0"]
        o.X1 = p["x1"]
        o.Y1 = p["y1"]
        all_parts.Add(o)

    wrong = static_call(rules, "BuildPanels", [all_parts, "NO_SUCH_PREFIX", TOL])
    print(f"  BuildPanels with a wrong prefix : {wrong.Count} panels "
          f"(0 is correct)")
    auto = static_call(rules, "BuildPanelsAuto", [all_parts])
    names = sorted(auto[i].Name for i in range(auto.Count))
    print(f"  BuildPanelsAuto                 : {auto.Count} panels {names}")
    ok_auto = (wrong.Count == 0 and auto.Count == len(panels)
               and names == sorted(p.Name for p in panels))
    print(f"  -> {'PASS' if ok_auto else 'FAIL'}: the automatic detection finds "
          f"the same panels as the prefix")

    return 0 if ok_auto else 1


if __name__ == "__main__":
    sys.exit(main())
