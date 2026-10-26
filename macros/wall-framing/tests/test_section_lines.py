"""
The section-line rule of the Pro edition, tested without a running Tekla.

WF_Rules_Pro.dll has no Tekla reference, so pythonnet can load it in a
plain Python process.

The rule is:  the hand-placed section lines of a model belong to ONE
block, and a line belongs to the layout when it touches it.  A model
has one wall layout drawing per block, and the blocks are metres apart,
so the test of the line against the layout is what keeps the lines of
one block out of the drawing of another one.

The data here is SYNTHETIC: no geometry of a client project is in the
public repository.

Run:

    python tests/test_section_lines.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import PRO_RULES_DLL  # noqa: E402

import clr  # noqa: E402  - must come before the System imports
import System  # noqa: E402
from System.Reflection import Assembly  # noqa: E402
from System.Collections.Generic import List  # noqa: E402

PASSED = 0
FAILED = 0

# a synthetic layout: 1000 x 500, at the origin
BX0, BY0, BX1, BY1 = 0.0, 0.0, 1000.0, 500.0
MARGIN = 1000.0


def check(name: str, ok: bool, detail: str = "") -> None:
    global PASSED, FAILED
    if ok:
        PASSED += 1
        print(f"  PASS  {name}")
    else:
        FAILED += 1
        print(f"  FAIL  {name}" + (f"   {detail}" if detail else ""))


def load_pro_rules():
    """Loads the Pro rules, or returns None when they are not built."""
    if not PRO_RULES_DLL.exists():
        return None
    asm = Assembly.LoadFrom(str(PRO_RULES_DLL))
    t = asm.GetType("P142.WallFraming.WfRulesPro")
    if t is None:
        return None
    return t


def lines_in_layout(rules_t, lines, bx0=BX0, by0=BY0, bx1=BX1, by1=BY1,
                    margin=MARGIN):
    """The lines that belong to the layout, by the rule."""
    method = rules_t.GetMethod("SectionLinesInLayout")
    # the list has to be of the type the rule asks for
    plist = System.Activator.CreateInstance(
        method.GetParameters()[0].ParameterType)
    line_t = rules_t.Assembly.GetType("P142.WallFraming.WfSectionLine")
    for (name, x0, y0, x1, y1) in lines:
        line = System.Activator.CreateInstance(line_t)
        line.Name = name
        line.X0, line.Y0 = float(x0), float(y0)
        line.X1, line.Y1 = float(x1), float(y1)
        plist.Add(line)
    return method.Invoke(None, [plist, float(bx0), float(by0),
                                 float(bx1), float(by1), float(margin)])


def main() -> int:
    rules_t = load_pro_rules()
    if rules_t is None:
        print("  (the Pro rules are not built here - the free edition only "
              "is checked)")
        print("     build them with:  cd source  &&  powershell -NoProfile "
              "-File build.ps1 -ProPath <the private folder>")
        return 0

    print("the version of the Pro rules")
    version = rules_t.GetField("ApiVersion").GetRawConstantValue()
    check("the Pro rules carry a version number", version is not None,
          f"version={version}")
    check("the Pro rules are of version 1", version == 1,
          f"version={version}")

    print("\na line that cuts the layout")
    out = lines_in_layout(rules_t, [("S1", -100.0, 250.0, 1100.0, 250.0)])
    check("the line is in the layout", len(out) == 1, f"n={len(out)}")

    print("\na line of another block, 68 m away")
    out = lines_in_layout(rules_t, [("S1", 68000.0, 250.0, 74000.0, 250.0)])
    check("the line is NOT in the layout", len(out) == 0, f"n={len(out)}")

    print("\na short line outside the layout, but within the margin")
    out = lines_in_layout(rules_t, [("S1", -900.0, 250.0, -800.0, 250.0)])
    check("the line is in the layout", len(out) == 1, f"n={len(out)}")

    print("\na short line outside the layout, beyond the margin")
    out = lines_in_layout(rules_t, [("S1", -1500.0, 250.0, -1200.0, 250.0)])
    check("the line is NOT in the layout", len(out) == 0, f"n={len(out)}")

    print("\na vertical line")
    out = lines_in_layout(rules_t, [("S3", 500.0, -200.0, 500.0, 700.0)])
    check("the line is in the layout", len(out) == 1, f"n={len(out)}")

    print("\na line that only touches the corner")
    out = lines_in_layout(rules_t, [("S4", 1000.0, 500.0, 1400.0, 900.0)])
    check("the line is in the layout", len(out) == 1, f"n={len(out)}")

    print("\nseveral lines, only one of them of this block")
    out = lines_in_layout(rules_t, [
        ("S1", 68000.0, 250.0, 74000.0, 250.0),   # another block
        ("S2", -100.0, 250.0, 1100.0, 250.0),      # this block
        ("S3", 68000.0, 250.0, 74000.0, 250.0),   # another block
    ])
    names = [str(l.Name) for l in out]
    check("only the line of this block is kept", names == ["S2"],
          f"names={names}")

    print("\nno line at all")
    out = lines_in_layout(rules_t, [])
    check("nothing is in the layout", len(out) == 0, f"n={len(out)}")

    print("\nthe test of one line")
    hits = rules_t.GetMethod("LineHitsBox")
    r = hits.Invoke(None, [0.0, 250.0, 1000.0, 250.0,
                           float(BX0), float(BY0), float(BX1), float(BY1)])
    check("a line through the box hits it", bool(r))
    r = hits.Invoke(None, [2000.0, 250.0, 3000.0, 250.0,
                           float(BX0), float(BY0), float(BX1), float(BY1)])
    check("a line beside the box does not hit it", not bool(r))

    print()
    print(f"{PASSED} passed, {FAILED} failed")
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
