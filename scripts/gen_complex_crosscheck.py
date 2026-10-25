"""Writes tests/Fixtures/Stdlib/complex_crosscheck_api.rf: C64, C128, and C256 results checked against exact rational
arithmetic and a high-precision reference made with Python's decimal module.

The sums, differences, products, quotients, reciprocals, squared moduli, moduli, scalings, and narrowings are rounded
once, so each expected value is the exact result (fractions.Fraction, and an integer square root for the modulus)
rounded once to the part type, a tie to the even one: the fixture checks them bit for bit, signed zeros included. The
elementary functions are not rounded once: each expected value is the function at 120 (B32 and B64 parts) or 160 (B128
parts) significant digits, and the fixture checks that the result is within a bound of it, the error measured
normwise in units in the last place of the larger part. Random operands from a fixed seed, and hand-picked cases:
products that cancel, quotients on and next to a tie, signed zeros, values near the underflow and overflow ends, and
points on both sides of every branch cut. The text cases read back what represent writes.

Run from the RazorForge folder: python3 scripts/gen_complex_crosscheck.py

COMPLEX_CROSSCHECK_COUNT sets how many random cases each operation gets (the fixture keeps 2), and
COMPLEX_CROSSCHECK_OUT writes the program elsewhere. With --dump the program prints every result's bits instead of
checking them, for comparing two builds of the library.
"""

import cmath
import math
import os
import random
import sys
from decimal import Decimal, localcontext
from fractions import Fraction

OUT = os.environ.get("COMPLEX_CROSSCHECK_OUT", "tests/Fixtures/Stdlib/complex_crosscheck_api.rf")
COUNT = int(os.environ.get("COMPLEX_CROSSCHECK_COUNT", "2"))
DUMP = "--dump" in sys.argv

# precision (with the hidden bit), exponent bits, width
FORMATS = {"B32": (24, 8, 32), "B64": (53, 11, 64), "B128": (113, 15, 128)}
TYPES = [("C64", "B32", "U32"), ("C128", "B64", "U64"), ("C256", "B128", "U128")]
# the bound, in units in the last place of the larger part, for each elementary function
BOUND = {"B32": 8, "B64": 8, "B128": 8}

rng = random.Random(98)


def fmt_info(f):
    p, eb, w = FORMATS[f]
    bias = (1 << (eb - 1)) - 1
    return p, eb, w, bias, 1 - bias, bias


# ── bits and values ──────────────────────────────────────────────────────────────


def decode(bits, f):
    """('num', negative, Fraction) or ('inf', negative) or ('nan', negative)."""
    p, eb, w, bias, emin, emax = fmt_info(f)
    neg = (bits >> (w - 1)) & 1 == 1
    e = (bits >> (p - 1)) & ((1 << eb) - 1)
    m = bits & ((1 << (p - 1)) - 1)
    if e == (1 << eb) - 1:
        return ("nan", neg) if m else ("inf", neg)
    if e == 0:
        v = Fraction(m) * Fraction(2) ** (emin - (p - 1))
    else:
        v = Fraction(m | (1 << (p - 1))) * Fraction(2) ** (e - bias - (p - 1))
    return ("num", neg, v)


def value(bits, f):
    d = decode(bits, f)
    if d[0] != "num":
        raise ValueError("not a number")
    return -d[2] if d[1] else d[2]


def is_neg_zero(bits, f):
    d = decode(bits, f)
    return d[0] == "num" and d[2] == 0 and d[1]


def floor_log2(x):
    """floor(log2(x)) for a positive Fraction."""
    e = x.numerator.bit_length() - x.denominator.bit_length()
    if Fraction(2) ** e > x:
        e -= 1
    elif Fraction(2) ** (e + 1) <= x:
        e += 1
    return e


def encode(neg, n, q, f):
    """The bits of (-1)^neg n 2^q with n < 2^p already rounded, normalizing n."""
    p, eb, w, bias, emin, emax = fmt_info(f)
    sign = (1 << (w - 1)) if neg else 0
    if n == 0:
        return sign
    while n >= (1 << p):
        n >>= 1
        q += 1
    while n < (1 << (p - 1)) and q > emin - (p - 1):
        n <<= 1
        q -= 1
    if n < (1 << (p - 1)):
        return sign | n
    e = q + (p - 1)
    if e > emax:
        return sign | (((1 << eb) - 1) << (p - 1))
    return sign | ((e + bias) << (p - 1)) | (n - (1 << (p - 1)))


def round_value(x, f, zero_negative=False):
    """The bits of the Fraction x rounded once to f (ties to even). A zero x gives -0 when zero_negative."""
    p, eb, w, bias, emin, emax = fmt_info(f)
    if x == 0:
        return (1 << (w - 1)) if zero_negative else 0
    neg = x < 0
    a = -x if neg else x
    e = max(floor_log2(a), emin)
    q = e - (p - 1)
    scaled = a / Fraction(2) ** q
    n = scaled.numerator // scaled.denominator
    rem = scaled - n
    if rem > Fraction(1, 2) or (rem == Fraction(1, 2) and n & 1):
        n += 1
    return encode(neg, n, q, f)


def round_sqrt(x, f):
    """The bits of sqrt(x) for a Fraction x >= 0, rounded once."""
    p, eb, w, bias, emin, emax = fmt_info(f)
    if x == 0:
        return 0
    e = max(floor_log2(x) // 2, emin)
    q = e - (p - 1)
    y = x / Fraction(2) ** (2 * q)
    four = 4 * y
    t = math.isqrt(four.numerator // four.denominator)
    exact = Fraction(t * t) == four
    n, half = t >> 1, t & 1
    if half and (not exact or n & 1):
        n += 1
    return encode(False, n, q, f)


def neg_bits(bits, f):
    return bits ^ (1 << (FORMATS[f][2] - 1))


def to_float(bits, f):
    d = decode(bits, f)
    if d[0] == "nan":
        return math.nan
    if d[0] == "inf":
        return -math.inf if d[1] else math.inf
    v = float(d[2]) if d[2] < Fraction(2) ** 1024 else math.inf
    return -v if d[1] else v


# ── operands ─────────────────────────────────────────────────────────────────────


def rand_part(f, lo, hi, zero_odds=0):
    p, eb, w, bias, emin, emax = fmt_info(f)
    if zero_odds and rng.random() < zero_odds:
        return rng.choice([0, 1 << (w - 1)])
    e = rng.randint(lo, hi)
    m = rng.getrandbits(p - 1)
    sign = rng.choice([0, 1 << (w - 1)])
    return sign | ((e + bias) << (p - 1)) | m


def from_float(x, f):
    return round_value(Fraction(x), f)


def ulp_bits(bits, f):
    """The bits of the unit in the last place of a finite nonzero value, as a positive value."""
    p, eb, w, bias, emin, emax = fmt_info(f)
    e = (bits >> (p - 1)) & ((1 << eb) - 1)
    q = max(e, 1) - bias - (p - 1)
    return encode(False, 1, q, f)


def pick(f, n, lo, hi, zero_odds=0.0):
    return [(rand_part(f, lo, hi, zero_odds), rand_part(f, lo, hi, zero_odds)) for _ in range(n)]


def arith_range(f):
    """Exponents whose products and squares stay inside the range the old library allowed."""
    p, eb, w, bias, emin, emax = fmt_info(f)
    return -(emax // 2) + 4, emax // 2 - 4


def binary_operands(f, n):
    lo, hi = arith_range(f)
    p = FORMATS[f][0]
    cases = []
    cases += list(zip(pick(f, n, -8, 8, 0.1), pick(f, n, -8, 8, 0.1)))
    cases += list(zip(pick(f, n, lo, hi), pick(f, n, lo, hi)))
    # products whose real part cancels: b d is next to a c
    for _ in range(n):
        a, b, c = (rand_part(f, -4, 4) for _ in range(3))
        d = round_value(value(a, f) * value(c, f) / value(b, f), f)
        cases.append(((a, b), (c, d)))
    # quotients on a tie: (a + b i) / (1 + i) is (a + b) / 2 + (b - a) / 2 i, and a + b needs one bit more than p
    one = from_float(1.0, f)
    for _ in range(n):
        a = rand_part(f, 0, 3)
        tie = ulp_bits(a, f)
        half = round_value(value(tie, f) / 2, f)
        b = half if rng.random() < 0.5 else neg_bits(half, f)
        cases.append(((a, b), (one, one)))
        near = round_value(value(half, f) * (1 + Fraction(1, 1 << (p + 2))), f)
        cases.append(((a, near), (one, one)))
    # signed zeros
    zeros = [0, 1 << (FORMATS[f][2] - 1)]
    for zr in zeros:
        for zi in zeros:
            cases.append(((zr, zi), (from_float(2.0, f), from_float(-3.0, f))))
            cases.append(((from_float(1.5, f), from_float(-0.5, f)), (zr, from_float(1.0, f) if zi else from_float(-1.0, f))))
    # tiny parts: products and squares fall below the normal range
    p_, eb, w, bias, emin, emax = fmt_info(f)
    for _ in range(n):
        cases.append(((rand_part(f, emin // 2 - 20, emin // 2), rand_part(f, emin // 2 - 20, emin // 2)),
                      (rand_part(f, -2, 2), rand_part(f, -2, 2))))
        cases.append(((rand_part(f, -2, 2), rand_part(f, -2, 2)),
                      (rand_part(f, emin // 2 + 2, emin // 2 + 10), rand_part(f, emin // 2 + 2, emin // 2 + 10))))
    return cases


def nonzero(cases):
    return [(z, w) for z, w in cases if not (value(w[0], FMT_OF[0]) == 0 and value(w[1], FMT_OF[0]) == 0)]


FMT_OF = [None]


def unary_operands(f, n, lo=-6, hi=4):
    cases = pick(f, n, lo, hi, 0.1)
    zeros = [0, 1 << (FORMATS[f][2] - 1)]
    for zi in zeros:
        for re in (-2.0, -1.0, -0.5, 0.5, 1.0, 2.0):
            cases.append((from_float(re, f), zi))
        for im in (-2.0, -1.0, -0.5, 0.5, 1.0, 2.0):
            cases.append((zi, from_float(im, f)))
    cases.append((from_float(0.75, f), from_float(-0.25, f)))
    cases.append((from_float(-3.5, f), from_float(1.25, f)))
    return cases


# ── the operations ───────────────────────────────────────────────────────────────

# name, RazorForge call on z (and w), what the result is ("c" complex, "p" part, "b" Bool, "t" Text)
BINARY = [
    ("add", "z + w", "c"), ("sub", "z - w", "c"), ("mul", "z * w", "c"), ("div", "z / w", "c"),
    ("eq", "z == w", "b"),
]
SCALE = [("add_real", "z + w.real", "c"), ("sub_real", "z - w.real", "c"), ("mul_real", "z * w.real", "c"),
         ("div_real", "z / w.real", "c")]
EXACT_UNARY = [
    ("neg", "-z", "c"), ("conjugate", "z.conjugate()", "c"), ("norm", "z.norm()", "p"), ("abs", "z.abs()", "p"),
    ("reciprocal", "z.reciprocal()", "c"),
]
CLASS = [("is_zero", "z.is_zero()", "b"), ("is_real", "z.is_real()", "b"), ("is_imaginary", "z.is_imaginary()", "b"),
         ("is_nan", "z.is_nan()", "b"), ("is_infinite", "z.is_infinite()", "b"), ("is_finite", "z.is_finite()", "b")]
TEXT = [("represent", "z.represent()", "t"), ("fixed", "z.fixed(places: 3)", "t"), ("diagnose", "z.diagnose()", "t")]
ELEMENTARY = [
    ("arg", "z.arg()", "p"), ("sqrt", "z.sqrt_unchecked()", "c"), ("exp", "z.exp_unchecked()", "c"),
    ("ln", "z.ln_unchecked()", "c"), ("log10", "z.log10()", "c"), ("log2", "z.log2()", "c"),
    ("sin", "z.sin()", "c"), ("cos", "z.cos()", "c"), ("tan", "z.tan()", "c"),
    ("asin", "z.asin()", "c"), ("acos", "z.acos()", "c"), ("atan", "z.atan()", "c"),
    ("sinh", "z.sinh_unchecked()", "c"), ("cosh", "z.cosh_unchecked()", "c"), ("tanh", "z.tanh()", "c"),
    ("asinh", "z.asinh()", "c"), ("acosh", "z.acosh()", "c"), ("atanh", "z.atanh()", "c"),
]
POLE = {"atan": [(0, 1), (0, -1)], "atanh": [(1, 0), (-1, 0)]}


def is_zero_pair(z, f):
    return value(z[0], f) == 0 and value(z[1], f) == 0


def elementary_ok(op, z, f):
    """Whether the old library computed op at z without crashing (no pole, no zero for the logarithms)."""
    if op in ("ln", "log10", "log2") and is_zero_pair(z, f):
        return False
    for pr, pi in POLE.get(op, []):
        if value(z[0], f) == pr and value(z[1], f) == pi:
            return False
    return True


def lit(bits):
    return f"0x{bits:X}"


def rf_list(name, ty, values):
    return f"    var {name}: List[{ty}] = [{', '.join(lit(v) for v in values)}]"


def rf_part(P, expr):
    return f"{P}.from_bits(bits: {expr})"


def result_text(kind, T):
    if kind == "c":
        return "{r.real.to_bits()} {r.imag.to_bits()}"
    if kind == "p":
        return "{r.to_bits()}"
    return "{r}"


# ── the cases each type gets ─────────────────────────────────────────────────────


def special_parts(f):
    p, eb, w, bias, emin, emax = fmt_info(f)
    s = 1 << (w - 1)
    inf = ((1 << eb) - 1) << (p - 1)
    qnan = inf | (1 << (p - 2))
    snan = inf | 1
    return [0, s, inf, inf | s, qnan, qnan | s, snan, from_float(1.0, f), from_float(-2.5, f)]


def build_cases(T, P):
    f = P
    p, eb, w, bias, emin, emax = fmt_info(f)
    lo, hi = arith_range(f)
    n = COUNT
    cases = {}
    mul_cases = binary_operands(f, n)
    cases["add"] = mul_cases
    cases["sub"] = mul_cases
    cases["mul"] = mul_cases
    div_cases = [c for c in binary_operands(f, n) if not is_zero_pair(c[1], f)]
    div_cases = [(z, wv) for z, wv in div_cases
                 if max(abs(value(wv[0], f)), abs(value(wv[1], f))) >= Fraction(2) ** (-(emax // 4))]
    div_cases += list(zip(pick(f, n, lo, hi), pick(f, n, -(emax // 4), emax // 4)))
    cases["div"] = div_cases
    eq_cases = [(z, z) for z, _ in mul_cases[:n]] + mul_cases[:n]
    s = 1 << (FORMATS[f][2] - 1)
    eq_cases += [((0, 0), (s, s)), ((0, s), (s, 0)), ((special_parts(f)[4], 0), (special_parts(f)[4], 0))]
    cases["eq"] = eq_cases
    scale = [(z, wv) for z, wv in div_cases if value(wv[0], f) != 0]
    for op, _, _ in SCALE:
        cases[op] = scale
    exact = pick(f, n, -8, 8, 0.1) + pick(f, n, lo, hi) + pick(f, n, emin // 2 - 20, emin // 2 + 8)
    exact += [(from_float(3.0, f), from_float(4.0, f)), (from_float(-5.0, f), from_float(12.0, f)), (0, s), (s, 0)]
    for op, _, _ in EXACT_UNARY:
        cases[op] = exact if op != "reciprocal" else [z for z in exact if not is_zero_pair(z, f)]
    specials = special_parts(f)
    classify = [(a, b) for a in specials for b in specials]
    for op, _, _ in CLASS:
        cases[op] = classify
    text = pick(f, n, -30, 30, 0.1) + [(a, b) for a in specials[:7] for b in specials[:7]]
    for op, _, _ in TEXT:
        cases[op] = text
    elem = unary_operands(f, n)
    for op, _, _ in ELEMENTARY:
        cases[op] = [z for z in elem if elementary_ok(op, z, f)]
    cases["pow"] = [(z, wv) for z, wv in zip(pick(f, n, -3, 2), pick(f, n, -3, 1)) if not is_zero_pair(z, f)]
    cases["powi"] = [(z, rng.randint(-8, 8)) for z in pick(f, n, -2, 2)] + [((from_float(1.0, f), from_float(1.0, f)), k)
                                                                         for k in (0, 1, 2, -1, -2, 7)]
    cases["from_polar"] = [(rand_part(f, -6, 6), rand_part(f, -4, 2)) for _ in range(n)]
    return cases


def narrow_cases(f_from, f_to):
    p, eb, w, bias, emin, emax = fmt_info(f_to)
    out = pick(f_from, COUNT, -40, 40, 0.1)
    out += pick(f_from, COUNT, emin - 30, emin + 5)
    return out


TEXT_CASES = ["1.5-2i", "3", "-i", "i", "+i", "2i", "1e-3+2.5i", "  1+2i  ", "1E+5-1E-5i", "+1+2i", "-0-0i",
              "nan+nani", "inf-infi", "-inf+0i", "0.1+0.2i", "1+-2i", "abc", "", "1+", "1+xi", "2e3i", "i-1",
              "1.5e+2-3.25e-1i"]


# ── the dump program ─────────────────────────────────────────────────────────────


def dump_routine(T, P, U, op, call, kind, cases, two):
    lines = [f"routine dump_{T.lower()}_{op}()"]
    zs = [c[0] for c in cases] if two else cases
    lines.append(rf_list("zr", U, [z[0] for z in zs]))
    lines.append(rf_list("zi", U, [z[1] for z in zs]))
    if two:
        lines.append(rf_list("wr", U, [c[1][0] for c in cases]))
        lines.append(rf_list("wi", U, [c[1][1] for c in cases]))
    lines += ["    var i: U64 = 0", "    until i >= zr.count()",
              f"        var z = {T}(real: {rf_part(P, 'zr[i]')}, imag: {rf_part(P, 'zi[i]')})"]
    if two:
        lines.append(f"        var w = {T}(real: {rf_part(P, 'wr[i]')}, imag: {rf_part(P, 'wi[i]')})")
    lines += [f"        var r = {call}", f'        show(f"{T} {op} {{i}} {result_text(kind, T)}")', "        i += 1",
              "    return", ""]
    return lines


def dump_program(all_cases):
    out = ["module Tests/Scratch/ComplexDump", "", "import IO/Console", ""]
    names = []
    for T, P, U in TYPES:
        cases = all_cases[T]
        for op, call, kind in BINARY + SCALE:
            out += dump_routine(T, P, U, op, call, kind, cases[op], True)
            names.append(f"dump_{T.lower()}_{op}")
        for op, call, kind in EXACT_UNARY + CLASS + TEXT + ELEMENTARY:
            out += dump_routine(T, P, U, op, call, kind, cases[op], False)
            names.append(f"dump_{T.lower()}_{op}")
        out += dump_routine(T, P, U, "pow", "z.pow(you: w)", "c", cases["pow"], True)
        names.append(f"dump_{T.lower()}_pow")
        pw = cases["powi"]
        out += [f"routine dump_{T.lower()}_powi()", rf_list("zr", U, [c[0][0] for c in pw]),
                rf_list("zi", U, [c[0][1] for c in pw]),
                f"    var ns: List[S64] = [{', '.join(str(c[1]) for c in pw)}]",
                "    var i: U64 = 0", "    until i >= zr.count()",
                f"        var z = {T}(real: {rf_part(P, 'zr[i]')}, imag: {rf_part(P, 'zi[i]')})",
                "        var r = z.pow(exp: ns[i])",
                f'        show(f"{T} powi {{i}} {result_text("c", T)}")', "        i += 1", "    return", ""]
        names.append(f"dump_{T.lower()}_powi")
        fp = cases["from_polar"]
        out += [f"routine dump_{T.lower()}_from_polar()", rf_list("rs", U, [c[0] for c in fp]),
                rf_list("ts", U, [c[1] for c in fp]), "    var i: U64 = 0", "    until i >= rs.count()",
                f"        var r = {T}.from_polar(r: {rf_part(P, 'rs[i]')}, theta: {rf_part(P, 'ts[i]')})",
                f'        show(f"{T} from_polar {{i}} {result_text("c", T)}")', "        i += 1", "    return", ""]
        names.append(f"dump_{T.lower()}_from_polar")
        out += [f"routine dump_{T.lower()}_parse()",
                "    var texts: List[Text] = [" + ", ".join('"' + t + '"' for t in TEXT_CASES) + "]",
                "    var i: U64 = 0", "    until i >= texts.count()",
                f"        var r = try texts[i].to[{T}]()", "        when r",
                f'            is None => show(f"{T} parse {{i}} invalid")',
                f'            else as v => show(f"{T} parse {{i}} {{v.real.to_bits()}} {{v.imag.to_bits()}}")',
                "        i += 1", "    return", ""]
        names.append(f"dump_{T.lower()}_parse")
    for (Ta, Pa, Ua), (Tb, Pb, Ub) in [(TYPES[1], TYPES[0]), (TYPES[2], TYPES[0]), (TYPES[2], TYPES[1]),
                                       (TYPES[0], TYPES[1]), (TYPES[0], TYPES[2]), (TYPES[1], TYPES[2])]:
        widen = FORMATS[Pb][0] > FORMATS[Pa][0]
        cs = all_cases["narrow"][(Ta, Tb)]
        out += [f"routine dump_{Ta.lower()}_to_{Tb.lower()}()", rf_list("zr", Ua, [c[0] for c in cs]),
                rf_list("zi", Ua, [c[1] for c in cs]), "    var i: U64 = 0", "    until i >= zr.count()",
                f"        var z = {Ta}(real: {rf_part(Pa, 'zr[i]')}, imag: {rf_part(Pa, 'zi[i]')})",
                f"        var r = z.to[{Tb}]()",
                f'        show(f"{Ta} to {Tb} {{i}} {result_text("c", Tb)}")', "        i += 1", "    return", ""]
        names.append(f"dump_{Ta.lower()}_to_{Tb.lower()}")
    out.append("routine start()")
    out += [f"    {n}()" for n in names]
    out += ["    return", ""]
    return "\n".join(out)


def all_cases():
    cases = {}
    for T, P, U in TYPES:
        cases[T] = build_cases(T, P)
    narrow = {}
    for (Ta, Pa, _), (Tb, Pb, _) in [(TYPES[1], TYPES[0]), (TYPES[2], TYPES[0]), (TYPES[2], TYPES[1]),
                                     (TYPES[0], TYPES[1]), (TYPES[0], TYPES[2]), (TYPES[1], TYPES[2])]:
        if FORMATS[Pb][0] > FORMATS[Pa][0]:
            narrow[(Ta, Tb)] = pick(Pa, COUNT, -40, 40, 0.1) + [(a, b) for a in special_parts(Pa)[:6] for b in
                                                                special_parts(Pa)[:2]]
        else:
            p, eb, w, bias, emin, emax = fmt_info(Pb)
            narrow[(Ta, Tb)] = pick(Pa, COUNT, -40, 40, 0.1) + pick(Pa, COUNT, emin - 30, emin + 5)
    cases["narrow"] = narrow
    return cases


if __name__ == "__main__" and DUMP:
    with open(OUT, "w") as fh:
        fh.write(dump_program(all_cases()))


# ── the exact expected values ────────────────────────────────────────────────────


def sign_of_zero_sum(x_neg, y_neg):
    """IEEE: the sum of two zeros is -0 only when both are -0 (round to nearest)."""
    return x_neg and y_neg


def prod_zero_neg(x, y, f):
    """Whether the product x × y, if zero, is -0."""
    return is_neg(x, f) != is_neg(y, f)


def is_neg(bits, f):
    return (bits >> (FORMATS[f][2] - 1)) & 1 == 1


def dot2_bits(a, b, c, d, f):
    """a b + c d rounded once, a zero sum with the sign IEEE addition gives the rounded products."""
    x = value(a, f) * value(b, f) + value(c, f) * value(d, f)
    if x != 0:
        return round_value(x, f)
    p, q = value(a, f) * value(b, f), value(c, f) * value(d, f)
    if p != 0 or q != 0:
        return 0
    return round_value(Fraction(0), f, sign_of_zero_sum(prod_zero_neg(a, b, f), prod_zero_neg(c, d, f)))


def quotient_bits(n, n_zero_neg, den, f):
    if n == 0:
        return round_value(Fraction(0), f, n_zero_neg)
    return round_value(n / den, f)


def expected_exact(op, z, w, f):
    """The bits (re, im) or (r,) an operation rounded once gives, or None when it is not one of those."""
    a, b = z
    if op in ("add", "sub", "mul", "div"):
        c, d = w
    if op in ("add", "sub"):
        c2, d2 = (c, d) if op == "add" else (neg_bits(c, f), neg_bits(d, f))
        re = value(a, f) + value(c2, f)
        im = value(b, f) + value(d2, f)
        re_b = round_value(re, f, sign_of_zero_sum(is_neg(a, f), is_neg(c2, f))) if re == 0 else round_value(re, f)
        im_b = round_value(im, f, sign_of_zero_sum(is_neg(b, f), is_neg(d2, f))) if im == 0 else round_value(im, f)
        return (re_b, im_b)
    if op == "mul":
        return (dot2_bits(a, c, neg_bits(b, f), d, f), dot2_bits(a, d, b, c, f))
    if op == "norm":
        return (dot2_bits(a, a, b, b, f),)
    if op == "abs":
        return (round_sqrt(value(a, f) ** 2 + value(b, f) ** 2, f),)
    if op == "div":
        den = value(c, f) ** 2 + value(d, f) ** 2
        n_re = value(a, f) * value(c, f) + value(b, f) * value(d, f)
        n_im = value(b, f) * value(c, f) - value(a, f) * value(d, f)
        zr = sign_of_zero_sum(prod_zero_neg(a, c, f), prod_zero_neg(b, d, f))
        zi = sign_of_zero_sum(prod_zero_neg(b, c, f), prod_zero_neg(neg_bits(a, f), d, f))
        return (quotient_bits(n_re, zr, den, f), quotient_bits(n_im, zi, den, f))
    if op == "reciprocal":
        den = value(a, f) ** 2 + value(b, f) ** 2
        return (quotient_bits(value(a, f), is_neg(a, f), den, f),
                quotient_bits(-value(b, f), not is_neg(b, f), den, f))
    if op == "neg":
        return (neg_bits(a, f), neg_bits(b, f))
    if op == "conjugate":
        return (a, neg_bits(b, f))
    return None


def expected_scale(op, z, s, f):
    a, b = z
    if op in ("add_real", "sub_real"):
        s2 = s if op == "add_real" else neg_bits(s, f)
        zero = 0 if op == "add_real" else 1 << (FORMATS[f][2] - 1)
        re = value(a, f) + value(s2, f)
        re_b = round_value(re, f, sign_of_zero_sum(is_neg(a, f), is_neg(s2, f))) if re == 0 else round_value(re, f)
        im = value(b, f)
        im_b = b if im != 0 else round_value(Fraction(0), f, sign_of_zero_sum(is_neg(b, f), is_neg(zero, f)))
        return (re_b, im_b)
    if op == "mul_real":
        out = []
        for x in (a, b):
            v = value(x, f) * value(s, f)
            out.append(round_value(v, f, prod_zero_neg(x, s, f)) if v == 0 else round_value(v, f))
        return tuple(out)
    if op == "div_real":
        out = []
        for x in (a, b):
            v = value(x, f) / value(s, f)
            out.append(round_value(v, f, prod_zero_neg(x, s, f)) if v == 0 else round_value(v, f))
        return tuple(out)
    return None


def check_dump(path):
    cases = all_cases()
    got = {}
    for line in open(path):
        parts = line.split()
        if len(parts) >= 4 and parts[1] not in ("to",):
            got[(parts[0], parts[1], int(parts[2]))] = parts[3:]
    bad = 0
    counts = {}
    for T, P, U in TYPES:
        f = P
        for op, _, _ in BINARY + SCALE + EXACT_UNARY:
            cs = cases[T][op]
            for i, c in enumerate(cs):
                if op in [o for o, _, _ in SCALE]:
                    want = expected_scale(op, c[0], c[1][0], f)
                elif op in [o for o, _, _ in BINARY]:
                    if op == "eq":
                        continue
                    want = expected_exact(op, c[0], c[1], f)
                else:
                    want = expected_exact(op, c, None, f)
                have = tuple(int(x) for x in got[(T, op, i)])
                counts[(T, op)] = counts.get((T, op), 0) + 1
                if have != want:
                    bad += 1
                    print("MISMATCH", T, op, i, c, "got", have, "want", want)
    for k, v in counts.items():
        print(k, v)
    print("mismatches", bad)


if __name__ == "__main__" and "--check-dump" in sys.argv:
    check_dump(sys.argv[sys.argv.index("--check-dump") + 1])


# ── stress: every exponent, the cases a rounding gets wrong first ──────────────────


def is_inf_bits(bits, f):
    d = decode(bits, f)
    return d[0] != "num"


def wide_part(f, zero_odds=0.05):
    p, eb, w, bias, emin, emax = fmt_info(f)
    r = rng.random()
    if r < zero_odds:
        return rng.choice([0, 1 << (w - 1)])
    sign = rng.choice([0, 1 << (w - 1)])
    if r < 0.15:
        return sign | rng.randint(1, (1 << (p - 1)) - 1)
    e = rng.randint(emin, emax)
    return sign | ((e + bias) << (p - 1)) | rng.getrandbits(p - 1)


def near(bits, f, spread):
    """A value whose exponent is within `spread` of bits's."""
    p, eb, w, bias, emin, emax = fmt_info(f)
    e = ((bits >> (p - 1)) & ((1 << eb) - 1)) - bias
    e2 = min(max(e + rng.randint(-spread, spread), emin), emax)
    return rng.choice([0, 1 << (w - 1)]) | ((e2 + bias) << (p - 1)) | rng.getrandbits(p - 1)


def stress_cases(f, n):
    p, eb, w, bias, emin, emax = fmt_info(f)
    out = {}
    pairs = []
    for _ in range(n):
        a = wide_part(f)
        pairs.append(((a, near(a, f, 60)), (wide_part(f), wide_part(f))))
        c = wide_part(f)
        pairs.append(((near(c, f, 30), near(c, f, 30)), (c, near(c, f, 30))))
    for _ in range(n):
        a, b, c = (wide_part(f, 0) for _ in range(3))
        if value(b, f) == 0:
            continue
        d = round_value(value(a, f) * value(c, f) / value(b, f), f)
        if not is_inf_bits(d, f):
            pairs.append(((a, b), (c, d)))
    for _ in range(n):
        a = wide_part(f, 0)
        if value(a, f) == 0:
            continue
        tie = ulp_bits(a, f)
        half = round_value(value(tie, f) / 2, f)
        k = rng.randint(emin // 2, emax // 2)
        scale = round_value(Fraction(2) ** k, f)
        for b in (half, neg_bits(half, f), round_value(value(half, f) * (1 + Fraction(1, 1 << (p + 3))), f)):
            pairs.append(((a, b), (scale, scale)))
    keep = {}
    for op in ("mul", "div"):
        good = []
        for z, wv in pairs:
            if op == "div" and value(wv[0], f) == 0 and value(wv[1], f) == 0:
                continue
            want = expected_exact(op, z, wv, f)
            if any(is_inf_bits(x, f) for x in want):
                continue
            good.append((z, wv))
        keep[op] = good
    singles = [z for z, _ in pairs] + [wv for _, wv in pairs]
    for op in ("norm", "abs", "reciprocal"):
        good = []
        for z in singles:
            if op == "reciprocal" and value(z[0], f) == 0 and value(z[1], f) == 0:
                continue
            want = expected_exact(op, z, None, f)
            if any(is_inf_bits(x, f) for x in want):
                continue
            good.append(z)
        keep[op] = good
    return keep


def stress_program(cases):
    out = ["module Tests/Scratch/ComplexStress", "", "import IO/Console", ""]
    names = []
    calls = {"mul": "z * w", "div": "z / w", "norm": "z.norm()", "abs": "z.abs()", "reciprocal": "z.reciprocal()"}
    kinds = {"mul": "c", "div": "c", "norm": "p", "abs": "p", "reciprocal": "c"}
    for T, P, U in TYPES:
        for op in ("mul", "div", "norm", "abs", "reciprocal"):
            out += dump_routine(T, P, U, op, calls[op], kinds[op], cases[T][op], op in ("mul", "div"))
            names.append(f"dump_{T.lower()}_{op}")
    out.append("routine start()")
    out += [f"    {n}()" for n in names]
    out += ["    return", ""]
    return "\n".join(out)


def check_stress(path, cases):
    got = {}
    for line in open(path):
        parts = line.split()
        got[(parts[0], parts[1], int(parts[2]))] = tuple(int(x) for x in parts[3:])
    bad = 0
    for T, P, U in TYPES:
        for op in ("mul", "div", "norm", "abs", "reciprocal"):
            cs = cases[T][op]
            for i, c in enumerate(cs):
                want = expected_exact(op, c[0], c[1], P) if op in ("mul", "div") else expected_exact(op, c, None, P)
                if got.get((T, op, i)) != want:
                    bad += 1
                    if bad < 40:
                        print("MISMATCH", T, op, i, c, "got", got.get((T, op, i)), "want", want)
            print(T, op, len(cs), "checked")
    print("mismatches", bad)


if __name__ == "__main__" and "--stress" in sys.argv:
    FMT_STRESS = {}
    stress = {T: stress_cases(P, COUNT) for T, P, U in TYPES}
    if "--check" in sys.argv:
        check_stress(sys.argv[sys.argv.index("--check") + 1], stress)
    else:
        with open(OUT, "w") as fh:
            fh.write(stress_program(stress))


# ── the high-precision reference for the elementary functions ───────────────────────

PREC = {"B32": 120, "B64": 120, "B128": 160}


class Ref:
    """Complex elementary functions at `digits` significant digits (decimal), principal branches. A zero part of an
    argument is moved off zero by a relative 10^-(digits - 10) with its sign, which puts a point on a branch cut on the
    side its signed zero names."""

    def __init__(self, digits):
        self.digits = digits
        self.eps = Decimal(10) ** (-(digits - 12))
        with localcontext() as ctx:
            ctx.prec = digits + 10
            self.pi = self._pi()
            self.ln10 = Decimal(10).ln()
            self.ln2 = Decimal(2).ln()

    def ctx(self):
        c = localcontext()
        c.__enter__().prec = self.digits + 10
        return c

    @staticmethod
    def _pi():
        # Machin: pi = 16 atan(1/5) - 4 atan(1/239)
        def atan_inv(n):
            from decimal import getcontext
            x = Decimal(1) / n
            x2 = x * x
            total, term, k = x, x, 1
            eps = Decimal(10) ** (-(getcontext().prec + 2))
            while abs(term) > eps:
                term *= -x2
                k += 2
                total += term / k
            return total
        return 16 * atan_inv(5) - 4 * atan_inv(239)

    def sin_cos(self, x):
        from decimal import getcontext
        two_pi = 2 * self.pi
        n = (x / two_pi).to_integral_value()
        r = x - n * two_pi
        eps = Decimal(10) ** (-(getcontext().prec + 2))
        s, c, term_s, term_c, k = r, Decimal(1), r, Decimal(1), 1
        r2 = r * r
        while True:
            term_s *= -r2 / ((2 * k) * (2 * k + 1))
            term_c *= -r2 / ((2 * k - 1) * (2 * k))
            s += term_s
            c += term_c
            k += 1
            if abs(term_s) < eps and abs(term_c) < eps:
                break
        return s, c

    def atan(self, x):
        from decimal import getcontext
        if x == 0:
            return x
        if abs(x) > 1:
            v = self.pi / 2 - self.atan(1 / abs(x))
            return v if x > 0 else -v
        halvings = 0
        while abs(x) > Decimal("0.05"):
            x = x / (1 + (1 + x * x).sqrt())
            halvings += 1
        eps = Decimal(10) ** (-(getcontext().prec + 2))
        total, term, k = x, x, 1
        x2 = x * x
        while abs(term) > eps:
            term *= -x2
            k += 2
            total += term / k
        return total * (2 ** halvings)

    def atan2(self, y, x):
        if x > 0:
            return self.atan(y / x)
        if x < 0:
            return self.atan(y / x) + (self.pi if y >= 0 else -self.pi)
        return self.pi / 2 if y > 0 else -self.pi / 2

    def exp(self, x):
        return x.exp()

    # complex helpers: (re, im) tuples of Decimal
    def cmul(self, z, w):
        return (z[0] * w[0] - z[1] * w[1], z[0] * w[1] + z[1] * w[0])

    def cdiv(self, z, w):
        d = w[0] * w[0] + w[1] * w[1]
        return ((z[0] * w[0] + z[1] * w[1]) / d, (z[1] * w[0] - z[0] * w[1]) / d)

    def cabs(self, z):
        return (z[0] * z[0] + z[1] * z[1]).sqrt()

    def cln(self, z):
        return ((z[0] * z[0] + z[1] * z[1]).ln() / 2, self.atan2(z[1], z[0]))

    def csqrt(self, z):
        a, b = z
        r = self.cabs(z)
        if a >= 0:
            t = ((r + a) / 2).sqrt()
            return (t, b / (2 * t))
        t = ((r - a) / 2).sqrt()
        return (abs(b) / (2 * t), t if b >= 0 else -t)

    def cexp(self, z):
        s, c = self.sin_cos(z[1])
        e = z[0].exp()
        return (e * c, e * s)

    def sinh_cosh(self, x):
        e = x.exp()
        ei = 1 / e
        return (e - ei) / 2, (e + ei) / 2

    def evaluate(self, op, z, w=None):
        one = (Decimal(1), Decimal(0))
        i = (Decimal(0), Decimal(1))
        if op == "arg":
            return (self.atan2(z[1], z[0]),)
        if op == "sqrt":
            return self.csqrt(z)
        if op == "exp":
            return self.cexp(z)
        if op == "ln":
            return self.cln(z)
        if op == "log10":
            v = self.cln(z)
            return (v[0] / self.ln10, v[1] / self.ln10)
        if op == "log2":
            v = self.cln(z)
            return (v[0] / self.ln2, v[1] / self.ln2)
        a, b = z
        if op in ("sin", "cos"):
            s, c = self.sin_cos(a)
            sh, ch = self.sinh_cosh(b)
            return (s * ch, c * sh) if op == "sin" else (c * ch, -s * sh)
        if op == "tan":
            s2, c2 = self.sin_cos(2 * a)
            sh2, ch2 = self.sinh_cosh(2 * b)
            d = c2 + ch2
            return (s2 / d, sh2 / d)
        if op in ("sinh", "cosh"):
            s, c = self.sin_cos(b)
            sh, ch = self.sinh_cosh(a)
            return (sh * c, ch * s) if op == "sinh" else (ch * c, sh * s)
        if op == "tanh":
            s2, c2 = self.sin_cos(2 * b)
            sh2, ch2 = self.sinh_cosh(2 * a)
            d = ch2 + c2
            return (sh2 / d, s2 / d)
        if op == "asin":
            iz = (-b, a)
            zz = self.cmul(z, z)
            s = self.csqrt((1 - zz[0], -zz[1]))
            v = self.cln((iz[0] + s[0], iz[1] + s[1]))
            return (v[1], -v[0])
        if op == "acos":
            v = self.evaluate("asin", z)
            return (self.pi / 2 - v[0], -v[1])
        if op == "atan":
            l1 = self.cln((1 + b, -a))
            l2 = self.cln((1 - b, a))
            d = (l1[0] - l2[0], l1[1] - l2[1])
            return (-d[1] / 2, d[0] / 2)
        if op == "asinh":
            zz = self.cmul(z, z)
            s = self.csqrt((zz[0] + 1, zz[1]))
            return self.cln((a + s[0], b + s[1]))
        if op == "acosh":
            s1 = self.csqrt((a + 1, b))
            s2 = self.csqrt((a - 1, b))
            p = self.cmul(s1, s2)
            return self.cln((a + p[0], b + p[1]))
        if op == "atanh":
            l1 = self.cln((1 + a, b))
            l2 = self.cln((1 - a, -b))
            return ((l1[0] - l2[0]) / 2, (l1[1] - l2[1]) / 2)
        if op == "pow":
            v = self.cln(z)
            return self.cexp(self.cmul(w, v))
        raise ValueError(op)


def to_decimal(bits, f, nudge=None):
    v = value(bits, f)
    d = Decimal(v.numerator) / Decimal(v.denominator)
    if v == 0 and nudge is not None:
        return -nudge if is_neg(bits, f) else nudge
    return d


def round_decimal(x, f):
    return round_value(Fraction(x), f)


def ulp_at(x, f):
    """The unit in the last place of a value of magnitude |x| (a Fraction), the subnormal one below the normal range."""
    p, eb, w, bias, emin, emax = fmt_info(f)
    if x == 0:
        return Fraction(2) ** (emin - (p - 1))
    e = max(floor_log2(abs(x)), emin)
    return Fraction(2) ** (e - (p - 1))


def exact_at_zero(x, ref):
    """A part of a function's value at 0 + 0i, which is 0, ±1, ±π, or ±π/2, at the reference's precision."""
    for k in (1, -1):
        if x == k * math.pi:
            return k * Fraction(ref.pi)
        if x == k * math.pi / 2:
            return k * Fraction(ref.pi) / 2
    return Fraction(x)


def reference(op, z, f, w=None):
    """The reference value of op at z (and w) as Fractions, from the decimal reference. A zero part is nudged off zero
    only when the other part is not zero."""
    ref = REFS[f]
    if value(z[0], f) == 0 and value(z[1], f) == 0 and w is None:
        # at 0 + 0i every function is exact, and cmath follows IEEE's signed zeros there
        zc = complex(to_float(z[0], f), to_float(z[1], f))
        if op == "arg":
            return (exact_at_zero(math.atan2(zc.imag, zc.real), ref),)
        r = {"sqrt": cmath.sqrt, "exp": cmath.exp, "sin": cmath.sin, "cos": cmath.cos, "tan": cmath.tan,
             "asin": cmath.asin, "acos": cmath.acos, "atan": cmath.atan, "sinh": cmath.sinh, "cosh": cmath.cosh,
             "tanh": cmath.tanh, "asinh": cmath.asinh, "acosh": cmath.acosh, "atanh": cmath.atanh}[op](zc)
        return (exact_at_zero(r.real, ref), exact_at_zero(r.imag, ref))
    with localcontext() as ctx:
        ctx.prec = ref.digits + 10
        scale = max(abs(to_decimal(z[0], f)), abs(to_decimal(z[1], f)), Decimal(1))
        nudge = ref.eps * scale
        both_zero = value(z[0], f) == 0 and value(z[1], f) == 0
        zd = (to_decimal(z[0], f, None if both_zero else nudge), to_decimal(z[1], f, None if both_zero else nudge))
        wd = None if w is None else (to_decimal(w[0], f), to_decimal(w[1], f))
        out = ref.evaluate(op, zd, wd)
        # a part the nudge made out of an exact zero goes back to zero
        snap = nudge.sqrt() * 10000
        return tuple(Fraction(0) if abs(x) <= snap else Fraction(x) for x in out)


REFS = {f: Ref(PREC[f]) for f in FORMATS}


def ulp_error(got, want, f):
    """The normwise error of got against want, in units in the last place of want's larger part."""
    scale = max(abs(x) for x in want)
    u = ulp_at(scale, f)
    return max(abs(g - wv) for g, wv in zip(got, want)) / u


def elementary_errors(path, cases_of, label=""):
    """The normwise errors, in ulps, of the elementary functions a dump printed."""
    got = {}
    for line in open(path):
        parts = line.split()
        if len(parts) >= 4:
            got[(parts[0], parts[1], parts[2])] = parts[3:]
    report = {}
    for T, P, U in TYPES:
        f = P
        cases = cases_of[T]
        for op in [o for o, _, _ in ELEMENTARY] + ["pow", "powi", "from_polar"]:
            worst, worst_case, n, over = 0, None, 0, 0
            for i, c in enumerate(cases[op]):
                key = (T, op, str(i))
                if key not in got:
                    continue
                bits = [int(x) for x in got[key]]
                if any(decode(b, f)[0] != "num" for b in bits):
                    continue
                if op == "pow":
                    want = reference("pow", c[0], f, c[1])
                elif op == "powi":
                    z, k = c
                    zr = (value(z[0], f), value(z[1], f))
                    acc = (Fraction(1), Fraction(0))
                    base = zr
                    if k < 0:
                        d = zr[0] ** 2 + zr[1] ** 2
                        base = (zr[0] / d, -zr[1] / d)
                    for _ in range(abs(k)):
                        acc = (acc[0] * base[0] - acc[1] * base[1], acc[0] * base[1] + acc[1] * base[0])
                    want = acc
                elif op == "from_polar":
                    r, t = c
                    ref = REFS[f]
                    with localcontext() as ctx:
                        ctx.prec = ref.digits + 10
                        s, co = ref.sin_cos(to_decimal(t, f))
                        rd = to_decimal(r, f)
                        want = (Fraction(rd * co), Fraction(rd * s))
                else:
                    want = reference(op, c, f)
                have = tuple(value(b, f) for b in bits)
                err = ulp_error(have, want, f)
                n += 1
                if err > BOUND[f]:
                    over += 1
                if err > worst:
                    worst, worst_case = err, c
            report[(T, op)] = (n, float(worst), over, worst_case)
            print(f"{label}{T} {op}: {n} checked, worst {float(worst):.2f} ulp, {over} over {BOUND[f]}")
    return report


if __name__ == "__main__" and "--elementary" in sys.argv:
    elementary_errors(sys.argv[sys.argv.index("--elementary") + 1], all_cases())


# ── the fixture ──────────────────────────────────────────────────────────────────

FIXTURE_BINARY = [("add", "z + w"), ("sub", "z - w"), ("mul", "z * w"), ("div", "z / w")]
FIXTURE_SCALE = [("add_real", "z + w.real"), ("sub_real", "z - w.real"), ("mul_real", "z * w.real"),
                 ("div_real", "z / w.real")]
FIXTURE_UNARY_C = [("neg", "-z"), ("conjugate", "z.conjugate()"), ("reciprocal", "z.reciprocal()")]
FIXTURE_UNARY_P = [("norm", "z.norm()"), ("abs", "z.abs()")]


def arg_bits(z, f):
    """The phase angle rounded once, a zero angle with the sign IEEE atan2 gives it."""
    x = reference("arg", z, f)[0]
    if x != 0:
        return round_value(x, f)
    angle = math.atan2(to_float(z[1], f), to_float(z[0], f))
    return round_value(Fraction(0), f, math.copysign(1.0, angle) < 0)


def limit_bits(want, f):
    """BOUND units in the last place of the larger part of want, as the part type's bits."""
    scale = max(abs(x) for x in want)
    return round_value(BOUND[f] * ulp_at(scale, f), f)


def fixture_program(seed_cases):
    out = [
        "module Tests/Stdlib/ComplexCrosscheckApi",
        "",
        "import IO/Console",
        "",
        "# C64, C128, and C256 against exact rational arithmetic and a high-precision reference: scripts/gen_complex_crosscheck.py",
        "# writes this file. The sums, differences, products, quotients, reciprocals, squared moduli, moduli, scalings, and",
        "# narrowings are rounded once, so each is checked bit for bit against the exact value rounded once (a tie to the",
        "# even one, signed zeros included). The elementary functions are checked to within "
        f"{BOUND['B32']} units in the last place of the",
        "# larger part of a 120 or 160 digit reference, principal branches with a zero part's sign picking the side of a cut.",
        "# A line names each value that is off, then each operation prints how many it checked.",
        "",
    ]
    body = []
    starts = []
    for T, P, U in TYPES:
        f = P
        t = T.lower()
        mk = lambda r, i: f"{T}(real: {P}.from_bits(bits: {r}), imag: {P}.from_bits(bits: {i}))"
        # exact two-operand operations
        out += [f"### Checks {T}'s operations rounded once on two operands, bit for bit.",
                f"routine check_{t}_exact(op: Text, z: List[{T}], w: List[{T}], want: List[{T}]) -> U64",
                "    var i: U64 = 0",
                "    until i >= z.count()",
                f"        var r: {T} = z[i]",
                "        var a = z[i]", "        var b = w[i]"]
        first = True
        for op, call in FIXTURE_BINARY + FIXTURE_SCALE + FIXTURE_UNARY_C:
            expr = call.replace("z", "a").replace("w.real", "b.real").replace("a + w", "a + b")
            expr = expr.replace(" w", " b")
            kw = "if" if first else "elseif"
            first = False
            out += [f'        {kw} op == "{op}"', f"            r = {expr}"]
        out += [f"        if not same_{t}(a: r, b: want[i])",
                f'            show(f"{T} {{op}} {{a.diagnose()}} {{b.diagnose()}}: {{r.diagnose()}}, want {{want[i].diagnose()}}")',
                "        i += 1", "    return i", ""]
        out += [f"### Whether two {T} values have the same bits in both parts.",
                f"routine same_{t}(a: {T}, b: {T}) -> Bool",
                "    return a.real.to_bits() == b.real.to_bits() and a.imag.to_bits() == b.imag.to_bits()", ""]
        out += [f"### Checks {T}'s squared modulus or modulus, bit for bit.",
                f"routine check_{t}_part(op: Text, z: List[{T}], want: List[{P}]) -> U64",
                "    var i: U64 = 0",
                "    until i >= z.count()",
                "        var r = z[i].abs()",
                '        if op == "norm"',
                "            r = z[i].norm()",
                '        elseif op == "arg"',
                "            r = z[i].arg()",
                "        if r.to_bits() != want[i].to_bits()",
                f'            show(f"{T} {{op}} {{z[i].diagnose()}}: {{r}}, want {{want[i]}}")',
                "        i += 1", "    return i", ""]
        out += [f"### Checks an elementary function of {T} to within the bound `limit` in each part.",
                f"routine check_{t}_near(op: Text, z: List[{T}], want: List[{T}], limit: List[{P}]) -> U64",
                "    var i: U64 = 0",
                "    until i >= z.count()",
                "        var a = z[i]",
                "        var r = a.sqrt_unchecked()"]
        first = True
        for op, call, kind in ELEMENTARY:
            if op == "arg" or op == "sqrt":
                continue
            kw = "if" if first else "elseif"
            first = False
            out += [f'        {kw} op == "{op}"', f"            r = {call.replace('z.', 'a.')}"]
        out += ["        var dr = (r.real - want[i].real).abs()", "        var di = (r.imag - want[i].imag).abs()",
                "        if dr > limit[i] or di > limit[i]",
                f'            show(f"{T} {{op}} {{a.diagnose()}}: {{r.diagnose()}}, want {{want[i].diagnose()}}")',
                "        i += 1", "    return i", ""]
        # the start of each type's checks: each list of operands once, the expected values per operation
        cases = seed_cases[T]
        lines = []
        mkb = lambda a, b: f"{t}({lit(a)}, {lit(b)})"

        def clist(vals):
            return "[" + ", ".join(mkb(a, b) for a, b in vals) + "]"

        def plist(vals):
            return "[" + ", ".join(f"{P}.from_bits(bits: {lit(v)})" for v in vals) + "]"

        named = {}

        def operand_list(name, vals):
            key = tuple(vals)
            if key in named:
                return named[key]
            var = f"{name}{len(named)}"
            named[key] = var
            lines.append(f"    var {var}: List[{T}] = {clist(vals)}")
            return var

        for op, _ in FIXTURE_BINARY + FIXTURE_SCALE:
            cs = cases[op]
            if op in [o for o, _ in FIXTURE_BINARY]:
                want = [expected_exact(op, z, w, f) for z, w in cs]
            else:
                want = [expected_scale(op, z, w[0], f) for z, w in cs]
            zv = operand_list("z", [z for z, _ in cs])
            wv = operand_list("w", [w for _, w in cs])
            lines += [f'    n = check_{t}_exact(op: "{op}", z: {zv}.duplicate(), w: {wv}.duplicate(), want: {clist(want)})',
                      f'    show(f"{T} {op}: {{n}} checked")']
        for op, _ in FIXTURE_UNARY_C:
            cs = cases[op]
            want = [expected_exact(op, z, None, f) for z in cs]
            zv = operand_list("z", cs)
            lines += [f'    n = check_{t}_exact(op: "{op}", z: {zv}.duplicate(), w: {zv}.duplicate(), want: {clist(want)})',
                      f'    show(f"{T} {op}: {{n}} checked")']
        for op, _ in FIXTURE_UNARY_P:
            cs = cases[op]
            want = [expected_exact(op, z, None, f)[0] for z in cs]
            zv = operand_list("z", cs)
            lines += [f'    n = check_{t}_part(op: "{op}", z: {zv}.duplicate(), want: {plist(want)})',
                      f'    show(f"{T} {op}: {{n}} checked")']
        cs = cases["arg"]
        want = [arg_bits(z, f) for z in cs]
        zv = operand_list("z", cs)
        lines += [f'    n = check_{t}_part(op: "arg", z: {zv}.duplicate(), want: {plist(want)})',
                  f'    show(f"{T} arg: {{n}} checked")']
        for op, call, kind in ELEMENTARY:
            if op == "arg":
                continue
            cs = cases[op]
            wants = [reference(op, z, f) for z in cs]
            wb = [(round_value(w[0], f), round_value(w[1], f)) for w in wants]
            lim = [limit_bits(w, f) for w in wants]
            zv = operand_list("z", cs)
            lines += [f'    n = check_{t}_near(op: "{op}", z: {zv}.duplicate(), want: {clist(wb)}, limit: {plist(lim)})',
                      f'    show(f"{T} {op}: {{n}} checked")']
        out += [f"### A {T} from its parts' bits.", f"routine {t}(re: {U}, im: {U}) -> {T}",
                f"    return {T}(real: {P}.from_bits(bits: re), imag: {P}.from_bits(bits: im))", ""]
        name = f"check_{t}"
        out += [f"### Runs every check of {T}.", f"routine {name}()", "    var n: U64 = 0"] + lines + ["    return", ""]
        starts.append(name)
    out += SURFACE_CHECKS.split("\n")
    out += ["routine start()"] + [f"    {s}()" for s in starts] + ["    check_surface()", "    show(\"DONE\")", "    return", ""]
    return "\n".join(out)


SURFACE_CHECKS = """### Text written and read back, the kinds of a number, equality and hashing of signed zeros, and conversions rounded
### once (the B128 value is just above a B32 tie, which going through B64 would round to the even side of).
routine check_surface()
    var z = C64(real: 1.5, imag: -0.25)
    var nz = C64(real: -0.0, imag: -0.0)
    show(f"{z} {z.fixed(2)} {z.diagnose()} {nz} {C128(real: 0.1, imag: 0.2)} {C256(real: 1.0, imag: 0.1)}")
    var texts: List[Text] = ["1.5-2i", " -i ", "+i", "2e3i", "1e-3+2.5i", "-0-0i", "3", "1+", "abc", "1+-2i", ""]
    each t in texts
        var r = try t.to[C64]()
        when r
            is None => show(f"{t}: not a C64")
            else as v => show(f"{t}: {v.real.to_bits()} {v.imag.to_bits()}")
    var back = 0
    var samples: List[C128] = [C128(real: 0.1, imag: -1e300), C128(real: 5e-324, imag: 123.456), C128(real: -0.0, imag: 2.5)]
    each s in samples
        var t = s.represent()
        if t.to[C128]().real.to_bits() == s.real.to_bits() and t.to[C128]().imag.to_bits() == s.imag.to_bits()
            back += 1
    show(f"read back: {back} of {samples.count()}")
    show(f"{nz.is_zero()} {nz.is_real()} {z.is_imaginary()} {z.is_finite()} {z == z} {nz == C64.zero()}")
    show(f"{nz.hash(k0: 1, k1: 2) == C64.zero().hash(k0: 1, k1: 2)} {C128(real: -0.0, imag: 0.0).hash(k0: 3, k1: 4) == C128.zero().hash(k0: 3, k1: 4)}")
    var wide = C256(real: B128.from_bits(bits: 0x3FFF0000010000000000000000000001), imag: 0.0)
    var narrow = wide.to[C64]()
    show(f"{narrow.real.to_bits()} {wide.to[C128]().to[C64]().real.to_bits()}")
    var p = C128(real: 3.0, imag: 4.0).to_polar()
    show(f"{p.item0} {p.item1}")
    return
"""

SURFACE_EXPECTED = ['1.5-0.25i 1.50-0.25i C64(1.5-0.25i) -0+0i 0.1+0.2i 1+0.1i',
    '1.5-2i: 1069547520 3221225472',
    ' -i : 0 3212836864',
    '+i: 0 1065353216',
    '2e3i: 0 1157234688',
    '1e-3+2.5i: 981668463 1075838976',
    '-0-0i: 2147483648 2147483648',
    '3: 1077936128 0',
    '1+: not a C64',
    'abc: not a C64',
    '1+-2i: not a C64',
    ': not a C64',
    'read back: 3 of 3',
    'true true false true true true',
    'true true',
    '1065353217 1065353216',
    '5 0.9272952180016122']


def fixture_cases():
    cases = {}
    for T, P, U in TYPES:
        f = P
        c = build_cases(T, P)
        sub = {}
        for op, _ in FIXTURE_BINARY + FIXTURE_SCALE:
            sub[op] = c[op]
        for op, _ in FIXTURE_UNARY_C + FIXTURE_UNARY_P:
            sub[op] = c[op]
        shared = [z for z in c["sqrt"] if all(elementary_ok(op, z, f) for op, _, _ in ELEMENTARY)]
        for op, _, _ in ELEMENTARY:
            sub[op] = shared
        cases[T] = sub
    return cases


def fixture_expected(cases):
    lines = []
    for T, P, U in TYPES:
        for op, _ in FIXTURE_BINARY + FIXTURE_SCALE + FIXTURE_UNARY_C + FIXTURE_UNARY_P:
            lines.append(f"{T} {op}: {len(cases[T][op])} checked")
        lines.append(f"{T} arg: {len(cases[T]['arg'])} checked")
        for op, _, _ in ELEMENTARY:
            if op == "arg":
                continue
            lines.append(f"{T} {op}: {len(cases[T][op])} checked")
    lines += SURFACE_EXPECTED
    lines.append("DONE")
    return "\n".join(lines) + "\n"


if __name__ == "__main__" and len(sys.argv) == 1:
    fc = fixture_cases()
    with open(OUT, "w") as fh:
        fh.write(fixture_program(fc))
    with open(OUT.replace(".rf", ".expected.txt"), "w") as fh:
        fh.write(fixture_expected(fc))
