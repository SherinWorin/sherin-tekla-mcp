# 03 — Hard rules

These are not preferences.  Breaking one of them breaks the project.

---

## R1 — Drawing [50] is the issued reference. **Never write to it.**

`[50] "WALL FRAMING LAYOUT"` of model `P142-COREL REEF_BP4_MOCKUP-R00` is
**frozen** — it is the issued drawing of a live client project.  It is the
**oracle**: the macro is compared against it.

* The macro **refuses** any drawing that is issued or frozen
  (`Drawing.IsFrozen`, `Drawing.IsIssued`, `Drawing.IsIssuedButModified`),
  and says so in a message box.
* **Reading it is fine and necessary** — a frozen drawing is still readable
  through `GetSheet() → GetViews() → GetAllObjects()`.  The five section
  marks are *copied* out of it.
* **Writing to it is never allowed**, by the macro or by hand or by any
  new code.  A frozen drawing is the one place where a mistake cannot be
  undone.

If a test or a tool ever reports that it wrote to [50], that is a **stop
and report** failure, not something to fix quietly.

---

## R2 — The client data does not go into the public repository.

The GitHub repository `SherinWorin/sherin-tekla-mcp` is **public**.

**These are client data and must never be committed to it:**

* the **reference dumps** — `asm_parts.txt` (247 wall parts: identifiers,
  profiles, coordinates), `full_50.txt` / `full_57.txt` (the full annotation
  dump of the two drawings), `marks_50.json`, `dims_50.txt`, `dims_57.txt`,
  `model_walls.txt`, `wall_panels.txt`, and the two dumps of the **second**
  model, `asm_parts_MODEL2.txt` (3875 wall parts) and `dims_MODEL2.txt` (the
  dimension sets of the 19 wall layout drawings of `MODEL-2`);
* the **model folder** itself and anything copied out of it;
* the **name of the issued drawing** where it identifies the client.

They are gitignored in `macros/wall-framing/.gitignore` and
`macros/wall-framing/tests/reference/.gitignore`, and the harnesses stop
with a clear message when a dump is missing.

**The way to work instead:** the rules and the code go in the repository;
the measured data stays on the machine, and the harnesses are run against a
regenerated copy.  See [REFERENCE-DATA.md](REFERENCE-DATA.md).

*Exception, already in the repository:* the project name `P142`, the model
name, the drawing marks and the drawing titles appear in the macro's own
documentation, because the macro's rules were **derived** from them and the
derivation has to be traceable.  That was a deliberate decision.  The
**raw geometry** stays out.

---

## R3 — The GPLv3 server never enters the MIT repository.

`teknovizier/tekla_mcp_server` (in the dev tree) is **GPLv3**.  The GitHub
repository is **MIT**.  Merging or copying GPLv3 code into it would change
the licence of the whole repository.

The two live side by side and stay separate.

---

## R4 — Tekla Structures **2025** only.

Every line of code is written against the 2025 Open API.  The APIs of 2023,
2024 and 2026 are **not** assumed to be compatible.

**Never invent a Tekla class or a member.**  Before using a member that is
not already in the code, confirm it against:

| source | where |
|---|---|
| the installed assemblies | `C:\TeklaStructures\2025.0\bin\` |
| the installed API documentation | `C:\TeklaStructures\2025.0\bin\Tekla.Structures.Drawing.xml` (and the other `.xml` files) |
| live reflection over the real assemblies | `tests/probe_*.py` |

If a member is not in the XML documentation of the installed assemblies, it
is **not** used.  Where the documentation and the behaviour disagree, the
**behaviour** wins and the difference is written down in
`macros/wall-framing/docs/API_NOTES.md`.

**Never hide a compilation error.**  If it does not compile, it is not
done.

---

## R5 — No project rules in the app layer.

The rules live in `macros/wall-framing/source/WF_Rules.cs`, which has
**no Tekla reference at all** and no I/O and no state.  The macro reads the
model and calls the rules.

That one-way dependency is what makes the rules testable without a running
Tekla.  **Do not move a rule into the macro.**  The only things that belong
in the macro are the adapter's own concerns: which drawing, which view,
which style, which coordinates.

The same rule for the C# server: `SherinTeklaMcp.Core` must never reference
Tekla.  Every rule, calculation and safety decision goes there and is
unit-tested with `MockTeklaService`.

---

## R6 — Every write goes through the pipeline.

**The MCP server:** every model-changing tool goes through `WriteOperations`
— preview → confirm token → apply → commit → read back → log.  Never add a
write path that skips it, and never add a tool that runs AI-generated code.

* Risk levels LOW / MEDIUM / HIGH, and the level is decided in the code,
  not by the caller.
* The confirm token is bound to the **arguments** and to the **server run**,
  and expires after `ConfirmTokenMinutes` (10).
* Delete is always HIGH and stays off unless `"AllowDelete": true`.

**The macro:** it writes only into the drawing it has resolved by title,
only when that drawing is neither issued nor frozen, and it tags its own
output with a user property so that a re-run **replaces** rather than
duplicates.  It writes an operation log.

---

## R7 — Geometry inside `InGlobalPlane`, and the work plane restored.

In the C# server, every geometry operation goes inside `InGlobalPlane`, and
the user's work plane is restored in a `finally`.

**stdout is the MCP protocol** — logs go to stderr, always.  A stray
`Console.WriteLine` in the server breaks the protocol.

---

## R8 — Writes into the Tekla installation are done by the human.

The macro catalog is inside the Program Files tree of a licensed Tekla
install.  **Claude does not copy files into it** — the safety layer of the
tool blocks that operation, and it is right to.

The human runs the `copy` command, or the `install.ps1` script.  This is
stated here so that the next instance does not try to work around it.

---

## R9 — Nothing secret is written into a file.

No GitHub token, no password, no API key, in any committed file, in any
hand-over file, in any document in this pack.

The token is obtained at the moment it is needed, from the Windows
credential manager:

**Command Prompt (Windows)**

```
printf 'protocol=https\nhost=github.com\n\n' | git credential fill
```

---

## R10 — The Pro sources and the private key stay out of the public repository.

The macro is sold in two editions.  The **Pro** stages (`WF_Pro.cs`) and
the **Pro** rules (`WF_Rules_Pro.cs`) live in the **dev tree**, under
`macros\wall-framing\private\`, and are **never committed**.  The published
repository holds the free edition only.

* The build takes the Pro sources from the dev tree with
  `build.ps1 -ProPath`; the public tree builds the free edition and says
  so.
* The file in the Tekla macro catalog is the **loader** — about 8000
  bytes, with no rule in it.  A file of about 94000 bytes is the **old**
  merged source; it carries the Pro stages and must not be installed, and
  it must not be committed.
* The owner's **private** key is the owner's alone.  It is never written
  by an assistant, never committed, never sent anywhere.  The public key
  goes into `source/WF_License.cs` — that one is public and belongs in
  the repository.

---

## R11 — The rules of the drawing are the rules of *this* drawing.

The macro's rules were derived from the issued drawing [50] of this one
project.  They are **general** rules of wall-framing layout drawing — bay
chains, band chains, the top outline, the overall sizes, the north run,
the mark placement — and they are written so that they hold on another
project with other numbers.

**What is project-specific is a constant, and a constant is a value that
belongs in a settings file.**  The paper distances, the notch width, the
panel prefix and the list of wall-layout title fragments are still constants
in the macro.  Moving them into a settings file is open item #4 in
[06-OPEN-QUESTIONS.md](06-OPEN-QUESTIONS.md).

**Never hard-code a number that came from a hand edit of the issued
drawing.**  Those six hand edits are documented in
`macros/wall-framing/docs/WALL_FRAMING_RULES.md` and are deliberately **not**
reproduced: they cannot be derived from the model, and baking them in would
make the macro wrong on every other project.

---

## R12 — The hand-made drawings of the second model are not a rule set.

`MODEL-2` has 19 wall layout drawings, and they **contradict each
other**:

* the **WP13** family measures the **centres** of the jambs of an opening;
  the **ledge-wall (LW)** family measures **no** opening at all;
* the WP8 panel of the *old* model measures the centre of the outer jamb
  of its openings, and the WP7 panel — the same construction — measures
  none;
* one hand-edited value differs from the model by 10 mm.

**The macro reproduces the rule, not the hand work.**  A rule is taken
from the cases where the drawings agree with each other; where they
disagree, the disagreement is recorded and the rule stands.

The 163 stations that `tests/test_upgrade.py` reports as "measured by the
macro and not by the drawing" are **not** a list of bugs: most of them
belong to the ledge-wall family, whose drawings are simply incomplete, and
the rest are the panel details that live in the per-panel drawings of that
model.  Each one still has to be looked at by a person (open item A10).

**Never hard-code a number that came from a hand edit** — that applies to
the second model exactly as it applies to the first.
