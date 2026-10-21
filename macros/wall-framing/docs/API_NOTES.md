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
`ContourPlate`, `ReportProperty` (`ASSEMBLY_POS`), `Identifier`.

### `Tekla.Structures.Geometry3d`

`Point`, `Vector`, `PointList`.

### `Tekla.Macros.Runtime`

`IMacroRuntime`, `MacroEntryPointAttribute`.

## The eight traps

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
