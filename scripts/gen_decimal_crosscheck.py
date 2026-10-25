"""Writes tests/Fixtures/Stdlib/decimal_crosscheck_api.rf: D32, D64, D128, and Decimal results checked against
Python's decimal module.

Every expected value is computed here exactly (or at 1000 digits for a quotient that doesn't end) and rounded once to
the type: IEEE 754 decimal32, decimal64, and decimal128 round a tie to the even one, and Decimal rounds half up, to
floor(x + 0.5) (a positive tie away from zero, a negative one toward it). The fixture computes each operation with the
library, reads the expected text back, and prints a line for every value that differs, then one count per type and
operation. Random operands (a fixed seed) and hand-picked ties.

Run from the RazorForge folder: python3 scripts/gen_decimal_crosscheck.py
"""

import os
import random
from decimal import (Decimal, Context, ROUND_HALF_EVEN, ROUND_HALF_UP, ROUND_HALF_DOWN, ROUND_FLOOR, ROUND_CEILING,
                     ROUND_DOWN, localcontext, setcontext)

# DECIMAL_CROSSCHECK_COUNT and DECIMAL_CROSSCHECK_OUT write a larger run elsewhere (the fixture keeps 12 per operation).
OUT = os.environ.get("DECIMAL_CROSSCHECK_OUT", "tests/Fixtures/Stdlib/decimal_crosscheck_api.rf")
EXACT = Context(prec=1000, Emax=10 ** 8, Emin=-10 ** 8)
FORMATS = {
    "D32": (7, 96, -95),
    "D64": (16, 384, -383),
    "D128": (34, 6144, -6143),
    "Decimal": (34, 6144, -6143),
}
RULES = ["HALF_UP", "HALF_EVEN", "HALF_AWAY", "FLOOR", "CEIL", "TRUNC"]
COUNT = int(os.environ.get("DECIMAL_CROSSCHECK_COUNT", "12"))

rng = random.Random(925)
# Every negation and sum written in this script is exact.
setcontext(Context(prec=1000, Emax=10 ** 8, Emin=-10 ** 8, traps=[]))


def ctx(t, rounding=ROUND_HALF_EVEN):
    p, emax, emin = FORMATS[t]
    return Context(prec=p, Emax=emax, Emin=emin, rounding=rounding, clamp=1, traps=[])


def round_to(t, v):
    """v rounded once to type t."""
    if t == "Decimal":
        mode = ROUND_HALF_UP if v >= 0 else ROUND_HALF_DOWN
        return ctx(t, mode).plus(v)
    return ctx(t).plus(v)


def text(v):
    return str(v)


def rand_value(t, spread=6):
    p = FORMATS[t][0]
    digits = rng.randint(1, p)
    coeff = rng.randint(10 ** (digits - 1), 10 ** digits - 1)
    exp = rng.randint(-spread - digits, spread)
    sign = rng.choice([1, 1, -1])
    return Decimal(sign * coeff).scaleb(exp)


def rand_small(t):
    """An operand whose sums and products stay exact in 34 digits (for Decimal's + - *)."""
    digits = rng.randint(1, 15)
    coeff = rng.randint(10 ** (digits - 1), 10 ** digits - 1)
    exp = rng.randint(-8, 4)
    return Decimal(rng.choice([1, -1]) * coeff).scaleb(exp)


def mode_of(rule, v):
    return {
        "HALF_UP": ROUND_HALF_UP if v >= 0 else ROUND_HALF_DOWN,
        "HALF_EVEN": ROUND_HALF_EVEN,
        "HALF_AWAY": ROUND_HALF_UP,
        "FLOOR": ROUND_FLOOR,
        "CEIL": ROUND_CEILING,
        "TRUNC": ROUND_DOWN,
    }[rule]


def exact_pow(x, n):
    if n >= 0:
        return EXACT.power(x, n)
    return EXACT.divide(Decimal(1), EXACT.power(x, -n))


# ── the rows ─────────────────────────────────────────────────────────────────────────────────────────────────────────

def binary_rows(t, op):
    rows = []
    small = t == "Decimal" and op in ("add", "sub", "mul")
    for _ in range(COUNT):
        a = rand_small(t) if small else rand_value(t)
        b = rand_small(t) if small else rand_value(t)
        if op == "div" and b == 0:
            continue
        a, b = round_to(t, a), round_to(t, b)
        exact = {
            "add": lambda: EXACT.add(a, b),
            "sub": lambda: EXACT.subtract(a, b),
            "mul": lambda: EXACT.multiply(a, b),
            "div": lambda: EXACT.divide(a, b),
            "midpoint": lambda: EXACT.divide(EXACT.add(a, b), Decimal(2)),
        }[op]()
        rows.append((a, b, round_to(t, exact)))
    p = FORMATS[t][0]
    big = Decimal(10 ** (p - 1))
    if op == "add" and t != "Decimal":
        # x + 0.5 and x + 1.5 at the last digit: ties
        for c in (big + 2, big + 3, -(big + 2)):
            for h in (Decimal("0.5"), Decimal("1.5")):
                rows.append((c, h, round_to(t, EXACT.add(c, h))))
    if op == "div":
        # an odd p-digit coefficient over 2 (a tie), and over -2
        odd = Decimal(10 ** p - 1)
        for b in (Decimal(2), Decimal(-2)):
            rows.append((odd, b, round_to(t, EXACT.divide(odd, b))))
        rows.append((Decimal(1), Decimal(3), round_to(t, EXACT.divide(Decimal(1), Decimal(3)))))
    if op == "midpoint":
        # neighbours: the midpoint is a tie; the largest value with itself: the sum alone would overflow
        for a, b in ((big + 1, big + 2), (big + 2, big + 3), (-(big + 1), -(big + 2))):
            rows.append((a, b, round_to(t, EXACT.divide(EXACT.add(a, b), Decimal(2)))))
        top = Decimal((0, (9,) * p, FORMATS[t][1] - p + 1))
        rows.append((top, top, top))
        rows.append((top, -top, Decimal(0)))
    return rows


def powi_rows(t):
    rows = []
    for _ in range(COUNT):
        x = round_to(t, Decimal(rng.randint(1, 10 ** rng.randint(1, 4))).scaleb(-rng.randint(0, 3)))
        if rng.random() < 0.3:
            x = -x
        n = rng.randint(-40, 40)
        if x == 0 and n < 0:
            continue
        rows.append((x, n, round_to(t, exact_pow(x, n))))
    # ties: 1.5^29 and (-1.5)^29 have 35 digits ending in 5 (D128 and Decimal), 2^-11 has 8 digits after the
    # point ending in 5 (D32), 1.5^35 for D64 is not a tie but checks the even rounding of a long power
    for x, n in ((Decimal("1.5"), 29), (Decimal("-1.5"), 29), (Decimal(2), -11), (Decimal(2), -49), (Decimal(2), -50),
                 (Decimal(5), -24), (Decimal("1.1"), 30), (Decimal("1.0001"), 1000), (Decimal(10), 20), (Decimal(7), 0)):
        rows.append((x, n, round_to(t, exact_pow(x, n))))
    return rows


def unary_rows(t, op):
    rows = []
    for _ in range(COUNT):
        x = round_to(t, rand_value(t))
        if op == "sqrt":
            x = abs(x)
        if op == "recip" and x == 0:
            continue
        with localcontext(EXACT):
            exact = {"sqrt": lambda: EXACT.sqrt(x), "recip": lambda: EXACT.divide(Decimal(1), x)}[op]()
        rows.append((x, round_to(t, exact)))
    if op == "recip":
        # 1 / 2^k with p + 1 digits ending in 5: ties
        for k in (11, 24, 49, 50):
            x = round_to(t, Decimal(2 ** k))
            rows.append((x, round_to(t, EXACT.divide(Decimal(1), x))))
    if op == "sqrt":
        for x in (Decimal(2), Decimal("0.01"), Decimal(10 ** 6)):
            rows.append((x, round_to(t, EXACT.sqrt(x))))
    return rows


def fma_rows(t):
    rows = []
    for _ in range(COUNT):
        a, b, c = (round_to(t, rand_value(t)) for _ in range(3))
        rows.append((a, b, c, round_to(t, EXACT.fma(a, b, c))))
    p = FORMATS[t][0]
    big = Decimal(10 ** (p - 1))
    # (big + 1) * 1 + 0.5 is exactly halfway
    rows.append((big + 1, Decimal(1), Decimal("0.5"), round_to(t, EXACT.add(big + 1, Decimal("0.5")))))
    rows.append((-(big + 1), Decimal(1), Decimal("-0.5"), round_to(t, EXACT.subtract(-(big + 1), Decimal("0.5")))))
    return rows


def round_rows(t):
    rows = []
    for i in range(COUNT * 2):
        x = round_to(t, rand_value(t, spread=3))
        places = rng.randint(0, 4)
        rule = RULES[i % len(RULES)]
        rows.append((x, places, rule))
    for x in ("2.5", "-2.5", "2.675", "-7.125", "0.5", "-0.5", "1.005"):
        for rule in RULES:
            rows.append((Decimal(x), 0 if x in ("2.5", "-2.5", "0.5", "-0.5") else 2, rule))
    out = []
    for x, places, rule in rows:
        q = Decimal(1).scaleb(-places)
        if x.as_tuple().exponent >= -places:
            want = x
        else:
            want = x.quantize(q, rounding=mode_of(rule, x), context=EXACT)
        if want == 0 and (t == "Decimal" or rule == "HALF_UP"):
            # Decimal has one zero, and half up is floor(x + 0.5), whose zero is +0
            want = Decimal(0)
        out.append((x, places, rule, want))
    return out


def to_int_rows(t):
    rows = []
    for _ in range(COUNT):
        x = round_to(t, rand_value(t, spread=20))
        if abs(x) >= 2 ** 127:
            continue
        rows.append((x, int(x)))
    return rows


def from_int_rows(t):
    rows = []
    for _ in range(COUNT):
        n = rng.randint(-(2 ** 127) + 1, 2 ** 127 - 1) >> rng.randint(0, 120)
        rows.append((n, round_to(t, Decimal(n))))
    p = FORMATS[t][0]
    # a tie at the last digit: p digits and a 5 past them
    n = (10 ** (p - 1) + 1) * 10 + 5
    rows.append((n, round_to(t, Decimal(n))))
    rows.append((-n, round_to(t, Decimal(-n))))
    return rows


# ── the fixture ──────────────────────────────────────────────────────────────────────────────────────────────────────

def lit(items):
    return "[" + ", ".join(f'"{text(v)}"' for v in items) + "]"


def ilist(items):
    return "[" + ", ".join(str(v) for v in items) + "]"


def main():
    out = []
    o = out.append
    o("module Tests/Stdlib/DecimalCrosscheckApi")
    o("")
    o("import IO/Console")
    o("")
    o("# D32, D64, D128, and Decimal against Python's decimal module: every expected value was computed exactly and rounded")
    o("# once (a tie to the even one for D32, D64, and D128, half up for Decimal), by scripts/gen_decimal_crosscheck.py,")
    o("# which writes this file. A line names each value that differs, then each operation prints how many it checked.")
    o("")
    for t in FORMATS:
        o(f"### Whether two {t} values are the same: one value with one sign (or both NaN).")
        o(f"routine same_{t.lower()}(a: {t}, b: {t}) -> Bool")
        if t == "Decimal":
            o("    return a.to_bits() == b.to_bits()")
        else:
            o("    if a.is_nan()")
            o("        return b.is_nan()")
            o("    return a == b and a.is_sign_negative() == b.is_sign_negative()")
        o("")
        o(f"### Checks a two-operand operation of {t}, printing each value that differs, and returns how many it checked.")
        o(f"routine check_{t.lower()}_binary(op: Text, a: List[Text], b: List[Text], want: List[Text]) -> U64")
        o("    var i: U64 = 0")
        o("    until i >= a.count()")
        o(f"        var x = a[i].to[{t}]()")
        o(f"        var y = b[i].to[{t}]()")
        o(f"        var got: {t} = x")
        o("        if op == \"add\"")
        o("            got = x + y")
        o("        elseif op == \"sub\"")
        o("            got = x - y")
        o("        elseif op == \"mul\"")
        o("            got = x * y")
        o("        elseif op == \"div\"")
        o("            got = x / y")
        o("        else")
        o("            got = x.midpoint(you: y)")
        o(f"        if not same_{t.lower()}(a: got, b: want[i].to[{t}]())")
        o(f'            show(f"{t} {{op}} {{a[i]}} {{b[i]}}: {{got}}, want {{want[i]}}")')
        o("        i += 1")
        o("    return i")
        o("")
        o(f"### Checks a one-operand operation of {t} (sqrt or recip).")
        o(f"routine check_{t.lower()}_unary(op: Text, a: List[Text], want: List[Text]) -> U64")
        o("    var i: U64 = 0")
        o("    until i >= a.count()")
        o(f"        var x = a[i].to[{t}]()")
        o(f"        var got: {t} = x.recip()")
        o("        if op == \"sqrt\"")
        o("            got = x.sqrt()")
        o(f"        if not same_{t.lower()}(a: got, b: want[i].to[{t}]())")
        o(f'            show(f"{t} {{op}} {{a[i]}}: {{got}}, want {{want[i]}}")')
        o("        i += 1")
        o("    return i")
        o("")
        o(f"### Checks {t}.powi.")
        o(f"routine check_{t.lower()}_powi(a: List[Text], n: List[S32], want: List[Text]) -> U64")
        o("    var i: U64 = 0")
        o("    until i >= a.count()")
        o(f"        var got = a[i].to[{t}]().powi(n: n[i])")
        o(f"        if not same_{t.lower()}(a: got, b: want[i].to[{t}]())")
        o(f'            show(f"{t} powi {{a[i]}} {{n[i]}}: {{got}}, want {{want[i]}}")')
        o("        i += 1")
        o("    return i")
        o("")
        o(f"### Checks {t}.mul_add (a * b + c rounded once).")
        o(f"routine check_{t.lower()}_fma(a: List[Text], b: List[Text], c: List[Text], want: List[Text]) -> U64")
        o("    var i: U64 = 0")
        o("    until i >= a.count()")
        o(f"        var got = a[i].to[{t}]().mul_add(b: b[i].to[{t}](), c: c[i].to[{t}]())")
        o(f"        if not same_{t.lower()}(a: got, b: want[i].to[{t}]())")
        o(f'            show(f"{t} fma {{a[i]}} {{b[i]}} {{c[i]}}: {{got}}, want {{want[i]}}")')
        o("        i += 1")
        o("    return i")
        o("")
        o(f"### Checks {t}.round(places:, rule:), the rule given by its place in RoundingRule's list.")
        o(f"routine check_{t.lower()}_round(a: List[Text], places: List[U64], rules: List[U64], want: List[Text]) -> U64")
        o("    var all: List[RoundingRule] = [RoundingRule.HALF_UP, RoundingRule.HALF_EVEN, RoundingRule.HALF_AWAY,")
        o("        RoundingRule.FLOOR, RoundingRule.CEIL, RoundingRule.TRUNC]")
        o("    var i: U64 = 0")
        o("    until i >= a.count()")
        o(f"        var got = a[i].to[{t}]().round(places: places[i], rule: all[rules[i]])")
        o(f"        if not same_{t.lower()}(a: got, b: want[i].to[{t}]())")
        o(f'            show(f"{t} round {{a[i]}} {{places[i]}} {{rules[i]}}: {{got}}, want {{want[i]}}")')
        o("        i += 1")
        o("    return i")
        o("")
        o(f"### Checks S128 to {t}, rounded once.")
        o(f"routine check_{t.lower()}_from_s128(n: List[Text], want: List[Text]) -> U64")
        o("    var i: U64 = 0")
        o("    until i >= n.count()")
        o(f"        var got = n[i].to[S128]().to[{t}]()")
        o(f"        if not same_{t.lower()}(a: got, b: want[i].to[{t}]())")
        o(f'            show(f"{t} from S128 {{n[i]}}: {{got}}, want {{want[i]}}")')
        o("        i += 1")
        o("    return i")
        o("")
        if t != "Decimal":
            o(f"### Checks {t} to S128 (toward zero).")
            o(f"routine check_{t.lower()}_to_s128(a: List[Text], want: List[Text]) -> U64")
            o("    var i: U64 = 0")
            o("    until i >= a.count()")
            o(f"        var got = a[i].to[{t}]().to[S128]()")
            o("        if got != want[i].to[S128]()")
            o(f'            show(f"{t} to S128 {{a[i]}}: {{got}}, want {{want[i]}}")')
            o("        i += 1")
            o("    return i")
            o("")
    o("routine start()")
    o("    var n: U64 = 0")
    for t in FORMATS:
        lt = t.lower()
        for op in ("add", "sub", "mul", "div", "midpoint"):
            rows = binary_rows(t, op)
            o(f"    n = check_{lt}_binary(op: \"{op}\", a: {lit(r[0] for r in rows)},")
            o(f"        b: {lit(r[1] for r in rows)},")
            o(f"        want: {lit(r[2] for r in rows)})")
            o(f"    show(f\"{t} {op}: {{n}} checked\")")
        for op in ("sqrt", "recip"):
            rows = unary_rows(t, op)
            o(f"    n = check_{lt}_unary(op: \"{op}\", a: {lit(r[0] for r in rows)},")
            o(f"        want: {lit(r[1] for r in rows)})")
            o(f"    show(f\"{t} {op}: {{n}} checked\")")
        rows = powi_rows(t)
        o(f"    n = check_{lt}_powi(a: {lit(r[0] for r in rows)}, n: {ilist(r[1] for r in rows)},")
        o(f"        want: {lit(r[2] for r in rows)})")
        o(f"    show(f\"{t} powi: {{n}} checked\")")
        rows = fma_rows(t)
        o(f"    n = check_{lt}_fma(a: {lit(r[0] for r in rows)}, b: {lit(r[1] for r in rows)},")
        o(f"        c: {lit(r[2] for r in rows)}, want: {lit(r[3] for r in rows)})")
        o(f"    show(f\"{t} fma: {{n}} checked\")")
        rows = round_rows(t)
        o(f"    n = check_{lt}_round(a: {lit(r[0] for r in rows)}, places: {ilist(r[1] for r in rows)},")
        o(f"        rules: {ilist(RULES.index(r[2]) for r in rows)}, want: {lit(r[3] for r in rows)})")
        o(f"    show(f\"{t} round: {{n}} checked\")")
        rows = from_int_rows(t)
        o(f"    n = check_{lt}_from_s128(n: {lit(r[0] for r in rows)}, want: {lit(r[1] for r in rows)})")
        o(f"    show(f\"{t} from S128: {{n}} checked\")")
        if t != "Decimal":
            rows = to_int_rows(t)
            o(f"    n = check_{lt}_to_s128(a: {lit(r[0] for r in rows)}, want: {lit(r[1] for r in rows)})")
            o(f"    show(f\"{t} to S128: {{n}} checked\")")
    o("    return")
    open(OUT, "w").write("\n".join(out) + "\n")


main()
