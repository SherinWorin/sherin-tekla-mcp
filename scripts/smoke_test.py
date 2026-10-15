"""
MCP smoke test: starts the server with the MOCK backend, talks to it over stdio exactly like
Claude Desktop does, and checks the tool list, a read, a report, a calculation and the safety
pipeline (read-only refusal). Used by CI. No Tekla needed.

Usage:  python scripts/smoke_test.py <command to start the server...>
e.g.    python scripts/smoke_test.py dotnet src/SherinTeklaMcp.Server/bin/Release/net8.0/SherinTeklaMcp.Server.dll
"""
import json
import os
import subprocess
import sys

EXPECTED_TOOLS = {
    "tekla_get_connection_info", "tekla_get_selected_objects", "tekla_get_objects", "tekla_find_objects",
    "tekla_select_objects", "tekla_highlight_objects", "tekla_quantity_report", "tekla_validate_objects",
    "tekla_calculate_distance", "tekla_list_rules", "tekla_plan_wall", "tekla_divide_opening",
    "tekla_create_beams", "tekla_create_columns", "tekla_modify_objects", "tekla_delete_objects",
}


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    env = dict(os.environ, SHERIN_TEKLA_MCP_MOCK="1")
    proc = subprocess.Popen(sys.argv[1:], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=sys.stderr,
                            text=True, encoding="utf-8", env=env)
    next_id = [0]

    def send(method, params=None, notify=False):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if not notify:
            next_id[0] += 1
            msg["id"] = next_id[0]
        proc.stdin.write(json.dumps(msg) + "\n")
        proc.stdin.flush()
        if notify:
            return None
        while True:
            line = proc.stdout.readline()
            if not line:
                raise RuntimeError("server closed stdout")
            reply = json.loads(line)  # any non-JSON line on stdout is a bug: it breaks MCP clients
            if reply.get("id") == next_id[0]:
                if "error" in reply:
                    raise RuntimeError(f"{method}: {reply['error']}")
                return reply["result"]

    def lower_keys(value):
        # Compare keys case-insensitively: the SDK may serialize camelCase or PascalCase.
        if isinstance(value, dict):
            return {k.lower(): lower_keys(v) for k, v in value.items()}
        if isinstance(value, list):
            return [lower_keys(v) for v in value]
        return value

    def call(name, args):
        result = send("tools/call", {"name": name, "arguments": args})
        if result.get("isError"):
            raise RuntimeError(f"{name} returned isError: {result}")
        if result.get("structuredContent") is not None:
            return lower_keys(result["structuredContent"])
        return lower_keys(json.loads(result["content"][0]["text"]))

    def check(cond, what):
        print(("PASS " if cond else "FAIL ") + what)
        if not cond:
            failures.append(what)

    failures = []
    try:
        init = send("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                   "clientInfo": {"name": "smoke-test", "version": "1"}})
        check(init["serverInfo"]["name"] == "sherin-tekla-mcp", "server name")
        send("notifications/initialized", notify=True)

        tools = {t["name"] for t in send("tools/list")["tools"]}
        check(EXPECTED_TOOLS <= tools, f"all {len(EXPECTED_TOOLS)} tools listed (missing: {sorted(EXPECTED_TOOLS - tools)})")

        info = call("tekla_get_connection_info", {})
        check(info["connected"] is True and info["backend"] == "mock", "connection info (mock)")
        check(info["readonlymode"] is True, "default settings are read-only")

        found = call("tekla_find_objects", {"profile": "IPE300"})
        check(found["count"] == 6, f"find IPE300 -> 6 (got {found['count']})")

        report = call("tekla_quantity_report", {"source": "filter", "types": "Beam", "groupBy": "profile"})
        check(report["totalquantity"] == 21 and len(report["rows"]) == 3, "quantity report over the sample frame")

        dist = call("tekla_calculate_distance", {"pointA": "0,0,0", "pointB": "3000,4000,0"})
        check(abs(dist["distancemm"] - 5000) < 1e-6, "distance 3-4-5")

        plan = call("tekla_plan_wall", {"rule": "boundary-wall", "wallLengthMm": 35500})
        check(plan["status"] == "success" and plan["wallplan"]["bays"] == 10, "wall plan with the boundary-wall rule file")

        write = call("tekla_create_beams", {"beams": [{"start": "0,0,0", "end": "6000,0,0", "profile": "IPE200", "material": "S355JR"}]})
        check(write["status"] == "refused" and "READ-ONLY" in write["summary"], "write refused in read-only mode")
    except Exception as exc:  # noqa: BLE001 - report any failure
        failures.append(str(exc))
        print("FAIL " + str(exc))
    finally:
        proc.stdin.close()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()

    print(f"\n{'OK' if not failures else 'FAILED'}: {len(failures)} failure(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
