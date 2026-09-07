#!/usr/bin/env python3
"""Find the first architectural divergence between canonical BESM-6 TSV traces."""

from __future__ import annotations

import argparse
import sys
from collections import deque
from pathlib import Path
from typing import Dict, Iterator, NamedTuple, Optional, TextIO, Tuple


IDENTITY_FIELDS = ("seq", "pc", "half", "raw48", "rk24", "opcode", "reg", "addr")
REGISTER_COLUMN_ALIASES = {
    "acc_b": "a_b", "rmr_b": "y_b", "rau_b": "r_b",
    "mod_b": "c_b", "amod_b": "apply_c_b",
    "acc_a": "a_a", "rmr_a": "y_a", "rau_a": "r_a",
    "mod_a": "c_a", "amod_a": "apply_c_a",
}


class TraceResult(NamedTuple):
    classification: str
    sequence: int
    pre_match: bool
    differences: Dict[str, Tuple[str, str]]
    cpp: Dict[str, str]
    cs: Dict[str, str]
    context_before: Tuple[Tuple[Dict[str, str], Dict[str, str]], ...] = ()
    context_after: Tuple[Tuple[Dict[str, str], Dict[str, str]], ...] = ()


def _rows(stream: TextIO, path: Path) -> Iterator[Dict[str, str]]:
    def make_row(header, values):
        return {
            REGISTER_COLUMN_ALIASES.get(column, column): value
            for column, value in zip(header, values)
        }

    def split_segment(line: str):
        values = line.rstrip("\r\n").split("\t")
        while values and values[-1] == "":
            values.pop()
        return values

    header_line = stream.readline()
    if not header_line:
        raise ValueError(f"empty trace: {path}")
    header = split_segment(header_line)
    legacy = "a_b" not in header and "acc_b" not in header
    if legacy:
        for _ in range(4):
            segment = stream.readline()
            if segment == "":
                raise ValueError(f"incomplete legacy TSV header in {path}")
            header.extend(split_segment(segment))
    if len(header) != len(set(header)) or any(not column for column in header):
        raise ValueError(f"invalid or duplicate TSV columns in {path}")

    line_number = 6 if legacy else 2
    while True:
        line = stream.readline()
        if line == "":
            return
        if not line.strip():
            line_number += 1
            continue
        values = split_segment(line)
        if legacy:
            for _ in range(4):
                segment = stream.readline()
                if segment == "":
                    if len(values) > len(header):
                        raise ValueError(
                            f"{path}:{line_number}: expected at most {len(header)} columns, got {len(values)}"
                        )
                    row = make_row(header, values)
                    row["__trace_incomplete"] = "1"
                    yield row
                    return
                values.extend(split_segment(segment))
        if len(values) != len(header):
            raise ValueError(
                f"{path}:{line_number}: expected {len(header)} columns, got {len(values)}"
            )
        yield make_row(header, values)
        line_number += 5 if legacy else 1


def _differences(
    cpp: Dict[str, str], cs: Dict[str, str], fields: Iterator[str] | Tuple[str, ...]
) -> Dict[str, Tuple[str, str]]:
    return {
        field: (cpp.get(field, "<missing>"), cs.get(field, "<missing>"))
        for field in fields
        if cpp.get(field) != cs.get(field)
    }


def _post_classification(fields) -> str:
    names = set(fields)
    if names and names <= {"r_a"}:
        return "R"
    if names & {"a_a", "y_a"}:
        return "A_Y"
    if names & {"c_a", "apply_c_a", "aex_a"}:
        return "C"
    if names & {"pc_a", "half_a"}:
        return "CONTROL_FLOW"
    if any(name.startswith("m") and name.endswith("_a") for name in names):
        return "REGISTER_STATE"
    return "POST_STATE"


def compare_traces(cpp_path: Path | str, cs_path: Path | str) -> TraceResult:
    cpp_path = Path(cpp_path)
    cs_path = Path(cs_path)
    with cpp_path.open("r", encoding="utf-8-sig", newline="") as cpp_stream, cs_path.open(
        "r", encoding="utf-8-sig", newline=""
    ) as cs_stream:
        cpp_rows = _rows(cpp_stream, cpp_path)
        cs_rows = _rows(cs_stream, cs_path)
        previous = deque(maxlen=5)

        def divergence(classification, sequence, pre_match, differences, cpp, cs):
            following = []
            for _ in range(5):
                next_cpp = next(cpp_rows, None)
                next_cs = next(cs_rows, None)
                if next_cpp is None or next_cs is None:
                    break
                following.append((next_cpp, next_cs))
            return TraceResult(
                classification, sequence, pre_match, differences, cpp, cs,
                tuple(previous), tuple(following),
            )

        index = 0
        while True:
            cpp = next(cpp_rows, None)
            cs = next(cs_rows, None)
            if cpp is None or cs is None:
                if cpp is None and cs is None:
                    return TraceResult("MATCH", index, True, {}, {}, {})
                present = cpp if cpp is not None else cs
                return TraceResult(
                    "LENGTH", index, False,
                    {"row": ("<eof>" if cpp is None else "present", "<eof>" if cs is None else "present")},
                    cpp or {}, cs or {},
                )

            sequence = int(cpp.get("seq", index))
            identity_diff = _differences(cpp, cs, IDENTITY_FIELDS)
            if identity_diff:
                classification = (
                    "CONTROL_FLOW"
                    if {"seq", "pc", "half"} & identity_diff.keys()
                    else "FETCH" if "raw48" in identity_diff
                    else "CONTROL_FLOW"
                )
                return divergence(classification, sequence, False, identity_diff, cpp, cs)

            pre_fields = tuple(field for field in cpp if field.endswith("_b"))
            pre_diff = _differences(cpp, cs, pre_fields)
            if pre_diff:
                return divergence("PRE_STATE", sequence, False, pre_diff, cpp, cs)

            if "__trace_incomplete" in cpp or "__trace_incomplete" in cs:
                trace_diff = {
                    "trace": (
                        "incomplete" if "__trace_incomplete" in cpp else "complete",
                        "incomplete" if "__trace_incomplete" in cs else "complete",
                    )
                }
                return divergence(
                    "TRACE_TRUNCATED", sequence, True, trace_diff, cpp, cs
                )

            post_fields = tuple(field for field in cpp if field.endswith("_a"))
            post_diff = _differences(cpp, cs, post_fields)
            if post_diff:
                return divergence(
                    _post_classification(post_diff), sequence, True, post_diff, cpp, cs
                )
            previous.append((cpp, cs))
            index += 1


def _octal(value: Optional[str]) -> str:
    try:
        return format(int(value or "0"), "05o")
    except ValueError:
        return "?"


def format_report(result: TraceResult) -> str:
    cpp = result.cpp
    lines = [
        "=" * 52,
        "FIRST BESM-6 DIVERGENCE" if result.classification != "MATCH" else "BESM-6 TRACES MATCH",
        "=" * 52,
        f"Classification: {result.classification}",
        f"Sequence: {result.sequence}",
    ]
    if cpp:
        lines.extend(
            [
                f"K decimal: {cpp.get('pc', '?')}",
                f"K octal: {_octal(cpp.get('pc'))}",
                f"Half: {cpp.get('half', '?')}",
                f"Raw48: {cpp.get('raw48', '?')}",
                f"RK: {cpp.get('rk24', '?')}",
                f"Opcode/reg/addr: {cpp.get('opcode', '?')}/{cpp.get('reg', '?')}/{cpp.get('addr', '?')}",
                f"PRE STATE MATCH: {'YES' if result.pre_match else 'NO'}",
            ]
        )
    if result.differences:
        lines.append("Differing fields:")
        for field, (cpp_value, cs_value) in result.differences.items():
            lines.append(f"  {field}: C++={cpp_value} C#={cs_value}")
    if result.context_before or result.context_after:
        def compact(label, pair):
            left, right = pair
            marker = "=" if all(left.get(field) == right.get(field) for field in IDENTITY_FIELDS) else "!"
            return (
                f"  {label} {marker} seq={left.get('seq', '?')} k={left.get('pc', '?')}"
                f"/{_octal(left.get('pc'))} {left.get('half', '?')} rk={left.get('rk24', '?')}"
            )

        lines.append("Context before:")
        for pair in result.context_before:
            lines.append(compact(" ", pair))
        lines.append(compact("*", (result.cpp, result.cs)))
        lines.append("Context after:")
        for pair in result.context_after:
            lines.append(compact(" ", pair))
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("cpp_trace", type=Path)
    parser.add_argument("cs_trace", type=Path)
    args = parser.parse_args(argv)
    try:
        result = compare_traces(args.cpp_trace, args.cs_trace)
    except (OSError, ValueError) as error:
        print(f"trace-diff error: {error}", file=sys.stderr)
        return 2
    print(format_report(result))
    return 0 if result.classification == "MATCH" else 1


if __name__ == "__main__":
    raise SystemExit(main())
