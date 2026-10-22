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
as a macro because that is the least thing a Tekla user has to install: two
files in the macro catalog, no DLL to register, no NuGet, no MSBuild.

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
macros/wall-framing/tests/                         6 harnesses
macros/wall-framing/docs/                          9 documents
```

## The three code trees

| tree | path | what it is | git? |
|---|---|---|---|
| **published** | `C:\Users\sheri\projects\sherin-tekla-mcp` | the GitHub repository, branch `feature/wall-framing-dimensioner` | yes, remote = GitHub |
| **dev** | `C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP` | the working copy; also holds the reference dumps and the Python MCP server | **no** |
| **upstream** | `D:\ONMIRUSH\Tekla Plugins` | the project the C# server was written in, under the old `TeklaAiAssistant.*` names | remote = the same GitHub repo, different branch |

**Keep the published tree and the dev tree in step.**  The **published** tree
holds the free edition only.  The **Pro** sources (the Pro stages and the
Pro rules) live in the **dev** tree, under
`macros\wall-framing\private\`, and are **never** committed.  The build
takes them from there with `-ProPath`, and the dev tree is the only place
where the Pro package is built.

The two copies of the free sources are the same files; compare the
**compiled** result when in doubt.

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
5. Read [06-OPEN-QUESTIONS.md](06-OPEN-QUESTIONS.md) and pick up one of the
   open items — or finish the one that is half-done.
