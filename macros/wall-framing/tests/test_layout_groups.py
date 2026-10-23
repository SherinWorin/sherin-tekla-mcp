"""
The layout rule of the AIO macro, tested without a running Tekla.

WF_Rules.dll has no Tekla reference, so pythonnet can load it in a plain
Python process.

The rule is:  a GA sheet can carry the wall framing layout of many
blocks, and the chains of one block must not run into the next one.  The
block of a wall panel is the part of its assembly position before the
first dash ("BP1-WP3" is a panel of the block "BP1"), and the panels of
one block are one layout.

The block names of a project are written in two ways - the model says
"BP7M" and the drawing of that block says "BP07M" - so the names are
compared in a normalised form.

The data here is SYNTHETIC: no geometry of a client project is in the
public repository.

Run:

    python tests/test_layout_groups.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN  # noqa: E402

import clr  # noqa: E402  - must come before the System imports
import System  # noqa: E402
from System.Reflection import Assembly  # noqa: E402

PASSED = 0
FAILED = 0

RULES_DLL = BIN / "WF_Rules.dll"


def check(name: str, ok: bool, detail: str = "") -> None:
    global PASSED, FAILED
    if ok:
        PASSED += 1
        print(f"  PASS  {name}")
    else:
        FAILED += 1
        print(f"  FAIL  {name}" + (f"   {detail}" if detail else ""))


def load_rules():
    """Loads the free rules, or returns None when they are not built."""
    if not RULES_DLL.exists():
        return None
    asm = Assembly.LoadFrom(str(RULES_DLL))
    t = asm.GetType("P142.WallFraming.WfRules")
    if t is None:
        return None
    return t


def make_panel(panel_t, name, x0, y0, x1, y1):
    """A wall panel with nothing but a name and a box."""
    p = System.Activator.CreateInstance(panel_t)
    p.Name = name
    p.Key = name
    p.XLo = float(x0)
    p.YLo = float(y0)
    p.XHi = float(x1)
    p.YHi = float(y1)
    return p


def groups(rules_t, panels):
    """The layouts, by the rule."""
    method = rules_t.GetMethod("LayoutGroups")
    plist = System.Activator.CreateInstance(
        method.GetParameters()[0].ParameterType)
    for p in panels:
        plist.Add(p)
    return method.Invoke(None, [plist])


def block_of(rules_t, panel):
    return str(rules_t.GetMethod("BlockOf").Invoke(None, [panel]))


def normalise(rules_t, name):
    return str(rules_t.GetMethod("NormaliseBlock").Invoke(None, [name]))


def same_block(rules_t, a, b):
    return bool(rules_t.GetMethod("SameBlock").Invoke(None, [a, b]))


def main() -> int:
    rules_t = load_rules()
    if rules_t is None:
        print(f"FATAL: {RULES_DLL} missing - build it first")
        return 1

    panel_t = rules_t.Assembly.GetType("P142.WallFraming.WfPanel")

    print("the version of the rules")
    version = rules_t.GetField("ApiVersion").GetRawConstantValue()
    check("the rules carry a version number", version is not None,
          f"version={version}")
    check("the rules are of version 3", version == 3, f"version={version}")

    print("\nthe block of a panel")
    p = make_panel(panel_t, "BP1-WP3", 0, 0, 100, 100)
    check("the block of BP1-WP3 is BP1", block_of(rules_t, p) == "BP1",
          block_of(rules_t, p))
    p = make_panel(panel_t, "BP1M-WP12", 0, 0, 100, 100)
    check("the block of BP1M-WP12 is BP1M", block_of(rules_t, p) == "BP1M",
          block_of(rules_t, p))
    p = make_panel(panel_t, "BP1-LW1", 0, 0, 100, 100)
    check("a ledge wall belongs to its block", block_of(rules_t, p) == "BP1",
          block_of(rules_t, p))
    p = make_panel(panel_t, "LW1", 0, 0, 100, 100)
    check("a panel with no dash is a layout of its own",
          block_of(rules_t, p) == "LW1", block_of(rules_t, p))

    print("\nthe name of a block")
    check("the leading zeros are dropped", normalise(rules_t, "BP07M") == "BP7M",
          normalise(rules_t, "BP07M"))
    check("the zeros that are not leading are kept",
          normalise(rules_t, "BP10") == "BP10", normalise(rules_t, "BP10"))
    check("the case does not matter", normalise(rules_t, "bp7m") == "BP7M",
          normalise(rules_t, "bp7m"))
    check("the spaces and the dashes are dropped",
          normalise(rules_t, "BP 7-M") == "BP7M", normalise(rules_t, "BP 7-M"))
    check("BP7M and BP07M are the same block",
          same_block(rules_t, "BP7M", "BP07M"))
    check("BP7 and BP07M are not the same block",
          not same_block(rules_t, "BP7", "BP07M"))
    check("BP10 and BP1 are not the same block",
          not same_block(rules_t, "BP10", "BP1"))
    check("an empty name is no block", not same_block(rules_t, "", "BP1"))

    print("\na sheet with three blocks")
    # three blocks, and the panels of each one are near each other
    panels = [
        make_panel(panel_t, "BP1-WP1", 0, 0, 1000, 100),
        make_panel(panel_t, "BP1-WP2", 1000, 0, 2000, 100),
        make_panel(panel_t, "BP1M-WP1", 6000, 0, 7000, 100),
        make_panel(panel_t, "BP2-WP1", 12000, 0, 13000, 100),
        make_panel(panel_t, "BP2-WP2", 13000, 0, 14000, 100),
    ]
    lays = groups(rules_t, panels)
    check("there are three layouts", len(lays) == 3, f"n={len(lays)}")

    blocks = [str(l.Block) for l in lays]
    check("the layouts are from left to right", blocks == ["BP1", "BP1M", "BP2"],
          f"blocks={blocks}")
    check("the first layout has two panels", len(lays[0].Panels) == 2,
          f"n={len(lays[0].Panels)}")
    check("the second layout has one panel", len(lays[1].Panels) == 1,
          f"n={len(lays[1].Panels)}")
    check("the third layout has two panels", len(lays[2].Panels) == 2,
          f"n={len(lays[2].Panels)}")

    print("\nthe box of a layout")
    check("the box of the first layout is its own",
          lays[0].X0 == 0.0 and lays[0].X1 == 2000.0 and
          lays[0].Y0 == 0.0 and lays[0].Y1 == 100.0,
          f"{lays[0].X0}..{lays[0].X1} / {lays[0].Y0}..{lays[0].Y1}")
    check("the box of the second layout is its own",
          lays[1].X0 == 6000.0 and lays[1].X1 == 7000.0,
          f"{lays[1].X0}..{lays[1].X1}")
    check("the box of the third layout is its own",
          lays[2].X0 == 12000.0 and lays[2].X1 == 14000.0,
          f"{lays[2].X0}..{lays[2].X1}")

    print("\nthe panels are not mixed up")
    names = [str(p.Name) for p in lays[2].Panels]
    check("the third layout holds the panels of BP2",
          names == ["BP2-WP1", "BP2-WP2"], f"names={names}")

    print("\na sheet with one block only")
    lays = groups(rules_t, panels[:2])
    check("there is one layout", len(lays) == 1, f"n={len(lays)}")
    check("the layout is BP1", str(lays[0].Block) == "BP1",
          str(lays[0].Block))

    print("\nno panel at all")
    lays = groups(rules_t, [])
    check("there is no layout", len(lays) == 0, f"n={len(lays)}")

    print()
    print(f"{PASSED} passed, {FAILED} failed")
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
