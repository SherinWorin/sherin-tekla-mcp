# Reference data

## What it is

The harnesses compare what the macro produces against **measured** data of
the project model.  Without it they cannot check anything, because the
drawing itself is the oracle.

| file | what it holds | used by |
|---|---|---|
| `asm_parts.txt` | the 247 wall parts of the model: name, id, plan extents, `ASSEMBLY_POS` | `test_rules.py`, `test_marks.py` |
| `full_50.txt` | the dimension sets, marks and texts of the issued reference drawing | `expected.py` → `test_rules.py` |
| `full_57.txt` | the same for the target drawing | `audit_50_57.py`, `cmp_chains.py` |
| `marks_50.json` | the placement of the 11 wall-panel marks of the reference drawing | `test_marks.py` |
| `dims_50.txt`, `dims_57.txt` | the dimension sets of the two drawings, in the short form | `cmp_chains.py` |
| `model_walls.txt` | a wider dump of the wall parts of the model | the probes in the dev tree |
| `wall_panels.txt` | the list of the wall panels | the probes in the dev tree |

All of them together are about **157 KB**.

## Where they are now

**Not in the GitHub repository** — it is public, and these files hold the
geometry of a client project: part identifiers, profiles, coordinates, and
the name of the issued drawing.

They are in the **dev tree**:

```
C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\debug\
```

The dev tree is **not** a git repository, so nothing in it is published by
accident.

In the published tree, `macros/wall-framing/tests/reference/` holds only
the `.gitignore` and a `README.md` that explains this.

## Copying them to a new computer

**Command Prompt (Windows)**

```
copy "C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\debug\asm_parts.txt"  "macros\wall-framing\tests\reference\"
copy "C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\debug\full_50.txt"    "macros\wall-framing\tests\reference\"
copy "C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\debug\full_57.txt"    "macros\wall-framing\tests\reference\"
copy "C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\debug\marks_50.json"  "macros\wall-framing\tests\reference\"
copy "C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\debug\dims_50.txt"    "macros\wall-framing\tests\reference\"
copy "C:\Users\sheri\projects\tekla-modeling-tool\Tekla-MCP\debug\dims_57.txt"    "macros\wall-framing\tests\reference\"
```

**Use a USB stick or a private file share for this.**  Do not put them in
the repository, and do not attach them to an issue.

## Regenerating them

Better than copying: **regenerate them on the machine that has the model.**
The dumps are then guaranteed to match the model in front of you.

Tekla open, the model open, the title without `(Loading)`, `pythonnet`
installed.

**Command Prompt (Windows)**

```
cd macros\wall-framing\tests
python dump_panel_parts.py > reference\asm_parts.txt
python dump_full.py "[50]" reference\full_50.txt
python dump_full.py "[57]" reference\full_57.txt
python dump_marks_json.py
python dump_dims.py "[50]" reference\dims_50.txt
python dump_dims.py "[57]" reference\dims_57.txt
```

| script | what it writes |
|---|---|
| `dump_panel_parts.py` | the wall parts of the model, read through the rules assembly |
| `dump_full.py` | the full dump of one drawing — the dimension sets with their stations, the marks and the texts |
| `dump_marks_json.py` | the placement of the wall-panel marks |
| `dump_dims.py` | the short form of the dimension sets of one drawing |

`tekla_bootstrap.py` finds the Tekla installation by itself and loads the
assemblies.

`pythonnet` may be missing:

```
pip install pythonnet
```

## Without the reference data

`test_rules.py` stops with a clear message when a dump is missing:

```
asm_parts.txt is missing: ...
See reference\README.md - the reference dumps are not in the public
repository, because they hold the geometry of a client project.
Regenerate them on a machine that has the model.
```

**That is the intended behaviour, not a failure.**  The harness is honest
about not having an oracle instead of quietly passing.

The **rules** can still be checked without any data: build `WF_Rules.dll`
and call it directly — it has no Tekla reference and no file dependencies.

And the **drawings themselves** are the real oracle: run the macro and read
the result with `audit_50_57.py` and `cmp_chains.py`.  Those work against
the live drawings, not against a dump.

## The rule

> The reference dumps are **measured data of a client project**.  They
> belong on the machine that has the model, and they do not go into a
> public repository.

See R2 in [03-HARD-RULES.md](03-HARD-RULES.md).
