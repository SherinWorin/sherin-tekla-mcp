# The reference dumps

The harnesses compare what the macro produces against **measured** data of the
project model:

| file | what it holds | used by |
|---|---|---|
| `asm_parts.txt` | the 247 wall parts of the model: name, id, plan extents, `ASSEMBLY_POS` | `test_rules.py`, `test_marks.py` |
| `full_50.txt` | the dimension sets, marks and texts of the issued reference drawing | `expected.py` → `test_rules.py` |
| `marks_50.json` | the placement of the 11 wall-panel marks of the reference drawing | `test_marks.py` |
| `dims_50.txt`, `dims_57.txt` | the dimension sets of the two drawings, in the short form | `cmp_chains.py` |

**They are not in this repository.** They hold the geometry of a client
project (part identifiers, profiles, coordinates and the name of the issued
drawing), and this repository is public.

## Regenerating them

On a machine that has the model and Tekla Structures 2025 running with the
drawings open, run the dump scripts from this folder:

**Command Prompt (Windows)**

```
python dump_panel_parts.py > asm_parts.txt
python dump_full.py "[50]" full_50.txt
python dump_marks_json.py
python dump_dims.py "[50]" dims_50.txt
python dump_dims.py "[57]" dims_57.txt
```

* `dump_panel_parts.py` reads the wall parts of the model through the rules
  assembly and writes the same format as `asm_parts.txt`.
* `dump_full.py` writes the full dump of a drawing — the dimension sets with
  their stations, the marks and the texts.
* `dump_marks_json.py` writes `marks_50.json` — the placement of the
  wall-panel marks.
* `dump_dims.py` writes the short form of the dimension sets of one drawing.

The scripts need `pythonnet` and a running Tekla Structures:

```
pip install pythonnet
```

`tekla_bootstrap.py` finds the Tekla installation by itself and loads the
assemblies.

## Without the reference data

`test_rules.py` stops with a clear message when a dump is missing. The
**rules** themselves can still be checked without any data: build the rules
assembly and call it directly — it has no Tekla reference and no file
dependencies.

The drawings themselves are the real oracle: run the macro and read the
result with `audit_50_57.py` and `cmp_chains.py`.
