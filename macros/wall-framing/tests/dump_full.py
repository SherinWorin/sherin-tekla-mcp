"""
Comprehensive GA-drawing dump for studying wall-framing layout drawings.

Captures, per view:
  - view type / name / scale / coordinate systems
  - dimension sets: Distance + child StraightDimension geometry
  - standalone dimensions
  - drawing Parts (model objects shown)
  - Marks (part marks), SectionMarks, Text notes with insertion points
  - the dimension style attributes (scalar values, resolved from nested objects)

Usage: python tests/dump_full.py MARK OUT
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from tekla_bootstrap import (  # noqa: E402
    DrawingHandler,
    StraightDimensionSet,
    Mark,
    SectionMark,
    Text as DwgText,
)
from Tekla.Structures.Drawing import StraightDimension  # noqa: E402
from System import Type as SystemType  # noqa: E402

DWG_PART = SystemType.GetType(
    "Tekla.Structures.Drawing.Part, Tekla.Structures.Drawing"
)


def p3(p):
    return f"({p.X:.2f},{p.Y:.2f},{p.Z:.2f})"


def scalar(v):
    """Render a .NET value compactly, expanding one nesting level."""
    if v is None:
        return "None"
    if isinstance(v, (str, int, float, bool)):
        return repr(v)
    t = v.GetType()
    if t.IsEnum:
        return f"{t.Name}.{v}"
    # nested attribute object -> list its scalar members
    out = []
    for p in t.GetProperties():
        if p.GetIndexParameters():
            continue
        try:
            val = p.GetValue(v, None)
        except Exception:  # noqa: BLE001
            continue
        if val is None:
            continue
        if isinstance(val, (str, int, float, bool)) or val.GetType().IsEnum:
            out.append(f"{p.Name}={val!r}")
    if out:
        return "{" + ", ".join(out) + "}"
    return t.Name


def style_of(attrs):
    """Compact one-line style summary of a dimension's attributes."""
    parts = []
    for prop in attrs.GetType().GetProperties():
        if prop.GetIndexParameters():
            continue
        try:
            val = prop.GetValue(attrs, None)
        except Exception:  # noqa: BLE001
            continue
        if val is None:
            continue
        if isinstance(val, (str, int, float, bool)) or val.GetType().IsEnum:
            parts.append(f"{prop.Name}={val!r}")
        else:
            parts.append(f"{prop.Name}={scalar(val)}")
    return " ".join(parts)


def mark_text(m):
    """Render a Mark's text. Plain Mark exposes Attributes.Content as an
    ordered element collection; Section/Detail marks use Attributes.MarkName."""
    attrs = getattr(m, "Attributes", None)
    if attrs is None:
        return "?"
    name = getattr(attrs, "MarkName", None)
    if name:
        return str(name)
    content = getattr(attrs, "Content", None)
    if content is None:
        return "?"
    out = []
    try:
        it = content.GetEnumerator()
        while it.MoveNext():
            el = it.Current
            v = getattr(el, "Value", None)
            if v is not None:
                out.append(str(v))
            elif type(el).__name__ == "SpaceElement":
                out.append(" ")
    except Exception:  # noqa: BLE001
        try:
            for el in content:
                v = getattr(el, "Value", None)
                if v is not None:
                    out.append(str(v))
        except Exception:  # noqa: BLE001
            return "?"
    return "".join(out) if out else "?"


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

    L: list[str] = [
        f"DRAWING {drawing.Mark} name={drawing.Name!r}",
        f"  title1={drawing.Title1!r} title2={drawing.Title2!r} title3={drawing.Title3!r}",
        f"  revision={drawing.IsFrozen} layout={drawing.Layout is not None}",
    ]

    for v in sheet.GetViews():
        try:
            dcs = v.DisplayCoordinateSystem
            dcs_s = f"origin=({dcs.Origin.X:.3f},{dcs.Origin.Y:.3f},{dcs.Origin.Z:.3f})"
        except Exception as exc:  # noqa: BLE001
            dcs_s = f"ERR {exc}"
        L.append(f"\n=== VIEW {v.ViewType} name={v.Name!r} scale={v.Attributes.Scale}")
        L.append(f"  DCS {dcs_s}")
        try:
            vcs = v.ViewCoordinateSystem
            L.append(f"  VCS origin=({vcs.Origin.X:.3f},{vcs.Origin.Y:.3f},{vcs.Origin.Z:.3f})")
        except Exception:  # noqa: BLE001
            pass

        # ---- dimension sets
        sets = []
        e = v.GetAllObjects(StraightDimensionSet)
        while e.MoveNext():
            sets.append(e.Current)
        L.append(f"  dimsets={len(sets)}")
        for i, ds in enumerate(sets, 1):
            L.append(f"  --- SET {i} Distance={ds.Distance:.4f}")
            try:
                objs = ds.GetObjects()
                while objs.MoveNext():
                    sd = objs.Current
                    if not isinstance(sd, StraightDimension):
                        continue
                    up = sd.UpDirection
                    L.append(
                        f"      DIM start={p3(sd.StartPoint)} end={p3(sd.EndPoint)} "
                        f"up=({up.X:.2f},{up.Y:.2f},{up.Z:.2f}) "
                        f"len={((sd.EndPoint.X-sd.StartPoint.X)**2 + (sd.EndPoint.Y-sd.StartPoint.Y)**2)**0.5:.2f}"
                    )
            except Exception as exc:  # noqa: BLE001
                L.append(f"      GetObjects ERR {exc}")
            L.append(f"      STYLE {style_of(ds.Attributes)}")

        # ---- standalone dims
        singles = []
        e = v.GetAllObjects(StraightDimension)
        while e.MoveNext():
            singles.append(e.Current)
        L.append(f"  standalone_dims={len(singles)}")

        # ---- marks
        marks = []
        e = v.GetAllObjects(Mark)
        while e.MoveNext():
            marks.append(e.Current)
        L.append(f"  marks={len(marks)}")
        for m in marks:
            txt = mark_text(m)
            try:
                ip = p3(m.InsertionPoint)
            except Exception:  # noqa: BLE001
                ip = "?"
            L.append(f"      MARK {txt!r} at {ip}")

        # ---- section marks
        secs = []
        e = v.GetAllObjects(SectionMark)
        while e.MoveNext():
            secs.append(e.Current)
        L.append(f"  sectionmarks={len(secs)}")
        for sm in secs:
            try:
                txt = mark_text(sm)
            except Exception:  # noqa: BLE001
                txt = "?"
            try:
                lp, rp = p3(sm.LeftPoint), p3(sm.RightPoint)
            except Exception:  # noqa: BLE001
                lp = rp = "?"
            L.append(f"      SECTION {txt!r} left={lp} right={rp}")

        # ---- texts
        texts = []
        e = v.GetAllObjects(DwgText)
        while e.MoveNext():
            texts.append(e.Current)
        L.append(f"  texts={len(texts)}")
        for t in texts:
            try:
                ip = p3(t.InsertionPoint)
            except Exception:  # noqa: BLE001
                ip = "?"
            L.append(f"      TEXT {t.TextString!r} at {ip}")

        # ---- drawing parts
        dparts = []
        e = v.GetAllObjects(DWG_PART)
        while e.MoveNext():
            dparts.append(e.Current)
        L.append(f"  drawing_parts={len(dparts)}")
        for p in dparts:
            mid = p.ModelIdentifier
            L.append(f"      PART id={mid.ID}")

    Path(out).write_text("\n".join(L), encoding="utf-8")
    print(f"wrote {out} ({len(L)} lines)")


if __name__ == "__main__":
    dump(
        sys.argv[1] if len(sys.argv) > 1 else "[50]",
        sys.argv[2] if len(sys.argv) > 2 else "tests/reference/full_out.txt",
    )
