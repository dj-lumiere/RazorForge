#!/usr/bin/env python3
"""
Writes Standard/Core/Types/CharCase.rf from UnicodeData.txt: the simple uppercase and lowercase mappings and the
uppercase (Lu) and lowercase (Ll) letters, as runs a binary search walks.

Usage: gen_char_case.py <UnicodeData.txt> <unicode version> <output .rf>

A mapping run is (first, last, step, shift): every code point from first to last, step apart, maps to itself plus
shift. A letter run is (first, last, step): every code point from first to last, step apart, is such a letter.
"""

import sys


def read(path):
    upper, lower, lu, ll = {}, {}, [], []
    with open(path, encoding="utf-8") as f:
        for line in f:
            fields = line.rstrip("\n").split(";")
            cp = int(fields[0], 16)
            if fields[2] == "Lu":
                lu.append(cp)
            elif fields[2] == "Ll":
                ll.append(cp)
            if fields[12]:
                upper[cp] = int(fields[12], 16)
            if fields[13]:
                lower[cp] = int(fields[13], 16)
    return upper, lower, lu, ll


def runs(points, shift_of):
    """Greedy runs over sorted code points: one step (1 or 2) and one shift per run."""
    out = []
    i = 0
    while i < len(points):
        first = points[i]
        shift = shift_of(first)
        last = first
        step = 0
        j = i + 1
        while j < len(points):
            gap = points[j] - last
            if shift_of(points[j]) != shift or gap not in (1, 2) or (step and gap != step):
                break
            step = gap
            last = points[j]
            j += 1
        out.append((first, last, step or 1, shift))
        i = j
    return out


def table(name, rows, width):
    values = []
    for row in rows:
        values.extend(row[:width])
    lines = [f"preset {name}: Array[S64, {len(values)}] = ["]
    for k in range(0, len(values), 8):
        lines.append("    " + ", ".join(f"0x{v:X}" if v >= 0 else f"-0x{-v:X}" for v in values[k:k + 8]) + ",")
    lines.append("]")
    return "\n".join(lines)


def mapping_search(routine, doc, name, count):
    return f'''{doc}
### :param cp: The code point.
### :returns: The code point it maps to, or `cp` itself.
secret routine {routine}(cp: S64) -> S64
    var low: U64 = 0
    var high: U64 = {count}
    while low < high
        var mid = (low + high) // 2
        var at = mid * 4
        if cp < {name}[at]
            high = mid
        elseif cp > {name}[at + 1]
            low = mid + 1
        elseif (cp - {name}[at]) % {name}[at + 2] == 0
            return cp + {name}[at + 3]
        else
            return cp
    return cp
'''


def letter_search(routine, doc, name, count):
    return f'''{doc}
### :param cp: The code point.
### :returns: `true` when it is one.
secret routine {routine}(cp: S64) -> Bool
    var low: U64 = 0
    var high: U64 = {count}
    while low < high
        var mid = (low + high) // 2
        var at = mid * 3
        if cp < {name}[at]
            high = mid
        elseif cp > {name}[at + 1]
            low = mid + 1
        else
            return (cp - {name}[at]) % {name}[at + 2] == 0
    return false
'''


def main():
    source, version, output = sys.argv[1:4]
    upper, lower, lu, ll = read(source)
    upper_runs = runs(sorted(upper), lambda cp: upper[cp] - cp)
    lower_runs = runs(sorted(lower), lambda cp: lower[cp] - cp)
    lu_runs = runs(lu, lambda cp: 0)
    ll_runs = runs(ll, lambda cp: 0)
    parts = [
        "module Core",
        "",
        f"# Unicode {version} simple case mappings and the uppercase (Lu) and lowercase (Ll) letters, written by",
        "# scripts/gen_char_case.py from UnicodeData.txt. Write it again with that script, not by hand.",
        "#",
        "# A mapping run is (first, last, step, shift): every code point from first to last, step apart, maps to itself",
        "# plus shift. A letter run is (first, last, step). Each table is sorted by first, and runs don't overlap.",
        "",
        "### The simple uppercase mappings, as runs of (first, last, step, shift).",
        table("UPPERCASE_RUNS", upper_runs, 4),
        "",
        "### The simple lowercase mappings, as runs of (first, last, step, shift).",
        table("LOWERCASE_RUNS", lower_runs, 4),
        "",
        "### The uppercase letters (Lu), as runs of (first, last, step).",
        table("UPPERCASE_LETTER_RUNS", lu_runs, 3),
        "",
        "### The lowercase letters (Ll), as runs of (first, last, step).",
        table("LOWERCASE_LETTER_RUNS", ll_runs, 3),
        "",
        mapping_search("unicode_uppercase", "### The simple uppercase mapping of a code point.", "UPPERCASE_RUNS",
                       len(upper_runs)),
        mapping_search("unicode_lowercase", "### The simple lowercase mapping of a code point.", "LOWERCASE_RUNS",
                       len(lower_runs)),
        letter_search("unicode_is_uppercase", "### Whether a code point is an uppercase letter (Lu).",
                      "UPPERCASE_LETTER_RUNS", len(lu_runs)),
        letter_search("unicode_is_lowercase", "### Whether a code point is a lowercase letter (Ll).",
                      "LOWERCASE_LETTER_RUNS", len(ll_runs)).rstrip("\n"),
    ]
    with open(output, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(parts) + "\n")
    print(f"upper {len(upper_runs)} lower {len(lower_runs)} Lu {len(lu_runs)} Ll {len(ll_runs)} runs")


if __name__ == "__main__":
    main()
