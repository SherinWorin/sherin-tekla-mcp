# 07 — The prompt for the next Claude

Paste this into the **first** Claude session on the new computer, with the
repository already cloned and the working directory set to the repository
root.  It is written to be self-sufficient: a Claude that has never seen
this project should be able to continue from it.

---

## The prompt

```
You are taking over a Tekla Structures 2025 project.  The repository is
sherin-tekla-mcp (MIT, public, https://github.com/SherinWorin/sherin-tekla-mcp)
and you are on branch feature/wall-framing-dimensioner.

Read the hand-over pack in HANDOVER/ before anything else, in this order:

  HANDOVER/00-START-HERE.md            what the project is, the three trees, the model
  HANDOVER/01-STATE-OF-PLAY.md         what is done, what is not, the open PR
  HANDOVER/02-ENVIRONMENT.md           what this computer needs, and four gotchas
  HANDOVER/03-HARD-RULES.md            the eleven rules that are not negotiable
  HANDOVER/04-TEKLA-2025-API-TRAPS.md  the Tekla behaviour that cost the most time to find
  HANDOVER/05-BUILD-AND-VERIFY.md      every command, labelled
  HANDOVER/06-OPEN-QUESTIONS.md        the open work, and who can settle it

Then read the repository's own CLAUDE.md and follow it: Tekla 2025 only,
SherinTeklaMcp.Core must never reference Tekla, every model-changing tool
goes through WriteOperations, geometry inside InGlobalPlane with the work
plane restored in a finally, stdout is the MCP protocol so logs go to
stderr, and after any change run the tests and update docs/TOOLS.md and
CHANGELOG.md.  Label every terminal instruction for the owner as
"Command Prompt (Windows)" or "Ubuntu (WSL)" - PowerShell is blocked on
their PC.

The two hard rules that matter most:

  1. Drawing [50] "WALL FRAMING LAYOUT" of model
     P142-COREL REEF_BP4_MOCKUP-R00 is the ISSUED, FROZEN reference
     drawing.  Never write to it.  Reading it is correct and necessary -
     the five section marks are copied out of it.  The macro already
     refuses any issued or frozen drawing; keep that refusal in place.

  2. The reference dumps of the client model (asm_parts.txt, full_50.txt,
     full_57.txt, marks_50.json, dims_50.txt, dims_57.txt) are client
     geometry and are NOT in the public repository.  Do not commit them.
     They are regenerated on a machine that has the model - see
     HANDOVER/REFERENCE-DATA.md.

Then, before you change anything, run the checks in
HANDOVER/05-BUILD-AND-VERIFY.md section F and report which of the fifteen
pass.  Do not fix anything that fails without saying so first.

The macro (macros/wall-framing/) is complete and verified: 12 of the 18
dimension chains of the reference are reproduced exactly, the marks match
11/11 on the angle, and the issued drawing is provably untouched.  The six
chains that do not match are hand edits of the issued drawing and are
deliberately not reproduced.

The macro is sold in two editions.  The free edition draws the dimension
chains; the Pro edition draws everything and is unlocked by a licence key
(RSA-2048, tied to the computer, with a three day trial).  The Pro stages
are NOT in this repository - they live in the dev tree under
macros\wall-framing\private\ and are built with build.ps1 -ProPath.  Two
things about the licence: the public key in source/WF_License.cs is still
the placeholder, so no key verifies yet, and the owner keeps the private
key - never write one, never commit one.

The biggest open gap is that the C# MCP server's live Tekla path has never
been run against a real model - it has only been tested against the mock
backend.  Everything else is documented in HANDOVER/06-OPEN-QUESTIONS.md.

Ask before doing anything that is not reversible, and never write a
credential into a file.
```

---

## Why the prompt is shaped this way

| part | why it is there |
|---|---|
| the file list, in order | a fresh instance has no memory of the previous sessions.  The pack is the memory. |
| "read `CLAUDE.md` and follow it" | the repository's own rules are the contract; they are not restated |
| rule 1 (the frozen drawing) | it is the one irreversible mistake available |
| rule 2 (the client data) | it would leak into a public repository and is easy to do by accident |
| "run the checks and report which of the fifteen pass" | it makes the new instance **report the state** before it **changes** the state, and it makes a broken handover visible instead of silently inherited |
| "do not fix anything that fails without saying so first" | the same |
| "the macro is complete and verified" | stops the new instance from rebuilding what works, and from "fixing" the six hand-edit chains that are correct as they are |
| "the biggest open gap is the live Tekla path" | points at the most valuable next task without ordering it |
| "ask before anything irreversible" | a handover is not a mandate |

---

## A shorter version, for a quick start

If the pack is going to be read anyway, this is enough to start:

```
This is a Tekla Structures 2025 project (sherin-tekla-mcp).  Read
HANDOVER/00-START-HERE.md, HANDOVER/03-HARD-RULES.md and
HANDOVER/04-TEKLA-2025-API-TRAPS.md first, then
HANDOVER/05-BUILD-AND-VERIFY.md section F to report the state before
changing anything.

Never write to drawing [50] - it is the issued, frozen reference.
Never commit the client reference dumps.  Tekla 2025 only, no invented
API members.  Label terminal instructions "Command Prompt (Windows)" or
"Ubuntu (WSL)".
```

---

## What to do in the first session

1. **Read** the pack.  Do not skip `04-TEKLA-2025-API-TRAPS.md` — it is
   the file that saves the most time.
2. **Run** the fifteen checks of `05-BUILD-AND-VERIFY.md` section F and
   report the result.  This is the most valuable thing the new instance
   can do, because it tells the owner whether the handover worked.
3. **Ask** which of the open items in `06-OPEN-QUESTIONS.md` to pick up.
   Do not choose a big one on your own.
4. **Do** the small, safe, obviously-wanted one: the settings file (B1) is
   a good candidate, because it is a refactor with no behaviour change and
   it removes the last project-specific constants from the code.

---

## A note on the memory of the previous sessions

The previous work also left **five memory files** in

```
C:\Users\sheri\.claude\projects\C--Users-sheri\memory\
```

(`tekla-mcp-server-setup`, `tekla-2025-drawing-api-traps`,
`tekla-trimble-login-failure`, `sherin-tekla-mcp-github`,
`tekla-wall-framing-dimensioning`).

**They are not in the repository**, because a memory directory is per
machine and per user, and because some of them name client things.  Their
content is folded into this pack:

| memory file | where it is now |
|---|---|
| `tekla-2025-drawing-api-traps` | `04-TEKLA-2025-API-TRAPS.md` (and the repo's own `docs/API_NOTES.md`) |
| `tekla-mcp-server-setup` | `02-ENVIRONMENT.md`, the four gotchas |
| `tekla-trimble-login-failure` | `02-ENVIRONMENT.md` — the Trimble login failure is harmless |
| `sherin-tekla-mcp-github` | `01-STATE-OF-PLAY.md` — the branches, the licence boundary |
| `tekla-wall-framing-dimensioning` | `00-START-HERE.md` and `01-STATE-OF-PLAY.md` |

If the new computer is a **different user**, the memory directory is
different anyway — the pack is the portable form.
