# Task brief for OmniRush: model the stadium roof in Tekla Structures 2025

Read `AGENTS.md` first. This brief follows its rules. Risk level of this task: **HIGH** (bulk creation through a macro), so every model change needs Sherin's explicit "yes".

## 1. What we are modeling
A tubular steel stadium roof like the reference image (a Dlubal-style framework model of a 70,000-seat stadium roof with about 17,791 members). We do **not** have that model's data, so this is a **parametric look-alike**, not a copy:

- **Plan:** a rounded-square ("superellipse") outer line, about 270 m × 230 m, with a roof opening of about 156 m × 116 m.
- **Section:** the facade rises from the ground, bulges outward, rolls over a shoulder at about +46 m and runs inward as a gently rising roof to the inner edge at +53 m. A 9 m skirt hangs at the inner edge.
- **Structure:**
  - Radial trussed ribs: an outer chord on the skin, an inner chord offset inward, posts, and Warren diagonals.
  - Hoop rings at every node level.
  - X-bracing in the skin in every 4th bay.
  - A heavy compression ring at the inner edge and a base ring at the ground.
- **Members:** all straight CHS tubes (`PDd*t`). Curved members are approximated by straight segments between nodes.

| Rib count | Members | Use |
|---|---|---|
| 48 | 3,504 | first Tekla run (test) |
| 144 | 10,512 | full model (about 60% of the reference's member count) |

`preview_48.png` and `preview_144.png` show the generated geometry. Compare them with the reference image before running anything in Tekla.

## 2. Files in this pack (`tasks/stadium-roof/`)
| File | What it is |
|---|---|
| `stadium-roof.json` | All parameters: plan axes, section points, truss depths, ribs, profiles, classes, origin. No numbers in code. |
| `stadium_geometry.py` | Pure-Python generator, no Tekla needed. Builds the member list, runs sanity checks and writes `members_<N>.csv`. |
| `preview.py` | Renders a CSV to a 3D PNG (needs matplotlib). |
| `MemberImport.cs` | **Generic** Tekla macro. Inserts straight members from any CSV (`name,profile,material,class,x1..z2`, mm, global). Runs silently. Refuses if the name prefix already exists. Saves and restores the work plane, commits every 500 members and writes a trace and log. |
| `members_48.csv`, `members_144.csv` | Pre-generated member lists |
| `summary_48.json`, `summary_144.json` | Expected counts per member type, used for verification |

Design principle: the AI produces **data** (a member list), and a fixed, reviewed importer executes it. No AI-generated code runs inside Tekla. Later, `MemberImport` becomes a controlled tool in our C# app (see §7).

## 3. Geometry rules (implemented in `stadium_geometry.py`)
- **Superellipse:**
  - `x = A·sgn(cosφ)|cosφ|^(2/n)`, `y = B·sgn(sinφ)|sinφ|^(2/n)`.
  - Outer: A=135 m, B=115 m, n=4. Inner: A=78 m, B=58 m, n=4.5.
- **Rib positions:** the ribs are spaced at equal arc length along the outer curve. The same φ is used on the inner curve.
- **Section:**
  - A section point (h, z) maps to `outer(φ) + h·(inner(φ) − outer(φ))` at height z.
  - h<0 is the outward facade bulge.
- **Inner chord:** offset along the section's inward normal by the truss depth: 3.0 m on the facade, 4.5 m on the roof, 2.5 m at the edge.
- **Rib truss:**
  - Outer chord: all nodes.
  - Inner chord: from node 1.
  - A post at every node.
  - Warren diagonals, alternating direction.
  - A skirt member hangs from the edge node.
- **Rings:**
  - Outer ring at every node: base ring at node 0, compression ring at the last node.
  - Inner ring at every 2nd node and at the last node.
  - A ring at the skirt bottom.
- **Bracing:** X in the skin between ribs r and r+1 when r % 4 == 0, over all levels.
- **Names:** every member name starts with `ST_` (`ST_OUTER_CHORD`, `ST_RING`, ...).
- **Placement:** the stadium centre is at global (0, 230 000, 0), clear of the gallery, steel structure and warehouse already in the test model.

## 4. Step-by-step for OmniRush

**Step 1 – Generate and check (LOW risk, no Tekla)**

Ubuntu (WSL):
```bash
cd "/mnt/d/ONMIRUSH/Tekla Plugins/tasks/stadium-roof"
python3 stadium_geometry.py --config stadium-roof.json --ribs 48
python3 preview.py members_48.csv preview_48.png   # optional, needs matplotlib
```
- The checks must pass: no member under 300 mm, ring and chord counts match the formula, and the z range is 0–53 000.
- Show Sherin the preview and the summary. Ask whether the shape is right, or whether any parameter in `stadium-roof.json` should change (size, bulge, opening, rib count, profiles). Change only the JSON, then regenerate.

**Step 2 – Deploy the importer (once)**

PowerShell (Windows):
```powershell
Copy-Item "D:\ONMIRUSH\Tekla Plugins\tasks\stadium-roof\MemberImport.cs" "C:\TeklaStructures\2025.0\Environments\common\macros\modeling\"
Set-Content "C:\Users\sheri\Sherin_AI\macros\MemberImport_source.txt" "D:\ONMIRUSH\Tekla Plugins\tasks\stadium-roof\members_48.csv"
```
- A **new** macro only appears in Tekla after Tekla restarts.
- The Tekla MCP `run_macro` tool can run it by file name without a restart.

**Step 3 – Approval (HIGH risk)**

Show Sherin exactly this, then wait for "yes":
- **Model:** `C:\TeklaModels\LiveTest_Disposable`, which must be the open model. Check the window title or `ModelInfo`. Never run it on a project model.
- **What is created:** 3,504 members named `ST_*`, centred at (0, 230 000, 0), with the summary per type.
- **Existing `ST_*` parts:** must be none. The importer refuses if any exist.

**Step 4 – Run**
- Tekla: Ctrl+Q → `Macro.MemberImport`.
- Or MCP: `run_macro("MemberImport.cs")`. A run longer than 60 s may report a timeout while it keeps running. Do not start it twice.
- Watch `C:\Users\sheri\Sherin_AI\macros\MemberImport_trace.txt`, which prints a progress line every 500 members and ends with the summary.

**Step 5 – Verify (do not skip)**
1. In the trace and model-folder log `MemberImport_log.txt`, inserted per name must equal `summary_48.json` and Failed must be 0.
2. Select `ST_*` (MCP `select_elements_by_filter`, name Starts With `ST_`). The count must be 3,504.
3. Run MCP `check_for_invalid_objects` on the selection. Expect 0 invalid. This also confirms the `PD` tube profiles exist in the catalog.
4. Zoom to the selection and compare it visually with the preview.
5. Save the model (Ctrl+S).

**Step 6 – Full model (after Sherin's OK)**
1. Delete the 48-rib set: select `ST_*`, show the count, get a "yes", then delete.
2. Point `MemberImport_source.txt` at `members_144.csv` and repeat steps 3–5. Expect 10,512 members.

**Step 7 – Learning loop**
1. Record the verified facts in `API_NOTES.md` (PD profiles valid, importer timing, counts).
2. Once it is verified in Tekla, copy `MemberImport.cs` to `examples/member-import/` with a header.
3. Write any failure under "Traps".

## 5. Traps already found on this PC (Oct 2026)
- **Macro format:**
  - Use the classic Akit format (`namespace Tekla.Technology.Akit.UserScript`, `public static void Run(IScript akit)`) with no `#pragma reference`.
  - The newer `UserMacros` + `#pragma` format silently did not run for geometry macros, because Geometry3d lives in `Tekla.Structures.Geometry3d.Compatibility.dll` in 2025.
- **Discovery:** new macros are only listed after a Tekla restart. Run them with Ctrl+Q, `Macro.<name>`. Double-clicking in Applications & components did not start one.
- **Dialogs:** never show a MessageBox in a macro run through MCP. It blocks Tekla ("busy running another macro") and can open behind the main window or on a disconnected monitor.
- **Connection loss:** a Tekla restart breaks the MCP connection until the Claude/OmniRush host process restarts.
- **Catalog check:** `LibraryProfileItem.Select()` returned false for valid profiles inside a macro. Verify profiles with `check_for_invalid_objects` instead.
- **Profile rotation:** for horizontal members, `Position.Rotation = TOP` keeps the web vertical. Depth `BEHIND` puts the top of steel on the reference line, `FRONT` the bottom.
- **Bracing components:** the standard Bolted gusset (11) and Bracing cross (19) extended HEA braces wildly. Never apply bracing components in bulk without a saved, tested attribute preset.

## 6. Out of scope for this task (next steps)
- **Curved members:** true arcs instead of segments. Check in `knowledge/api/` whether `PolyBeam` with arc-type contour chamfers exists in 2025 before using it.
- **Tube connections:** cast nodes, gusset plates, slotted tubes. Agree on a joint type with Sherin, save a component preset, and test it on one node first.
- **Supports, seating bowl, cladding, purlins.**

## 7. Path into the C# app (Phase 4+)
Turn `MemberImport` into a controlled tool, `ImportMemberList(csvPath, expectedCounts)`:
- **Risk:** HIGH, with a preview listing counts per name, the bounding box and the target model, and a required confirmation token.
- **Validation:** the CSV schema, profiles against the catalog, material names, minimum length and the prefix guard.
- **Execution:** batch insert in global coordinates with the work plane restored in `finally`, then commit.
- **Verification:** re-read the counts and the invalid-object check.
- **Reuse:** generators (stadium, warehouse, gallery, trusses) stay as pure, unit-tested geometry modules that output member lists. They never touch Tekla.

## 8. Acceptance criteria
- [ ] Sherin approved the preview shape and parameters.
- [ ] The 48-rib run shows Inserted = 3,504, Failed = 0 and 0 invalid objects.
- [ ] The view matches the preview, and nothing outside `ST_*` changed.
- [ ] (If approved) the 144-rib run shows 10,512 members, 0 failed, 0 invalid.
- [ ] API_NOTES.md and examples/ are updated.
