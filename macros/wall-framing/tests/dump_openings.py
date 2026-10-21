"""Print the opening (jamb) stations of every wall panel, to compare them
with the measured sets of the reference drawing [50].

Usage: python tests/dump_openings.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN  # noqa: E402
from test_rules import load_parts, static_call, TOL, PANEL_PREFIX  # noqa: E402

import clr  # noqa: E402
from System.Reflection import Assembly  # noqa: E402
from System.Collections import ArrayList  # noqa: E402
import System  # noqa: E402


def main():
    asm = Assembly.LoadFrom(str(BIN / "WF_Rules.dll"))
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
        o.X0, o.Y0, o.X1, o.Y1 = p["x0"], p["y0"], p["x1"], p["y1"]
        parts.Add(o)
    panels = static_call(rules, "BuildPanels", [parts, PANEL_PREFIX, TOL])
    for i in range(panels.Count):
        p = panels[i]
        op = static_call(rules, "OpeningStations", [p, TOL])
        vals = [round(float(v), 2) for v in op]
        print(f"{p.Name:10} {'X' if p.Horiz else 'Y':1} "
              f"openings={len(vals):2} {vals}")
        for j in range(p.Openings.Count):
            op2 = p.Openings[j]
            lo = op2.PanelLo(p.Horiz)
            hi = op2.PanelHi(p.Horiz)
            print(f"     jamb {op2.Name:12} {min(lo,hi):9.2f}..{max(lo,hi):9.2f} "
                  f"centre={op2.Cx if p.Horiz else op2.Cy:9.2f}")


if __name__ == "__main__":
    main()
