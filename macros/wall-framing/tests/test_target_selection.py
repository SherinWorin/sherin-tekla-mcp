"""
Test of the TARGET SELECTION of the wall-framing macro.

The macro must not work on a fixed drawing mark.  It has to:
  1. work on the active drawing when that is a wall layout drawing
  2. find and activate the wall layout drawing when the active drawing is
     something else (here: [56], the BASE FRAMING LAYOUT)
  3. REFUSE a drawing that is issued or frozen (here: [50], the issued
     reference drawing) - this is what keeps the macro away from [50]

Usage: python tests/test_target_selection.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN  # noqa: E402
from tekla_bootstrap import TEKLA_BIN, DrawingHandler  # noqa: E402

import clr  # noqa: E402
from System.Reflection import Assembly  # noqa: E402
import System  # noqa: E402
import Tekla.Structures.Drawing as D  # noqa: E402

DLL = BIN / "P142_WF_AutoDimension.dll"
RULES_DLL = BIN / "WF_Rules.dll"


def load():
    Assembly.LoadFrom(str(RULES_DLL))
    for n in ("Tekla.Structures.dll", "Tekla.Structures.Drawing.dll",
              "Tekla.Structures.Model.dll", "Tekla.Macros.Runtime.dll"):
        Assembly.LoadFrom(str(TEKLA_BIN / n))
    asm = Assembly.LoadFrom(str(DLL))
    macro_t = asm.GetType("UserMacros.Macro")
    return macro_t, macro_t.GetNestedType("Result"), macro_t.GetMethod("Execute")


def counts(dr):
    """(dimsets, marks, sectionmarks, texts) of the first view of a drawing."""
    views = list(dr.GetSheet().GetViews())
    if not views:
        return (0, 0, 0, 0)
    view = views[0]
    out = []
    for cls in (D.StraightDimensionSet, D.Mark, D.SectionMark, D.Text):
        n = 0
        e = view.GetAllObjects(cls)
        while e.MoveNext():
            n += 1
        out.append(n)
    return tuple(out)


def is_wall_layout(dr):
    """The same rule as the macro: the title holds a wall layout fragment.

    Note that the panel drawings of the model are named WALL_PANEL and are
    therefore NOT wall layout drawings.
    """
    text = " ".join(str(x or "") for x in
                    (dr.Title1, dr.Title2, dr.Title3, dr.Name)).upper()
    return any(f in text for f in
               ("WALL FRAMING", "WALL LAYOUT", "WALL FRAMING LAYOUT"))


def main():
    dh = DrawingHandler()
    if not dh.IsAnyDrawingOpen():
        print("FATAL: no drawing editor is open.")
        return 2

    macro_t, result_t, execute = load()
    drawings = {d.Mark.replace(" ", ""): d for d in dh.GetDrawings()}
    print(f"the model has {len(drawings)} drawings.  The interesting ones:")
    for m, d in sorted(drawings.items()):
        if not (is_wall_layout(d) or d.IsFrozen or d.IsIssued):
            continue
        print(f"   {m:6} {d.Title2 or '':24} frozen={d.IsFrozen} "
              f"issued={d.IsIssued}  wall-layout={is_wall_layout(d)}")
    print()

    failures = 0

    # ------------------------------------------------------------------
    # 1. the active drawing is NOT a wall layout drawing
    # ------------------------------------------------------------------
    other = None
    for m, d in sorted(drawings.items()):
        if is_wall_layout(d) or d.IsFrozen or d.IsIssued:
            continue
        if not list(d.GetSheet().GetViews()):
            continue          # a drawing without a view is of no use here
        other = d
        break
    if other is None:
        print("SKIP test 1: the model has no writable non wall-layout drawing.")
    else:
        print(f"--- test 1: the active drawing is {other.Mark} "
              f"({other.Title2 or other.Name}) - not a wall layout drawing")
        dh.SetActiveDrawing(other)
        dr = dh.GetActiveDrawing()
        before = counts(dr)
        result = System.Activator.CreateInstance(result_t)
        execute.Invoke(None, [dr, result])
        log = str(result.Log).strip().splitlines()
        after = dh.GetActiveDrawing()
        print(f"    log          : {log[0] if log else '(empty)'}")
        print(f"    active now   : {after.Mark}")
        print(f"    other drawing: {before} -> {counts(other)}")
        switched = after.Mark != other.Mark and is_wall_layout(after)
        untouched = counts(other) == before
        ok = switched and untouched
        failures += 0 if ok else 1
        print(f"    -> {'PASS' if ok else 'FAIL'}: switched to the wall layout "
              f"drawing and left {other.Mark} alone")
        if switched:
            dh.SaveActiveDrawing()

    # ------------------------------------------------------------------
    # 2. the active drawing is the ISSUED reference drawing
    # ------------------------------------------------------------------
    # It has to be a WALL LAYOUT drawing that is issued or frozen: a frozen
    # drawing of another kind is simply not the target of the macro, and
    # the macro correctly leaves it alone.
    frozen = None
    for m, d in sorted(drawings.items()):
        if is_wall_layout(d) and (d.IsFrozen or d.IsIssued):
            frozen = d
            break
    if frozen is None:
        print("\nSKIP test 2: the model has no issued/frozen wall layout "
              "drawing.")
    else:
        print(f"\n--- test 2: the active drawing is {frozen.Mark} "
              f"(frozen={frozen.IsFrozen}, issued={frozen.IsIssued})")
        dh.SetActiveDrawing(frozen)
        dr = dh.GetActiveDrawing()
        before = counts(dr)
        result = System.Activator.CreateInstance(result_t)
        execute.Invoke(None, [dr, result])
        after_counts = counts(frozen)
        print(f"    ok={result.Ok}  made={result.Made}")
        print(f"    message: {result.Message}")
        print(f"    content: {before} -> {after_counts}")
        refused = (not result.Ok) and result.Made == 0 and \
            "issued or frozen" in str(result.Message)
        untouched = before == after_counts
        ok = refused and untouched
        failures += 0 if ok else 1
        print(f"    -> {'PASS' if ok else 'FAIL'}: refused to write into the "
              f"issued drawing and left it unchanged")

    # put the wall layout drawing back as the active one
    for m, d in sorted(drawings.items()):
        if m == "[57]":
            dh.SetActiveDrawing(d)
            break

    print()
    print("FAILURES: %d" % failures)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
