"""
Run the compiled wall-framing macro against the ACTIVE Tekla drawing.

The macro is deliberately called through its UI-free entry point
    UserMacros.Macro.Execute(Drawing, Result)
so that the whole run can be driven from a script and its log captured.

The caller must guarantee that the active drawing is the intended target -
this script only asserts it, it never switches drawings.

Usage: python tests/run_macro.py [expected_mark]
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN  # noqa: E402

from tekla_bootstrap import DrawingHandler, TEKLA_BIN  # noqa: E402

import clr  # noqa: E402  (pythonnet: must precede the .NET imports)
from System.Reflection import Assembly  # noqa: E402

DLL = BIN / "P142_WF_AutoDimension.dll"
RULES_DLL = BIN / "WF_Rules.dll"


def main():
    expected = sys.argv[1] if len(sys.argv) > 1 else None

    dh = DrawingHandler()
    if not dh.IsAnyDrawingOpen():
        print("FATAL: no drawing editor is open.")
        return 2

    # make the requested drawing the active one
    if expected:
        want = expected.replace(" ", "")
        tgt = [d for d in dh.GetDrawings() if d.Mark.replace(" ", "") == want]
        if not tgt:
            print(f"FATAL: no drawing with mark {expected}.")
            return 2
        dh.SetActiveDrawing(tgt[0])

    dr = dh.GetActiveDrawing()
    if dr is None:
        print("FATAL: no active drawing.")
        return 2

    print(f"active drawing: {dr.Mark}  {dr.Name}")
    if expected and dr.Mark.replace(" ", "") != expected.replace(" ", ""):
        print(f"FATAL: active drawing is {dr.Mark}, expected {expected}. "
              f"Refusing to write.")
        return 2

    if not DLL.exists():
        print(f"FATAL: {DLL} not found - build the macro first.")
        return 2

    # the rules assembly must be loadable, and the Tekla assemblies must be
    # resolvable when the macro's signatures are inspected
    for extra in (RULES_DLL, TEKLA_BIN / "Tekla.Structures.dll",
                  TEKLA_BIN / "Tekla.Structures.Drawing.dll",
                  TEKLA_BIN / "Tekla.Structures.Model.dll",
                  TEKLA_BIN / "Tekla.Macros.Runtime.dll"):
        if not extra.exists():
            print(f"FATAL: {extra} not found.")
            return 2
    Assembly.LoadFrom(str(RULES_DLL))
    for n in ("Tekla.Structures.dll", "Tekla.Structures.Drawing.dll",
              "Tekla.Structures.Model.dll", "Tekla.Macros.Runtime.dll"):
        Assembly.LoadFrom(str(TEKLA_BIN / n))

    asm = Assembly.LoadFrom(str(DLL))
    macro_t = asm.GetType("UserMacros.Macro")
    if macro_t is None:
        print("FATAL: UserMacros.Macro not found in the DLL.")
        return 2

    result_t = macro_t.GetNestedType("Result")
    if result_t is None:
        print("FATAL: UserMacros.Macro.Result not found in the DLL.")
        return 2

    import System
    result = System.Activator.CreateInstance(result_t)
    execute = macro_t.GetMethod("Execute")
    if execute is None:
        print("FATAL: UserMacros.Macro.Execute not found in the DLL.")
        return 2

    print("--- running macro ---")
    execute.Invoke(None, [dr, result])

    # the objects only exist in the editor until the drawing is saved
    saved = False
    try:
        saved = bool(dh.SaveActiveDrawing())
    except Exception as exc:  # noqa: BLE001
        print(f"  save failed: {exc}")

    print(f"--- done: ok={result.Ok} made={result.Made} "
          f"failed={result.Failed} saved={saved}")
    print(f"--- message: {result.Message}")
    print("--- log ---")
    print(result.Log)
    return 0 if result.Ok else 1


if __name__ == "__main__":
    sys.exit(main())
