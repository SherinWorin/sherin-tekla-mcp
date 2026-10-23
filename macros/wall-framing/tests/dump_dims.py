"""
Dump the dimension sets of a GA drawing view: for each set, its child
StraightDimension objects (start/end points, up direction, distance) plus the
set's Distance and the view's coordinate system.

Usage: python tests/dump_dims.py [MARK] [OUT]
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from tekla_bootstrap import (  # noqa: E402
    DrawingHandler,
    StraightDimensionSet,
)
from Tekla.Structures.Drawing import StraightDimension  # noqa: E402
from Tekla.Structures.Geometry3d import PointList  # noqa: E402
from System import Type as SystemType  # noqa: E402


def f(p):
    return f"({p.X:.2f},{p.Y:.2f},{p.Z:.2f})"


def dump(mark, out):
    dh = DrawingHandler()
    target = None
    for d in dh.GetDrawings():
        if d.Mark == mark:
            target = d
            break
    if target is None:
        print(f"{mark} not found")
        return
    dh.SetActiveDrawing(target)
    drawing = dh.GetActiveDrawing()
    sheet = drawing.GetSheet()

    lines = [f"DRAWING {drawing.Mark} name={drawing.Name}"]
    for v in sheet.GetViews():
        try:
            dcs = v.DisplayCoordinateSystem
            dcs_s = (
                f"origin=({dcs.Origin.X:.3f},{dcs.Origin.Y:.3f},{dcs.Origin.Z:.3f}) "
                f"axisX=({dcs.AxisX.X:.3f},{dcs.AxisX.Y:.3f},{dcs.AxisX.Z:.3f}) "
                f"axisY=({dcs.AxisY.X:.3f},{dcs.AxisY.Y:.3f},{dcs.AxisY.Z:.3f})"
            )
        except Exception as exc:  # noqa: BLE001
            dcs_s = f"ERR {exc}"
        lines.append(f"\n=== VIEW {v.ViewType} name={v.Name!r} scale={v.Attributes.Scale}")
        lines.append(f"  DCS {dcs_s}")
        try:
            vcs = v.ViewCoordinateSystem
            lines.append(
                f"  VCS origin=({vcs.Origin.X:.3f},{vcs.Origin.Y:.3f},{vcs.Origin.Z:.3f})"
            )
        except Exception:  # noqa: BLE001
            pass

        sets = []
        enum = v.GetAllObjects(StraightDimensionSet)
        while enum.MoveNext():
            sets.append(enum.Current)
        lines.append(f"  dimsets={len(sets)}")

        for i, ds in enumerate(sets, 1):
            lines.append(f"  --- SET {i} Distance={ds.Distance:.4f}")
            try:
                objs = ds.GetObjects()
                while objs.MoveNext():
                    sd = objs.Current
                    if not isinstance(sd, StraightDimension):
                        continue
                    up = sd.UpDirection
                    lines.append(
                        f"      DIM start={f(sd.StartPoint)} end={f(sd.EndPoint)} "
                        f"up=({up.X:.2f},{up.Y:.2f},{up.Z:.2f}) dist={sd.Distance:.3f}"
                    )
            except Exception as exc:  # noqa: BLE001
                lines.append(f"      GetObjects ERR {exc}")

        # standalone dimensions (not in a set)
        singles = []
        enum2 = v.GetAllObjects(StraightDimension)
        while enum2.MoveNext():
            singles.append(enum2.Current)
        lines.append(f"  standalone straight dimensions={len(singles)}")
        for sd in singles:
            try:
                dss = sd.GetDimensionSet()
            except Exception:  # noqa: BLE001
                dss = None
            if dss is None:
                up = sd.UpDirection
                lines.append(
                    f"      STANDALONE start={f(sd.StartPoint)} end={f(sd.EndPoint)} "
                    f"up=({up.X:.2f},{up.Y:.2f},{up.Z:.2f}) dist={sd.Distance:.3f}"
                )

    Path(out).write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {out} ({len(lines)} lines)")


if __name__ == "__main__":
    mark = sys.argv[1] if len(sys.argv) > 1 else "[50]"
    out = sys.argv[2] if len(sys.argv) > 2 else "tests/reference/dims_out.txt"
    dump(mark, out)
