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

from pathlib import Path

HERE = Path(__file__).resolve().parent        # .../wall-framing/tests
MACRO = HERE.parent                            # .../wall-framing
SOURCE = MACRO / "source"                      # the C# sources
BIN = SOURCE / "bin"                          # the build output
REFERENCE = HERE / "reference"                 # the reference dumps


def require(path: Path, what: str) -> Path:
    """Stop with a clear message when a reference file is missing."""
    if not path.exists():
        raise SystemExit(
            f"{what} is missing: {path}\n"
            f"See reference\\README.md - the reference dumps are not in the "
            f"public repository, because they hold the geometry of a client "
            f"project.  Regenerate them on a machine that has the model.")
    return path
