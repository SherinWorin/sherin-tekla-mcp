"""
A check of the LOADER chain, without a running Tekla Structures.

The file the Tekla macro catalog compiles is a thin loader: it loads
WF_Core.dll out of %LOCALAPPDATA%\\P142\\WallFraming and hands the work
over by reflection.  This harness does the same two things the loader
does, so that a broken deployment is found here and not in Tekla.

The Tekla assemblies are needed (the core references them), but they are
only loaded - no Tekla has to be running.

Usage: python test_loader.py
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

import clr  # noqa: E402
import System  # noqa: E402
from System.Reflection import Assembly  # noqa: E402

TEKLA = os.environ.get("TEKLA_HOME", r"C:\TeklaStructures\2025.0")
APP = Path(os.environ["LOCALAPPDATA"]) / "P142" / "WallFraming"

TEKLA_DLLS = (
    "Tekla.Structures.dll",
    "Tekla.Structures.Drawing.dll",
    "Tekla.Structures.Model.dll",
    "Tekla.Structures.Geometry3d.Compatibility.dll",
    "Tekla.Macros.Runtime.dll",
)


def main():
    if not Path(TEKLA).is_dir():
        print(f"FATAL: the Tekla installation is not there: {TEKLA}")
        return 2

    # the Tekla assemblies, as the macro catalog has them
    for name in TEKLA_DLLS:
        path = Path(TEKLA) / "bin" / name
        if not path.is_file():
            print(f"FATAL: {path} is missing")
            return 2
        Assembly.LoadFrom(str(path))
    print(f"the Tekla assemblies are loaded from {TEKLA}")

    # ---- the loader: the core, out of the app folder
    core_path = APP / "WF_Core.dll"
    if not core_path.is_file():
        print(f"FATAL: the core is not installed: {core_path}")
        print("       run install.ps1, or copy the modules into that folder")
        return 2
    core = Assembly.LoadFrom(str(core_path))
    print(f"  the core is loaded: {core_path}")

    core_type = core.GetType("P142.WallFraming.WfCore")
    result_type = core.GetType("P142.WallFraming.WfResult")
    if core_type is None or result_type is None:
        print("FATAL: the core is not the expected one")
        return 1
    print("  the types are found: WfCore, WfResult")

    # ---- the licence, exactly as the loader asks for it
    edition = core_type.GetMethod("LicenceEdition").Invoke(None, None)
    message = core_type.GetField("LicenceMessage").GetValue(None)
    print(f"  the licence says: edition {edition}  ({message})")
    if edition not in (0, 1):
        print("FATAL: the edition is not 0 or 1")
        return 1

    # ---- the Pro module, the way the core loads it
    pro_path = APP / "WF_Pro.dll"
    if pro_path.is_file():
        pro = Assembly.LoadFrom(str(pro_path))
        pro_type = pro.GetType("P142.WallFraming.WfPro")
        if pro_type is None:
            print("  the Pro module is installed but is not the expected one")
            return 1
        if pro_type.GetMethod("Execute") is None:
            print("  the Pro module has no entry point")
            return 1
        print("  the Pro module is loaded and has its entry point")
    else:
        print(f"  the Pro module is not installed ({pro_path}) - the free edition")

    print()
    print("the loader chain is OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
