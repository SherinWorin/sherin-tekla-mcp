"""
Unit test of the wall-panel MARK placement rule.

The rules assembly has no Tekla dependency, so this test runs without a
running Tekla Structures.  It feeds the rules the real model parts of
P142-COREL REEF_BP4_MOCKUP-R00 and compares the computed mark placement
against the 11 BP4-WP* marks that GA drawing [50] actually contains
(tests/reference/marks_50.json).

Usage: python tests/test_marks.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN, REFERENCE, PRO_RULES_DLL, require  # noqa: E402

import clr  # noqa: E402
import System  # noqa: E402
from System.Reflection import Assembly  # noqa: E402

TOL = 0.5
PANEL_PREFIX = "BP4-WP"
OFFSET = 29.25

RULES_DLL = BIN / "WF_Rules.dll"
PARTS = REFERENCE / "asm_parts.txt"
MARKS = REFERENCE / "marks_50.json"


def static_call(rules, name, args):
    return rules.GetMethod(name).Invoke(None, args)


def load_parts():
    out = []
    for line in PARTS.read_text(encoding="utf-8").splitlines():
        f = line.split("|")
        if len(f) < 8:
            continue
        name, _cls, pid, x0, y0, x1, y1, apos = f[:8]
        if not apos.startswith(PANEL_PREFIX):
            continue
        out.append({
            "name": name, "id": int(pid),
            "x0": float(x0), "y0": float(y0),
            "x1": float(x1), "y1": float(y1),
            "apos": apos,
        })
    return out


def main():
    if not RULES_DLL.exists():
        print(f"FATAL: {RULES_DLL} missing")
        return 2
    asm = Assembly.LoadFrom(str(RULES_DLL))
    rules = asm.GetType("P142.WallFraming.WfRules")
    part_t = asm.GetType("P142.WallFraming.WfPart")

    # The mark placement is a PRO rule: it is not in the public repository.
    # It is checked here when the private sources have been built.
    if not PRO_RULES_DLL.exists():
        print("The mark placement is a Pro rule, and the Pro rules are not in")
        print(f"the public repository.  Build them first:  {PRO_RULES_DLL}")
        print("  (build.ps1 -ProPath \"<the private sources>\")")
        return 0
    pro_asm = Assembly.LoadFrom(str(PRO_RULES_DLL))
    pro = pro_asm.GetType("P142.WallFraming.WfRulesPro")

    parts = System.Collections.Generic.List[part_t]()
    for p in load_parts():
        o = System.Activator.CreateInstance(part_t)
        o.Name = p["name"]
        o.Id = p["id"]
        o.AssemblyPos = p["apos"]
        o.X0 = p["x0"]
        o.Y0 = p["y0"]
        o.X1 = p["x1"]
        o.Y1 = p["y1"]
        parts.Add(o)

    panels = static_call(rules, "BuildPanels", [parts, PANEL_PREFIX, TOL])
    byname = {panels[i].Name: panels[i] for i in range(panels.Count)}

    marks = json.loads(require(MARKS, "the placement of the marks of the reference drawing").read_text(encoding="utf-8"))

    ok = 0
    ok_angle = 0
    ok_leader = 0
    ok_offset = 0
    for m in marks:
        pan = byname.get(m["apos"])
        if pan is None:
            print(f"  {m['apos']:10} NO PANEL")
            continue
        mp = static_call(pro, "MarkPlacement", [pan, OFFSET])

        d_ang = abs(mp.Angle - m["angle"])
        st = m["start"] or [0, 0]
        en = m["end"] or [0, 0]
        d_st = max(abs(mp.StartX - st[0]), abs(mp.StartY - st[1]))
        d_en = max(abs(mp.EndX - en[0]), abs(mp.EndY - en[1]))
        # the label's distance from the measured leader line
        if abs(m["angle"] - 0) < 0.5 or abs(m["angle"] - 180) < 0.5:
            d_off = abs(abs(m["ins"][1] - st[1]) - OFFSET)
        else:
            d_off = abs(abs(m["ins"][0] - st[0]) - OFFSET)

        a_ok = d_ang < 0.5
        l_ok = d_st < 1.0 and d_en < 1.0
        o_ok = d_off < 1.0
        ok_angle += a_ok
        ok_leader += l_ok
        ok_offset += o_ok
        good = a_ok and l_ok and o_ok
        if good:
            ok += 1
        print(f"  {m['apos']:10} {'OK  ' if good else 'DIFF'} "
              f"angle {mp.Angle:>5.0f}/{m['angle']:.0f} "
              f"{'ang' if a_ok else 'ANG'} {'ldr' if l_ok else 'LDR'} "
              f"{'off' if o_ok else 'OFF'}  "
              f"d_leader=({d_st:6.2f},{d_en:6.2f}) d_off={d_off:6.2f} "
              f"ins_along={mp.InsX if pan.Horiz else mp.InsY:9.2f}/"
              f"{m['ins'][0] if pan.Horiz else m['ins'][1]:9.2f}")

    n = len(marks)
    print(f"\nexact mark placements: {ok} / {n}")
    print(f"  text angle  (rule-derived):  {ok_angle} / {n}")
    print(f"  leader line (rule-derived):  {ok_leader} / {n}")
    print(f"  label offset(rule-derived):  {ok_offset} / {n}")
    return 0 if ok == n else 1


if __name__ == "__main__":
    sys.exit(main())
