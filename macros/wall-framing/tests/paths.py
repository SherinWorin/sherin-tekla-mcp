"""
Where the test harnesses find their files.

The tree of the macro:

    macros/wall-framing/
        README.txt
        INSTALL_NOTES.txt
        install.ps1
        source/                 the C# sources, and source\\bin after a build
            WF_Rules.cs
            P142_WF_AutoDimension.cs
            build.ps1
        docs/                   the documentation set
        package/                the deployment package
        tests/                  this folder - the harnesses
            reference/           the reference dumps of the project model

The REFERENCE folder holds the dumps of the model and of the issued
drawing.  They are **not** in the public repository: they hold the
geometry of a client project.  See reference\\README.md for how to
regenerate them on a machine that has the model.
"""
from __future__ import annotations

import os
from pathlib import Path

HERE = Path(__file__).resolve().parent        # .../wall-framing/tests
MACRO = HERE.parent                            # .../wall-framing
SOURCE = MACRO / "source"                      # the C# sources
BIN = SOURCE / "bin"                          # the build output


def _has_dumps(folder: Path) -> bool:
    """Does the folder really hold the reference dumps?

    tests\\reference exists in the public repository, but it holds only a
    README and a .gitignore: the dumps themselves are not published.  A
    folder that exists but is empty of dumps must not stop the search.
    """
    if not folder.is_dir():
        return False
    return any(folder.glob("*.txt")) or any(folder.glob("*.json"))


def _find_reference() -> Path:
    """Where the reference dumps are.

    They are not in the public repository, so they are usually somewhere else
    on the disk.  The folder is looked for in this order:
      1. the WF_REFERENCE environment variable
      2. tests\\reference of this tree
      3. the reference-data folder of a development tree

    A folder that exists but holds no dumps is skipped, so that the empty
    tests\\reference of the public repository does not hide the real one.
    """
    env = os.environ.get("WF_REFERENCE", "").strip()
    if env:
        p = Path(env)
        if p.is_dir():
            return p
    local = HERE / "reference"
    if _has_dumps(local):
        return local
    for base in (Path.home() / "projects",):
        if not base.is_dir():
            continue
        for tree in base.glob("**/HANDOVER/reference-data"):
            if _has_dumps(tree):
                return tree
    return local


REFERENCE = _find_reference()                    # the reference dumps

# The Pro rules are NOT in the public repository.  When the private sources
# have been built, this assembly holds them and the harness checks them too.
PRO_RULES_DLL = BIN / "WF_Rules_Pro.dll"


def require(path: Path, what: str) -> Path:
    """Stop with a clear message when a reference file is missing."""
    if not path.exists():
        raise SystemExit(
            f"{what} is missing: {path}\n"
            f"See reference\\README.md - the reference dumps are not in the "
            f"public repository, because they hold the geometry of a client "
            f"project.  Point the WF_REFERENCE environment variable at the "
            f"folder that holds them, or regenerate them on a machine that "
            f"has the model.")
    return path
