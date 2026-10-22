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

## 14. The merged file must have **no byte-order mark**

Windows PowerShell's `Set-Content -Encoding UTF8` writes a BOM, and
Tekla's own macro compiler is stricter than Roslyn about a BOM at the
start of a file.  `source/build.ps1` uses
`File.WriteAllText` with `UTF8Encoding($false)` instead.

If the macro will not compile and the error is at line 1, this is why.
