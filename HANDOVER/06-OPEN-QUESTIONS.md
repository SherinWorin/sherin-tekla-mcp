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

*Where:* `PAPER_NORTH` in `source/P142_WF_AutoDimension.cs`.
*How to settle it:* run the macro on [57], look at the sheet, change the
constant, run again.

### A3 — The two hand-placed wall lines of [50]

Sets 10 and 18 of the issued drawing are hand-placed wall lines whose end
points are **hand points** (296.07 and 1705.85).  They cannot be derived
from the model.

*If the user wants them*, they have to be hard-coded per project — which
the rules deliberately do not do (R10 in [03-HARD-RULES.md](03-HARD-RULES.md)).
**Ask before doing this.**  It is a decision about the product, not a bug.

### A4 — The package has never been run on a second computer

The installer is written and dry-run here, but it has not been run on the
other machine yet.

*How to settle it:* on the second computer, unzip the package, run
`install.ps1 -WhatIfOnly` first, then the install, then the macro, then
check the log.

### A5 — The base-framing macro has no shared style

`P142_BF_AutoDimension.cs` (the base framing, in the dev tree and installed
in the catalog) and the wall-framing macro do not share a style definition,
so the two drawings do not look the same.

*Where:* the dev tree, `C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\`.
*Note:* the base-framing macro is **not** in the GitHub repository yet, and
it must not go in without the owner's agreement about how the two are
meant to relate.

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
| hard-code the six hand edits of [50] | they cannot be derived, and they would be wrong on every other project — R10 |
| regenerate a replacement for the Python MCP server | it was complete before this work; extend it |
| copy files into the Tekla installation | the human does that — R8 |
