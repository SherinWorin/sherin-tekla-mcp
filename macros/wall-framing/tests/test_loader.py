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
from System.Reflection import Assembly, BindingFlags  # noqa: E402

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

        # ---- the version handshake
        # The Pro module is built against the rules assembly, and the two
        # have to be of the same version.  A mixed installation - a new
        # core with an old Pro module - makes every call of the module
        # fail with a bare "Method not found", so it is checked here.
        print("  the version handshake:")
        rules_version = read_rules_version(core)
        pro_version = read_pro_version(pro_type)
        if rules_version is None:
            print("    the rules assembly carries no version number - it is "
                  "older than the macro")
            return 1
        if pro_version is None:
            print("    the Pro module carries no version number - it is older "
                  "than the rules, and the Pro stages would fail on every "
                  "object")
            return 1
        if rules_version != pro_version:
            print(f"    FAIL: the Pro module was built for version "
                  f"{pro_version} of the rules, and the installed rules are "
                  f"version {rules_version}.  Install the modules again.")
            return 1
        print(f"    OK: the rules and the Pro module are both version "
              f"{rules_version}")
    else:
        print(f"  the Pro module is not installed ({pro_path}) - the free edition")

    # ---- the AIO entry point of the core
    #
    # The AIO macro (the second one, the free one) calls the core by
    # reflection as well, and it calls this entry point.  A core that is
    # older than the AIO loader has no such method, and the macro would
    # say "the module is an older version" and stop.
    aio = core_type.GetMethod("ExecuteAio")
    if aio is None or len(aio.GetParameters()) != 3:
        print("FATAL: the core has no entry point for the AIO macro")
        print("       install the modules again (build.ps1 -Deploy)")
        return 1
    print("  the core has the AIO entry point (ExecuteAio)")

    # ---- the AIO loader in the macro catalog
    catalog = Path(TEKLA) / "Environments" / "common" / "macros" / "drawings"
    aio_loader = catalog / "WallFramingAutoDimensionPro-AIO.cs"
    if aio_loader.is_file():
        print(f"  the AIO loader is in the catalog: {aio_loader}")
    else:
        print(f"FATAL: the AIO loader is not in the macro catalog: {aio_loader}")
        print("       install it again (build.ps1 -Deploy)")
        return 1

    print()
    print("the loader chain is OK")
    return 0


def read_rules_version(core):
    """The version of the rules assembly that the core was built against.

    The core holds the rules assembly as a reference, so the assembly
    that is really loaded beside it is the one the Pro module will be
    called against.  It is looked for in the folder of the core, which
    is where the runtime finds it too.
    """
    try:
        folder = Path(core.Location).parent
        path = folder / "WF_Rules.dll"
        if not path.is_file():
            return None
        rules = Assembly.LoadFrom(str(path))
        t = rules.GetType("P142.WallFraming.WfRules")
        if t is None:
            return None
        f = t.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)
        if f is None:
            return None
        return int(f.GetRawConstantValue())
    except Exception:  # noqa: BLE001
        return None


def read_pro_version(pro_type):
    """The version of the rules the Pro module was built against."""
    try:
        f = pro_type.GetField("RulesApiVersion",
                               BindingFlags.Public | BindingFlags.Static)
        if f is None:
            return None
        return int(f.GetRawConstantValue())
    except Exception:  # noqa: BLE001
        return None


if __name__ == "__main__":
    sys.exit(main())
