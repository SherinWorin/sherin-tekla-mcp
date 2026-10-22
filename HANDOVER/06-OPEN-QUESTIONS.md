# 06 — Open questions and open work

Everything here is unfinished.  The list is split by **who can settle it**.

---

## A. The human must decide

### A1 — Merge PR #1?

**PR #1** — <https://github.com/SherinWorin/sherin-tekla-mcp/pull/1>
— `feature/wall-framing-dimensioner` → `main`.  Both CI jobs green.

**Open.**  Merging is the owner's call.  If it is merged, the branch is
finished and the macro lives on `main`.

*Why it is still open:* the branch was published to be reviewed, not to be
merged on its own.  There is no technical blocker.

### A2 — The north run on the sheet

The north run is placed **30 mm north** of the wall it measures, because
the reference drawing puts it on top of the top outline.  Nobody has looked
at the generated drawing and confirmed that the placement reads well.

*Where:* `PAPER_NORTH` in the Pro module (`macros/wall-framing/private/WF_Pro.cs`
in the dev tree).
*How to settle it:* run the macro on [57], look at the sheet, change the
constant, run again.

### A3 — The two hand-placed wall lines of [50]

Sets 10 and 18 of the issued drawing are hand-placed wall lines whose end
points are **hand points** (296.07 and 1705.85).  They cannot be derived
from the model.

*If the user wants them*, they have to be hard-coded per project — which
the rules deliberately do not do (R11 in [03-HARD-RULES.md](03-HARD-RULES.md)).
**Ask before doing this.**  It is a decision about the product, not a bug.

### A4 — The package has never been run on a second computer

The installer is written and dry-run here, but it has not been run on the
other machine yet.

*How to settle it:* on the second computer, unzip the package, run
`install.ps1 -WhatIfOnly` first, then the install, then the macro, then
check the log.

### A5 — The Pro path has never been run against a live Tekla

The Pro stages were moved out of the macro into `WF_Pro.dll`, and the Pro
rules into `WF_Rules_Pro.dll`, after the drawing was finished.  The two
harnesses prove that the *rules* were moved without changing an answer
(12 of 18 chains, angle 11/11, leader 9/11, offset 9/11), and
`tests/test_loader.py` proves that the module loads and has its entry
point.  What is **not** proven is that the Pro module *writes* the same
objects in the same places once it is called by the core.

*Where:* `macros/wall-framing/source/WF_Core.cs`, the `RunPro` method.
*How to settle it:* on the dev machine, build with `-ProPath`, install the
modules, run the macro on [57] with a Pro licence, then run
`tests/cmp_chains.py` and `tests/audit_50_57.py`.  The chains must still
be 12 of 18, and [50] must be untouched.

### A6 — The owner has not pasted the public key

`PublicKeyXml` in `source/WF_License.cs` is still the placeholder
`REPLACE_WITH_THE_PUBLIC_KEY`, so **no licence key verifies** and the
licence cannot be issued end to end yet.  The engine, the key tool and
the 42 checks are ready; only this one constant is missing.

*Where:* `source/WF_License.cs`.
*How to settle it:* `WF_KeyGen.exe newkey owner-private.xml owner-public.xml`,
put the public key into `PublicKeyXml`, rebuild, and walk through
`issue` → `install` → `status`.  The **private** key is the owner's
alone: it is never written by an assistant and never committed.

### A7 — The base-framing macro has no shared style

`P142_BF_AutoDimension.cs` (the base framing, in the dev tree and installed
in the catalog) and the wall-framing macro do not share a style definition,
so the two drawings do not look the same.

*Where:* the dev tree, `C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\`.
*Note:* the base-framing macro is **not** in the GitHub repository yet, and
it must not go in without the owner's agreement about how the two are
meant to relate.

### A8 — The rebuilt modules are not deployed

`%LOCALAPPDATA%\P142\WallFraming\` still holds the build from **before**
the new-model work, so the macro on this computer runs the old rules.  The
deployment writes outside the repository, which is why an assistant does
not do it on its own.

*Where:* `macros/wall-framing/source/build.ps1 -Deploy`.
*How to settle it:* build with `-Deploy` (or copy the three modules by
hand), then run the macro and look at the log.

### A9 — The writing path has never run on the new model

The **rules** are checked against `MODEL-2`
(`tests/test_upgrade.py`, which reads only).  What has **not** happened is
a real run of the macro against that model — one run over the 19 wall
layout drawings.

*Where:* the second client model (`MODEL-2`), in the models folder of the
owner.
*How to settle it:* run the macro once, then dump two or three of the
drawings (`tests/dump_dims.py "<mark>" <file>`) and look at them.  The
**frozen** drawings of that model ([50], [72] and every `WALL_PANEL` and
`BASE, CEILING, WALL` drawing) must not change.

### A10 — The 163 stations the hand-made drawings of the new model do not measure

`test_upgrade.py` lists them one by one.  Most of them belong to the
**ledge-wall (LW)** family, whose drawings measure **no** opening at all,
and the rest are the panel details that live in the **per-panel drawings**
of the new model.

*Each one is either a rule that is wrong or a drawing that is
incomplete* — and the two cannot be told apart without a person who knows
the project.  This is the reason the macro reproduces the **rule** and not
the hand work.

---

## B. A programmer can settle these

### B1 — The settings file

The constants that belong to a project, not to the code, are still in the
macro:

| constant | value | what it is |
|---|---|---|
| `PANEL_PREFIX` | `"BP4-WP"` | the panel naming — **already only a preference**: the automatic detection takes over when it matches nothing |
| `NOTCH` | `2.0` | the width of the notch that is bridged in the top outline |
| `PAPER_PANEL` | `8.8` | the paper distance of the bay chains |
| `PAPER_BAND` | `15.9` | the paper distance of the band chains |
| `PAPER_OUTLINE` | `21.82` | the paper distance of the top outline |
| `PAPER_OVERALL_H` | `24.27` | the paper distance of the overall height |
| `PAPER_OVERALL_W` | `27.74` | the paper distance of the overall width |
| `PAPER_NORTH` | `30.0` | the paper distance of the north run — see A2 |
| `WALL_LAYOUT_TITLES` | a list of title fragments | how a drawing is recognised as a wall layout drawing |
| the reference drawing | the mark of the issued drawing | a preference; the section marks are copied from it |

*How to settle it:* move them into a JSON file next to the macro, read at
start-up, with the current values as the defaults.  The C# server already
does this with `config/settings.json` and `config/rules/*.json` — the
pattern is there to copy.

*Watch out:* the file has to be found **next to the macro in the catalog**,
and the macro runs from the catalog, so the path cannot be relative to the
working directory.

### B2 — A unit test of the writing path

The writing path is verified by running the macro and dumping the result.
A real unit test needs a live Tekla instance.

*How to settle it:* a test that runs against a **live** Tekla, skipped when
Tekla is not running, that writes to a scratch drawing and asserts the
object count and the idempotency (run twice → the same count).

### B3 — Only the GA drawing has been verified

The macro writes whatever wall layout drawing it finds, but only the GA
drawing has been verified end to end.  A detail drawing has no view that
looks straight down, and the macro creates nothing there.

*How to settle it:* run it on another drawing of the model and see.

### B4 — The assembly drawings are not touched

`[BP4-WP1]` … `[BP4-WP11]` are the assembly drawings of the 11 panels.  The
macro does not touch them, and should not.

### B5 — A plugin instead of a macro

The rules assembly is already separate and testable, so it can be lifted
into a plugin unchanged.  The value of a plugin: it can add a ribbon tab,
and it does not depend on the macro catalog.

*Why it is not done:* a macro is the least thing a Tekla user has to
install, and that was the point of this deliverable.

---

## C. The live Tekla connection of the **C#** server

The C# MCP server in the repository has **not** been tested against a live
Tekla.  It has been tested against the mock backend (48 unit tests + a
smoke test), and it has been run in read-only mode, but the live path —
the `TeklaService` implementation in `src/SherinTeklaMcp.Tekla` — has not
been exercised against a real model.

*How to settle it:* start in **read-only** mode on a **copy** of a model,
work through `docs/TESTING.md` in the repository root, and report what
worked.  `docs/TESTING.md` and the issue template
`tekla-test-report.yml` exist for exactly this.

*This is the single biggest gap in the C# half of the project.*

---

## D. Things that are deliberately NOT to be done

| do not | why |
|---|---|
| merge `feature/tekla-ai-assistant` into `main` | unrelated histories; the merge would be a mess of two `src/` trees — see [01-STATE-OF-PLAY.md](01-STATE-OF-PLAY.md) |
| copy the GPLv3 `tekla_mcp_server` into the MIT repository | a licence condition |
| commit the reference dumps | client data in a public repository — R2 |
| write to drawing [50] | it is the issued reference — R1 |
| hard-code the six hand edits of [50] | they cannot be derived, and they would be wrong on every other project — R11 |
| regenerate a replacement for the Python MCP server | it was complete before this work; extend it |
| copy files into the Tekla installation | the human does that — R8 |
