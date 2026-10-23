"""
The licence engine, tested without a running Tekla Structures.

WF_License.dll and WF_KeyGen.exe have no Tekla reference, so pythonnet can
load them in a plain Python process.  The whole round trip is exercised:

    key pair  ->  sign a key  ->  verify it  ->  the tier it grants

The key pair is made IN MEMORY, in this process, and is thrown away at the
end.  No private key is ever written to a file by these tests: the real
private key belongs to the owner, and only the owner creates it.

Run:

    python tests/test_license.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN  # noqa: E402

import clr  # noqa: E402  - must come before the System imports
import System  # noqa: E402
from System import DateTime, DateTimeKind  # noqa: E402
from System.Collections.Generic import Dictionary, List  # noqa: E402
from System.Security.Cryptography import RSACryptoServiceProvider  # noqa: E402

PASSED = 0
FAILED = 0


def check(name: str, ok: bool, detail: str = "") -> None:
    global PASSED, FAILED
    if ok:
        PASSED += 1
        print(f"  PASS  {name}")
    else:
        FAILED += 1
        print(f"  FAIL  {name}" + (f"   {detail}" if detail else ""))


def load(name: str):
    """Loads one of the built assemblies (a .dll or the .exe)."""
    candidates = [BIN / f"{name}.dll", BIN / f"{name}.exe"]
    path = next((p for p in candidates if p.exists()), None)
    if path is None:
        raise SystemExit(
            f"{candidates[0]} is missing.\n"
            "Build it first:  cd source  &&  powershell -NoProfile -File build.ps1")
    import System.Reflection
    return System.Reflection.Assembly.LoadFrom(str(path))


def static_call(asm, type_name: str, method: str, *args):
    """
    Calls a static method through reflection.

    pythonnet 3 does not hang the static members of a RuntimeType on the
    type object, so they are reached through MethodInfo.  When a method is
    overloaded (Evaluate has a 4-argument and a 5-argument form) the one
    with the matching number of arguments is used.
    """
    t = asm.GetType(type_name)
    if t is None:
        raise SystemExit(f"the type {type_name} is not in the assembly")
    for m in t.GetMethods():
        if m.Name == method and m.IsStatic and m.GetParameters().Length == len(args):
            return m.Invoke(None, list(args))
    raise SystemExit(f"no static {type_name}.{method} with {len(args)} arguments")


def enum_int(value) -> int:
    """The number of a .NET enum value."""
    return int(System.Convert.ToInt32(value))


def new_state() -> Dictionary:
    return Dictionary[str, str]()


def state_with(**values) -> Dictionary:
    st = new_state()
    for key, value in values.items():
        st[key] = str(value)
    return st


def utc(ticks: int) -> DateTime:
    return DateTime(ticks, DateTimeKind.Utc)


# A fixed moment for the tests: 2026-09-21 00:00:00 UTC.
# The .NET ticks are asked of .NET itself - they are counted from the year 1,
# not from 1970, so they cannot be worked out from a Unix timestamp.
START = DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
START_TICKS = START.Ticks


def str_list(items) -> List:
    """A .NET List[str] out of a Python list."""
    out = List[str]()
    for item in items:
        out.Add(str(item))
    return out


def machine(asm, guid: str = "g", volume: str = "v", cpu: str = "c"):
    """A fake set of machine ids, so the tests do not read the real ones."""
    # pythonnet: a RuntimeType is not callable - use the Activator.
    ids = System.Activator.CreateInstance(asm.GetType("P142.WallFraming.WfMachineIds"))
    ids.Guid = guid
    ids.Volume = volume
    ids.Cpu = cpu
    return ids


def main() -> int:
    print("The licence engine")
    print("=" * 70)

    lic = load("WF_License")
    keygen = load("WF_KeyGen")

    def L(method: str, *args):
        return static_call(lic, "P142.WallFraming.WfLicense", method, *args)

    def K(method: str, *args):
        return static_call(keygen, "P142.WallFraming.WfKeyGen", method, *args)

    # ---- a key pair, made in memory and thrown away afterwards
    rsa = RSACryptoServiceProvider(2048)
    private_xml = rsa.ToXmlString(True)
    public_xml = rsa.ToXmlString(False)

    ids = machine(lic)
    dev = [ids.Guid, ids.Volume, ids.Cpu]

    def issue(customer: str, expiry: str = "", devices=None, note: str = "") -> str:
        return K("BuildFromXml", private_xml, customer, expiry,
                 str_list(devices if devices is not None else dev), note)

    # ------------------------------------------------------------ the signing
    print("\n1. signing and verifying")
    key = issue("ACME Structures", "2027-12-31")
    check("a key has the WF1 shape", key.startswith("WF1.") and key.count(".") == 2, key[:40])

    verified = L("VerifyKey", key, public_xml)
    check("a fresh key verifies", verified is not None)
    if verified is not None:
        check("the customer is in the key", verified.Customer == "ACME Structures",
              str(verified.Customer))
        check("the expiry is in the key", verified.Expiry == "2027-12-31", str(verified.Expiry))
        check("the machine ids are in the key", len(verified.MachineIds) == 3,
              str(len(verified.MachineIds)))

    # a key signed with a DIFFERENT private key must not verify against our public key
    rsa2 = RSACryptoServiceProvider(2048)
    other_key = K("BuildFromXml", rsa2.ToXmlString(True), "Someone else", "",
                  str_list(dev), "")
    check("a key signed with another private key does not verify",
          L("VerifyKey", other_key, public_xml) is None)

    # tampering
    parts = key.split(".")
    tampered = parts[0] + "." + parts[1][:-4] + "AAAA." + parts[2]
    check("a tampered payload does not verify", L("VerifyKey", tampered, public_xml) is None)
    check("garbage does not verify", L("VerifyKey", "not a key", public_xml) is None)
    check("an empty key does not verify", L("VerifyKey", "", public_xml) is None)

    # the shipped public key is still the placeholder, so nothing verifies with it
    check("the placeholder public key verifies nothing",
          L("VerifyKey", key) is None)

    # ------------------------------------------------------------ the tiers
    print("\n2. what a key grants")
    now = START                # a fixed moment

    pro = L("Evaluate", new_state(), key, now, machine(lic), public_xml)
    check("a valid key gives Pro", enum_int(pro.Edition) == 1, str(pro.Edition))
    check("the state is Pro", enum_int(pro.State) == 2, str(pro.State))
    check("the customer is reported", pro.Customer == "ACME Structures", str(pro.Customer))

    other_pc = L("Evaluate", new_state(), key, now, machine(lic, "x", "y", "z"), public_xml)
    check("a key does not work on another computer", enum_int(other_pc.Edition) == 0,
          str(other_pc.Edition))
    check("the state is Invalid", enum_int(other_pc.State) == 4, str(other_pc.State))

    # two of the three ids are enough: replacing a disk must not break a licence
    moved_disk = machine(lic, "g", "other-volume", "c")
    still_ok = L("Evaluate", new_state(), key, now, moved_disk, public_xml)
    check("a licence survives a disk change (2 of 3 ids match)",
          enum_int(still_ok.Edition) == 1, str(still_ok.Edition))

    expired_key = issue("ACME Structures", "2020-01-01")
    exp = L("Evaluate", new_state(), expired_key, now, machine(lic), public_xml)
    check("an expired key gives Free", enum_int(exp.Edition) == 0, str(exp.Edition))
    check("the state is Expired", enum_int(exp.State) == 3, str(exp.State))

    # ------------------------------------------------------------ the trial
    print("\n3. the trial")
    day = 24 * 60 * 60 * 10000000          # one day in ticks
    start = START

    fresh = L("Evaluate", new_state(), "", start, machine(lic), public_xml)
    check("with no state at all it is Free", enum_int(fresh.Edition) == 0, str(fresh.Edition))

    trial_state = state_with(first=start.Ticks, last=start.Ticks)
    trial = L("Evaluate", trial_state, "", start, machine(lic), public_xml)
    check("on the first day of the trial it is Pro", enum_int(trial.Edition) == 1,
          str(trial.Edition))
    check("the state is Trial", enum_int(trial.State) == 1, str(trial.State))
    check("the trial end is reported", trial.TrialEnds.startswith("2026-"), str(trial.TrialEnds))

    # The VERY FIRST run: the state has the "first" stamp but no "last" one
    # yet.  This must not throw - the free edition would otherwise be used
    # for ever, because the trial would never start.
    first_run = state_with(first=start.Ticks)
    try:
        very_first = L("Evaluate", first_run, "", start, machine(lic), public_xml)
        check("the very first run starts the trial", enum_int(very_first.Edition) == 1,
              str(very_first.Edition))
        check("the very first run is not an error", "clock" not in very_first.Message,
              very_first.Message)
    except Exception as e:
        check("the very first run starts the trial", False, str(e))

    day2 = L("Evaluate", trial_state, "", utc(start.Ticks + 2 * day), machine(lic), public_xml)
    check("on the third day it is still Pro", enum_int(day2.Edition) == 1, str(day2.Edition))

    day4 = L("Evaluate", trial_state, "", utc(start.Ticks + 4 * day), machine(lic), public_xml)
    check("on the fifth day it is Free", enum_int(day4.Edition) == 0, str(day4.Edition))
    check("the message says the trial ran out", "ran out" in day4.Message, day4.Message)

    # the clock is moved back
    rolled = state_with(first=start.Ticks, last=start.Ticks + 10 * day)
    back = L("Evaluate", rolled, "", utc(start.Ticks + day), machine(lic), public_xml)
    check("moving the clock back ends the trial", enum_int(back.Edition) == 0,
          str(back.Edition))
    check("the message says the clock was moved back", "clock" in back.Message, back.Message)

    # a small clock difference is tolerated (daylight saving, a slow clock):
    # the last run was 30 minutes later than now
    small = state_with(first=start.Ticks, last=start.Ticks + day)
    nearly = L("Evaluate", small, "", utc(start.Ticks + day - 30 * 60 * 10000000),
               machine(lic), public_xml)
    check("a small clock difference is tolerated", enum_int(nearly.Edition) == 1,
          str(nearly.Edition))

    # a licence beats the trial
    both = state_with(first=start.Ticks, last=start.Ticks)
    both_key = L("Evaluate", both, key, start, machine(lic), public_xml)
    check("a licence wins over the trial", enum_int(both_key.Edition) == 1,
          str(both_key.Edition))
    check("the state is Pro, not Trial", enum_int(both_key.State) == 2, str(both_key.State))

    # ------------------------------------------------------------ the state file
    print("\n4. the state file")
    check("the state file is under LOCALAPPDATA",
          "P142" in L("AppFolder") and "WallFraming" in L("AppFolder"), L("AppFolder"))
    check("the state file is called state.txt", L("StatePath").endswith("state.txt"))

    # the round trip of the state, in a dictionary
    st = state_with(first="123", last="456", instance="abc")
    check("the first stamp is read back", L("TrialStart", st).Ticks == 123)
    check("the last stamp is read back", L("LastSeen", st).Ticks == 456)
    check("a state without a first stamp has no trial",
          L("TrialStart", new_state()) == DateTime.MinValue)

    # ------------------------------------------------------------ the machine
    print("\n5. the machine ids")
    real = L("GetMachineIds")
    check("the machine GUID is read", len(real.Guid) == 64, real.Guid[:16])
    check("the volume serial is read", len(real.Volume) == 64, real.Volume[:16])
    check("the processor id is read", len(real.Cpu) == 64, real.Cpu[:16])
    check("the ids are hashes, not the raw values",
          "guid" not in real.Guid.lower() and real.Guid != real.Volume)

    parsed = static_call(lic, "P142.WallFraming.WfMachineIds", "Parse", real.Serialize())
    check("the ids survive a round trip", parsed.Serialize() == real.Serialize())

    # ------------------------------------------------------------ the result
    print()
    print("=" * 70)
    print(f"{PASSED} passed, {FAILED} failed")
    return 0 if FAILED == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
