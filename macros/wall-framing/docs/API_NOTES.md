# API notes — Tekla Structures 2025

Everything in this project is written against **Tekla Structures 2025**
only. The APIs of 2023, 2024 and 2026 are **not** assumed to be
compatible, and no member is used that has not been seen on this
install.

## The sources of truth

| what | where |
|---|---|
| the installed assemblies | `C:\TeklaStructures\2025.0\bin\` |
| the installed API documentation | `C:\TeklaStructures\2025.0\bin\Tekla.Structures.Drawing.xml` (and the `.xml` of the other assemblies) |
| live reflection over the real assemblies | `tests/asm_versions.py`, `tests/probe_*.py` |

The assembly versions this project binds to:

| assembly | version |
|---|---|
| `Tekla.Structures.dll` | 2025.0.0.0 |
| `Tekla.Structures.Drawing.dll` | 2025.0.0.0 |
| `Tekla.Structures.Model.dll` | 2025.0.0.0 |
| `Tekla.Structures.Geometry3d.Compatibility.dll` | 2025.0.0.0 |
| `Tekla.Macros.Runtime.dll` | 7.1.0.0 |

If a member is not in the XML documentation of the installed assemblies,
it is **not** used. Where the documentation and the behaviour disagree
(that happened once — trap 2 below), the behaviour wins and the
difference is written down.

## The members this project uses

### `Tekla.Structures.Drawing`

| member | used for |
|---|---|
| `Drawing.GetSheet()` | the sheet of any drawing of the model — **also of a drawing that is not open** |
| `Drawing.Mark`, `Name`, `Title1`, `Title2`, `Title3` | recognising a drawing by its **title**, not by its mark |
| `Drawing.IsFrozen`, `IsIssued`, `IsIssuedButModified` | refusing to write into an issued or frozen drawing |
| `DrawingHandler.GetDrawings()` | every drawing of the model (`DrawingEnumerator`) |
| `DrawingHandler.GetActiveDrawing()` | the drawing the user is on |
| `DrawingHandler.SetActiveDrawing(Drawing)` | activating another drawing — **returns `bool`** |
| `DrawingHandler.SaveActiveDrawing()` | making the objects survive (see trap 1) |
| `Sheet.GetViews()` | the views of a sheet |
| `View.GetAllObjects()` | every object of a view (used to copy the section marks) |
| `View.GetModelObjects(Identifier)` | the drawing `Part` of a model part (needed to attach a `Mark`) |
| `View.DisplayCoordinateSystem` | the coordinate system the model is read in |
| `View.Attributes.Scale` | the paper → model conversion of the dimension distances |
| `StraightDimensionSetHandler.CreateDimensionSet(view, PointList, Vector, double)` | the dimension sets |
| `StraightDimensionSet.Attributes` | the style (a **copy** — see trap 2) |
| `StraightDimensionSet.Distance` | the distance of the dimension line, **in model mm** |
| `StraightDimensionSet.Modify()` | write the changes back |
| `Mark(ModelObject)`, `Mark.InsertionPoint`, `Mark.Placing`, `Mark.Attributes` | the wall-panel marks |
| `Mark.MarkAttributes.RotationAngle` | the text angle of a mark (the only public lever) |
| `PropertyElement`, `PartMarkPropertyElementTypes.AssemblyPosition()` | the associative `ASSEMBLY_POS` content of a mark |
| `AlongLinePlacing(start, end)` | the leader line of a mark |
| `Text` | the notes |
| `SectionMark` | the section marks that are copied from the reference drawing |

### `Tekla.Structures.Model`

`Model`, `Part` (`ProfileName`, `StartPoint`, `EndPoint`), `Beam`,
`ContourPlate`, `ReportProperty` (`ASSEMBLY_POS`), `Identifier`,
**`Part.GetAssembly()`**, **`Assembly.Identifier.ID`**,
**`Assembly.GetReportProperty(name, ref value)`**,
`ModelObjectSelector`, `WorkPlaneHandler`, `TransformationPlane`,
`Solid` (`MinimumPoint`, `MaximumPoint`).

### `Tekla.Structures.Geometry3d`

`Point`, `Vector`, `PointList`.

### `Tekla.Macros.Runtime`

`IMacroRuntime`, `MacroEntryPointAttribute`.

## The thirteen traps

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

### 8. `SetActiveDrawing` returns `bool`, and a frozen drawing is still readable

`DrawingHandler.SetActiveDrawing(Drawing)` has three overloads —
`(Drawing)`, `(Drawing, bool showDrawing)` and
`(Drawing, bool, bool forceOpen)` — and **all three return `bool`**. The
value says whether the drawing could be made active.

`Drawing.IsFrozen`, `Drawing.IsIssued` and `Drawing.IsIssuedButModified`
tell whether a drawing may be written to.  A frozen drawing is still
**readable** through `GetSheet()` (trap 6) — the issued reference drawing
[50] of this project is frozen, and the macro copies the section marks out
of it.

### 9. The drawing API has NO `AssemblyIdentifier`

A GA drawing shows the assemblies of one block, and the macro has to know
which.  The **drawing** side gives the id of the **model part**
(`ModelObject.ModelIdentifier.ID`); there is no member that gives the id of
the assembly the part belongs to.

The join therefore has to be made on the **model** side:

```csharp
// model side: the assembly of every wall part
TSM.Assembly asm = part.GetAssembly();
int assemblyId = asm.Identifier.ID;          // the key of the group

// drawing side: the model parts the view shows
DrawingObjectEnumerator en = view.GetAllObjects();
while (en.MoveNext())
{
    ModelObject mo = en.Current as ModelObject;
    int id = mo.ModelIdentifier.ID;          // the id of the MODEL PART
    ...                                       // look the assembly up in the map
}
```

The map is built once per run and is cleared when the model is read again.

### 10. `ASSEMBLY_POS` can live on the assembly only

`Part.GetReportProperty("ASSEMBLY_POS", ref value)` returns **nothing** on
a model that writes the position on the assembly — and the assembly
carries it:

```csharp
string apos = "";
asm.GetReportProperty("ASSEMBLY_POS", ref apos);   // the assembly first
if (string.IsNullOrEmpty(apos))
    p.GetReportProperty("ASSEMBLY_POS", ref apos);   // then the part
```

The macro reads the assembly first and falls back to the part, so both
kinds of model work.

The position is a **name**, not an identity: two different assemblies can
carry the same `ASSEMBLY_POS`, so the parts are grouped by the **id of the
assembly** and the position is only used to name the panel on the drawing.

### 11. A drawing's title is FOUR separate fields, and they must be searched one by one

`Title1`, `Title2`, `Title3` and `Name` are four independent strings.
A title phrase can be **split across two of them**:

| drawing | `Title2` | `Title3` |
|---|---|---|
| `[75]`…`[93]` (the per-block drawings of `MODEL-2`) | `BASE, CEILING, WALL ` | `FRAMING LAYOUT` |

Glue those two fields together and the drawing holds the phrase
`WALL FRAMING LAYOUT` — it would be picked up as a wall layout drawing.
**The macro searches every field on its own**, so it never is.

Measured on `MODEL-2` (695 drawings):

| the title rule | drawings it matches |
|---|---|
| per field — **the macro** | 21 |
| joined with a space | 21 |
| glued without a space | **40** |

The 19 extra drawings are the per-block drawings `[75]`…`[93]`. They are
frozen and issued as well, so even a false positive would be refused —
but the per-field test is the thing that keeps the selection honest, and
it is what the macro does.

### 12. The FIRST view of a drawing is not always the plan view

`Drawing.GetSheet().GetViews()` gives the views of a drawing in the
order they sit in the sheet, and the first one is not always the plan
view.  In `MODEL-2` two of the wall layout drawings (`[139]` and
`[145]`) have an **unnamed container view** in front of their plan
view:

| drawing | view 0 | view 1 |
|---|---|---|
| `[139]` `BP02-WPL` | `(unnamed)` — 0 section marks | `Plan 0` — 2 section marks |
| `[145]` `BP05M-WPL` | `(unnamed)` — 0 section marks | `Plan 0` — 1 section mark |

The macro finds the plan view by its **coordinate system** (looking
straight down: `AxisX.Z` and `AxisY.Z` both zero) and not by its
position in the list — see `WfCore.DimensionDrawing`.

A count that walks the views and stops at the first one therefore
reports **zero** for a drawing that does carry the objects.  Every count
over the views of a drawing has to walk **all** of them.

### 13. A section mark is in the MODEL coordinates, the layout is in the VIEW coordinates

`SectionMark.LeftPoint` and `SectionMark.RightPoint` are the two ends of
the cutting line **in the model coordinates of the model** — they are
where the line is in the *model*, not where it is drawn on the sheet.

The wall layout of a drawing, on the other hand, is in the coordinates of
the **plan view** of that drawing: `WfCore.ReadWallParts` reads the
solids in the work plane of `view.DisplayCoordinateSystem`, so the panels
are in that system.

The two are the same only when the plan view is aligned with the model —
and it is, in the drawings of `MODEL-2` (origin `0,0`, axes
`1,0` and `0,1`), but nothing guarantees it.  A view that is rotated or
moved gives a layout in a *different* system, and a section line of
another block then looks as if it were on the walls.

The comparison is therefore made in one system: the point of the line is
brought into the system of the view first.  The coordinate system of a
view is given by its `Origin` and its two `Axis` vectors, so the point is
projected on them:

```csharp
double dx = p.X - cs.Origin.X, dy = p.Y - cs.Origin.Y, dz = p.Z - cs.Origin.Z;
double x = dx * cs.AxisX.X + dy * cs.AxisX.Y + dz * cs.AxisX.Z;
double y = dx * cs.AxisY.X + dy * cs.AxisY.Y + dz * cs.AxisY.Z;
```

`CoordinateSystem` has **no** transform method of its own (it has only
`Origin`, `AxisX` and `AxisY`); the projection above is the whole of it.

## Units

| quantity | unit | where it is converted |
|---|---|---|
| model geometry | mm | — |
| the paper distance of a dimension line | paper mm | `× view.Attributes.Scale` before it is given to the API |
| `StraightDimensionSet.Distance` (read and written) | **model mm** | the API property is *not* in paper mm — this is the easiest thing to get wrong |
| the text height of a mark | mm | — |

## The compilation

The macro is compiled with the Roslyn compiler of Visual Studio 2022,
against the .NET Framework 4.x reference assemblies and the Tekla
assemblies of the install:

```
C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\*.dll
C:\TeklaStructures\2025.0\bin\Tekla.Structures*.dll
```

`source/build.ps1` does this and nothing else — no MSBuild project, no
NuGet, no packages.

**The paths are not hard coded.** The build script finds the Tekla
installation (`TEKLA_HOME`, `C:\TeklaStructures\*`,
`C:\Program Files\Tekla Structures\*`, the other drives; a 2025
installation is preferred) and the compiler (every edition of Visual
Studio 2022, and the Build Tools).  `-TeklaPath` and `-CscPath` override
the search.  The same command therefore works on every computer of the
project.

The **merged single file** that the Tekla macro catalog compiles is
written **without a byte-order mark**: Windows PowerShell's
`Set-Content -Encoding UTF8` writes one, and Tekla's own macro compiler
is stricter than Roslyn about a BOM at the start of a file.  The build
uses `File.WriteAllText` with `UTF8Encoding($false)` instead.

## 3D views on a GA drawing (checked 2026-10-24, Tekla 2025.0 `Tekla.Structures.Drawing.xml`)

**Trap: `View.Create3dView(...)` cannot be used on a GA drawing.**  Both
overloads (`Drawing, Point, ViewAttributes, out View` and
`Drawing, CoordinateSystem, CoordinateSystem, AABB, Point, ViewAttributes, out View`)
document `InvalidTypeException` *"Thrown if given drawing is GA drawing"*.
The isometric drawings of the project are GA drawings.

The GA route is the constructor on the sheet, then `Insert()`:

```csharp
View v = new View(drawing.GetSheet(), viewCS, displayCS, partIdentifiers /* ArrayList of Identifier */);
v.Insert();
v.Attributes.Scale = 20;  v.Origin = new Point(x, y, 0);  v.Modify();
drawing.CommitChanges();
```

Other overloads: `(ContainerView, CS, CS, AABB restrictionBox[, string attributesFile])`
and `(ContainerView, CS, CS, ArrayList[, string attributesFile])`.
`View.ViewAttributes` has `LoadAttributes(string)` but **no save** - a
view's attributes cannot be written to a file from the API, so the
isometric macro copies geometry, scale, position and name only.
Read back from an existing view: `ViewCoordinateSystem`,
`DisplayCoordinateSystem`, `RestrictionBox` (AABB in view coordinates),
`ViewBase.Origin`, `Attributes.Scale`, `Name`, `ViewType`
(`ModelView` / `_3DView`).  Compile-checked against the 2025.0 DLLs;
**not yet run live**.

## Isometric views - traps found live on 2026-10-24 (Tekla 2025.0, drawing [184] BP20M-80001)

1. **A view of a drawing that is NOT open reports no sheet geometry.**
   `GetSheet().GetViews()` works on a non-active drawing (HANDOVER trap 7), and
   `ViewCoordinateSystem`, `DisplayCoordinateSystem`, `RestrictionBox`,
   `Attributes` and `Name` read correctly - but `Origin` is meaningless and
   `FrameOrigin` / `Width` / `Height` read as 0.  Do not take sheet positions
   from a reference drawing that is not open; place by role instead.
2. **Right after `Insert()`, the first new view can report a stale frame**
   (921 x 991 instead of 182 x 237 - the frame at a default scale).  Create all
   views, `CommitChanges()`, THEN `Select()` each view and measure; correct in
   passes with a commit between passes.
3. **The view origin is not the frame centre** - with a global view plane it is
   a model-related point far from the frame (offsets of -4900 mm seen).  Place a
   view by moving its frame centre: `Origin + FrameOrigin + (Width, Height)/2`.
4. **The depth cut of ISOMETRIC VIEW 3** is the `RestrictionBox` (z up to 1500
   of 2600 on BP01/BP02M).  It is recorded as a fraction of the reference
   block's extent in the view coordinates and applied to the target block.
5. **Part marks are created in the background** ("Creating marks...") after
   `Insert()` returns: remove them, wait, and remove again.
6. **A failed Tekla remote connection poisons the macro session**: once
   `DelegateProxy`'s type initializer throws ("Remote connection to Tekla
   Structures channel ... failed"), every later macro call fails the same way
   until Tekla restarts.  Do not drive Tekla's UI from outside while a macro
   is waiting on a dialog.
7. **Assembly.LoadFrom keeps the first build in memory** (and locks the DLL)
   until Tekla restarts.  The isometric loader loads `Assembly.Load(bytes)`
   with an `AssemblyResolve` handler for the `WF_*` dependencies, so a rebuild
   is picked up on the next run without a restart.
8. `Part.PartAttributes` has no `DrawInnerContours` in 2025, and `LineTypes`
   is a class (static members), not an enum.
