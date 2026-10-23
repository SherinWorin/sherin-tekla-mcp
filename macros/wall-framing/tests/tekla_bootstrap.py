"""
Loads the Tekla Structures 2025 assemblies for pythonnet.

The test harnesses need the Tekla Open API, but not the whole MCP server:
this module puts the assemblies of the Tekla installation on the .NET
search path and hands out the few types the harnesses use.

It finds the installation by itself (TEKLA_HOME, C:\\TeklaStructures\\*,
C:\\Program Files\\Tekla Structures\\*), so the harnesses run on any
computer of the project.

Usage:

    from tekla_bootstrap import DrawingHandler, Drawing, Mark, SectionMark
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

import clr  # noqa: F401  (pythonnet: must come before the .NET imports)
import System  # noqa: F401

# the assemblies the harnesses need
_DLLS = (
    "Tekla.Structures.dll",
    "Tekla.Structures.Model.dll",
    "Tekla.Structures.Drawing.dll",
    "Tekla.Structures.Geometry3d.Compatibility.dll",
    "Tekla.Macros.Runtime.dll",
)


def find_tekla_bin() -> Path:
    """The bin folder of the Tekla Structures installation.

    A 2025 installation is preferred over any other one.
    """
    candidates = []

    env = os.environ.get("TEKLA_HOME") or os.environ.get("TEKLA_STRUCTURES_HOME")
    if env:
        candidates.append(Path(env))

    for root in ("C:/TeklaStructures",
                 "C:/Program Files/Tekla Structures",
                 "C:/Program Files (x86)/Tekla Structures"):
        p = Path(root)
        if p.is_dir():
            candidates += sorted(d for d in p.iterdir() if d.is_dir())

    good = [d for d in candidates
            if (d / "bin" / "Tekla.Structures.Drawing.dll").exists()]
    if not good:
        raise FileNotFoundError(
            "No Tekla Structures installation was found.  Looked at TEKLA_HOME, "
            "C:\\TeklaStructures\\* and C:\\Program Files\\Tekla Structures\\*.")

    v2025 = [d for d in good if d.name.startswith("2025")]
    pool = v2025 or good
    return (sorted(pool, key=lambda d: d.name, reverse=True)[0] / "bin")


def load() -> Path:
    """Put the Tekla assemblies on the .NET search path and load them."""
    bin_dir = find_tekla_bin()
    sys.path.append(str(bin_dir))

    # pythonnet resolves the dependent assemblies through sys.path
    runtime = bin_dir.parent / "Net48Runtime"
    if runtime.is_dir():
        sys.path.append(str(runtime))

    for name in _DLLS:
        dll = bin_dir / name
        if dll.exists():
            clr.AddReference(str(dll))
    return bin_dir


TEKLA_BIN = load()

# the types the harnesses use
from Tekla.Structures.Drawing import (  # noqa: E402
    Drawing,
    DrawingHandler,
    Mark,
    SectionMark,
    StraightDimensionSet,
    Text,
)

__all__ = [
    "TEKLA_BIN",
    "Drawing",
    "DrawingHandler",
    "Mark",
    "SectionMark",
    "StraightDimensionSet",
    "Text",
    "find_tekla_bin",
    "load",
]
