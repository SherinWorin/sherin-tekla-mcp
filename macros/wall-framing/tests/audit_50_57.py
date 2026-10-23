"""
Verify the state of the reference drawing [50] and the target drawing [57].

Reports for both drawings:
  * the number of dimension sets, marks, section marks and texts
  * the wall-panel marks: their ASSEMBLY_POS, text angle, leader geometry
  * the section marks: their name and geometry

Read-only.

Usage: python tests/audit_50_57.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from tekla_bootstrap import DrawingHandler  # noqa: E402
import Tekla.Structures.Drawing as D  # noqa: E402


def content_value(m, prop):
    c = m.Attributes.Content
    if c is None:
        return None
    it = c.GetEnumerator()
    while it.MoveNext():
        el = it.Current
        if getattr(el, "Name", None) == prop:
            return str(el.Value)
    return None


def audit(dr, label):
    view = list(dr.GetSheet().GetViews())[0]
    print(f"\n########## {label} : {dr.Mark}  {dr.Name} ##########")
    print(f"  view '{view.Name}' scale 1:{view.Attributes.Scale}")

    counts = {}
    for cls, key in ((D.StraightDimensionSet, "dimsets"),
                     (D.Mark, "marks"),
                     (D.SectionMark, "sectionmarks"),
                     (D.Text, "texts")):
        n = 0
        e = view.GetAllObjects(cls)
        while e.MoveNext():
            n += 1
        counts[key] = n
    print("  " + "  ".join(f"{k}={v}" for k, v in counts.items()))

    print("  -- wall-panel marks --")
    rows = []
    e = view.GetAllObjects(D.Mark)
    while e.MoveNext():
        m = e.Current
        v = content_value(m, "ASSEMBLY_POS")
        if not v:
            continue
        a = m.Attributes
        pl = m.Placing
        geo = ""
        if pl is not None and hasattr(pl, "StartPoint"):
            geo = (f"({pl.StartPoint.X:.2f},{pl.StartPoint.Y:.2f})->"
                   f"({pl.EndPoint.X:.2f},{pl.EndPoint.Y:.2f})")
        rows.append((v, a.Angle, a.RotationAngle, geo))
    for v, ang, rot, geo in sorted(rows):
        print(f"     {v:12} Angle={ang:6.1f} Rot={rot:6.1f}  {geo}")

    print("  -- section marks --")
    e = view.GetAllObjects(D.SectionMark)
    srows = []
    while e.MoveNext():
        s = e.Current
        srows.append((s.Attributes.MarkName, s.LeftPoint, s.RightPoint))
    for name, lp, rp in sorted(srows):
        print(f"     {name:5} ({lp.X:9.2f},{lp.Y:9.2f}) -> "
              f"({rp.X:9.2f},{rp.Y:9.2f})")
    return counts


def main():
    dh = DrawingHandler()
    drawings = list(dh.GetDrawings())
    out = {}
    for mk in ("[50]", "[57]"):
        tgt = [d for d in drawings if d.Mark.replace(" ", "") == mk]
        if not tgt:
            print(f"{mk} not found")
            continue
        dh.SetActiveDrawing(tgt[0])
        out[mk] = audit(dh.GetActiveDrawing(), mk)
    return 0


if __name__ == "__main__":
    sys.exit(main())
