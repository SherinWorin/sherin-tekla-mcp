# Handover — Tekla Structures 2025 work

This folder is the **hand-over pack**.  It is written for a **fresh Claude
instance on another computer**, and for the human who sets that computer up.

It answers three questions, in this order:

1. **What is this project and where does it live?** → [00-START-HERE.md](00-START-HERE.md)
2. **What state is it in, and what is still open?** → [01-STATE-OF-PLAY.md](01-STATE-OF-PLAY.md)
3. **What must not be broken?** → [03-HARD-RULES.md](03-HARD-RULES.md)

| file | what it holds |
|---|---|
| [00-START-HERE.md](00-START-HERE.md) | the one-page briefing. Read this first, it is the shortest path to working. |
| [01-STATE-OF-PLAY.md](01-STATE-OF-PLAY.md) | what is done, what is not, the three code trees, the branches, the open PR. |
| [02-ENVIRONMENT.md](02-ENVIRONMENT.md) | what the new computer needs, and the four gotchas that make the tools silently return nothing. |
| [03-HARD-RULES.md](03-HARD-RULES.md) | the rules that are not negotiable: the frozen drawing, the client data, the licences, the API discipline. |
| [04-TEKLA-2025-API-TRAPS.md](04-TEKLA-2025-API-TRAPS.md) | the Tekla 2025 behaviour that cost the most time to find. Do not rediscover it. |
| [05-BUILD-AND-VERIFY.md](05-BUILD-AND-VERIFY.md) | every command, labelled, to rebuild the macro and to prove the state on the new computer. |
| [06-OPEN-QUESTIONS.md](06-OPEN-QUESTIONS.md) | the open items, who owns them, and what would settle them. |
| [07-PROMPT-FOR-NEXT-CLAUDE.md](07-PROMPT-FOR-NEXT-CLAUDE.md) | the text to paste into the new Claude session to start it correctly. |
| [REFERENCE-DATA.md](REFERENCE-DATA.md) | the reference dumps of the client model: why they are not in the repository and how to regenerate them. |

**There is no token, password or key in this folder, and none may be added to
it.**  The GitHub token is obtained at the moment it is needed, from the
Windows credential manager — see [01-STATE-OF-PLAY.md](01-STATE-OF-PLAY.md).

**Two things in this folder are for the human, not for Claude:** the install
steps in [02-ENVIRONMENT.md](02-ENVIRONMENT.md) and the "write it yourself"
steps in [05-BUILD-AND-VERIFY.md](05-BUILD-AND-VERIFY.md).  Claude cannot
copy a file into the Tekla installation — the safety layer of the tool blocks
it, and that is deliberate.  The human runs the `copy` command.
