# The sectional elevation and the isometric drawing — the study

The wall-framing macro draws the **layout** of a block: the plan, the bay
chains, the band chains, the marks.  The client makes two more kinds of
drawing out of the same model:

* a **sectional elevation** — one view per section line, with the material
  notes, the level marks, the dimension chains and the marks of the
  assemblies the section cuts;
* an **isometric drawing** — three 3D views of the block, with the marks of
  the parts and the weld marks.

This document is the **study** of the two: what they are, what the API of
Tekla 2025 offers, what can be derived from the model and what cannot, and
the plan of the work.  Nothing of it is built yet — the rules below are the
proposal, and the first thing to do is to turn them into a first run.

Everything here was measured on the model of the client (`MODEL-2` in the
hand-over pack), reading only.  The drawing numbers of the client are not
written down; the references are "the sectional elevation of MODEL-2" and
"the isometric drawing of MODEL-2".

---

## 1. What the two drawings are

### 1.1 The sectional elevation

A **GA drawing** whose `Title2` is `SECTIONAL ELEVATION` (the name of the
drawing carries the block, e.g. `BPxxM-20001`).

It holds one **`SectionView`** per section line of the plan.  The view is
named after the section mark it belongs to (`S1`, `S2`, `S3`, `S4` …) and
its **display coordinate system** is the point of the cut in the model
(the plan coordinate of the section line), while its **origin** is the
position of the view on the sheet.

The reference of `MODEL-2` holds four section views.  Every one of them
holds the same five kinds of things:

| what | how many in the reference |
|---|---|
| the model objects the section cuts | 95 … 199 per view |
| the **notes** (`Text`) | 8 … 12 per view |
| the **marks** of the assemblies (`Mark`) | 12 … 71 per view |
| the **leader lines** of the notes | 12 … 17 per view |
| the **dimension sets** | 3 … 5 per view, 6 … 10 dimensions |

The notes of a view are the **material notes of the block**, and they are
the same set in every view (the ones that are not in the cut are simply not
drawn there):

```
BASE HRS
RHS120*60*4.0 TYP.
CEILING HRS
RHS40*20*3
ALL AROUND TYP.
LGS BOTTOM TRACK
C63*40*0.7 TYP.
LGS TOP TRACK
C63*40*0.7 TYP.
LGS MIDDLE
C63*40*0.7 TYP.
LGS VERTICAL STUD
C61*35*0.7 TYP.
12.5mm  GYPSUM BOARD
CEILING LGS FRAMING TYP.
```

and three **level marks** — `SSL`, `FFL` and `T.O.S` — which are texts of
their own, placed at the levels of the model.

### 1.2 The isometric drawing

A **GA drawing** whose `Title2` is `ISOMETRIC VIEW` and whose `Title3` is
`3D` (the name carries the block, e.g. `BPxxM-80001`).

It holds three **`ModelView`s**, all named `3d` — the same block seen from
three directions.  A 3D view carries **no dimension and no text** at all:

| view | the marks | the weld marks | the leader lines |
|---|---|---|---|
| the first | 0 | 20 | 20 |
| the second | 211 | 20 | 42 |
| the third | 3 | 20 | 20 |

The marks of the middle view are the marks of **every part of the block**,
and the weld marks are the welds of the block.  The leader lines are the
leaders of the marks.

---

## 2. The API of Tekla 2025 (checked, not remembered)

| what | how |
|---|---|
| the kind of a view | `View.ViewType`, the enum `ViewTypes`: `UnknownViewType`, `FrontView`, `TopView`, `BackView`, `BottomView`, `EndView`, `SectionView`, `ModelView`, `DetailView`, `_3DView` |
| **create a section view** | `bool View.CreateSectionView(View view, Point startPoint, Point endPoint, Point viewInsertionPoint, double depthUp, double depthDown, ViewAttributes viewAttributes, SectionMarkAttributes sectionMarkAttributes, out View sectionView, out SectionMark sectionMark)` |
| **create a 3D view** | `bool View.Create3dView(Drawing drawing, Point viewInsertionPoint, ViewAttributes viewAttributes, out View view)` and the overload with the view plane, the display plane and the extents |
| the model objects of a view | `View.GetModelObjects()` and `View.GetAllObjects()`; the model objects are `ModelObject`s, and `ModelObject.ModelIdentifier.ID` identifies the part |
| the parts of a view | the core already has it: `WfCore.ReadWallParts(view, model)` and `WfCore.ShownAssemblies(view)` |
| the text of a dimension | `StraightDimension.Value` is a `ContainerElement`; `GetUnformattedString()` gives the text, `Distance` the measured value |
| the text of a mark | an associative mark gets it from the model: the macro adds a `PropertyElement` (`AssemblyPosition` with the name `ASSEMBLY_POS`) to `Mark.Attributes.Content` and Tekla fills the value |
| the profile of a part | `part.GetReportProperty("PROFILE", ref value)` → `C63*40*0.7`.  **The one-argument overload returns nothing on this model** — the `ref` form is the one that works |
| the position of a part | `assembly.GetReportProperty("ASSEMBLY_POS", ref value)` → `BP3-WP2`; the family is the part after the dash: `WP` (wall panel), `LW` (ledge wall), `BF` (base framing), `CF` (ceiling framing), `CP` (ceiling panel) |
| the section lines | the plan drawing holds them as `SectionMark`s (`LeftPoint`, `RightPoint`); the reference plan of the client holds 34 of them |

---

## 3. The rules (the proposal)

### R11 — the sectional elevation

* **R11.1** The target is found by its **title**, like the other drawings:
  `Title2` holds `SECTIONAL ELEVATION` (or `Title1`/`Title3`).  A drawing
  that is **issued or frozen is refused** — the reference of the client is
  exactly that.
* **R11.2** Every **`SectionView`** of the drawing is worked on on its own.
  The views are found by their **type**, never by their order in the sheet.
* **R11.3** The **notes** are drawn from the **note catalogue** (section 4):
  one note per material family that the section cuts.  The family is found
  from the part names of the model objects of the view; the wording is the
  table, not a template.
* **R11.4** The **level marks** (`SSL`, `FFL`, `T.O.S`) are drawn at the
  levels of the model of the section.
* **R11.5** The **dimension chains** are drawn from the model: the level
  heights and the heights of the walls, measured on the same coordinate
  system as the view.
* **R11.6** The **marks** of the assemblies the section cuts are drawn with
  the associative mark (`ASSEMBLY_POS`), the same way as the wall-panel
  marks of the plan.

### R12 — the isometric drawing

* **R12.1** The target is found by its title: `Title2` holds
  `ISOMETRIC VIEW`.  An issued or frozen drawing is refused.
* **R12.2** Every **`ModelView`** of the drawing is worked on on its own.
* **R12.3** The **marks** of the parts the view shows are drawn with the
  associative mark, each with its **leader line**.
* **R12.4** The **weld marks** of the welds of the block are drawn, with
  their leaders.
* **R12.5** The isometric drawing carries **no dimension and no note**.

---

## 4. The note catalogue

The wording of a note is the **client's** wording, and it is not a
template of the profile: the model says `CC61-0.7-7-35` where the note says
`C61*35*0.7`.  The catalogue is therefore a **table**, one row per family
of the model:

| the part of the model | the profile in the model | the note |
|---|---|---|
| `BASE_FRAME` | `RHS120*60*4.0` | `BASE HRS\nRHS120*60*4.0 TYP.` |
| `CEILING BEAM` | `RHS40*20*3` | `CEILING HRS\nRHS40*20*3\nALL AROUND TYP.` |
| `BOTTOM_TRACK` | `C63*40*0.7` | `LGS BOTTOM TRACK\nC63*40*0.7 TYP.` |
| `TOP_TRACK` | `C63*40*0.7` | `LGS TOP TRACK\nC63*40*0.7 TYP.` |
| `MIDDLE_TRACK` | `C63*40*0.7` | `LGS MIDDLE\nC63*40*0.7 TYP.` |
| `STUD` | `CC61-0.7-7-35` | `LGS VERTICAL STUD\nC61*35*0.7 TYP.` |
| `GYPSUM_BOARD` | — | `12.5mm  GYPSUM BOARD` |
| the ceiling framing (`PLATE`, `SHEET`, `FRAME`) | — | `CEILING LGS FRAMING TYP.` |

A note is **two lines**: the line break is a `\n` inside the text of the
`Text` object.

**The catalogue belongs to the free rules** (`WF_Rules.cs`), like every
other table of the macro, so that it can be tested without Tekla.

---

## 5. What is derived, and what is not

| it | derived from the model? |
|---|---|
| which notes a section needs | **yes** — the part names of the objects of the view |
| the wording of a note | **no** — the table of section 4 |
| the level heights | **yes** — the solids of the parts |
| the stud spacing of a section | **yes** — the same way as the plan |
| the assembly of a mark | **yes** — `ASSEMBLY_POS` |
| the placement of a note and its leader | **no** — a constant per family, like the paper distances of the plan |
| the three directions of the isometric views | **no** — the view attributes of the drawing |
| the weld marks | **yes** — the welds of the parts of the block |

This is the same line the wall-framing macro walks: **the rule is
reproduced, the hand work is not.**  The notes of the reference were placed
by hand, and the macro places its own notes at its own offsets.

---

## 6. The traps

1. **The reference is issued and frozen.**  A sectional elevation and an
   isometric drawing of the client are both `IsIssued = true` and
   `IsFrozen = true`.  The macro must refuse them, exactly as it refuses
   the frozen plan.
2. **A section view shows parts, not assemblies.**  The assembly of a part
   is found on the model side (`part.GetAssembly()`), as the wall-framing
   macro already does.
3. **The views are found by their type.**  A sheet can hold a section view,
   a 3D view and the sheet view at the same time.
4. **The 3D view lives in the model space.**  The origin of a `ModelView`
   and its display coordinate system are `(0,0,0)`; the placement of a mark
   in it is a model coordinate, not a place on the sheet.
5. **The marks of a drawing are not the marks of the model.**  The client's
   own marks are not ours: everything the macro writes carries the tag
   `P142_WF_AUTODIM`, and only the tagged objects are removed on a re-run.
6. **`GetReportProperty` with one argument returns nothing** on this model
   (section 2).
7. **The section line of a view is in another drawing.**  The section marks
   live in the plan; the section views live in the elevation.  The two are
   paired by the **name** of the view (`S1` … `S4`) — and the plan of the
   client holds 34 section marks, so the name alone is not enough: the
   section line of the block of the view has to be found as well.

---

## 7. The plan of the work

| stage | what | where |
|---|---|---|
| 1 | the **target selection**: find the drawing by its title, refuse the issued and the frozen one | the core |
| 2 | the **note catalogue** and the level marks | the free rules + the core |
| 3 | the **notes** of a section, with their leaders | the core |
| 4 | the **dimension chains** of a section | the core |
| 5 | the **marks** of a section | the core |
| 6 | the **isometric marks** and the **weld marks** | the core |
| 7 | the **creation of the views** themselves (`CreateSectionView`, `Create3dView`) | the core — **it writes a view into the drawing**, so it is the last stage and the owner's decision |

Every stage is written the same way as the wall-framing macro: a rule in
`WF_Rules.cs` that holds no Tekla reference and is tested without Tekla,
and a writer in the core that holds the Tekla types.

The two new macros get their **own loaders**, the way the AIO macro has its
own: `P142_SE_AutoDimension.cs` (the sectional elevation) and
`P142_ISO_AutoDimension.cs` (the isometric).  The stages that are *free*
(dimensions, marks, notes) go in the free rules; the stages that are
*Pro* (the whole set) go in `WF_Pro.dll` — the same cut as the plan.

---

## 8. What is still open

| # | the question | who answers it |
|---|---|---|
| 1 | Is the note catalogue the same for **every** block?  The reference is one block of one project. | the owner |
| 2 | Which three directions are the isometric views of?  They are in the attributes of the reference drawing. | the owner |
| 3 | Do the two macros ask for a licence, or are they free like the AIO one? | the owner |
| 4 | Is the **placement** of a note (the offset of the text and of the leader) a constant per family, and which constant? | measured on the reference |
| 5 | Shall the macro **create** the section views and the 3D views (stage 7), or only fill the ones that are already there? | the owner |
| 6 | The 211 marks of the middle isometric view: every part of the block, or only the parts of a family? | measured on the reference |
