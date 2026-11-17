#!/usr/bin/env python3
"""Run one AI reference algorithm in a fresh Python process.

Used by :mod:`benchmark_ai_python` to measure the real end-to-end process time
(interpreter start-up + import + algorithm + output) instead of composing it
from a separately measured start-up constant.
"""
from __future__ import annotations

import json
import sys

from benchmark_ai import ALGORITHMS


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: run_ai_one.py <algorithm-id>", file=sys.stderr)
        return 2
    wanted = sys.argv[1]
    for name, func, _ in ALGORITHMS:
        if name == wanted:
            print(json.dumps(func(), ensure_ascii=False, sort_keys=True))
            return 0
    print(f"unknown algorithm: {wanted}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
