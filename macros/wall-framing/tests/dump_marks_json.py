"""
Dump the 11 BP4-WP* wall-panel marks of GA drawing [50] to JSON, so the
rules can be unit-tested against them without a running Tekla.

Read-only.  Output: tests/reference/marks_50.json
Usage: python tests/dump_marks_json.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import REFERENCE  # noqa: E402

from tekla_bootstrap import DrawingHandler, Mark  # noqa: E402
from Tekla.Structures.Model import Model  # noqa: E402
from Tekla.Structures.Model import ModelObject  # noqa: E402

OUT = REFERENCE / "marks_50.json"


def main():
    dh = DrawingHandler()
    tgt = [d for d in dh.GetDrawings() if d.Mark == "[50]"][0]
    dh.SetActiveDrawing(tgt)
    drawing = dh.GetActiveDrawing()
    view = list(drawing.GetSheet().GetViews())[0]

    model = Model()
    sel = model.GetModelObjectSelector()
    parts = {}
    for ename in ("BEAM", "CONTOURPLATE"):
        e = sel.GetAllObjectsWithType(getattr(ModelObject.ModelObjectEnum, ename))
        while e.MoveNext():
            p = e.Current
            if p is not None:
                parts[p.Identifier.ID] = p

    rows = []
    e = view.GetAllObjects(Mark)
    while e.MoveNext():
        m = e.Current
        # the ASSEMBLY_POS value of the mark
        apos = None
        content = m.Attributes.Content
        if content is not None:
            it = content.GetEnumerator()
            while it.MoveNext():
                el = it.Current
                if getattr(el, "Name", None) == "ASSEMBLY_POS":
                    apos = el.Value
        if not apos or not str(apos).startswith("BP4-WP"):
            continue

        # the model part the mark is attached to
        model_id = None
        rel = m.GetRelatedObjects()
        while rel.MoveNext():
            o = rel.Current
            mid = getattr(o, "ModelIdentifier", None)
            if mid is None:
                continue
            raw = getattr(mid, "ID", None)
            if raw is None or int(raw) == 0:
                continue
            model_id = int(raw)
            break

        pt = parts.get(model_id) if model_id else None
        bt = None
        if pt is not None:
            s = pt.GetSolid()
            bt = [s.MinimumPoint.X, s.MinimumPoint.Y, s.MaximumPoint.X, s.MaximumPoint.Y]

        pl = m.Placing
        st = getattr(pl, "StartPoint", None)
        en = getattr(pl, "EndPoint", None)
        rows.append({
            "apos": str(apos),
            "model_id": model_id,
            "part_name": pt.Name if pt is not None else None,
            "bottom_track": bt,
            "ins": [m.InsertionPoint.X, m.InsertionPoint.Y],
            "angle": m.Attributes.Angle,
            "start": [st.X, st.Y] if st is not None else None,
            "end": [en.X, en.Y] if en is not None else None,
        })

    rows.sort(key=lambda r: r["apos"])
    OUT.write_text(json.dumps(rows, indent=2), encoding="utf-8")
    print(f"wrote {len(rows)} marks to {OUT}")


if __name__ == "__main__":
    main()
