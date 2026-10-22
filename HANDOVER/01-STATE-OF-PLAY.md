# 01 — State of play

Date of this snapshot: **2026-10-22**.

## The GitHub repository

`https://github.com/SherinWorin/sherin-tekla-mcp` — MIT, public.
The token is **not** in any file.  Get it at the moment it is needed:

**Command Prompt (Windows)**

```
printf 'protocol=https\nhost=github.com\n\n' | git credential fill
```

(the credential helper is `manager`; it returns `username=` and `password=`
lines, and the password is the token).

### Branches

| branch | what it holds | state |
|---|---|---|
| `main` | the C# MCP server product | 4 commits of its own history |
| `feature/tekla-ai-assistant` | the upstream project under its old `TeklaAiAssistant.*` names | **deliberately unmerged** |
| `feature/wall-framing-dimensioner` | the wall-framing macro, on top of `main` | **PR #1 open** |

**Do not merge `feature/tekla-ai-assistant` into `main`.**  The two histories
are unrelated — `git merge-base main origin/feature/tekla-ai-assistant`
returns nothing — so a merge would need `--allow-unrelated-histories` and
would produce two parallel `src/` trees.  Reconciling them means porting one
namespace scheme onto the other, not merging.

### The open pull request

**PR #1** — *Add the wall-framing layout auto-dimensioner (Tekla 2025)*
<https://github.com/SherinWorin/sherin-tekla-mcp/pull/1>

- `feature/wall-framing-dimensioner` → `main`
- +6503 / −0 across 37 files (before this hand-over pack was added)
- both CI jobs green on both commits:
  - `test-and-smoke` — 48 unit tests + the MCP smoke test on the mock backend
  - `build-windows-tekla2025` — the net48 build for Tekla 2025 + the smoke test
- **not merged** — that is the owner's call.

## The macro — what is done

Everything in `macros/wall-framing/docs/TODO.md` under **Done** is done:

- the wall parts are read out of the model and grouped into panels
  (`ASSEMBLY_POS`, prefix `BP4-WP`, 11 panels `BP4-WP1`..`BP4-WP11`);
- the **bay chain** of every panel (R1, R2) — the track ends, the assembly
  ends, the stud centres, the openings;
- the **band chains** of the bottom, left and right edges of the layout (R3);
- the **top outline** (R4) — a staircase, chamfered over a wall that crosses
  it, a narrow notch bridged when the level is the same on both sides;
- the **overall height** and the **overall width** (R5);
- the **north run** (R6);
- the 11 **wall-panel marks**, with the text angle, the leader line and the
  label offset derived from the model;
- the 5 **section marks**, copied from the reference drawing;
- the 3 **notes**;
- **idempotency** — the output is tagged with a user property and replaced on
  a re-run, never duplicated;
- the **operation log**;
- the build script, the thin loader, the deployment packages;
- the **target drawing is found by its title, not by its mark** (R8) — so the
  same file works on another computer where the mark is different;
- an **issued or frozen drawing is refused**;
- the **wall panels are found automatically** when the configured prefix
  matches nothing (R9);
- the **two editions** — the free one (the dimension chains) and the Pro one
  (everything), cut at the assembly boundary: the Pro stages are in
  `WF_Pro.dll`, the Pro rules in `WF_Rules_Pro.dll`, and the Pro sources
  are **not** in the public repository;
- the **licence engine** — a signed key (RSA-2048 over SHA-256), a three
  day trial from the first run, a machine binding on three hashes, a
  clock-rollback rule, and the owner's key tool `WF_KeyGen.exe`.  42
  checks, no Tekla needed.

### What it reproduces, measured

| what | result |
|---|---|
| dimension chains of [50] reproduced exactly in [57] | **12 of 18** |
| mark angle | 11 / 11 |
| mark leader direction | 9 / 11 |
| mark offset | 9 / 11 |
| objects per run | 37 (0 failed, 0 skipped) |
| [50] after the run | 18 dimsets / 126 marks / 5 section marks / 3 texts — **unchanged** |

The six chains that do **not** match are **hand edits of the issued drawing**
— a value typed by hand (2999.32 instead of 2981.82), extra jamb centres
added by hand, two hand-placed wall lines whose end points are hand points
(296.07 and 1705.85), a hand-typed 1034.30 instead of 1035.00, and a pair
where both 568.32 and 603.32 are written.  **The macro reproduces the rule,
not the hand work.**  That is the right behaviour: those numbers cannot be
derived from the model, and hard-coding them would make the macro wrong on
every other project.

## The macro — what is still open

See [06-OPEN-QUESTIONS.md](06-OPEN-QUESTIONS.md) for the full list with
owners.  The short version:

| # | open item | where it lives |
|---|---|---|
| 1 | **the Pro path has never been run against a live Tekla** — the Pro stages were moved into `WF_Pro.dll` after the drawing was finished.  The harnesses prove the *rules* were moved without changing an answer, but that the Pro module *writes* the same objects in the same places is not proven | `docs/TODO.md` |
| 2 | **the owner has not pasted the public key** into `PublicKeyXml` in `source/WF_License.cs` — it is still the placeholder, so **no key verifies** and the licence cannot be issued end to end yet | `source/WF_License.cs` |
| 3 | **the north run placement on the sheet** — it sits 30 mm north of the wall it measures; nobody has looked at it and confirmed it reads well | `PAPER_NORTH` in the Pro module |
| 4 | **the settings file** — the paper distances, the notch width, the panel prefix, the reference drawing and the list of wall-layout title fragments are still constants in the code | the core's `const` block |
| 5 | **a unit test of the writing path** — it needs a live Tekla; today the writing path is verified by running the macro and dumping the result | `docs/TESTING.md` |
| 6 | **the package has never been run on a second computer** — the installer is dry-run here only | `package/` |
| 7 | **the two macros have no shared style** — the base-framing macro `P142_BF_AutoDimension.cs` and the wall-framing macro do not share a style definition, so the two drawings do not look the same | the dev tree |
| 8 | **a plugin instead of a macro** — the rules assembly is already separate, so it can be lifted into a plugin unchanged | later |

## The MCP server — what is done

- 16 tools: 7 read, 4 geometry/rule, 3 write, 2 report.
- `SherinTeklaMcp.Core` has no Tekla reference and is covered by 42 unit
  tests against `MockTeklaService`.
- The write pipeline is `preview → confirm token → apply → commit →
  read back → log`; the confirm token is bound to the arguments and expires
  after 10 minutes.
- `config/settings.json` ships **read-only** (`"ReadOnly": true`,
  `"AllowDelete": false`).
- The operation log is `operations-YYYYMMDD.jsonl`.
- CI is green on both jobs on both commits.

### The MCP server — what is still open

The live Tekla connection has **not** been tested against the C# server.
It has only been tested against the *other* server (the Python one, see
below).  Start read-only, on a **copy** of a model — see
`docs/TESTING.md` in the repository root.

## The other Tekla MCP server on this machine

There is a **second, unrelated** MCP server installed here:

```
C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\tekla_mcp_server\
```

It is `teknovizier/tekla_mcp_server` v0.2.3 — **GPLv3**, 64 tools, Python,
registered with Claude Code at user scope under the name `tekla`
(`claude mcp list` shows it).  It was complete before this work started.

**Two rules about it:**

1. **It is GPLv3.  It must never be merged into the MIT repository.**  That
   is a licence condition, not a preference.
2. **Do not generate a replacement for it.**  Extend it if it needs
   extending.  The C# server in the repository is a separate, cleaner
   re-implementation of a different idea (typed tools + a write pipeline),
   and the two are allowed to coexist.
