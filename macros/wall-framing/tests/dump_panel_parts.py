"""Print the raw parts of one wall panel, grouped by profile name.

Usage: python tests/dump_panel_parts.py BP4-WP8
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from paths import BIN  # noqa: E402
from test_rules import load_parts, PANEL_PREFIX  # noqa: E402


def main():
    want = sys.argv[1] if len(sys.argv) > 1 else "BP4-WP8"
    rows = [p for p in load_parts() if p["apos"] == want]
    rows.sort(key=lambda p: (p["name"], min(p["x0"], p["x1"])))
    print(f"{want}: {len(rows)} parts")
    for p in rows:
        print(f"  {p['name']:14} X {min(p['x0'],p['x1']):9.2f}..{max(p['x0'],p['x1']):9.2f}"
              f"  Y {min(p['y0'],p['y1']):9.2f}..{max(p['y0'],p['y1']):9.2f}")


if __name__ == "__main__":
    main()
