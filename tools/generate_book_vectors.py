"""Reproduce stage-2 fixtures from the 1976 book, using exact rational arithmetic.

This is a deliberately limited specification oracle, not another BESM-6 emulator.
It does not import the C#/C++ ALU, emulate division recurrence, or infer undefined bits.
Run --check to verify the committed JSON without rewriting it.
"""
import argparse
from fractions import Fraction as F
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "tests/Besm6.Processor.Tests/TestData/book-vectors.json"
MASK40 = (1 << 40) - 1
MASK41 = (1 << 41) - 1
SENTINEL = 0xAB1234567890
SOURCES = {
    "format": "book/fortran-1976/01-besm6.md#section-2",
    "registers": "book/fortran-1976/01-besm6.md#section-4",
    "arithmetic": "book/fortran-1976/01-besm6.md#section-5",
    "extracodes": "book/fortran-1976/01-besm6.md#section-6",
    "constants": "book/fortran-1976/03-madlen.md#section-40",
    "table": "book/fortran-1976/05-appendices.md",
}


def scale(exponent):
    return F(2) ** exponent


def exponent_of(value):
    """Positive mantissa [1/2,1); negative [-1,-1/2), as described in §2."""
    exponent = 0
    if not value:
        return exponent
    while value / scale(exponent) >= 1 or value / scale(exponent) < -1:
        exponent += 1
    while 0 < value / scale(exponent) < F(1, 2) or -F(1, 2) <= value / scale(exponent) < 0:
        exponent -= 1
    return exponent


def pack(value, exponent=None):
    value = F(value)
    if not value:
        return 0
    if exponent is None:
        exponent = exponent_of(value)
    mantissa = value * scale(40 - exponent)
    assert mantissa.denominator == 1, "Only exactly representable inputs"
    assert -64 <= exponent <= 63
    return ((exponent + 64) << 41) | (int(mantissa) & MASK41)


def arithmetic_result(value, exponent, flags):
    """§5: exact 80-bit dyadic result, normalization, then OR rounding.

    Cases are limited to tails fitting in 80 bits; sticky bits beyond those
    and negative multiplication overflow are intentionally not specified here.
    """
    value = F(value)
    if not value:
        return 0, SENTINEL & ~MASK40
    while value / scale(exponent) >= 1 or value / scale(exponent) < -1:
        exponent += 1  # mandatory right shift, including with normalization disabled
    before = exponent
    if not flags & 1:
        exponent = exponent_of(value)
    if exponent < -64:
        return 0, SENTINEL & ~MASK40
    scaled = value * scale(80 - exponent)
    assert scaled.denominator == 1, "Do not silently truncate the oracle"
    whole = int(scaled)
    high, low = divmod(whole, 1 << 40)
    before_tail = value * scale(80 - before)
    assert before_tail.denominator == 1
    tail = int(before_tail) & MASK40
    shift = before - exponent
    carried_one = shift > 0 and tail >> max(0, 40 - shift) != 0
    if not flags & 2 and tail and not carried_one:
        high |= 1
    return (((exponent + 64) & 127) << 41) | (high & MASK41), low


def half(opcode, address=0, register=0):
    # Raw command layout; opcode literals come from appendix 1, not the assembler.
    assert address < (32768 if opcode >= 128 else 4096)
    return (register << 20) | (opcode << 12) | address


def word(left, right=0xD8000):
    return f"{(left << 24) | right:012X}"


def vector(name, opcode, initial_a=0x123456789ABC, operand=0, initial_r=38, address=1024, register=0,
           sources=("table",), **expected):
    indices = [0] * 16
    indices[3], indices[15] = 7, 16384
    result = dict(a=f"{initial_a:012X}", y=f"{SENTINEL:012X}", r=initial_r, m=indices.copy(),
                  k=8, right=True, c=0, applyC=False, stop=False, error=None, ticks=1)
    result.update(expected)
    if "expected_r" in result:
        result["r"] = result.pop("expected_r")
    writes = result.pop("writes", [])
    return dict(id=name, sources=[SOURCES[s] for s in sources],
                bookDefined=["a", "y", "r", "m", "k"], differences=[],
                initial=dict(a=f"{initial_a:012X}", y=f"{SENTINEL:012X}", r=initial_r, m=indices, k=8),
                program=[word(half(opcode, address, register))], steps=1,
                memory=[dict(address=1024, word=f"{operand:012X}")], writes=writes, expected=result)


def build():
    vectors = []
    # Literal printed octal values: pp.201-202, visually checked PDF pp.203-204.
    constants = [("LOG-4", "0000000000000004", "4"),
                 ("INT-38", "6400000000000046", "38"),
                 ("INT-minus5", "6437777777777773", "-5"),
                 ("REAL-1", "4050000000000000", "1"),
                 ("REAL-20", "4252000000000000", "20"),
                 ("REAL-minus1", "4020000000000000", "-1")]
    for name, octal, value in constants:
        if name.startswith("REAL"):
            assert pack(F(value)) == int(octal, 8), "Printed constant disagrees with the rational oracle"
        elif name.startswith("INT"):
            assert pack(F(value), 40) == int(octal, 8)
    assert arithmetic_result(F(1, 2) + scale(-41), 0, 0) == (0x808000000001, 0x8000000000)
    assert arithmetic_result(F(1, 2) + scale(-41), 0, 2) == (0x808000000000, 0x8000000000)
    for flags in range(4):
        for name, opcode, x, y, answer in [
            ("add", 4, F(9), F(6), F(15)),
            ("subtract", 5, F(9), F(6), F(3)),
            ("reverse-subtract", 6, F(9), F(6), F(-3)),
            ("modulus-subtract", 7, F(-6), F(-9), F(-3)),
            ("multiply", 15, F(9), F(6), F(54)),
            ("multiply-negative", 15, F(-9), F(6), F(-54)),
            ("round-even-lsb", 4, F(1, 2), scale(-41), F(1, 2) + scale(-41)),
            ("round-odd-lsb", 4, F(1, 2) + scale(-40), scale(-41), F(1, 2) + 3 * scale(-41)),
            ("normalize-tail", 5, F(1, 2) + scale(-40), F(1, 2) - scale(-41), 3 * scale(-41)),
            ("multiply-tail", 15, F(1, 2) + scale(-40), F(1, 2) + scale(-40),
             (F(1, 2) + scale(-40)) ** 2),
        ]:
            exponent = (exponent_of(x) + exponent_of(y) if opcode == 15
                        else max(exponent_of(x), exponent_of(y)))
            a, low = arithmetic_result(answer, exponent, flags)
            v = vector(f"{name}-flags{flags}", opcode, pack(x), pack(y), 36 | flags,
                       sources=("format", "arithmetic", "table"),
                       a=f"{a:012X}", y=f"{low:012X}", expected_r=(32 | flags | (8 if opcode == 15 else 16)))
            vectors.append(v)
    # Exact division is specified; inexact quotient recurrence and Y are not.
    for flags in (0, 2):
        for x, y in ((9, 6), (-9, 6), (9, -6), (-9, -6), (1, 2), (0, 6)):
            result = pack(F(x, y))
            v = vector(f"divide-{x}-by-{y}-flags{flags}", 14, pack(x), pack(y), 36 | flags,
                       sources=("format", "arithmetic", "table"),
                       a=f"{result:012X}", y=f"{0 if x else SENTINEL & ~MASK40:012X}", expected_r=40 | flags)
            v["bookDefined"].remove("y")
            vectors.append(v)
    for name, opcode, operand, answer, group in [
        ("load", 8, 0xFEDCBA987654, 0xFEDCBA987654, 4),
        ("and", 9, 0x0000FFFF0000, 0x000056780000, 4),
        ("xor", 10, 0x0000FFFF0000, 0x1234A9879ABC, 4),
        ("or", 13, 0x0000FFFF0000, 0x1234FFFF9ABC, 4),
        ("cyclic-carry", 11, 1, 1, 8),
    ]:
        a = 0xFFFFFFFFFFFF if opcode == 11 else 0x123456789ABC
        y = SENTINEL if opcode == 8 else a if opcode == 10 else 0
        vectors.append(vector(name, opcode, a, operand, 51, a=f"{answer:012X}", y=f"{y:012X}", expected_r=35 | group))
    vectors.append(vector("store-preserves-registers", 0, writes=[dict(address=1024, word="123456789ABC")]))
    for sign in (1, -1):
        vectors.append(vector(f"change-sign-{sign}", 12, pack(9), pack(sign), 38,
                              sources=("format", "arithmetic", "table"),
                              a=f"{pack(9 * sign):012X}", y="000000000000", expected_r=50))
    v = vector("accumulator-to-index-low15", 32, address=3, sources=("registers", "table"))
    v["expected"]["m"][3] = 0x1ABC
    vectors.append(v)
    vectors.append(vector("index-to-accumulator", 34, address=3, initial_r=51,
                          sources=("registers", "table"), a="000000000007", expected_r=39))
    for opcode, name, target, answer in ((36, "index-copy", 2, 7), (37, "index-add", 15, 16391)):
        v = vector(name, opcode, address=target, register=3, sources=("registers", "table"))
        v["expected"]["m"][target] = answer
        vectors.append(v)
    for r in (0, 1, 2, 3, 6, 19, 32, 63):
        vectors.append(vector(f"ntr-{r}", 31, address=r, expected_r=r))
    for name, opcode in (("utc", 144), ("wtc", 152)):
        vectors.append(vector(f"{name}-sets-c", opcode, operand=3,
                              address=3 if opcode == 144 else 1024,
                              sources=("registers", "table"), c=3, applyC=True))
        v = vector(f"{name}-one-command", opcode, operand=3, address=3 if opcode == 144 else 1024,
                   sources=("registers", "table"), k=9, right=False, c=3, applyC=False)
        v["program"] = [word(half(opcode, 3 if opcode == 144 else 1024), half(160, 5, 2))]
        v["steps"] = 2
        v["expected"]["ticks"] = 2
        v["expected"]["m"][2] = 8
        vectors.append(v)
    v = vector("utc-address-wrap", 144, address=32767, sources=("registers", "table"),
               k=9, right=False, c=32767, applyC=False, ticks=2)
    v["program"] = [word(half(144, 32767), half(160, 5, 2))]
    v["steps"], v["expected"]["m"][2] = 2, 4
    vectors.append(v)
    v = vector("index-address-wrap", 168, register=3, address=32767, sources=("registers", "table"))
    v["expected"]["m"][3] = 6
    vectors.append(v)
    for group, a, condition in ((4, 0, False), (4, 1, True), (8, pack(F(1, 2)), False),
                               (8, pack(F(1, 4)), True), (16, pack(1), False), (16, pack(-1), True)):
        for opcode in (176, 184):
            branch = condition if opcode == 184 else not condition
            v = vector(f"branch-{opcode}-group{group}-a{a:X}", opcode, a, initial_r=35 | group,
                       address=16, sources=("registers", "table"), y=f"{a:012X}",
                       k=16 if branch else 8, right=not branch)
            if a:
                v["differences"] = [dict(field="y", book="000000000000", issue="BOOK-Y-BRANCH")]
            vectors.append(v)
    v = vector("rte-preservation-discrepancy", 24, initial_r=51, address=63,
               a=f"{51 << 41:012X}", expected_r=39)
    v["differences"] = [dict(field="r", book=51, issue="BOOK-R-RTE")]
    v["bookDefined"].remove("a")  # appendix does not give the bit placement of the returned R
    vectors.append(v)
    for side in ("left", "right"):
        v = vector(f"extracode-{side}-preserves-other-registers", 40, initial_r=51, address=17,
                   sources=("extracodes",), k=9, right=False, expected_r=39)
        v["expected"]["m"][14] = 17
        v["differences"] = [dict(field="r", book=51, issue="BOOK-R-EXTRACODE")]
        v["bookDefined"] = ["r", "m", "k"]  # A/Y depend on the chosen service
        if side == "right":
            v["program"] = [word(half(160, 7, 3), half(40, 17))]
            v["steps"], v["expected"]["ticks"] = 2, 2
        v["dispatch"] = True  # no-op service isolates CPU dispatch from monitor policy
        vectors.append(v)
    for operand in (0, pack(F(1, 4), 0)):
        v = vector(f"division-avost-{operand:X}", 14, pack(9), operand, initial_r=6,
                   sources=("arithmetic",), ticks=0, error="Division by zero")
        v["bookDefined"] = ["error"]  # fault register ordering is a Dubna contract
        vectors.append(v)
    v = vector("overflow-after-normalization", 15, pack(scale(62)), pack(4), initial_r=6,
               sources=("arithmetic",), a="028000000000", y="000000000000",
               ticks=0, error="Arithmetic overflow")
    # Wrapped exponent 65 -> machine exponent 1, mantissa 1/2.
    v["bookDefined"] = ["error"]
    vectors.append(v)
    for opcode, name, x, y in ((15, "zero-multiply", 9, 0), (5, "zero-subtract", 9, 9),
                              (15, "underflow", scale(-65), F(1, 2))):
        v = vector(name, opcode, pack(x), pack(y), initial_r=6, sources=("arithmetic", "table"),
                   a="000000000000", y=f"{SENTINEL & ~MASK40:012X}", expected_r=18 if opcode == 5 else 10)
        v["bookDefined"].remove("y")  # upper 8 bits of Y are not specified by the 80-bit-result rule
        vectors.append(v)
    # STOP is a compatibility extension: the book says opcode 33 is unused.
    v = vector("dubna-stop", 216, address=0, stop=True, sources=("table",))
    v["bookDefined"] = []
    vectors.append(v)
    return dict(schemaVersion=1, scope="Dubna compatibility with independently book-derived defined bits",
                sources=SOURCES, constants=[dict(id=n, octal=o, value=x) for n, o, x in constants], vectors=vectors)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    content = json.dumps(build(), indent=2, ensure_ascii=False) + "\n"
    if args.check:
        if DEST.read_text(encoding="utf-8") != content:
            raise SystemExit("Book vector fixture differs; review the independent derivation")
        print(f"Verified {len(build()['vectors'])} instruction vectors and 6 printed constants")
    else:
        DEST.parent.mkdir(parents=True, exist_ok=True)
        DEST.write_text(content, encoding="utf-8", newline="\n")
        print(DEST)


if __name__ == "__main__":
    main()
