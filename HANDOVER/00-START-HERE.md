# 00 — Start here

## What this project is

Two things, in one repository, that share the same discipline (**Tekla
Structures 2025 only, verified APIs, no writing into a model or a drawing
that the human did not ask for**):

### A. The MCP server — `SherinWorin/sherin-tekla-mcp`

A C# MCP server that lets Claude work with a live Tekla Structures 2025
model through a fixed set of typed tools. 16 tools, three risk levels
(LOW / MEDIUM / HIGH), every write goes through a preview → confirm-token →
apply → read-back → log pipeline.  The rule logic and the safety logic live
in `SherinTeklaMcp.Core`, which **never references Tekla** and is
unit-tested with a mock backend (48 tests).

```
src/SherinTeklaMcp.Core     Tekla-free logic: rules, geometry, safety, write pipeline, mock
src/SherinTeklaMcp.Tekla    the Tekla Structures 2025 backend (net48, x64)
src/SherinTeklaMcp.Server   the MCP server (stdio) and the 16 tool definitions
tests/                      48 unit tests, no Tekla needed
config/                     settings.json and rules/*.json
```

### B. The wall-framing auto-dimensioner — `macros/wall-framing/`

A standalone Tekla 2025 **drawing macro** that reproduces the annotations of
a `WALL FRAMING LAYOUT` GA drawing: the stud chains, the band chains, the
top outline, the overall dimensions, the north run, the wall-panel marks, the
section marks and the notes.

It is **not** an MCP tool and does not depend on the server.  It was built
as a macro because that is the least thing a Tekla user has to install: one
file in the macro catalog (plus its icon), no plugin to register, no NuGet,
no MSBuild.  The file in the catalog is a thin **loader**; the modules it
needs sit in `%LOCALAPPDATA%\P142\WallFraming` and are copied there by
`install.ps1`.

It is sold in two editions: a **free** one (the dimension chains) and a
**Pro** one (everything), unlocked by a licence key.  Without a key the Pro
features are free for three days.  The Pro stages are **not** in the public
repository — they are a separate module, loaded only when the licence
allows it.  See `macros/wall-framing/docs/LICENSING.md`.

```
macros/wall-framing/source/WF_Rules.cs             the free rules, no Tekla reference
macros/wall-framing/source/WF_License.cs           the licence engine, no Tekla reference
macros/wall-framing/source/WF_Core.cs              the shared drawing machinery
macros/wall-framing/source/WF_KeyGen.cs            the owner's key tool
macros/wall-framing/source/P142_WF_AutoDimension.cs the LOADER (no rule in it)
macros/wall-framing/source/build.ps1               compiles + packages
macros/wall-framing/install.ps1                    installs into the Tekla catalog
macros/wall-framing/tests/                         7 harnesses
macros/wall-framing/docs/                          9 documents
```

## The three code trees

| tree | path | what it is | git? |
|---|---|---|---|
| **published** | `C:\Users\sheri\projects\sherin-tekla-mcp` | the GitHub repository, branch `feature/wall-framing-dimensioner` | yes, remote = GitHub |
| **dev** | `C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP` | the private tree: the Pro sources, the reference dumps, the Python MCP server | **no** |
| **upstream** | `D:\ONMIRUSH\Tekla Plugins` | the project the C# server was written in, under the old `TeklaAiAssistant.*` names | remote = the same GitHub repo, different branch |

**Keep the published tree and the dev tree in step.**  The **published** tree
holds the free edition only.  The **Pro** sources (the Pro stages and the
Pro rules) live in the **dev** tree, under
`macros\wall-framing\private\`, and are **never** committed.  The build
takes them from there with `-ProPath`, and the dev tree is the only place
where the Pro package is built.

> **The dev tree's `macros\` folder used to hold the OLD, pre-tiering free
> sources** (`P142_WF_AutoDimension.cs` of about 49000 bytes, `WF_Rules.cs`
> of about 44000 bytes, and their own `build.ps1`).  They were **moved out
> of the way** on 2026-10-22 into `macros\_legacy_2026-10-22\`, with a
> `READ-ME-FIRST.txt` that says what they are and why they must not be
> built, installed or sent to anybody.  What is left in the dev tree's
> `macros\` is: `wall-framing\private\` (the Pro sources), the
> customer-facing `INSTALL_NOTES.txt`, `install.ps1` and
> `PACKAGE_README.txt`, and a copy of the current packages in
> `package\`.  The tiered free sources — the loader, `WF_Core.cs`,
> `WF_License.cs`, `WF_KeyGen.cs` — exist **only** in the published tree.
> **Edit, build, test and package in the published tree.**  Take *only*
> the `private\` folder from the dev tree, through `-ProPath`.

## Where the design lives

The pack in this folder is the **map**: the state, the rules, the traps,
the commands, the open questions.  The **substance** — the ideas
themselves — is in the macro's own nine documents.  Read them from there,
not from here:

| document | what it holds |
|---|---|
| `macros/wall-framing/docs/ARCHITECTURE.md` | the four modules and how they depend on each other, the edition cut, the loader, the deployment, the honest caveat |
| `macros/wall-framing/docs/LICENSING.md` | the two editions, the key format, the machine binding, the trial, the key tool, the customer workflow |
| `macros/wall-framing/docs/WALL_FRAMING_RULES.md` | every rule of the drawing, with the reference set it was derived from, and the six hand edits that are deliberately not reproduced |
| `macros/wall-framing/docs/API_NOTES.md` | the Tekla 2025 members actually used, and where the documentation and the behaviour disagree |
| `macros/wall-framing/docs/SETUP.md` | how to install and how to check the install, step by step |
| `macros/wall-framing/docs/TESTING.md` | the eight test sections, and what is **not** tested |
| `macros/wall-framing/docs/TODO.md` | what is done and what is open |
| `macros/wall-framing/docs/README.md` | the index of the macro's documentation |
| `macros/wall-framing/docs/CHANGELOG.md` | the history of the macro |

## The model

```
C:\Users\sheri\OneDrive\Desktop\Tekla Model\P142-COREL REEF_BP4_MOCKUP-R00
```

GA drawings in it:

| mark | title | state | what it is |
|---|---|---|---|
| **[50]** | `WALL FRAMING LAYOUT` | **frozen** | the **issued reference**. Read-only. Never written to. |
| **[57]** | `WALL FRAMING LAYOUT` | open | the **target** the macro writes into |
| [56] | `BASE FRAMING LAYOUT` | open | the other layout — a different rule set, a different macro |
| [49] | `CEILING FRAMING LAYOUT` | open | — |
| BP4-WP.* | `WALL_PANEL` | open | the assembly drawings of the 11 panels; not touched |

### The second model

```
C:\TeklaStructuresModels\<the second client model>
```

`MODEL-2` is a neutral name used throughout this repository for the
second client model — the name of the model itself is **not** published
here.  It is in the private tree, and the dumps of the model are the
files `asm_parts_MODEL2.txt` and `dims_MODEL2.txt` of the dev tree.

A real project with **19 writable wall layout drawings** (`BP01`…`BP10`,
`BP01M`…`BP10M`, scale 1:15) and 234 wall panels.  The macro was checked
against it and changed for it (the panels of a drawing are the assemblies
the drawing shows; every wall layout drawing of the model is worked on).

**The frozen drawings of that model are [50] and [72], and every drawing
named `WALL_PANEL` or `BASE, CEILING, WALL`.**  They are never written to.

The selection was **measured on the live model**: of the 695 drawings,
21 match the wall-layout title rule, 2 of them are frozen and refused,
and **19** are the targets.  The rule searches the four title fields
**one by one** — glued together they would match 40 drawings instead of
21, because the per-block drawings split the phrase over two fields
(trap 17 of [04-TEKLA-2025-API-TRAPS.md](04-TEKLA-2025-API-TRAPS.md)).

Its hand-made drawings contradict each other, so they are **not** a rule
set: the macro reproduces the rule, not the hand work.  See
[01-STATE-OF-PLAY.md](01-STATE-OF-PLAY.md) and
`macros/wall-framing/docs/WALL_FRAMING_RULES.md`.

## The one thing to know before touching anything

**Drawing [50] is the issued reference of a live client project.**  It is
frozen.  The macro refuses to write into an issued or frozen drawing, and so
must any new code.  See [03-HARD-RULES.md](03-HARD-RULES.md).

## The first hour on the new computer

1. Read [03-HARD-RULES.md](03-HARD-RULES.md).  Ten minutes.
2. Do the setup in [02-ENVIRONMENT.md](02-ENVIRONMENT.md).
3. Prove the state with the commands in [05-BUILD-AND-VERIFY.md](05-BUILD-AND-VERIFY.md).
4. Read [04-TEKLA-2025-API-TRAPS.md](04-TEKLA-2025-API-TRAPS.md) before writing
   a single line of Tekla code.
5. If you are working on the macro, read its own documents — start with
   `macros/wall-framing/docs/ARCHITECTURE.md` and
   `macros/wall-framing/docs/LICENSING.md`.  The table above says which
   one answers which question.
6. Read [06-OPEN-QUESTIONS.md](06-OPEN-QUESTIONS.md) and pick up one of the
   open items — or finish the one that is half-done.
