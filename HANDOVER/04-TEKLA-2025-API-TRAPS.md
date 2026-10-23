# 04 — The Tekla 2025 traps

Every trap here was found the hard way, on the real assemblies of
`C:\TeklaStructures\2025.0` (build 2025.0.46985.0, `Tekla.Structures.*`
v2025.0.0.0, `Tekla.Macros.Runtime` v7.1.0.0, .NET Framework 4.x).

**Do not rediscover these.**  Most of them produce a wrong result with **no
error message at all** — that is what makes them expensive.

The full notes with the member list are in
[`macros/wall-framing/docs/API_NOTES.md`](../macros/wall-framing/docs/API_NOTES.md).

---

## 1. `Drawing.CommitChanges()` does NOT persist

It commits to the in-memory editor only.  The objects vanish when the
drawing is re-read.

```csharp
dr.CommitChanges();                     // not enough
bool saved = dh.SaveActiveDrawing();    // this persists it
```

**This is the single most common way to lose an afternoon.**  If the
macro's output disappears, this is why.

---

## 2. `MarkBaseAttributes.Angle` has an INTERNAL setter

`Angle` is documented as *"the actual angle of the mark, in degrees"*.
`CanWrite` is `True`, but `set_Angle` is `IsAssembly=True` — so C# gives
**CS0200**.

Reflection *can* write it.  **Do not.**  The drawing engine **recomputes
`Angle` on save**, so the reflected value is discarded.

**The public lever is `MarkBaseAttributes.RotationAngle`** — *"the mark
rotation angle set by the user"*.  Setting it makes the engine compute the
matching `Angle`: 90 → 90, 0 → 0, 270 → 270.

---

## 3. `Mark.Attributes` returns a COPY

Mutating it in place does nothing.  Write to a copy and assign it back:

```csharp
Mark.MarkAttributes attrs = mk.Attributes;   // a COPY
attrs.RotationAngle = 90.0;
mk.Attributes = attrs;                       // write the copy back
```

The same applies to `StraightDimensionSet.Attributes`.

---

## 4. The engine derives the mark text angle from the LEADER direction

In the reference drawing every wall-panel mark has `RotationAngle = 0` but
a non-zero `Angle`.  `Angle` follows the direction of the leader line:

| leader runs | `Angle` |
|---|---|
| +Y (up) | 90 |
| −Y (down) | 270 |
| +X (right) | 0 |
| −X (left) | 180 |

So the rule and the API agree — the macro only has to *tell* the engine the
angle.

---

## 5. `Mark` has exactly ONE constructor

```csharp
new Mark(ModelObject)
```

`Tekla.Structures.Drawing.ModelObject` is abstract and its
`ModelIdentifier` setter is internal, so the only way to attach a mark to
an arbitrary model part is to read the drawing `Part` that already
represents it:

```csharp
DrawingObjectEnumerator en = view.GetModelObjects(new Identifier(modelId));
while (en.MoveNext()) { var mo = en.Current as ModelObject; ... }
```

---

## 6. `SetUserProperty(name, value)` DOES survive a save

This is the way to mark your own output so that a re-run **replaces** it
instead of duplicating it:

```csharp
obj.SetUserProperty("P142_WF_AUTODIM", "1");
```

Reading it back needs an out/ref parameter:

```csharp
string value = "";
bool ok = obj.GetUserProperty("P142_WF_AUTODIM", ref value);
```

> **pythonnet returns a TUPLE for an out-parameter call:**
> `ok, value = obj.GetUserProperty(key, value)`.
> Reading the tuple as a boolean silently reports *"no property"* and cost
> a debugging cycle.

---

## 7. `Drawing.GetSheet()` works on a NON-active drawing

`GetSheet()` → `GetViews()` → `GetAllObjects()` reads any drawing of the
model, **including one that is not open in the editor**.

That is what makes it possible to copy the hand-made section marks out of
the frozen reference drawing [50] without disturbing what the user is
looking at — and without activating it.

---

## 8. Changing the style RESETS the position of the dimension line

`StraightDimensionSet.Attributes` is a copy (trap 3).  Writing the style
back and calling `Modify()` moves the dimension line: the engine re-places
the set with the **default** distance, and the chain ends up on top of the
geometry it measures.

**The fix: write `Distance` again AFTER the style change.**

```csharp
set.Modify();                                     // the style is applied here
dr.CommitChanges();
set.Distance = paperDist * view.Attributes.Scale;   // ...and again
set.Modify();
dr.CommitChanges();
```

Verified on [57]: every chain sits at the paper distance it was asked for.

---

## 9. `DrawingHandler.SetActiveDrawing` returns `bool` — all three overloads

`(Drawing)`, `(Drawing, bool showDrawing)` and `(Drawing, bool, bool
forceOpen)`.  All three return `bool`: whether the drawing could be made
active.

`Drawing.IsFrozen`, `IsIssued` and `IsIssuedButModified` decide whether a
drawing may be **written** to.  A frozen drawing is still **readable**
(trap 7).

---

## 10. `StraightDimensionSet.Distance` is in MODEL millimetres

```
model mm  =  paperDistance × view.Attributes.Scale
```

The API property is **not** in paper mm.  This is the easiest thing in the
whole API to get wrong, and it is wrong in a way that looks right.

---

## 11. `PropertyElement` — do not assign `Value`

```csharp
var pe = new PropertyElement(
    PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition());
pe.Name = "ASSEMBLY_POS";   // Tekla fills Value from the model
```

The `Value` setter is internal — **do not assign it**.

---

## 12. The pythonnet rules

* `import clr` **before** any `from System...` import.
* `System.Collections.Generic.List[T]()` — not `List_`.
* `System.Activator.CreateInstance(t)` for types.
* `clr.GetClrType(x)` before calling `GetProperties()`.
* `GetUserProperty` returns a tuple (trap 6).

---

## 13. The geometry traps of this model

* **A stud runs ACROSS the wall.**  Its extent along the panel axis is its
  **short** axis.  Using the part's own long axis (`Part.Along0/Along1`)
  gives the wrong coordinate.  Use `WfPart.PanelLo(panelHoriz)` /
  `WfPart.PanelHi(panelHoriz)`.
* **A panel has TWO extents, and the rules use different ones:**
  * `Asm0/Asm1` — along the panel axis (track **+** board);
  * `XLo/XHi`, `YLo/YHi` — the absolute bounding box of the whole assembly.

  The **band chains use the absolute box**.  Using the panel-relative
  extent gives a chain that is off by the 1 mm board overhang.

---

## 14. The file in the catalog must have **no byte-order mark**

Windows PowerShell's `Set-Content -Encoding UTF8` writes a BOM, and
Tekla's own macro compiler is stricter than Roslyn about a BOM at the
start of a file.  `source/build.ps1` uses
`File.WriteAllText` with `UTF8Encoding($false)` instead.

If the macro will not compile and the error is at line 1, this is why.

> The file that goes into the catalog is now the **loader**
> (`P142_WF_AutoDimension.cs`, about 8000 bytes), not the old merged
> source.  The rule is the same for any `.cs` file that Tekla compiles.

---

## 15. The drawing API has NO `AssemblyIdentifier`

A GA drawing shows the assemblies of **one block**, and the macro has to
know which ones it is looking at.  The drawing side gives the id of the
**model part** (`ModelObject.ModelIdentifier.ID`); there is no member that
gives the id of the assembly the part belongs to.

The join has to be made on the **model** side:

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

## 16. `ASSEMBLY_POS` can live on the ASSEMBLY only

`Part.GetReportProperty("ASSEMBLY_POS", ref value)` returns **nothing** on
a model that writes the position on the assembly — and the assembly
carries it.  The model `MODEL-2` is such a model: its parts carry no
position at all.

```csharp
string apos = "";
asm.GetReportProperty("ASSEMBLY_POS", ref apos);   // the assembly first
if (string.IsNullOrEmpty(apos))
    p.GetReportProperty("ASSEMBLY_POS", ref apos);   // then the part
```

Read the assembly first and fall back to the part, so that both kinds of
model work.

The position is a **name**, not an identity: two different assemblies can
carry the same `ASSEMBLY_POS`, so the parts are grouped by the **id of the
assembly** and the position is only used to name the panel on the drawing.

## 17. A drawing's title is FOUR fields, and the phrase can be SPLIT across two

`Title1`, `Title2`, `Title3` and `Name` are four separate strings, and a
title phrase can be split over two of them.  The per-block drawings of
`MODEL-2` are the example:

```
Title2 = "BASE, CEILING, WALL "
Title3 = "FRAMING LAYOUT"
```

Glue those two fields together and the drawing holds the phrase
`WALL FRAMING LAYOUT` — it would pass a wall-layout test.

**The macro searches every field on its own, so it never does.**  Measured
on the 695 drawings of `MODEL-2`:

| the title rule | drawings it matches |
|---|---|
| per field — **the macro** | 21 |
| joined with a space | 21 |
| glued without a space | **40** |

The 19 extra are `[75]`…`[93]`, the per-block drawings.  They are frozen
and issued anyway, so even a false positive would be refused — but the
per-field test is the thing that keeps the selection honest, and it is
what the macro does.  **Do not "simplify" it into a joined string.**

## 18. The FIRST view of a drawing is NOT always the plan view

`Drawing.GetSheet().GetViews()` gives the views in the order they sit in
the sheet, and the first one is not always the plan view.  In
`MODEL-2` two of the wall layout drawings have an **unnamed
container view** in front of their plan view:

| drawing | view 0 | view 1 |
|---|---|---|
| `[139]` `BP02-WPL` | `(unnamed)` — 0 section marks | `Plan 0` — 2 section marks |
| `[145]` `BP05M-WPL` | `(unnamed)` — 0 section marks | `Plan 0` — 1 section mark |

The macro finds the plan view by its **coordinate system** — looking
straight down, `AxisX.Z` and `AxisY.Z` both zero — and not by its
position in the list.

A count that walks the views and stops at the first one reports **zero**
for a drawing that does carry the objects.  That is exactly what
happened to the section marks of the two drawings above.  **Every count
over the views of a drawing has to walk all of them.**

## 19. The modules are three ASSEMBLIES — and they have to be of the same version

The macro is not one file.  It is:

| assembly | what it is | where it lives |
|---|---|---|
| `P142_WF_AutoDimension.dll` | the thin loader the catalog compiles | the macro catalog |
| `WF_Core.dll` | the drawing machinery | `%LOCALAPPDATA%\P142\WallFraming` |
| `WF_Rules.dll` | the free rules, no Tekla dependency | the same folder |
| `WF_Pro.dll` | the Pro stages (**private**, never published) | the same folder |
| `WF_Rules_Pro.dll` | the Pro rules (**private**, never published) | the same folder |

`WF_Pro.dll` is built **against** `WF_Rules.dll` and is loaded by
reflection at run time.  When the two are of different versions, the
module calls members of the rules that no longer exist and **every one
of its calls fails**:

```
Method not found: 'Double P142.WallFraming.WfPanel.get_A0()'
```

That is what happened on this computer: the deployed `WF_Pro.dll` was
built when `WfPanel.A0` was a **property**, the new rules made it a
**field**, and the module then failed 234 times — once per wall panel
mark.  The message says nothing about the cause, and it looks like 234
unrelated failures rather than one deployment problem.

So the rules carry `WfRules.ApiVersion`, the Pro module carries
`WfPro.RulesApiVersion`, and the core compares the two **at run time**
before it calls the module.  A `const` is fixed when the assembly is
compiled, so the number has to be read by **reflection** — a constant
read at compile time reports the version the rules had at build time,
not the one that is on the disk.

**When the message about the versions comes up, the fix is
`build.ps1 -Deploy`.  Do not try to fix it in the code.**

## 20. A section mark is in the MODEL coordinates, the layout is in the VIEW coordinates

`SectionMark.LeftPoint` and `SectionMark.RightPoint` are the two ends of
the cutting line **in the model coordinates of the model**.

The wall layout of a drawing is in the coordinates of the **plan view**:
`WfCore.ReadWallParts` reads the solids in the work plane of
`view.DisplayCoordinateSystem`, so the panels are in that system.

The two are the same only when the plan view is aligned with the model.
They are, in the drawings of `MODEL-2` (origin `0,0`, axes `1,0`
and `0,1`), but nothing guarantees it — a view that is rotated or moved
gives a layout in a *different* system, and a section line of another
block then looks as if it were on the walls.

Bring the point into the system of the view before it is compared.
`CoordinateSystem` has **no** transform method (it has only `Origin`,
`AxisX` and `AxisY`), so the point is projected on the two axes:

```csharp
double dx = p.X - cs.Origin.X, dy = p.Y - cs.Origin.Y, dz = p.Z - cs.Origin.Z;
double x = dx * cs.AxisX.X + dy * cs.AxisX.Y + dz * cs.AxisX.Z;
double y = dx * cs.AxisY.X + dy * cs.AxisY.Y + dz * cs.AxisY.Z;
```

This is what the section-line rule is about: the hand-placed lines
belong to ONE block, and the block is told from the layout the lines
have to touch.

## 21. The Pro rules have a version number of their own

`WF_Rules_Pro.dll` is a second rules assembly, and the Pro module calls
it — so the two can be of different builds in the same way as the free
rules and the Pro module (trap 19).

The Pro rules therefore carry `WfRulesPro.ApiVersion`, the Pro module
carries `WfPro.ProRulesApiVersion`, and the module compares the two **at
run time**, by reflection, before it draws anything.  A module and a Pro
rules assembly of different builds are now refused with a clear message
instead of failing on a member that is not there.

**When the message about the versions comes up, the fix is
`build.ps1 -Deploy`.  Do not try to fix it in the code.**
