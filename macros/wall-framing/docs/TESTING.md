# Testing

Five harnesses, on two levels: the **rules** are tested without a
running Tekla, the **drawing** is tested against the reference drawing.

| harness | needs Tekla | what it proves |
|---|---|---|
| `tests/test_rules.py` | **no** | the rules produce the right numbers, and the panels are found automatically |
| `tests/cmp_chains.py` | yes | the drawing that was written contains the right chains |
| `tests/test_marks.py` | yes | the marks are placed like the ones of the reference |
| `tests/test_target_selection.py` | yes | the macro works on the right drawing and refuses an issued one |
| `tests/audit_50_57.py` | yes | both drawings are in a sane state, and [50] was not touched |

## 1. `tests/test_rules.py` — the rules, without Tekla

`WF_Rules.dll` has no Tekla reference, so pythonnet can load it in a
plain Python process:

```python
asm = Assembly.LoadFrom("source/bin/WF_Rules.dll")
rules = asm.GetType("P142.WallFraming.WfRules")
panels = rules.GetMethod("BuildPanels").Invoke(None, [parts, "BP4-WP", 0.5])
```

The parts come from `tests/reference/asm_parts.txt`, a dump of the 247 wall parts
of the model (name, profile, id, extents, `ASSEMBLY_POS`).

The **oracle** is `tests/expected.py`, which parses
`tests/reference/full_50.txt` — a dump of the 18 dimension sets of the reference
drawing **[50]** — and normalises every set into

```
axis | stations | paperDist | up
```

The test then compares every generated chain with every measured set and
reports the ones that match exactly (0.4 mm).

Current result:

```
  BP4-WP11     -> SET 11  EXACT  (paperDist 8.47)
  BP4-WP2      -> SET 13  EXACT  (paperDist 8.80)
  BP4-WP7      -> SET  2  EXACT  (paperDist 7.06)
  BP4-WP3      -> SET 16  EXACT  (paperDist 8.88)
  BP4-WP1      -> SET 15  EXACT  (paperDist 10.28)
  band Bottom  -> SET 17  EXACT  (paperDist 15.89)
  band Left   -> SET  8  EXACT  (paperDist 17.04)
  band Right  -> SET  7  EXACT  (paperDist 16.97)
  top outline  -> SET  6  EXACT  (paperDist 21.82)
  north run   -> SET  3  EXACT  (paperDist 21.63)
  overall height -> SET  4  EXACT  (paperDist 24.27)
  overall width  -> SET  5  EXACT  (paperDist 27.74)

exact chain matches: 12 / 18
```

The six that do not match are the hand edits of [50] listed in
[WALL_FRAMING_RULES.md](WALL_FRAMING_RULES.md).

### The automatic panel detection

The same harness also checks R9, the automatic panel detection:

```
=== the automatic panel detection ===
  BuildPanels with a wrong prefix : 0 panels (0 is correct)
  BuildPanelsAuto                 : 11 panels ['BP4-WP1', 'BP4-WP10', ...]
  -> PASS: the automatic detection finds the same panels as the prefix
```

The ceiling panels of the model (`BP4-CP-1`, `BP4-CP-2`) are not walls
and are not taken for one.

## 2. `tests/cmp_chains.py` — the drawing

`tests/dump_dims.py "[57]" tests/reference/dims_57.txt` dumps the dimension sets of
a drawing (the child `StraightDimension` objects: start, end, up
direction, distance) and `cmp_chains.py` compares them with the same
dump of the reference drawing.

A chain matches when its axis and its stations are equal (0.05 mm).

Current result:

```
12 of 18 chains of [50] reproduced exactly in [57]
```

The six that do not are the hand edits of [50]; the six chains of [57]
that have no counterpart are the rule version of those hand edits plus
the stud chains of `BP4-WP9` and `BP4-WP10`, which [50] does not
dimension at all.

## 3. `tests/test_marks.py` — the marks

Compares the placement of the 11 wall-panel marks of [57] with the
placement of the 11 real marks of [50] (`tests/reference/marks_50.json`):
the text angle, the leader line and the offset of the label.

```
exact mark placements: 7 / 11
  text angle  (rule-derived):  11 / 11
  leader line (rule-derived):   9 / 11
  label offset(rule-derived):   9 / 11
```

The placement *along* the leader line is a drafter's choice in [50] and is
never equal — it is reported separately and is not a failure.

## 4. `tests/audit_50_57.py` — the state of both drawings

Counts the objects of both drawings, compares the mark angles and the
section geometry, and reports the user-property tag. It is the check to
run after a macro run to prove that **[50] was not modified**.

## 5. `tests/test_target_selection.py` — which drawing the macro works on

The macro must not work on a fixed mark. The harness checks the three
steps of R8:

```
--- test 1: the active drawing is [56] (BASE FRAMING LAYOUT) - not a wall layout drawing
    log          : the wall layout drawing [57] was activated
    active now   : [57]
    other drawing: (0, 10, 0, 3) -> (0, 10, 0, 3)
    -> PASS: switched to the wall layout drawing and left [56] alone

--- test 2: the active drawing is [50] (frozen=True, issued=False)
    ok=False  made=0
    message: No wall layout drawing to work on: the active drawing [50] is issued or frozen - it is not written to
    content: (18, 126, 5, 3) -> (18, 126, 5, 3)
    -> PASS: refused to write into the issued drawing and left it unchanged
```

The counts in brackets are (dimension sets, marks, section marks, texts).
The 18 / 126 / 5 / 3 of [50] are the numbers of the issued drawing — they
must never change.

## How to add a test for a new rule

1. **Write the rule** in `source/WF_Rules.cs`, as a pure function of the
   panels — no Tekla types, no I/O.
2. **Call it from the macro** and log what it returns.
3. **Add it to `tests/test_rules.py`**: compute the chain, print it, and
   let the comparison against the oracle decide whether it is right.
4. **Add it to `tests/cmp_chains.py`** by running the macro and dumping
   the drawing.
5. **Document it** in [WALL_FRAMING_RULES.md](WALL_FRAMING_RULES.md) with
   the set of [50] it was derived from.

The rule that is not written down is not finished.

A rule that changes **which drawing** the macro works on also needs a
test in `tests/test_target_selection.py`.

## What is *not* tested

* The **section marks** are copied from [50]; there is nothing to test
  but that five marks arrive.
* The **notes** are three fixed strings.
* The **placement** of a dimension line (how far from the geometry it
  sits) is a constant per chain type; only the value of the constant was
  checked against [50].
* The macro has **no automated test of the writing path** — it is
  verified by running it and dumping the drawing, which is what
  `run_macro.py` + `dump_dims.py` do. A unit test of the writing path
  would need a Tekla instance and is listed in [TODO.md](TODO.md).
