# Wall-layout dimensioning rules (P142 BP4 MOCKUP)

Derived from GA drawing **[50] "WALL FRAMING LAYOUT"** of model
`P142-COREL REEF_BP4_MOCKUP-R00`.

The rules live in **`source/WF_Rules.cs`** — a file with *no* Tekla
reference, so they can be unit-tested without a running Tekla Structures.
`source/P142_WF_AutoDimension.cs` reads the model and calls them.

| check | command | result |
|---|---|---|
| dimension chains vs [50] (rules only) | `python tests/test_rules.py` | 12 of 18 chains exact |
| the automatic panel detection | `python tests/test_rules.py` | the same 11 panels, the ceilings are left out |
| dimension chains [50] → [57] (drawing) | `python tests/cmp_chains.py` | 12 of 18 chains exact |
| which drawing the macro works on | `python tests/test_target_selection.py` | 2 of 2 tests pass |
| mark placement vs [50] | `python tests/test_marks.py` | angle 11/11, leader 9/11, offset 9/11 |
| state of both drawings | `python tests/audit_50_57.py` | counts, mark angles, section geometry |

## The model

Light-gauge-steel (LGS) wall panels, grouped by the model report property
`ASSEMBLY_POS` with the prefix `BP4-WP` (11 panels: `BP4-WP1`..`BP4-WP11`):

| assembly | part names |
|---|---|
| wall panel | `BOTTOM_TRACK`, `TOP_TRACK`, `MIDDLE_TRACK`, `STUD`, `GYPSUM_BOARD`, `JAMB`, `LINTEL` |

Parts are read as `BEAM` and `CONTOURPLATE` model objects, in the plan
view's `DisplayCoordinateSystem`.

Geometry constants that the rules depend on:

* stud: 35 mm wide, its long axis runs **across** the wall;
* track: 63 mm deep;
* plasterboard: 12.5 mm;
* the plasterboard is on the **outer** side of the track.

> **Coordinate trap.** A stud runs *across* the wall, so its extent along
> the panel axis is its **short** axis. Using a part's own long axis
> (`Part.Along0/Along1`) gives the wrong coordinate. Use
> `WfPart.PanelLo(panelHoriz)` / `WfPart.PanelHi(panelHoriz)`.

> **Assembly extents.** Every panel has two extents, and the rules use
> different ones:
> * `Asm0/Asm1` — along the panel axis (track **+** board);
> * `XLo/XHi`, `YLo/YHi` — the absolute bounding box of the whole assembly.
>
> The band chains use the **absolute** box; using the panel-axis-relative
> extent gives a chain that is off by the 1 mm board overhang.

## R1 — bay chain (one dimension set per wall panel)

`WfRules.ChainStations(panel, tol)`. Along the panel's own axis:

1. the **track** ends of the panel are always measured;
2. the **assembly** ends (track + board) are measured as well, so the
   overhang of a board past the track shows up as its own segment
   (`BP4-WP11` in [50]);
3. every stud contributes its **centre**;
4. a stud **flush with a panel end** is skipped — the panel end measures it;
5. a stud **inside a lintel** (i.e. in an opening) is skipped;
6. a run of **touching** studs contributes **one** value: the shared face
   between the first and the second stud of the run.

## R2 — opening stations (merged into the panel's chain)

`WfRules.OpeningStations(panel, tol)`. An opening is a **pair of adjacent
jambs**. The lintel bears on one of them, and the opening is measured at
the **outer edge of that jamb** (the edge the lintel bears on). The other
jamb of the pair is not measured.

## R3 — band chain (one dimension set per edge of the layout)

`WfRules.BandStations(panels, edge, bbox, tol)`. For each of the four
edges of the layout:

* the chain starts at the **origin** of the bounding box along the measured
  axis;
* every wall panel whose **assembly bounding box** touches that edge
  contributes its extent along the axis;
* the chain contains the **start of every panel** and the **end of every
  run of overlapping panels**.

Using the assembly extent (not the track extent) is what makes the 1 mm gap
between a track end and the next panel's plasterboard appear as its own
measured segment — the detail that distinguishes the band chains from a
simple overall dimension.

**Verified exact against [50]:** bottom (SET 17), left (SET 8),
right (SET 7).

## R4 — the top outline of the layout

The top edge of the layout is **not** a band chain: in [50] it is the
**top outline**, a polyline that follows the upper boundary of the walls.

`WfRules.TopOutlinePolyline(panels, notch, tol)` builds it, in three steps.

**Step 1 — the profile intervals.** Every panel is a rectangle from its
west edge to its east edge, with the top of its assembly as its height.
The layout is cut into the elementary intervals of those edges
(`ProfileIntervals`), and the level of an interval is the **highest** panel
that covers the interval *entirely*. A panel that ends 1 mm before the
interval starts does **not** raise that interval.

Every interval also carries a flag: **vertical-only** when its level is
reached *only* by panels that run along Y.

**Step 2 — the runs.** Intervals of the same level are merged into runs
(`MergeRuns`).

**Step 3 — the walk** over the runs:

* a run that is **vertical-only** is a wall that crosses the outline — a
  re-entrant corner of the layout. The outline is **chamfered** across it:
  the level does not change, the outline cuts diagonally over the corner.
* a **dip** in the profile (a group of runs whose level drops and comes
  back) is a notch between two walls:
  * when the level is the **same** on both sides of the dip and the dip is
    narrower than `notch` (2 mm), the gap is **bridged** — it is not
    measured at all (the 1 mm gap between `BP4-WP5` and `BP4-WP7`);
  * when the level **changes** across the dip, **both** edges of the gap
    are measured (the 1 mm gap between `BP4-WP3` and `BP4-WP5`).
* a plain **step** is measured at the new level.

The result for this model is the polyline

```
(0, 3012.59) - (1785.82, 3012.59) - (1798.32, 2400.59) - (1799.32, 2400.59) - (3187.32, 2400.59)
```

which is [50] SET 6 — except that [50] draws the lower level on the top of
the plasterboard of the west wall (2401.09) instead of on the wall itself
(2400.59). The 0.5 mm difference changes no dimension value.

`WfRules.TopOutlineStations` returns the X coordinates of that polyline:
0 – 1785.82 – 1798.32 – 1799.32 – 3187.32.

**Verified exact against [50]:** SET 6.

## R5 — the overall dimensions of the layout

Two more dimension sets, both of them on the outline:

* the **overall height** is measured on the **west edge** of the layout,
  from the south-west corner to the north-west corner, with the up vector
  (−1, 0) — [50] SET 4, paper distance 24.27 mm;
* the **overall width** is measured **between the two ends of the top
  outline** — the diagonal of [50] SET 5, paper distance 27.74 mm.

**Verified exact against [50]:** SET 4, SET 5.

## R6 — the north run

`WfRules.NorthRun(panels, notch, tol)`. The **north run** is the run of the
northern wall, measured from the start of that wall to the end of the
**layout**, with the end of the wall itself as the middle station:

```
1799.32 - 3110.82 - 3187.32
```

* 1799.32 — the start of the north wall `BP4-WP5`, which is also the
  station where the top outline steps down;
* 3110.82 — the end of the north wall, 1 mm short of the inner face of
  the east wall `BP4-WP7`;
* 3187.32 — the east end of the layout.

The rule: the run is the **last horizontal segment of the top outline**; the
wall that forms it is the horizontal panel whose top is on that level and
whose west end is the start of the segment; the chain is then the start of
the segment, the end of that wall and the end of the segment.

**Verified exact against [50]:** SET 3 (values). The *placement* differs on
purpose: in [50] the north run and the top outline are measured northwards
at 21.63 and 21.82 mm from their geometry, so they sit on top of each
other; in the generated drawing the north run is moved to 30 mm so that
both chains stay readable.

## R7 — the up vector

The dimension line always sits **outside** the layout and the `up` vector
points **away from** it, so the text reads inwards:

| chain runs along | the line is on | `up` |
|---|---|---|
| X | above the layout (larger Y) | (0, +1) |
| X | below the layout (smaller Y) | (0, −1) |
| Y | right of the layout (larger X) | (+1, 0) |
| Y | left of the layout (smaller X) | (−1, 0) |

The **panel chains** and the **band chains** follow this table: the line
sits just outside the wall it measures. The **top outline** and the
**north run** are both measured northwards (0, +1) — they are the two
chains that lie above the layout. The **overall height** is measured on
the west edge with (−1, 0) and the **overall width** with (0, +1), the
same as the reference drawing.

## R8 — which drawing the macro works on

The macro must work on **any** computer of the project, where the wall
layout drawing has another mark — or several wall layout drawings. A
mark is therefore never used to recognise a drawing.

**R8.1 — a wall layout drawing is a drawing whose title holds one of
`WALL FRAMING`, `WALL LAYOUT`, `WALL FRAMING LAYOUT`** (any of `Title1`,
`Title2`, `Title3`, `Name`, case-insensitive).

In the model of this project that is exactly two drawings: **[50]** and
**[57]**, both with `Title2 = "WALL FRAMING LAYOUT"`. The panel drawings
are named `WALL_PANEL` and the other layout drawings are named
`BASE FRAMING LAYOUT` and `CEILING FRAMING LAYOUT` — none of them
matches.

**R8.2 — the target is the active drawing when it is a wall layout
drawing; otherwise the wall layout drawing of the model is activated.**

**R8.3 — an issued or frozen drawing is never written to.** The macro
stops and reports it. The issued reference drawing of the project is
frozen, so this is what protects it.

| drawing | title | frozen | the macro |
|---|---|---|---|
| [50] | WALL FRAMING LAYOUT | **yes** | **refused** — this is the reference |
| [57] | WALL FRAMING LAYOUT | no | the target |
| [56] | BASE FRAMING LAYOUT | no | not a target; the macro activates [57] |
| [49] | CEILING FRAMING LAYOUT | yes | not a target |
| BP4-WP.1 … 11 | WALL_PANEL | yes | not a target |

**R8.4 — the reference drawing** (the one the section marks are copied
from) is looked up by its preferred mark first, and by its title
otherwise. It is only ever read.

## R9 — the wall panels of a model

The wall panels are the groups of parts that share the model report
property `ASSEMBLY_POS`. The prefix of this project is `BP4-WP`, but that
is only a **preference**: on another computer the model may use another
prefix.

**R9.1 — the configured prefix wins.** `WfRules.BuildPanels(parts,
prefix, tol)` groups the parts by `ASSEMBLY_POS` and keeps the groups
that start with the prefix.

**R9.2 — when the prefix matches nothing, the panels are found
automatically.** `WfRules.BuildPanelsAuto(parts)` groups the parts by
`ASSEMBLY_POS` and keeps the groups that are a **wall assembly**: the
group holds a `BOTTOM_TRACK`, a `TOP_TRACK`, a `STUD` and a
`GYPSUM_BOARD`, and no part whose name starts with `CEILING`.

The second condition is what keeps the ceiling panels of the model
(`BP4-CP-1`, `BP4-CP-2`) out of the layout — they are `CEILING` parts.
`BP4-BF1` and `BP4-CF1` are not walls either: they have no gypsum board.

Both ways find the same 11 panels in this model (`tests/test_rules.py`).

## Dimension style

Taken verbatim from the source drawing (`tests/anno_50.txt`):
`DimensionType=Relative`, `ExtensionLine=No`, `ShortDimension=Outside`,
`Arrowhead.Head=NoArrow` with `Height=1`, `Width=2.5`, `Color=NewLine6`,
`Format.Precision=Whole`, `Text.Font.Height=2.75`,
`Text.Font.Name=Arial narrow`, `Text.TextPlacing=AboveDimensionLine`,
`Placing.Placing=Fixed`.

> **`Distance` is in model millimetres.** The API property
> `StraightDimensionSet.Distance` equals
> `paperDistance × view.Attributes.Scale`. The paper distances in [50]
> range from 7.06 mm to 32.00 mm and are a drafter's choice — the macro
> uses 8.8 mm for the panel chains, 15.9 mm for the band chains, 21.82 mm
> for the top outline, 24.27 / 27.74 mm for the two overall dimensions and
> 30.0 mm for the north run.

## Wall-panel marks

11 marks, one per panel, `BP4-WP1` .. `BP4-WP11`.
`WfRules.MarkPlacement(panel, offset)` returns the placement; it was
checked against the 11 real marks of [50] (`tests/marks_50.json`).

* **target**: the panel's `BOTTOM_TRACK`;
* **content**: a `PropertyElement` of type
  `PartMarkPropertyElementTypes.AssemblyPosition()` with
  `Name = "ASSEMBLY_POS"` — Tekla fills in the **value** from the model
  (the `Value` setter is internal and must not be assigned);
* **side**: the label goes on the side of the wall that carries **no**
  plasterboard, so it never covers the board line. When both sides carry a
  board, the positive side is used. *(11 of 11 in [50])*;
* **leader**: an `AlongLinePlacing` on the **outer edge of the track** on
  the label side, spanning the whole panel. *(9 of 11; `BP4-WP1` and
  `BP4-WP10` were nudged by hand to clear the dimension sets.)*;
* **direction**: the leader runs from the end that the text angle implies —
  label on −X → angle 90 (leader +Y), label on +X → angle 270 (leader −Y),
  label on +Y → angle 0 (leader +X), label on −Y → angle 180 (leader −X).
  *(11 of 11 in [50])*;
* **offset**: the label sits 29.25 mm beyond the leader line. *(9 of 11;
  `BP4-WP7` and `BP4-WP11` were nudged by hand.)*;
* **along the leader**: the position is a drafter's choice in [50] and is
  **not** reproducible; the macro puts the label at the panel centre.

## Section marks and notes

* 5 section marks, `S1` .. `S5`, `LineWidth=5`, `LineColor=Cyan`;
* 3 text notes, Arial 2.5 mm, black, framed:
  * `LGS\nC61*35*0.7 TYP.`
  * `12.5mm  GYPSUM BOARD TYP.`
  * `LGS\nC63*40*0.7 TYP.`

### The section marks are copied, not derived

The five section lines of [50] are **hand placed**. They do not lie on any
feature of the model:

| section | line | closest model feature | distance |
|---|---|---|---|
| S1 | y = 2157.96 | `BP4-WP3` stud centre 2153.59 | 4.37 mm |
| S2 | y = 400.59 | `BP4-WP7` stud centre 400.59 | 0.00 mm |
| S3 | x = 347.36 | `BP4-WP4` middle-track centre 377.07 | 29.71 mm |
| S4 | x = 1405.25 | `BP4-WP4` middle-track end 1403.32 | 1.93 mm |
| S5 | x = 2928.77 | `BP4-WP2` middle-track end 2912.82 | 15.95 mm |

Only S2 lands exactly on a feature, and the other four are up to 30 mm away
from anything. The lines also run 200–400 mm past the wall layout, and
their **direction** differs per line (S1 runs left→right, S2 runs
right→left). None of that is in the model, so the rule cannot be derived —
it is a drafter's decision.

`Macro.CopySections` therefore **copies the section marks verbatim** from
the reference drawing. A drawing that is *not* open in the editor can still
be read: `Drawing.GetSheet()` and `View.GetAllObjects()` work on any
drawing of the model, so the reference does not have to be activated.

## Tekla 2025 API traps found while building this

These cost real time and are worth writing down.

### 1. `CommitChanges()` does not persist — `SaveActiveDrawing()` does

`Drawing.CommitChanges()` only commits to the in-memory editor. The
objects vanish when the drawing is re-read unless the drawing is saved:

```csharp
dr.CommitChanges();                     // not enough
bool saved = dh.SaveActiveDrawing();    // this persists it
```

### 2. `MarkBaseAttributes.Angle` has an INTERNAL setter

`Angle` is documented as *"the actual angle of the mark, in degrees"*.
Its setter is `internal`, so C# cannot assign it (CS0200) and reflection
*can* write it — but the drawing engine **recomputes `Angle` when the
drawing is saved**, so the reflected value is lost.

The public lever is **`MarkBaseAttributes.RotationAngle`** (*"the mark
rotation angle set by the user"*). Setting it makes the engine compute the
matching `Angle`:

| `RotationAngle` | resulting `Angle` |
|---|---|
| 90 | 90 |
| 0 | 0 |
| 270 | 270 |

`RotationAngle` must be written to a **copy** of the attributes and the
copy written back — the getter of `Mark.Attributes` returns a copy, so
changing it in place has no effect:

```csharp
Mark.MarkAttributes attrs = mk.Attributes;   // a COPY
attrs.RotationAngle = 90.0;
mk.Attributes = attrs;                       // write the copy back
```

### 3. The engine derives the text angle from the leader direction

In the reference drawing [50] every wall-panel mark has `RotationAngle = 0`
but a non-zero `Angle`. The `Angle` follows the **direction of the leader
line**:

| leader runs | `Angle` |
|---|---|
| +Y (up) | 90 |
| −Y (down) | 270 |
| +X (right) | 0 |
| −X (left) | 180 |

That is exactly what `WfRules.MarkPlacement` computes, so the rule and the
API agree — the macro simply has to tell the engine the angle.

### 4. `Mark` has only one constructor

`new Mark(ModelObject)` — a mark must be attached to a model object.
`Tekla.Structures.Drawing.ModelObject` is abstract and its
`ModelIdentifier` setter is internal, so the only way to get one for an
arbitrary model part is to read the drawing `Part` that already represents
it:

```csharp
DrawingObjectEnumerator en = view.GetModelObjects(new Identifier(modelId));
while (en.MoveNext()) { var mo = en.Current as ModelObject; ... }
```

### 5. User properties DO survive a save

`SetUserProperty(name, value)` persists, and the macro uses it to mark its
own output so that a re-run replaces it instead of duplicating it:

```csharp
obj.SetUserProperty("P142_WF_AUTODIM", "1");
```

Reading it back needs an `out`/`ref` parameter:

```csharp
string value = "";
bool ok = obj.GetUserProperty("P142_WF_AUTODIM", ref value);
```

> pythonnet returns a **tuple** for an out-parameter call:
> `ok, value = obj.GetUserProperty(key, value)`. Reading the tuple as a
> boolean silently reports "no property" and cost a debugging cycle.

### 6. `Drawing.GetSheet()` works on a non-active drawing

A drawing that is not open in the editor can still be read through
`GetSheet()` → `GetViews()` → `GetAllObjects()`. This is what makes
copying the hand-made section marks from a reference drawing possible
without disturbing what the user is looking at.

### 7. Changing the style RESETS the position of the dimension line

`StraightDimensionSet.Attributes` is a **copy** (see trap 2). Writing the
style back and calling `Modify()` moves the dimension line: the engine
re-places the set with the *default* distance, and the chain ends up on
top of the geometry it measures.

The fix is to write the `Distance` **again after** the style change:

```csharp
set.Modify();                       // the style is applied here
dr.CommitChanges();
set.Distance = paperDist * view.Attributes.Scale;   // ...and again
set.Modify();
dr.CommitChanges();
```

Verified on [57]: every chain of the generated drawing sits at the paper
distance it was asked for.


## Known inconsistencies in the source drawing [50]

**12 of the 18 sets of [50] are reproduced exactly** by the rules
(`python tests/cmp_chains.py`). The six that are not are hand-made
details of [50] that carry no information the model holds — the macro
reproduces the **rule**, not the hand edit:

| set | what [50] shows | why it is not reproduced |
|---|---|---|
| 1 | the `BP4-WP5` chain with the station **2981.82** instead of 2999.32 | a hand edit: the drafter moved a stud station onto the edge of the stud |
| 9 | the `BP4-WP8` chain **plus** 559.81 and 1639.81 | the extra stations are the **centres of the outer jambs** of the two openings. The same construction in `BP4-WP7` (SET 2) is dimensioned **without** those centres, so the rule cannot tell the two apart — it is a drafter's choice |
| 10 | a **wall line** at x = 257.19, from the hand point (296.07, 75.50) up to the board of `BP4-WP11` (88.00) | the wall line is hand placed and its southern end (296.07) points at nothing in the model |
| 12 | the `BP4-WP6` chain with the station **1034.30** | a typo: the panel end is 1035.00 |
| 14 | the `BP4-WP4` chain with **568.32 and 603.32** | a hand edit: the stud centre 585.82 was replaced by the two edges of the stud |
| 18 | a **wall line** at x = 1710.32 with the hand point 1705.85 and a hand diagonal to (1587.32, 2937.09) | hand placed, like SET 10 |

The generated drawing therefore contains **six chains of its own** that
have no counterpart in [50]: the rule version of the four hand-edited
panel chains (WP5, WP8, WP4, WP6) and the stud chains of `BP4-WP9` and
`BP4-WP10`, which [50] does not dimension at all.

## Sets in [50] and what they are

| set | axis | paperDist | what it is |
|---|---|---|---|
| 1 | X | 32.00 | overall of the WP5 wall, above the panel (hand edit) |
| 2 | Y | 7.06 | bay chain of `BP4-WP7` ✔ |
| 3 | X | 21.63 | the north run: WP5 + the gap to WP7 ✔ |
| 4 | Y | 24.27 | overall height of the layout, on the left ✔ |
| 5 | X | 27.74 | overall width of the layout, on the top ✔ |
| 6 | X | 21.82 | the top outline (the staircase) ✔ |
| 7 | Y | 16.97 | band right ✔ |
| 8 | Y | 17.04 | band left ✔ |
| 9 | X | 15.01 | bay chain of `BP4-WP8` (with two extra jamb centres) |
| 10 | Y | 11.73 | the hand-placed wall line of `BP4-WP11` |
| 11 | X | 8.47 | the board width of `BP4-WP11` ✔ |
| 12 | Y | 7.71 | bay chain of `BP4-WP6` (typo) |
| 13 | X | 8.80 | bay chain of `BP4-WP2` ✔ |
| 14 | X | 8.76 | bay chain of `BP4-WP4` (hand edit) |
| 15 | Y | 10.28 | bay chain of `BP4-WP1` ✔ |
| 16 | Y | 8.88 | bay chain of `BP4-WP3` ✔ |
| 17 | X | 15.89 | band bottom ✔ |
| 18 | X | 13.51 | the hand-placed west wall line |

✔ = reproduced exactly by `source/WF_Rules.cs` (12 of 18).
