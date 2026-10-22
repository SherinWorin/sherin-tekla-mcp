# TODO

## Done

* [x] Read the model and group the wall parts into panels (`ASSEMBLY_POS`
      prefix `BP4-WP`).
* [x] The bay chain of every panel (R1, R2).
* [x] The band chains of the bottom, left and right edges of the layout
      (R3).
* [x] The top outline of the layout, with the chamfer over a wall that
      crosses it and the bridging of a narrow notch (R4).
* [x] The overall height and the overall width of the layout (R5).
* [x] The north run (R6).
* [x] The 11 wall-panel marks, with the text angle, the leader line and
      the label offset derived from the model.
* [x] The 5 section marks, copied from the reference drawing.
* [x] The 3 notes.
* [x] Idempotency: the output is tagged and replaced on a re-run.
* [x] The operation log.
* [x] The build script, the merged single-file source and the deployment
      to the macro catalog.
* [x] The documentation set (README, ARCHITECTURE, API_NOTES, SETUP,
      TESTING, TODO, CHANGELOG, WALL_FRAMING_RULES).
* [x] **The target drawing is found by its title**, not by its mark (R8),
      and an issued or frozen drawing is refused.
* [x] **The wall panels are found automatically** when the configured
      prefix matches nothing (R9).
* [x] **The deployment package** for the other computers:
      `package/WallFramingAutoDimension.zip`, with an installer that
      finds the Tekla installation by itself.
* [x] **The two editions.** The free edition (the dimension chains) and
      the Pro edition (everything), cut at the assembly boundary: a thin
      loader in the macro catalog, `WF_Core.dll`, `WF_Rules.dll`,
      `WF_License.dll` and — in the Pro package — `WF_Pro.dll`.
* [x] **The licence engine.** A signed key (RSA-2048 over SHA-256), a
      three day trial, a machine binding on three hashes, a clock-rollback
      rule, and the owner's key tool `WF_KeyGen.exe`. 42 checks, no Tekla
      needed.

## Next

* [ ] **Run the Pro path against a live Tekla.** The Pro stages were
      moved out of the macro into `WF_Pro.dll` and the Pro rules into
      `WF_Rules_Pro.dll` after the drawing was finished.  The harnesses
      prove that the *rules* were moved without changing an answer (12 of
      18 chains, angle 11/11, leader 9/11, offset 9/11), but that the
      Pro module **writes** the same objects in the same places is not
      proven.  One run of the macro with a Pro licence, followed by
      `cmp_chains.py` and `audit_50_57.py`, settles it.
* [ ] **The owner pastes the public key** into `PublicKeyXml` in
      `source/WF_License.cs` (it is still the placeholder
      `REPLACE_WITH_THE_PUBLIC_KEY`) and rebuilds.  Until that is done,
      **no key verifies** — the engine and the tools are ready, but the
      licence cannot be issued end to end.
* [ ] **Deploy the macro to the catalog.** `powershell -NoProfile -File
      source/build.ps1 -Deploy` writes into the Tekla installation; that
      step needs a human to run it. On another computer the package's
      `install.ps1` does it.
* [ ] **Check the north run on the sheet.** It is placed 30 mm north of
      the wall it measures, because the reference drawing puts it on top
      of the top outline. Look at [57] and confirm the placement reads
      well; the constant is `PAPER_NORTH` in the macro.
* [ ] **The two hand-placed wall lines of [50]** (sets 10 and 18) are
      not reproduced. They cannot be derived — the endpoints are hand
      points (296.07 and 1705.85). If the user wants them, they have to
      be hard-coded per project, which the rules deliberately do not do.
* [ ] **A settings file.** The paper distances, the notch width, the
      panel prefix and the reference drawing are constants in the macro.
      They should move into a file when a second project needs them. The
      panel prefix is already only a preference — the automatic panel
      detection takes over when it matches nothing — and the reference
      drawing is only a preference too.
* [ ] **A unit test of the writing path** — it needs a live Tekla
      instance; today the writing path is verified by running the macro
      and dumping the result.
* [ ] **The wall layout fragments are a list in the macro**
      (`WALL_LAYOUT_TITLES`). A second project with another wording
      ("LGS WALL PLAN", "PARTITION LAYOUT") would need that list
      extended; it belongs in the settings file of the item above.

## Later

* [ ] **A plugin instead of a macro.** The rules assembly is already
      separate and testable, so it can be lifted into a plugin unchanged.
      The value of a plugin is that it can add a ribbon tab and run
      without the macro catalog.
* [ ] **Other drawings of the same model** — the macro writes whatever
      wall layout it finds, but only the GA drawing has been verified.
* [ ] **The package on a second computer** — the installer is written
      and dry-run here, but it has not been run on the other machine
      yet. The first run there should be a dry run
      (`install.ps1 -WhatIfOnly`) and then a check of the log of the
      macro.
* [ ] **A licence key that is actually issued end to end** — the
      `newkey` → `issue` → `install` → `status` round trip has not been
      walked through with a real key, because the public key is still the
      placeholder.  The first time it is done, the whole chain should be
      checked on a **second** computer, so that the machine binding is
      proved to refuse a key on the wrong machine.
* [ ] **The assembly drawings** `[BP4-WP1]` … `[BP4-WP11]` are not
      touched at all.
* [ ] **A second rule set** (e.g. the pod base frame) is already
      implemented as a separate macro,
      `C:\TeklaStructures\2025.0\Environments\common\macros\drawings\P142_BF_AutoDimension.cs`.
      The two should share a common style definition, so that both
      drawings look the same.
* [ ] **Version control.** The project is not a git repository yet. The
      §22 rule wants one, with the history in `CHANGELOG.md`.
* [ ] **The knowledge base** (§16): the rules of this project are
      written down in `WALL_FRAMING_RULES.md`; the same treatment for
      the API traps belongs in `API_NOTES.md` — done — and any user
      documentation of Tekla itself should be added as it arrives.

## Known open questions

* **The chain of the WP8 openings.** [50] measures the centres of the
  outer jambs of the two openings of `BP4-WP8` (559.81 and 1639.81) in
  addition to the edges; the same construction in `BP4-WP7` is measured
  **without** those centres. Either [50] is inconsistent, or there is a
  rule that has not been found. Until that is settled the rule measures
  the lintel-bearing edge only.
* **The position of a mark along its leader** is a drafter's choice in
  [50] and is not reproducible. The macro puts the label at the centre
  of the panel.
* **The section marks** carry no information from the model; they are
  copied. If a future project has a rule for them, it should live in the
  rules assembly, not in the copy step.
