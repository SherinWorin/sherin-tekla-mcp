# Wall-layout dimensioning rules

The rules live in **`source/WF_Rules.cs`** — a file with *no* Tekla
reference, so they can be unit-tested without a running Tekla Structures.
`source/P142_WF_AutoDimension.cs` reads the model and calls them.

## The two reference models

The rules are derived from **two** models, and every rule has to hold in
both of them:

| model | reference drawing | what it is |
|---|---|---|
| `P142-COREL REEF_BP4_MOCKUP-R00` | **[50] "WALL FRAMING LAYOUT"** (issued, frozen) | the model the macro was written on: 11 wall panels, one wall layout drawing |
| `MODEL-2` | 20 wall layout drawings (`BP01`…`BP10`, `BP01M`…`BP10M`, scale 1:15) | a real project: 234 wall panels in 19 writable wall layout drawings |

The first model is the one the rules were **written** against; the second is
the one they were **checked** against, and it is where the two rules that
the first model could not show (R2, R8) come from.

| check | command | result |
|---|---|---|
| dimension chains vs [50] (rules only) | `python tests/test_rules.py` | 12 of 18 chains exact |
| the automatic panel detection | `python tests/test_rules.py` | the same 11 panels, the ceilings are left out |
| dimension chains [50] → [57] (drawing) | `python tests/cmp_chains.py` | 12 of 18 chains exact |
| which drawing the macro works on | `python tests/test_target_selection.py` | 2 of 2 tests pass |
| mark placement vs [50] | `python tests/test_marks.py` | angle 11/11, leader 9/11, offset 9/11 |
| state of both drawings | `python tests/audit_50_57.py` | counts, mark angles, section geometry |
| the rules against the new model | `python tests/test_upgrade.py` | 205 of 291 chains are a subset of a hand-made chain |

## The model

Light-gauge-steel (LGS) wall panels, grouped by the model report property
`ASSEMBLY_POS` with the prefix `BP4-WP` (11 panels: `BP4-WP1`..`BP4-WP11`):

| assembly | part names |
|---|---|
| wall panel | `BOTTOM_TRACK`, `TOP_TRACK`, `MIDDLE_TRACK`, `STUD`, `GYPSUM_BOARD`, `JAMB`, `LINTEL` |

Parts are read as `BEAM` and `CONTOURPLATE` model objects, in the plan
view's `DisplayCoordinateSystem`.

The new model `MODEL-2` is built the same way — the same part names,
the same 35 mm stud, the same 63 mm track and the same 12.5 mm board — but
it holds **234** wall panels in **19** writable wall layout drawings, one
per block, and a panel can be a **ledge wall**: a wall that steps out of
the line of the wall behind it, built with more than one `BOTTOM_TRACK`.

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

> **The position of the assembly.** `ASSEMBLY_POS` is read from the
> **assembly**, not from the part, because a model may leave it off its
> parts: in `MODEL-2` the parts carry nothing, and the position is
> only written on the assembly.  The position of the part is used when the
> assembly has none, so the old model keeps working.
>
> The position is **not** an identity: two different assemblies can carry
> the same `ASSEMBLY_POS`.  The parts are therefore grouped by the **id of
> the assembly** they belong to (`WfPart.Key`), and the position is only a
> name.


## R1 — bay chain (one dimension set per wall panel)

`WfRules.ChainStations(panel, tol)`. Along the panel's own axis:

1. the **track** ends of the panel are always measured;
2. the **assembly** ends (track + board) are measured as well, so the
   overhang of a board past the track shows up as its own segment
   (`BP4-WP11` in [50]);
3. every stud contributes its **centre**;
4. a stud **flush with a panel end** is skipped — the panel end measures it;
5. a stud **inside an opening** is skipped — *unless the opening has no
   door frame*, where the stud is a mullion of the wall and is measured
   like any other member (see R2);
6. a run of **touching** studs contributes **one** value: the shared face
   between the first and the second stud of the run.

## R2 — opening stations (merged into the panel's chain)

`WfRules.OpeningStations(panel, tol)`. An opening is a lintel and the
**jambs that hold it**, and there are **two kinds** of it. The kind is
decided by the jambs alone, never by the lintel:

* a **door frame** is a **pair of adjacent jambs**: the jamb that holds the
  lintel has a **touching partner**;
* a **gap** in the wall is an opening whose jambs have **no touching
  partner** — there is no door frame, the two jambs are the end studs of
  the wall either side of the opening.

`WfRules.IsOpenGap(panel, lintel, tol)` tells the two apart.

### R2.1 — an opening with a door frame

The lintel bears on one of the two jambs of the pair, and the opening is
measured at the **outer edge of that jamb** (the edge the lintel bears on).
The other jamb of the pair is not measured.

**Verified on the new model:** the edge of the jamb is measured in
**52 of 56** cases; the **centre** of the jamb in **1 of 56**.  So the
edge is the rule and the centre is the exception.

### R2.2 — an opening without a door frame (a gap)

The jambs that hold the lintel are **ordinary members of the wall** — they
are the end studs of the wall either side of the opening — so they are
measured at their **centre**, like any other stud.  The **mullion** that
stands in the gap is an ordinary member of the wall as well, and it is
measured at its centre too (this falls out of R1.5: a stud inside an
opening that has no door frame is measured).

**Verified on the new model:**

| what | how often the hand-made drawing measures it |
|---|---|
| the centre of the mullion in the gap | **20 of 21** |
| the centre of a jamb of the gap | **14 of 28** |
| the edge of a jamb of the gap | **0 of 28** |

The edge of a gap jamb is **never** measured, so the centre is the rule.
The jamb centre is only drawn in half of the cases, but the drawing that
leaves it out is a drawing that leaves out most of the opening — the
LW (ledge wall) family of the new model measures **no** opening at all.
The rule is the same in every case: a gap is made of ordinary members, and
ordinary members are measured at their centre.

> This rule came out of the new model.  The old model has **no** opening
> without a door frame, so nothing there changed when it was added: the
> old model still gives 12 of 18 exact chains.


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

**Every field is searched on its own, and the fields are not joined.**
A phrase can be split over two fields: the per-block drawings of the new
model have `Title2 = "BASE, CEILING, WALL "` and
`Title3 = "FRAMING LAYOUT"`, and glued together they hold
`WALL FRAMING LAYOUT`.  The per-field test is what leaves them alone
(see the trap 17 of the hand-over pack).

In the model of this project that is exactly two drawings: **[50]** and
**[57]**, both with `Title2 = "WALL FRAMING LAYOUT"`. The panel drawings
are named `WALL_PANEL` and the other layout drawings are named
`BASE FRAMING LAYOUT` and `CEILING FRAMING LAYOUT` — none of them
matches.

**R8.2 — the target is the active drawing when it is a wall layout
drawing; otherwise EVERY wall layout drawing of the model that may be
written to is worked on, one after the other.**

The new model has **21** wall layout drawings — one per block — and the
blocks are far apart in the model.  Working on one of them means working
on all of them, because the model as a whole is what gets laid out.
Two of the 21 are frozen, so **19** of them are worked on.

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
from) is the hand-made drawing of the **same block**; the preferred mark
is used when the model has no drawing of that block. It is only ever read.

**R8.5 — the panels of a drawing are the assemblies the drawing shows.**

A GA drawing shows the assemblies of **its own block** and of no other.
The macro therefore takes the wall panels out of the **assemblies that
the drawing shows**, and not out of the whole model.

This is what makes a model with many blocks work: the macro cannot
dimension a wall that is not on the drawing it is dimensioning.  When the
drawing shows no wall assembly at all — an empty view, or a model whose
assemblies are not shown in the drawing — the panels are found the old
way, by the name of the assembly (R9).

The drawing API has **no** `AssemblyIdentifier`: the part → assembly map
is built on the **model** side (`Part.GetAssembly()`), and the drawing
gives the id of the model part.  The two are joined in
`WfCore.ShownAssemblies`.


## R9 — the wall panels of a model

The wall panels are the groups of parts that belong to the **same
assembly**. The prefix of this project is `BP4-WP`, but that is only a
**preference**: on another computer the model may use another prefix.

**R9.1 — the configured prefix wins.** `WfRules.BuildPanels(parts,
prefix, tol)` groups the parts by their assembly and keeps the groups
whose name starts with the prefix.

**R9.2 — when the prefix matches nothing, the panels are found
automatically.** `WfRules.BuildPanelsAuto(parts)` groups the parts by
their assembly and keeps the groups that are a **wall assembly**: the
group holds a `BOTTOM_TRACK`, a `TOP_TRACK`, a `STUD` and a
`GYPSUM_BOARD`, and no part whose name starts with `CEILING`.

The second condition is what keeps the ceiling panels of the model
(`BP4-CP-1`, `BP4-CP-2`) out of the layout — they are `CEILING` parts.
`BP4-BF1` and `BP4-CF1` are not walls either: they have no gypsum board.

Both ways find the same 11 panels in this model (`tests/test_rules.py`).

**R9.3 — the groups are keyed by the assembly, not by the position.**

`ASSEMBLY_POS` is a **name**, not an identity: in the new model two
different assemblies carry the same position, and a drawing that shows
one of them must not dimension the other.  The parts are therefore
grouped by the **id of the assembly** they belong to
(`WfRules.GroupKey`), and the position is only used to name the panel on
the drawing.

`WfRules.BuildPanelsForAssemblies(parts, keys, tol)` keeps **only** the
assemblies whose key is in the set — the set that the drawing shows
(R8.5).  A group that is shown in a drawing is not always a wall: a
ceiling panel is built like a wall and a vanity post is not a wall at
all, so the wall test of R9.2 still applies.

**R9.4 — a ledge wall runs from the first track to the last one.**

A **ledge wall** (a wall that steps out of the line of the wall behind
it) is built with **more than one `BOTTOM_TRACK`**.  The panel axis is
the direction of the **longest** of them, and the panel runs from the
start of the first track that is parallel to that axis to the end of the
last one — not from the start to the end of one track.  Without this the
ledge wall of the new model loses its far end.


## R10 — a sheet that carries many wall framing layouts

A GA sheet does not always carry **one** wall framing layout.  The sheet
of the P142 project that the client built by hand, **[176]**
`P021.64_SD_MI_BP1 M/U_ST-133`, carries the wall framing layout of
**19 blocks** of the model, side by side in one view.

The macro of R8 works on **one** layout: it takes the box of all the
panels of the drawing, and the band chain of that box runs from the
first block of the sheet to the last one.  On such a sheet the top and
the bottom chain are one long chain that crosses every block, while the
left and the right chain of each block is right — which is exactly what
was reported.

The AIO macro (`WallFramingAutoDimensionPro-AIO`) draws every layout of
the sheet **on its own**, as if each one of them were alone on a sheet
of its own.

**R10.1 — the block of a wall panel is the part of its assembly
position before the first dash.**

| assembly position | block |
|---|---|
| `BP1-WP3` | `BP1` |
| `BP1M-WP12` | `BP1M` |
| `BP1-LW1` | `BP1` |
| `LW1` | `LW1` — a name without a dash is a layout of its own |

The rule is in the free rules (`WfRules.BlockOf`), so that it can be
tested without a running Tekla.

**R10.2 — the block names are compared in a normalised form.**

The model writes the block `BP7M`, and the drawing of that block writes
`BP07M`.  The names are therefore compared in upper case, without the
spaces and the dashes, and with the **leading zeros of every run of
digits dropped** (`NormaliseBlock`): `BP07M` and `BP7M` are the same
block, but **`BP10` and `BP1` are not**.

**R10.3 — every layout is dimensioned on its own.**

The box of a layout is the box of **its own panels**, and every chain
of the layout uses that box: the band chains, the top outline, the
overall dimensions, the north run, the wall-panel marks, the section
marks and the notes.  The chain of one block never runs into the next
one — that is the whole point of the AIO macro.

**R10.4 — the stud chain belongs to the panel, not to the layout.**

The chain of the openings of a wall panel is drawn **once for every
panel of the sheet**, not once for every layout: the panels of a block
share their layout, but their chains are their own.

**R10.5 — the section marks of the sheet are kept.**

The section marks of a sheet that carries many layouts belong to the
layouts, and the ones of the client carry the tag of the macro as well
(a drawing that was put together from the drawings of the macro keeps
the tag of the objects it was copied from).  The AIO macro therefore
**does not delete** the section marks of the sheet, and it copies the
section marks of the drawing of the block only into a layout that has
**none of its own** — the same rule as R8.4, counted per layout.

**R10.6 — the AIO macro asks for no licence and draws everything.**

`WfCore.ExecuteAio` takes no edition and never reads the licence.  It
draws the free stages (the stud chain of every panel, the band chains of
every layout) and, when the Pro module is installed, the Pro stages as
well (the top outline, the overall dimensions, the north run, the
wall-panel marks, the section marks and the notes) — for **every** layout
of the sheet.


## The new model (MODEL-2) — what it changed and what it did not

`MODEL-2` holds 4887 parts in 503 assemblies, of which 3875 are
wall parts and 292 assemblies carry walls.  The 234 wall panels are
spread over **19 writable wall layout drawings** (`BP01`…`BP10`,
`BP01M`…`BP10M`, scale 1:15) — all of them in **one** display coordinate
system.  Two further drawings, [50] and [72], are frozen.

The model also holds **per-panel drawings** (`[BP1-WP.10] WALL_PANEL`) and
**per-block drawings** (`[BP76] BP01M-10001`, `BASE, CEILING, WALL`).
Those are issued and frozen, and the macro never touches them.

### The selection was measured on the live model

The model holds **695 drawings**.  The rule of R8.1 was run over all of
them, with the macro's own test:

| | drawings |
|---|---|
| the model | 695 |
| match the title rule, per field — **the macro** | 21 |
| match the title rule, joined with a space | 21 |
| match the title rule, glued without a space | 40 |
| **the targets: writable wall layout drawings** | **19** |

The 21 are the 19 `BP01`…`BP10M` drawings, plus [50] and [72] — both
**frozen**, and both **refused**.  The 19 extra drawings of the last row
are the per-block drawings `[75]`…`[93]`, whose title phrase is split
over two fields; they are frozen and issued as well, so they would be
refused even if the rule had matched them.


### The hand-made drawings of the new model are not a rule set

The wall layout drawings of the new model are **hand-made**, and they do
not agree with each other:

* the **WP13 family** measures the **centres** of the jambs of an opening;
  the **LW (ledge wall) family** measures **no** opening at all;
* the WP8 panel of the **old** model measures the centre of the outer
  jamb of its openings, and the WP7 panel — the same construction —
  measures none;
* the WP4 panel of the old model measures the **centres** of a touching
  stud run, while the same construction in the new model is measured at
  the **shared face**;
* one hand-edited value differs from the model by 10 mm (31947.5 against
  a stud centre of 31957.5).

Chasing those differences would be chasing hand-made noise, and the
hand-over pack forbids hard-coding a number that came from a hand edit.
The rules are therefore taken from the cases where the drawings **agree**
with each other, and the cases where they disagree are left to the rule.

### The right measure is a subset, not equality

The hand-made drawings of the new model use **wall-line chains** — one
chain that spans several panels — while the macro gives every panel its
own chain.  The two can never be equal, so the test is the weaker one:
**is the chain of the macro contained in one chain of the drawing?**

```
the chains of the macro are a SUBSET of a hand-made chain in 205 of 291 cases (70 %)
the chains of the macro are the SAME as a hand-made chain in 132 of 291 cases
stations the macro measures that the drawing does not: 163
```

The 163 stations are the panel details that live in the **per-panel**
drawings, and the jamb centres of the ledge-wall family that the drawing
leaves out.  `tests/test_upgrade.py` reports them one by one, and it
**reads only** — it writes nothing into the model.

### The old model did not change

The new rule of R2.2 (a gap in the wall) has no counterpart in the old
model, so the old model still gives **12 of 18** exact chains and the same
six hand-made sets.  Nothing was traded away to gain the new model.

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

### Where the section marks come from, in a model with many blocks

The section lines belong to **one block**. Copying them from another
block's drawing would put them in the wrong place, so the reference is
chosen in this order:

1. **the drawing that is being dimensioned keeps its own** — every
   writable wall layout drawing of `MODEL-2` already carries its own
   section lines (one or two), and all 19 of them keep them;
2. **the hand-made drawing of the same block** — the block is named in the
   first title of a drawing (`BP07M` for the wall layout drawing of that
   block), and the hand-made drawing of a block is the `<BLOCK>-10001`
   one, which carries the section lines of that block;
3. the **preferred mark** (`[50]`), when the model has it *and* it really
   carries section marks;
4. **nothing** — and the macro says so and copies none.

The section lines of the reference are also **checked against the layout
of the drawing that is being dimensioned**: they are brought into the
coordinate system of its plan view, and a line that does not touch the
layout is left out. The blocks of a model are metres apart, so a line of
another block cannot pass that test.

That test is a **rule** and not a line of code in the module: it is
`WfRulesPro.SectionLinesInLayout` in the Pro rules
(`WF_Rules_Pro.dll`, no Tekla reference), and it is tested without
Tekla by `tests/test_section_lines.py`. The Pro module only reads the
drawings and brings the points into the right system.

A wall layout drawing that the macro would work on is **never** used as a
reference: it belongs to another block.

Three things had to be right for this to work, and all three were wrong
at first:

* the count of the section marks of a drawing has to walk **every** view
  of it, not only the first one — see API trap 12;
* in the new model `[50]` is a *different* drawing — another drawing of
  the **BP1 M/U** block — that carries **no** section marks at all;
* the search then took the **first** hand-made drawing that carried
  section marks, and in a model of many blocks that is `[72]`, the
  drawing of the **BP1 M/U**
  block. Its five section lines are hand placed at X −468 … +3604, while
  the walls of `BP07M` are at X 72012 … 74788: they landed about 68 m to
  the left of the layout, on the side of the sheet. The rule asks now for
  the drawing of the **same block** (`[87] BP07M-10001` for `BP07M`) and
  checks its lines against the layout.

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
