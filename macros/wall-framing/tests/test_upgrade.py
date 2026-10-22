"""
Test of the UPGRADE of the wall-framing macro on a model that has many wall
layout drawings.

The test READS the model and the drawings; it writes NOTHING.  It checks
that the macro, run against a drawing of the model, would:
  1. pick the wall panels of THAT drawing only - a model has one wall
     layout drawing per block, and the blocks are far apart;
  2. put the chains in the right place;
  3. measure only stations that the hand-made drawing of that block also
     measures.

The hand-made drawings of a block put the whole wall line in ONE chain
(the panels of the line follow each other), while the macro gives every
wall panel its own chain.  The two therefore never match exactly: the
chain of the macro has to be a SUBSET of a chain of the drawing.  The
report says how many chains are a subset, and it lists the stations the
macro measures that the drawing does not measure at all - those are the
places where the rules of the macro are wrong.

Usage: python tests/test_upgrade.py [DRAWING NAME]
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN  # noqa: E402
from tekla_bootstrap import TEKLA_BIN, DrawingHandler  # noqa: E402

import clr  # noqa: E402
import System  # noqa: E402
from System.Reflection import Assembly  # noqa: E402
import Tekla.Structures.Drawing as D  # noqa: E402

TOL = 0.4


def load():
    for n in ("Tekla.Structures.dll", "Tekla.Structures.Drawing.dll",
              "Tekla.Structures.Model.dll", "Tekla.Macros.Runtime.dll"):
        Assembly.LoadFrom(str(TEKLA_BIN / n))
    rules = Assembly.LoadFrom(str(BIN / "WF_Rules.dll"))
    for n in ("WF_License.dll", "WF_Core.dll"):
        Assembly.LoadFrom(str(BIN / n))
    core = Assembly.LoadFrom(str(BIN / "WF_Core.dll"))
    return (core.GetType("P142.WallFraming.WfCore"),
            rules.GetType("P142.WallFraming.WfRules"))


def is_wall_layout(dr):
    text = " ".join(str(x or "") for x in
                    (dr.Title1, dr.Title2, dr.Title3, dr.Name)).upper()
    return any(f in text for f in
               ("WALL FRAMING", "WALL LAYOUT", "WALL FRAMING LAYOUT"))


def plan_view(dr):
    for v in dr.GetSheet().GetViews():
        cs = None
        try:
            cs = v.DisplayCoordinateSystem
        except Exception:  # noqa: BLE001
            pass
        if cs is None:
            continue
        if abs(cs.AxisX.Z) < 1e-3 and abs(cs.AxisY.Z) < 1e-3:
            return v
    return None


def sets_of(view):
    """The dimension sets of the view, as (axis, stations)."""
    out = []
    en = view.GetAllObjects(D.StraightDimensionSet)
    while en.MoveNext():
        s = en.Current
        if s is None:
            continue
        pts = []
        sub = s.GetObjects()
        while sub.MoveNext():
            dd = sub.Current
            if dd is None:
                continue
            pts.append((dd.StartPoint.X, dd.StartPoint.Y))
            pts.append((dd.EndPoint.X, dd.EndPoint.Y))
        if len(pts) < 2:
            continue
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        horiz = (max(xs) - min(xs)) > (max(ys) - min(ys))
        vals = sorted({round(v, 2) for v in (xs if horiz else ys)})
        out.append(("X" if horiz else "Y", tuple(vals)))
    return out


def main():
    wanted = sys.argv[1] if len(sys.argv) > 1 else ""
    dh = DrawingHandler()
    core_t, rules_t = load()

    drawings = []
    for d in dh.GetDrawings():
        if d is None:
            continue
        if is_wall_layout(d) and not (d.IsFrozen or d.IsIssued):
            drawings.append(d)
    if not drawings:
        print("FATAL: the model has no writable wall layout drawing.")
        return 2
    drawings.sort(key=lambda d: d.Name or "")
    if wanted:
        drawings = [d for d in drawings
                    if wanted.lower() in (d.Name or "").lower()]
        if not drawings:
            print("FATAL: no drawing matches", wanted)
            return 2

    import Tekla.Structures.Model as TSM
    model = TSM.Model()

    n_chains = n_subset = n_exact = 0
    n_wrong = 0
    failures = 0
    wrong = []

    for dr in drawings:
        view = plan_view(dr)
        if view is None:
            print(f"{dr.Name}: no plan view - skipped")
            continue

        # ---- what the macro would do with this drawing
        core_t.GetMethod("ReadWallParts").Invoke(None, [view, model])
        core_t.GetMethod("SelectPanels").Invoke(None, [view])
        panels = core_t.GetField("Panels").GetValue(None)

        if panels.Count == 0:
            print(f"{dr.Name}: NO WALL PANEL - the macro would leave it alone")
            failures += 1
            continue

        names = [panels[i].Name for i in range(panels.Count)]
        blocks = set(n.split("-")[0] for n in names)

        # ---- the chains of the free edition
        chains = []
        for i in range(panels.Count):
            p = panels[i]
            vals = [round(float(v), 2) for v in
                    rules_t.GetMethod("ChainStations").Invoke(None, [p, 0.5])]
            if len(vals) >= 2:
                chains.append(("X" if p.Horiz else "Y", tuple(vals), p.Name))

        # the box of THIS layout
        bx0 = min(panels[i].XLo for i in range(panels.Count))
        by0 = min(panels[i].YLo for i in range(panels.Count))
        bx1 = max(panels[i].XHi for i in range(panels.Count))
        by1 = max(panels[i].YHi for i in range(panels.Count))

        band_t = rules_t.GetMethod("BandStations")
        band_edge = rules_t.Assembly.GetType("P142.WallFraming.WfBandEdge")
        for nm, code in (("Bottom", 0), ("Left", 2), ("Right", 3)):
            ev = System.Enum.ToObject(band_edge, code)
            vals = [round(float(v), 2) for v in
                    band_t.Invoke(None, [panels, ev, bx0, by0, bx1, by1, 0.5])]
            if len(vals) >= 2:
                chains.append(("X" if nm == "Bottom" else "Y", tuple(vals),
                               "band " + nm))

        # ---- the hand-made chains of the drawing
        handmade = sets_of(view)
        every = set()
        for _axis, vals in handmade:
            every.update(vals)

        subset = exact = 0
        for axis, vals, name in chains:
            n_chains += 1
            # a chain of the macro is right when every station of it is
            # measured by the drawing
            miss = [v for v in vals
                    if not any(abs(v - w) < TOL for w in every)]
            if miss:
                n_wrong += len(miss)
                wrong.append((dr.Name, name, axis, miss))
            # ... and when it is contained in one chain of the drawing
            if any(a2 == axis and len(v2) >= len(vals) and
                   all(any(abs(a - b) < TOL for b in v2) for a in vals)
                   for a2, v2 in handmade):
                subset += 1
            if any(a2 == axis and len(v2) == len(vals) and
                   all(abs(a - b) < TOL for a, b in zip(vals, v2))
                   for a2, v2 in handmade):
                exact += 1
        n_subset += subset
        n_exact += exact

        print(f"{dr.Name:12} {dr.Mark:7} panels={panels.Count:3} "
              f"chains={len(chains):3} hand-made sets={len(handmade):3} "
              f"exact={exact:3} subset={subset:3}  blocks={sorted(blocks)}")
        if len(blocks) > 1:
            print("     FAIL: more than one block in this drawing")
            failures += 1

    print()
    print(f"the chains of the macro are a SUBSET of a hand-made chain in "
          f"{n_subset} of {n_chains} cases "
          f"({100.0 * n_subset / max(1, n_chains):.0f} %)")
    print(f"the chains of the macro are the SAME as a hand-made chain in "
          f"{n_exact} of {n_chains} cases")
    print(f"stations the macro measures that the drawing does not: {n_wrong}")
    for name, chain, axis, miss in wrong:
        print(f"   {name:12} {chain:12} {axis} {miss}")
    print("NOTE: this test READS only - it wrote nothing into the model.")
    print("FAILURES: %d" % failures)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
