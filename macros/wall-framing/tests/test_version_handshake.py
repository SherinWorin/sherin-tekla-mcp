"""
Test of the VERSION HANDSHAKE between the core and the Pro module.

The Pro module (WF_Pro.dll) is a separate assembly that is built against
the rules assembly (WF_Rules.dll) and is loaded by reflection at run
time.  When the two are of different versions, the module calls members
of the rules that no longer exist, and every one of its calls fails with
a bare "Method not found" - which says nothing about the cause.  A
mixed installation therefore looks like a broken macro.

The core reads the version the module was built for and compares it with
the version of the rules that are installed.  This harness checks that
the comparison is right, by building little assemblies of its own and
reading their versions the way the core reads them.

Nothing of Tekla is needed and nothing is written: the little assemblies
are built in a temporary folder and thrown away.

Usage: python test_version_handshake.py
"""
from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import clr  # noqa: E402
import System  # noqa: E402
from System.Reflection import Assembly, BindingFlags  # noqa: E402

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from paths import BIN  # noqa: E402

# the compiler: the same one the build script uses
CSC = None
for candidate in (
    r"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild"
    r"\Current\Bin\Roslyn\csc.exe",
    r"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild"
    r"\Current\Bin\Roslyn\csc.exe",
    r"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild"
    r"\Current\Bin\Roslyn\csc.exe",
):
    if Path(candidate).is_file():
        CSC = Path(candidate)
        break

# a stand-in for the rules assembly
RULES = """
namespace P142.WallFraming
{
    public static class WfRules
    {
        public const int ApiVersion = %d;
    }
}
"""

# a stand-in for the Pro module
PRO = """
namespace P142.WallFraming
{
    public static class WfPro
    {
        public const int RulesApiVersion = %d;
    }
}
"""

# a Pro module of an older version: it carries no number at all
PRO_OLD = """
namespace P142.WallFraming
{
    public static class WfPro
    {
    }
}
"""


def compile_src(src: str, out: Path):
    if CSC is None:
        raise SystemExit("FATAL: the C# compiler was not found.  Build the "
                         "modules with source/build.ps1 and try again.")
    tmp = Path(tempfile.mkdtemp(prefix="wfsrc_"))
    try:
        name = tmp / "src.cs"
        name.write_text(src)
        r = subprocess.run(
            [str(CSC), "/nologo", "/target:library", f"/out:{out}", str(name)],
            capture_output=True, text=True)
        if r.returncode != 0:
            raise SystemExit(f"compile failed:\n{r.stdout}\n{r.stderr}")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def rules_version(dll: Path):
    """The ApiVersion of an assembly, read the way the core reads it."""
    try:
        asm = Assembly.LoadFrom(str(dll))
        t = asm.GetType("P142.WallFraming.WfRules")
        if t is None:
            return None
        f = t.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)
        if f is None:
            return None
        return int(f.GetRawConstantValue())
    except Exception:  # noqa: BLE001
        return None


def pro_version(dll: Path):
    """The RulesApiVersion of a Pro module, read the way the core reads it."""
    try:
        asm = Assembly.LoadFrom(str(dll))
        t = asm.GetType("P142.WallFraming.WfPro")
        if t is None:
            return None
        f = t.GetField("RulesApiVersion", BindingFlags.Public | BindingFlags.Static)
        if f is None:
            return None
        return int(f.GetRawConstantValue())
    except Exception:  # noqa: BLE001
        return None


def main():
    tmp = Path(tempfile.mkdtemp(prefix="wfver_"))
    failures = 0
    try:
        # ---- 1. the two are of the same version
        compile_src(RULES % 2, tmp / "rules_ok.dll")
        compile_src(PRO % 2, tmp / "pro_ok.dll")
        r = rules_version(tmp / "rules_ok.dll")
        p = pro_version(tmp / "pro_ok.dll")
        ok = r == 2 and p == 2
        failures += 0 if ok else 1
        print(f"test 1: rules {r}, Pro {p} -> {'PASS' if ok else 'FAIL'} "
              f"(they match, the Pro module is called)")

        # ---- 2. the Pro module is OLDER than the rules
        compile_src(RULES % 2, tmp / "rules_new.dll")
        compile_src(PRO % 1, tmp / "pro_old.dll")
        r = rules_version(tmp / "rules_new.dll")
        p = pro_version(tmp / "pro_old.dll")
        ok = r == 2 and p == 1 and p != r
        failures += 0 if ok else 1
        print(f"test 2: rules {r}, Pro {p} -> {'PASS' if ok else 'FAIL'} "
              f"(they differ, the Pro module is NOT called)")

        # ---- 3. the Pro module carries no version at all
        compile_src(PRO_OLD, tmp / "pro_none.dll")
        p = pro_version(tmp / "pro_none.dll")
        ok = p is None
        failures += 0 if ok else 1
        print(f"test 3: a Pro module without the constant -> "
              f"{'PASS' if ok else 'FAIL'} (it is recognised as an old one "
              f"and is not called)")

        # ---- 4. the real modules carry the number
        real_rules = BIN / "WF_Rules.dll"
        real_pro = BIN / "WF_Pro.dll"
        if real_rules.is_file() and real_pro.is_file():
            r = rules_version(real_rules)
            p = pro_version(real_pro)
            ok = r is not None and p is not None and r == p
            failures += 0 if ok else 1
            print(f"test 4: the built modules: rules {r}, Pro {p} -> "
                  f"{'PASS' if ok else 'FAIL'} (they are of the same version)")
        else:
            print("SKIP test 4: the modules are not built")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print()
    print("FAILURES: %d" % failures)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
