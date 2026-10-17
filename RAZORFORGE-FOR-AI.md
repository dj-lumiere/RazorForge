# RazorForge — Reference for AI Assistants

You are reading this because RazorForge (v0.2.0, 2026) is **not in your
training data**. Do not guess syntax from Rust/Swift/Python intuitions — this
file lists exactly where those intuitions are wrong. Everything here is taken
from programs that compile and run in CI.

When unsure, consult ground truth in the repo/package:

- `tests/Fixtures/Stdlib/*.rf` — 140+ complete programs with expected output (the cookbook).
- `Standard/RazorForge/` — the standard library sources (`.rf`), including every public API.

---

## 1. The rules most likely to break your assumptions

1. **Calls with three or more parameters REQUIRE named arguments (RF-S510).** Two parameters may be positional
   (no warning), naming them is still clearer: `gcd(a: 252, b: 105)`.
   Single-argument calls may be positional. A few stdlib routines annotated
   `@positional` (e.g. `x.clamp(0, 100)`) accept all-positional; mixing named
   and positional in one call is always an error (RF-S512). A variable named
   like its parameter goes by its name alone (`area(width, height: 4)` =
   `area(width: width, height: 4)`); it counts as named.
2. **Normal routine completion uses an explicit `return`** — even void ones.
   This is load-bearing (destructor scheduling anchors at `return`), not style.
   Failable routines may also terminate with `throw`.
3. **Conditional expressions are top-level only.**
   `return if c then a else b` ✓ — but `f(x: if c then a else b)` or
   `(if c then a else b) + 1` are parse errors *by design*. Use `when` or a
   named intermediate instead.
4. **`//` is integer floor division; `/` is true division.**
5. **Default arithmetic is checked** — `+ - *` throw on overflow. Wrapping
   variants: `+% -% *%`. Clamping (saturating): `+^ -^ *^`. Floating-point
   types (B16/B32/B64/B128/D32/D64/D128) have two tiers only: checked `+ - * / **`
   (a result that is not finite crashes) and unchecked `+! -! *! /! **!` = raw
   IEEE 754 (±infinity and NaN pass through; no `danger` block needed, unlike
   integers). Decimal and C64/C128/C256 have only `+ - * / **`.
   Division by zero crashes on every number type, arbitrary precision included.
   On the floats, `Decimal` and the complex types `x / 0` with `x` not
   zero throws `DivisionByZeroError` and `0 / 0` throws `NumericDomainError`
   (integer `//` and `%` by zero throw `DivisionByZeroError`).
   There is no arbitrary-precision binary float: `B128` is the widest, and exact
   arithmetic is `Integer` and the decimals.
6. **Entities have a single owner.** Assigning or passing an entity requires
   explicit transfer: `consume(r: steal b)`. Plain `var s = obj.field_entity`
   is rejected (RF-S413).
7. **Failable routines carry a `!` suffix on the DECLARATION** (`routine parse!(...)`);
   call sites never write it (`parse(...)`, `try parse(...)`). A BARE call
   crashes LOUDLY on failure. To RECOVER, prefix the whole expression with a
   keyword and match the carrier with `when`:
   `try EXPR` → `Maybe[T]` (present | absent, no error kept);
   `check EXPR` → `Check[T]` (T | the caught `Crashables`).
   A routine with no value to give throws `AbsentValueError()`, an ordinary
   stdlib crashable: `throw AbsentValueError()`. To tell "not found" apart from
   other failures, match it by type: `is AbsentValueError => ...` on a `check`.
   **With a keyword in front, every failure beneath it is recovered, at any depth;
   only `pierce` still crashes.** That covers a `throw` in the called
   routine and in every routine it calls, bare calls included (failable or not:
   `try f()` also recovers a division by zero inside a plain helper `f` calls); a
   checked operator (`try a + b` recovers the overflow, `check a // b` the
   divide-by-zero, `-x` of the minimum, `x += d`); a subscript out of range or a
   missing key (`try xs[9]`), a failing conversion (`big.to[S8]()` with `big` at 300,
   `"x".to[S64]()`). Each becomes the carrier's failure (`try`: absent,
   `check`: the error), and evaluation stops at the first one. A bare call (no keyword)
   still crashes loudly. Lambdas and routine values are covered the same way: a
   failure inside a lambda handed to another routine
   (`try xs.select(transform: x => 10 // x).to[List]()`, `check xs.sort_by(compare: f)`,
   a user routine that calls the value it was given, a routine value passed down
   several levels or kept in a field) is recovered by the keyword above the call
   that reaches it, at any depth. A routine value called with no keyword above
   that call still crashes on failure. An `each` loop over an adapter holding a
   lambda ends only when its source ends: a failure inside the lambda is the
   loop's failure, recovered by a keyword above the loop or crashing without one.
   Not covered: a crash inside a library routine declared without `!` (other
   than in a routine value you handed it) stays a crash, and native runtime
   fatal errors, out of memory and running out of stack are fatal everywhere.
   **`pierce`** is the failure that must NOT be recovered: `pierce NopeError()`
   crashes like an unrecovered `throw` (status 82), and it crashes the same way
   under `try`/`check` at any depth. Use it for a broken invariant (state
   the program can no longer trust), never for a condition a caller could handle.
   It does not make a routine failable (no `!` needed for it).
   **`todo`** is a statement for code not written yet (`routine parse(bytes: Bytes) -> Header` with the body
   `todo`): running it crashes with `NotImplementedError` naming the routine, like `pierce`. It does not make
   the routine failable and ends its path, so a body that is only `todo` needs no `return`. The build warns
   once per routine that still has one (RF-W303). `pass` is an empty body on purpose, a different thing.
   The keyword takes the whole following expression down to `??` (so `try f() ?? d` = `(try f()) ?? d`);
   a trailing `!!` applies to the recovery result. There is no `try_foo` name form —
   recovery is a keyword at the call site, not a mangled variant name. There are no
   exceptions in the Java/C# sense.
   **The errors.** `crashable NopeError` declares an error type: a RECORD. It must
   write `routine NopeError.crash_message() -> Text` (RF-S704); `crash_title()` is
   given (the name in sentence case, `AbsentValueError` → "Absent value error") unless
   it writes its own. There is no `Crashable` protocol. `Crashables` (plural) is the
   stdlib record for "any caught error": `crash_title()`, `crash_message()`,
   `represent()`, `diagnose()` and `crash_type_id()` on it reach the actual
   crashable's own routines.
   **Matching a carrier.** `Check[T]` is the variant `T | Crashables`. A `when` on one MUST have the `T` arm (build error
   otherwise). `is Crashables as e` takes any error; `is NopeError as e` takes only that
   crashable and binds it as a `NopeError` (fields readable); `x is NopeError` works
   as an expression too. A `when` with NO error arm passes the error on: in a
   failable routine as if `is Crashables as e => throw e` were written (it propagates),
   anywhere else it crashes loudly. `throw e` of a caught `Crashables` rethrows it. A caught error keeps the
   trace of its first throw: if it ends the program after being rethrown or passed on, the report shows that
   trace (the routines between the throw and the catcher included) and a `Thrown again at file:line:col` line.
   Every crash report is headed by the error's crash title (`Division error: ...`, the type name in sentence
   case unless the crashable writes its own `crash_title`), runtime crashes too (`Integer overflow error: ...`).
   `is None` is a `Maybe` arm only (a `Check` has no absent state: "not found" in a
   `Check` is the arm `is AbsentValueError`). When the value is itself a variant, an arm may name the types it holds instead of the
   whole value (`when check parse(...)` with `is Deposit as d`, `is Withdraw as w`, `is Crashables as e`),
   and those arms together must name every one of them. The same holds on a plain variant that holds another
   variant (`is Deposit` on an `Event` holding `Command`).
   **A crash exits the process with status 82** (`0x52`, `'R'` for Runtime error —
   `RF_EXIT_CRASH`), for every runtime crash (unrecovered `throw`, checked-
   arithmetic failure, out of memory, stack overflow). If a run ended with 82, it
   crashed: read stderr for the error name, message, and stack trace. Status 1 is NOT a
   crash. A native signal (e.g. a segfault) is not 82 either — that is a builder or
   runtime bug (or `danger` code), not a language failure. A run that returns from
   `start` normally exits with 0, or with the status `set_exit_code(code)` (an `S32`,
   in Core) set last: one atomic value, so any thread may set it and the last call
   wins. A crash keeps 82 whatever was set.
8. **A number takes its type from where it goes; variables do not adapt.**
   A number carries no type of its own: there are no type suffixes (`5_u8`, `10n`, `2.5dn` are RF-G010). It
   conforms to the place it goes (an argument, an assignment, the other side of an operator): `h << 5` and
   `x.clamp(0, 100)` are fine, and a whole number written in base 10 conforms to a float or decimal type too
   (`var r: B64 = 3`, `price: 5` for a `D128` parameter; `0xFF` does not). Where nothing gives it a type, the
   declaration states it (`var x: U8 = 5`), and otherwise it is `S64` (`B64` with a fraction point). A
   variable's type is never inferred from its later uses. `migrate --step suffix` rewrites old code. Mixing a
   `U64` variable with an `S64` variable needs an explicit conversion
   (`n.to[S64]()`, §4).
   Out-of-range literals are build errors (RF-S010): `-1` never fits an
   unsigned type — spell all-ones as `U8_MAX`, `U64_MAX`, etc.
9. **A result you mean to throw away takes `drop`**: `drop name.trim()` (RF-W007 otherwise). A routine marked
   `@discardable` returns a by-product (`Set.add`/`Set.remove`/`Dict.remove`'s Bool, `Atomic.fetch_*`'s old value,
   `File.write_text`'s count): its result may be dropped silently, `seen.add(value: v)`. `drop x` on a local
   binding the routine owns (a local, an entity parameter handed over with `steal`) tears it down there and ends
   the name like `steal x` (RF-S615 after, RF-S616 on some paths only, tokens from it end, RF-S643); a member
   variable, a token, `me` or a global is RF-S421.
10. **Indentation is 4 spaces and blocks are indentation-delimited.** No braces.
11. **Chained comparisons are one expression**: `0 <= x <= 10` works. A chain goes one way: `<`, `<=`, `==`
    together or `>`, `>=`, `==` together (`a < b > c`, `!=` and `<=>` in a chain are errors). Every
    operand is evaluated once, eagerly, left to right, before any comparison:
    `a < b < f()` calls `f` even when `a < b` is false. Only the comparisons stop
    at the first false link. A failure in any operand is a failure like any
    other (`try 1 < 5 < 10 // n` recovers the division by zero).
12. **Printing is `show(...)`** (after `import IO/Console`), not print/println. Reading a line as a value is `ask[T]()`
    (`var n: S64 = ask()`), failing like `line.to[T]()`. A tuple reads the line's words, one item each:
    `var (y, m, d): (S64, S64, S64) = ask()`, `"2024 4 1".to[(S64, S64, S64)]()` (a `Text` item is the word itself;
    the word count must match, InvalidValueError otherwise).
13. **The entry point is `routine start()`**, not `main`.
14. **Infix routines**: a member routine with one parameter, marked `@infix`, is written between its
    operands (`a dot b` = `a.dot(b)`), left to right, looser than ranges and tighter than comparisons
    (`(a dot b) / n` needs the parentheses, `a dot b > 0` does not). Unmarked: RF-S706.

## 1b. Naming, terminology, and conventions (use the RIGHT word)

RazorForge has its own idiom — do not import Rust/serde/C# vocabulary.

- No kind or scope prefixes on names (`m_pos`, `_count`, `p_node`, `bDone`, `IComparable`): `lint` hints them.

- function → **routine**; method → **member routine** (`Type.name()`).
- struct/class → **record** (value) or **entity** (heap reference). Never
  "class"/"struct".
- field → **member variable**, declared as a bare `name: Type` line — **no `var`,
  no `open`**. Only restricted visibility is written: `posted` (module-write) /
  `secret` (type/module-private). `open` is the unwritten default. Giving a `secret` or `posted` member
  variable a value in `T(field: value, ...)` is allowed only inside the type's module (RF-S403 / RF-S404):
  outside, build it with a routine the module opens (`HttpResult.ok(body: b)`). There is no
  static `let`/`var` distinction for members. Locals inside routines still use
  `var` / `lateinit var`.
- tagged union / sum type → **variant**; plain enum → **choice**; bitfield →
  **flags**. `choice`/`flags` **variant names are SCREAMING_SNAKE_CASE**
  (`LIVE`, `READ`, `NEGATIVE_INF`); routine names and locals stay `snake_case`.
  A flags value combines with `and`, tests with `has` / `has_any` / `lacks` / `==` (`perms.has(READ and WRITE)`
  is all of them, `perms.has_any(READ and WRITE)` at least one, `perms.lacks(READ and WRITE)` none), and drops
  members with `without` (`perms.without(WRITE)`: where a value of the flags type goes, its members are named bare).
  A `choice` auto-provides `Equatable`/`Comparable`/`Hashable` — no `obeys` and no
  method bodies needed.
- **`Me` (capital) = the receiver's TYPE (the Self type); `me` (lowercase) = the
  receiver INSTANCE.** Do not conflate them.
- The generic-constraint keyword is **`needs`** (e.g. `needs T obeys P`), **not
  `where`** — `where` is not RF syntax.
- **Generic parameter names.** A type parameter is `T` alone only when it is the
  only one and its role is obvious (`List[T]`, `Maybe[T]`, `Array[T, COUNT]`).
  Otherwise every type parameter is `T` + a PascalCase role (`Dict[TKey, TValue]`,
  `select[TResult]`, `Guarded[T, TPolicy]`, `LLVM::sign_extend[TFrom, TTo]`), never
  another single letter (`K`, `V`, `U`, `E`, ...). A const generic is
  SCREAMING_CASE with a meaning (`needs U64 COUNT`, `BITS`, `LANES`, `ARITY`),
  never `N`. Why: the `T` prefix shows at a use site (`var k: TKey`, a hover
  `get(key: TKey) -> TValue?`) that a name is a type parameter and not a real
  type, and SCREAMING marks a buildtime constant (like `preset`s and `U64_MAX`),
  so case alone tells type / type parameter / buildtime constant / runtime value
  apart. A receiver pattern's own parameter shadows the owner's parameter of the
  same name (`routine List[Agent[T]].gather() -> List[T]`: that `T` is the pattern's).
- Say **"buildtime dispatch" / "runtime dispatch"**, not "static / dynamic".
  Everything monomorphizes; RF's default is buildtime dispatch, no runtime dispatch.
- varargs parameter notation is **`name...: Type`** (the `...` follows the param
  NAME, before the colon); it binds to a `List[Type]`.
- **RazorForge has NO "borrow" concept** (that is Rust). Do not use the word
  "borrow" in prose or API names. Ownership is expressed via entities + `steal`
  + RC wrappers + **access tokens** (below).
- builder-synthesized per-type routines are **wired routines** (`represent`,
  `diagnose`, `serialize`, the constructor, `eq`, `cmp`, `hash`, …) — named with **NO
  sigil**; wired-ness is INFERRED from protocol conformance. Not
  "derives"/"macros"/"trait impls". (The `$` sigil is a separate, unrelated
  feature — a buildtime SPLICE, e.g. `$nameof(m)`.)
- **user prefers methods over free routines.** Prefer `routine Type.name(...)`
  (implicit `me`) over a free routine; putting capability-generic dispatch on a
  free routine is an anti-pattern (make it a method whose bound type is `Me`).
- One TYPE declaration per file (its routines/conversions may accompany it).

## 2. Program skeleton

```razorforge
module My/Module/Path
import IO/Console

routine start()
    show("Hello from RazorForge!")
    return
```

A single-file program may skip `routine start()` (script mode): loose top-level
statements, plus top-level `var`s, run top to bottom as the body of an implicit
`start()`, and teardown happens at the end of the file.

```razorforge
import IO/Console

var total = 0
each n in 1 to 5
    total += n
show(f"total = {total}")
```

- Declarations (`import`/types/routines/`preset`) may sit among the statements.
- Mixing loose statements with an explicit `routine start()` is an error (RF-G150).
- Only the program's entry file may be a script. Loose statements in an imported
  file are an error (RF-S443) — they would start a second program.
- A top-level `var` lives in the script's top-level scope, not the module:
  routines in the file cannot see it (RF-S444). Pass it as an argument —
  RazorForge has no module-level mutable state.
- Routines you define are still their own scopes and still end with `return`.

`module <Path>` is optional. Without it the module path is derived from the file's
location relative to `config.toml`: each folder and the file name are PascalCased and
joined with `/` (`tools/text utils.rf` → `Tools/TextUtils`). Declare `module` to
override that. Imports use `/` separators.

**Two import forms, distinct meaning:** `import Foo/Bar` is a **module** import
(`Bar` is a submodule of `Foo`). `import Foo.bar` is a **member** import (`bar` is
a type/routine/preset in module `Foo`). `Core` is auto-imported everywhere.

**Globals (RazorForge).** `global name: Type = init` (type and initializer both required):
initialized in dependency order when `start()` begins (a cycle is RF-S436, no `start()` is
RF-S438), torn down at every exit of `start()`. Every thread reaches it, so the binding is
never assigned again: shared changing state is an `Atomic` (`hits.fetch_add(delta: 1)`) or a
`Guarded` (`using cache.amend() as c`), any other global is only read (writes are RF-S251 /
RF-S256). A module-level `var` is an error (RF-S435). Constants the build knows are `preset`.

## 3. Types

- Signed ints: `S8 S16 S32 S64 S128 S256` · unsigned: `U8 U16 U32 U64 U128 U256`
- Binary floats: `B16 B32 B64 B128` · decimals: `D32 D64 D128`
- `BF16` (bfloat16) is a STORAGE type (ML weights/tensors): bit patterns (`from_bits`/`to_bits`/bytes),
  `b.to[B32]()`/`b.to[B64]()` widen exactly, `x.to[BF16]()` (from `B32`/`B64`) narrows with ONE
  round-to-nearest-even (from `B64` directly, never through `B32`), `represent`/`t.to[BF16]()`, `==`/`<`. No arithmetic and
  no literals of its own: widen to compute, narrow the result back.
- Hex float literals (binary floats ONLY): `0x1.8p3` = hex mantissa × 2^exponent (= 12), `var eps: B32 = 0x1p-23`,
  `0x1.fffffep+127` where a `B32` goes. The `p` exponent is required (`0x1.8` is not a float). The value must fit the type
  EXACTLY: too many significant bits or a bit below the smallest subnormal is RF-S018, not a rounding. On a
  `D32`/`D64`/`D128`/`Decimal` type it is RF-S017. It takes a binary float context, else `B64`
  (in Suflae too).
- Arbitrary precision: `Integer`. `Decimal` is a finite-only decimal with 34 significant digits (decimal128, canonical, never inf/NaN) (a number takes them from context, `var big: Integer = 10`). No arbitrary-precision binary float or
  complex: `B128` and `C256` are the widest.
- Complex (total-bit): `C64` (2×B32) `C128` (2×B64) `C256` (2×B128). Imaginary literal is the width-less
  `i` (also `_i`): `3 + 4i`. Width comes from context (a `C64`/`C256` slot), and with no context it
  defaults to `C128`. Plain `3` fills the real part, `4i` the imaginary.
- `Bool`, `Text` (UTF-32 string), `Char`, `Byte`, `Bytes`
- `Duration`, `ByteSize` (with literal forms), `Moment`/`LocalMoment` temporals
- Collections: `List[T]`, `Dict[TKey, TValue]`, `Set[T]`, `CircularList[T]`, `BitList`,
  `PriorityQueue[TPriority, TElement]`, `SortedDict[TKey, TValue]`, `SortedList[T]`, `SortedSet[T]`,
  fixed-size `Array[T, COUNT]`, `BitArray[BITS]`
- Tuples: `(TFirst, TSecond)` / `Tuple[TFirst, TSecond]`, with fields `item0`, `item1`, ...
- Carriers: `Maybe[T]`, `Check[T]` (builder-synthesized only —
  user routines cannot declare them as return types; you obtain one by wrapping a
  failable call with the `try`/`check` keyword).
- A number never names its type (no `7s32`, `0_u64`, `10n`: RF-G010). Unit suffixes stay: durations
  (`50ms 5s 2m 1h 1d 1w us ns`), sizes (`4kb 4kib 2mb ...`), and the imaginary `i`.
- `inf` and `nan` are number literals (reserved words) that take their type from where they go: `var x: B64 = inf`,
  `limit: B32 = -inf` (negative infinity is `-` on `inf`), `return nan`, `x < inf`. Every binary float (B16 B32 B64
  B128) and the decimal floats D32 D64 D128 take them. With nothing giving a type (`var x = inf`) it is RF-S019,
  and where a `Decimal`, `Integer` or integer type goes it is RF-S020. There are no infinity/NaN presets and no
  `inf_b64` spelling (RF-G010).

**Optionals and `none`:**
- **`T?` (postfix `?`)** = `Maybe[T]` optional shorthand — the ONLY `?`-on-a-type
  form. There is no prefix `?T`.
- **`None` (capital)** is the type / pattern marker (`when x is None`, type
  position). **`none` (lowercase)** is the value literal (like `true`/`false`).
- `none` is legal ONLY where the target type is a carrier with an absent arm
  (`Maybe[T]`) or a variant with a `None` member.
  `var x = none`, `var x: S64 = none`, `foo(none)` into a non-carrier slot are all
  errors — `none` has no free-standing type.
- Absent is matched by `is None` on `Maybe[T]` / a variant's zero
  tag. `Check[T]` has no absent state (only `Crashables | T`).

**Variants written in place:** `-> S64 | Text`, `List[Circle | Square]`, `x: S64 | Text | Bool`. The same types in
any order are one type; a value of a member goes in as an argument, initializer, `return` or new value; a smaller
variant goes where a larger one is expected; `define Circle | Square as Shape` names it. Taken apart with `when` /
`is T as t`.

## 4. Variables and operators

```razorforge
var count = 0            # variable binding (the everyday declaration), S64
var flags: U8 = 0        # the declaration states the type a number takes
count = count + 1        # bare literal 1 conforms to S64

preset MAX_RETRIES: S32 = 5   # named constant: UPPER_CASE, explicit type required
```

There is **no `let` and no `const`** — `var` for bindings, `preset` for
constants. A preset is computed when the program is built: one that calls a routine, does arithmetic or fills in
a text runs once during the build and becomes the value it came to (number, Bool, Text, Char, or a record/tuple/
choice of those; RF-S258 if it fails or has no such value). A preset made by a construction is one read-only value
everywhere (an entity preset has one identity). (`lateinit var x: T` defers initialization: storage is allocated at
the declaration — entities get a real zeroed block, the constructor not run — so the
binding is immediately valid and borrowable; assign before reading.)

**No shadowing inside a routine.** A `var` may not reuse the name of a parameter
or of a local of an enclosing block (RF-S008, the same rule as `when` pattern
bindings): one name is one variable within a routine. Sibling blocks may each
declare their own `i`.

- Checked: `+ - *` (throw on overflow) · wrapping: `+% -% *%` · clamping: `+^ -^ *^`
  (floats: checked `+ - * / **` or raw IEEE `+! -! *! /! **!` with no `danger` needed)
- Shifts: `<<` left, `>>` right filling with the sign bit, `>>>` right filling with
  zeros (the same as `>>` on unsigned types). There is no `<<<`. A shift amount may be
  any integer type (`x << n` with `n: S64`); a negative amount crashes. Shifting by the
  width or more pushes every bit out: `<<` and `>>>` give 0, `>>` gives -1 for a negative
  value, else 0. A subscript index may be any integer type too (`xs[i]` with `i: S64` or
  `U8`); a negative index crashes with `NegativeIndexError`. The named routines (`x.ashl(bits:)`, `xs.getitem(index:)`)
  keep their parameter types.
- `//` floor division, `/` true division, `%` remainder
- `abs()` on signed ints is failable (`abs!()` throws on MIN); the force-unwrap
  idiom is `try x.abs()!!`
- Equality `==`/`!=` compares VALUES (lowers to `.eq()`); identity `===`/`!==`
  compares whether two references are the SAME object (entities / access-token /
  RC-wrapper operands only — RF-S440 on value types). See §7.

**Conversions: `x.to[T]()`, the one way to convert.**

```razorforge
import Numerics.Integer            # Integer is not in Core

var small = count.to[U8]()        # number to number
var big = count.to[Integer]()      # Integer/Decimal convert on to B32/B64 (nearest value)
var n = "42".to[S64]()             # text parsing (also Integer, Decimal, B64, ...)
var code = c.to[U32]()             # a Char's codepoint (b.to[U8]() for a Byte, a.to[U64]() for an Address)
var letter = code.to[Char]()       # 'A' when code is a U32 of 65, and 'x'.to[Text]() makes one-character text
var byte = bits.to[Byte]()         # the same bits as a Byte, from a U8
var raw = status.to[S32]()         # a choice's value (also to[S64]()), perms.to[U64]() a flags bitmask
var wide: U64 = count.to()         # T left out: the declared type gives it
var fits: U8? = try count.to()     # none when it does not fit
var low = count.to_wrap[U8]()      # the low 8 bits (300 -> 44), never fails
var near = count.to_clamp[U8]()    # the nearest U8 (300 -> 255), never fails
var why: Check[S64] = check line.to()
```

- Widening never fails. A value that does not fit `T`, or text that does not read as a `T`, crashes
  loudly (`IntegerOverflowError`, `InvalidValueError`, ...). Recover with `try` (→ `T?`) or `check`
  (→ `Check[T]`, holding the error).
- `[T]` may be left out where the surrounding code says the type: a declared variable type, the
  routine's return type (`return count.to()`), a parameter (`takes(n: x.to())`), and through
  `try`/`check`. Where nothing gives it (`var n = count.to()`, mid-chain `count.to().add(1)`) it is
  RF-S645: write `.to[S64]()`.
- A pair with no conversion is RF-S646 at the call ("There is no conversion from 'Text' to 'Bool'.").
  Converting a value to its own type is RF-S646 too (use the value as it is), and so is `.to[...]()` on a
  value of a type parameter: conversions are written per concrete type, so take the parameter as the
  target type or convert where the type is known.
- Constructor or conversion, by one checkable rule: a constructor's arguments become parts of the new value as
  they are (`Point(x: 1, y: 2)`, `Retained(from: steal e)`, `SerialValue(from: hp)` putting an `S64` in an arm);
  a value computed from one value of another type is a conversion, `x.to[T]()` (`"hi".to[CStr]()`,
  `local.to[Moment]()`, `ratio.to[Q128]()` from a `B32`). So `S64(from: x)` is not a routine. A variant's arm comes out
  the same way: `sv.to[S64]()` / `sv.to[Dict]()` is failable (another arm is active → crash, `try` gives
  `None`). The old spellings `sv.S64()`, `S64(from: sv)`, `Dict[Text, SerialValue](from: sv)` are gone (RF-S458 /
  RF-S646 point at `.to[...]()`).
- Each conversion is its own routine, a specialization written with the target type in the bracket:

  ```razorforge
  routine Fahrenheit.to[Celsius]() -> Celsius
      return Celsius(degrees: (me.degrees - 32.0) * 5.0 / 9.0)
  ```

  A call picks the one whose bracket matches (`f.to[Celsius]()`, or `var c: Celsius = f.to()`). A
  conversion that can fail is declared `routine Text.to![S64]() -> S64`. A routine written for every
  choice (`routine T.to[S32]() needs ChoiceType T`) reaches every choice type.

**Precedence**, tightest first:

1. indexing, member access, calls
2. `**` (right-associative, the exponent may carry a sign: `2 ** -1`)
3. unary `-`, `not`, `~` (`-x ** 2` is `-(x ** 2)`)
4. `* / // %` (and their `%`/`^`/`!` variants)
5. `+ -` (and their variants)
6. bitwise `<< >> >>> & ^ |`, ONE level: different bitwise operators side by side
   need parentheses (`(a & b) | c`, `(a >>> 58) << 2`), a chain of one does not
   (`a | b | c`). Shifts are looser than arithmetic: `1 << n + 1` shifts by `n + 1`.
7. ranges `to` / `til` / `by` (`0 to n - 1` reads as written, a range test is a call: `(0 to 10).has(x)`)
8. comparisons `== != < <= > >= <=>` (chained), `is` `isnot` `obeys` (`x & 1 == 0` is `(x & 1) == 0`).
   Containment is not an operator but a call on the container: `xs.has(v)` / `xs.lacks(v)`, and on a map
   `d.has_key(k)` / `d.lacks_key(k)` (there is no `contains`, and `x in xs` as a test is RF-G212)
9. `and` / `or`, ONE level: mixing them needs parentheses (`a or (b and c)`), a
   chain of one does not
10. `??`

Mixing `and` with `or`, or two different bitwise operators, without parentheses is RF-G216.

## 5. Control flow

```razorforge
if x == 3
    show("three")

each x in 1 to 5          # range iteration
    if x == 3
        continue
    if x > 4
        break
    show(f"x={x}")

while i < 10
    i = i + 1

loop                      # infinite loop, exit with break
    n = n + 1
    if n >= 3
        break
```

`until c` loops while `c` is false (`while not (c)`). A `while`, `until`, `each` or `loop` may end
with a `then` block. It runs once whenever the loop is left for the line after it: when the loop ends by itself
and after its own `break` (a `break` of a loop inside only leaves that loop). A `return` or `throw` leaves the
routine and skips it. A loop whose `then` ends in `return` always returns, so the routine needs nothing after it.
Loops have no `else` (RF-G219): the case where the loop would not run even once is an `if` before the loop.
A loop may carry values from one round to the next: `while n > 1 with n: U64 = start, steps: U64 = 0`
(`each x in xs with sum: S64 = 0` too). Each carried value is bound anew every round, from what the round's
`continue with n: after, steps: steps + 1` passes (every carried name once, in any order, all computed before
any is stored, so `continue with x: y, y: x % y` swaps). The names are visible in the condition, the body and
the `then` block, and nowhere after the loop. A body that carries values must not run off its end: every path
ends in `continue with`, `break` or a `return` (RF-G217), a bare `continue` in such a loop is RF-G218, as is
`continue with` in a loop without `with`. `break` takes no values. The idiom replaces a `var` that only the
loop changes:

```razorforge
routine collatz_steps(start: U64) -> U64
    while n > 1 with n: U64 = start, steps: U64 = 0
        var after = if n % 2 == 0 then n // 2 else 3 * n + 1
        continue with n: after, steps: steps + 1
    then
        return steps
```

`when` is the pattern match:

```razorforge
when m
    is None => show("absent")
    else as p  => show(f"present: x={p.x}")   # else with binding

when n
    == 0 => throw DivisionByZeroError()
    == 1, 2 => return "one or two"    # several values in one arm, after ==
    else => return "many"

when v
    is Crashables as e => show("failed")
    is S64 as x       => show(f"ok: {x}")     # type pattern with binding

when cell                                     # record patterns, written like construction
    is Chest(item: is Weapon as w) => equip(w)
    is Monster(hp: 0) as m => remove(m)       # a value it must equal, a name for the whole
    is Monster(hp: < 5, name) => flee(from: name)   # `name` alone = `name: as name`
    is Monster => pass                        # an arm testing a member variable doesn't cover the type
```

The same record pattern works after `if`: `if cell is Monster(hp: 0) as m`.

In a `when` statement an arm may be a block: break the line after `=>` and indent
the statements one level, as in an `if` body. Keep a one-statement arm on one line.

```razorforge
when terrain
    == Terrain.SPIKE_TRAP =>
        me.hero.position = cell
        me.spring_trap()
    == Terrain.MONSTER => me.battle(cell)
    else => me.hero.position = cell
```

A `when` that gives a value (`var label = when …`) takes one expression per arm,
or a block arm that ends with `becomes value` (its lines run, then `value` is the
arm's value). A block arm without `becomes` is RF-S307, `becomes` elsewhere RF-S308.

## 6. Routines

```razorforge
routine add(a: S64, b: S64) -> S64
    return a + b

routine Point.magnitude() -> B64        # method: Type.name, receiver is `me`
    return (me.x * me.x + me.y * me.y).to[B64]().sqrt()

routine get_text!(n: S64) -> Text      # `!` = failable
    when n
        == 0 => throw DivisionByZeroError()
        else => return "ok"

dangerous routine raw_poke(p: Address)  # callable only inside danger blocks
    ...
    return
```

- Call with named args: `add(a: 1, b: 2)`. Recover a failable call with a keyword:
  `try get_text(n: 0)` → `Maybe[Text]`/`Text?`; `check get_text(n: 0)` → `Check[Text]`
  (keeps the `Crashables`). A bare `get_text(n: 0)` crashes loudly on failure.
- **Wired routines** are builder-synthesized lifecycle/operator hooks (NO sigil):
  the constructor `T(...)`, `destruct`, `copy`, `eq`, `cmp`, `represent` (to-text), `diagnose`
  (debug text), `getitem!`/`setitem` (indexing), `iter`/`next` (iteration), `add`
  etc. (operator overloads). Wired-ness is inferred from protocol conformance.
- Failure inside a failable routine: `throw SomeError(...)`. A routine with no
  value to give throws `AbsentValueError()`.
- The `!` goes on the DECLARATION only (`routine S64.from_digit_bytes_at!(...)`,
  `routine Celsius!(from: Text)`). Calls never write it:
  `S64.from_digit_bytes_at(bytes: bs)`, `x.divmod(other: m)`, `t.to[Celsius]()`.
  `foo!(...)`, `x.foo!(...)` and `T![..](...)` are RF-G211; `foo(args)!` is a parse
  error too. A constructor is declared `routine T(...)` / `routine T!(...)`
  (`routine T.create(...)` is an ordinary member routine named `create`), and
  overload resolution picks the failable or plain one for you.
- **Annotations are a fixed set.** `@readonly`, `@writable`, `@reshaping`, `@discardable`, `@positional`,
  `@infix`, `@inline`, `@no_optimize`, `@crash_only`, `@untraced`, `@blocking`, `@layout(...)`, `@link(...)`,
  `@target(...)`, `@test`/`@case`/`@time_limit`/`@memory_limit`/`@skip`/`@bench` (test runner, not run yet) and the
  library's own (`@llvm`, `@llvm_ir`, `@rf`, `@innate`, `@generated`, `@override`, `@overridable`,
  `@self_locking`). Any other name is RF-G221 (a misspelling names the close one). There is no
  `@deprecated` and no `@initonly`.
- Bare routine names are first-class values: `select(transform: double)`
  (free routines only).

## 7. Records vs entities (the memory model)

```razorforge
record Point          # VALUE type: copied, no identity, no destructor
    x: S64
    y: S64
# construct memberwise, strictly named:
var p = Point(x: 3, y: 4)

entity Resource       # HEAP type: single owner, deterministic destruct
    tag: S64

var b = Resource(tag: 7)
consume(r: steal b)   # ownership moves; using b afterwards = build error
# destruct runs exactly once, at the owner's scope exit (anchored at `return`)
```

- Containment is ownership: one owner at a time; `steal` marks every transfer.
- A written `destruct` (a plain `routine T.destruct()`, never `dangerous`: RF-S807) tears down what the entity
  owns (its member variables, buffers it holds) and nothing else: the builder gives back the entity's own box
  before each of its returns, so one `destruct` call still ends the entity. Do not write
  `me.hijack().invalidate()` in it. A step that handles raw memory sits in a `danger` block inside the body.
- `destruct` is the builder's to call: at the scope's end, or where you write `drop x`. A program's own
  `x.destruct()` is RF-S812 (write `drop x`). Only a type's `destruct` body calls its member variables'
  `destruct` by name.
- Taking a value out of a member variable or an element is a statement:
  `steal slot.item as old inject Item(name: "b")` puts the new value in and binds the
  old one to `old` in one step (`fresh` is computed first, so the place is never
  seen empty). Without `inject`, a `T?` place is left `none` (`steal slot.spare as
  kept`), and any other place is RF-S647. An element goes through its container
  (`steal items[0] as first inject Item(name: "y")`). A whole variable still moves
  as an expression (`consume(r: steal b)`). The statement takes only a place
  (RF-S648 otherwise).
- Writing over a place (`slot.item = Item(name: "b")`, `items[0] = item`,
  `d[k] = v` on a key that is there) tears the old value down first, an entity
  included, so a token on it ends. To keep the old one, take it out with
  `steal … as old inject …`.
  Returning a bare entity transfers ownership to the caller (`var a = make()`
  owns it, torn down at scope exit). A routine handing back an entity it does NOT
  own (e.g. a container's element) must return an access token, never a bare
  entity — else the caller becomes a second owner and double-frees.
- **Access tokens** (RF's answer to "borrow" — never call them borrows). A token
  remembers the object it was taken from (its SOURCE), and every rule below is
  checked at build time:
  - Use one inline, keep it in a variable (`var v = a.view()`,
    `var m = a.modify()`, re-pointable to another object of the same type), or
    open it with `using ... as`. Tokens are never stored in fields or variants.
  - The source is free to change. Replacing it (`a = …`, `h.inner = …`), moving
    it out (`steal a`), leaving its scope, or calling a routine that replaces it
    (`h.reset()` whose body assigns `me.inner`, followed through its calls) ENDS
    the token. Using an ended token is RF-S643, reported at the use. A change that
    cannot reach the source (`h.count = 1`, `h.bump()` touching only `count`)
    does not end it.
  - A call that can replace the source of a token handed to that same call is
    RF-S639 (`h.replace_with(t: h.inner.view())`).
  - A token parameter takes the entity itself: `total(items: xs)` passes
    `xs.view()` for `Viewing[T]` (a temporary works too), `grow(items: xs)` passes
    `xs.modify()` for `Modifying[T]`. A record value or a value of a type parameter
    is handed over as the address of the place it is in, and a handle or a wider
    token over a `T` is narrowed the same way: `Retained[T]`, `Roamed[T]`, `Amending[T]`,
    `Consulting[T]` or `Modifying[T]` to `Viewing[T]`, and `Retained[T]`, `Roamed[T]` or
    `Amending[T]` to `Modifying[T]`. Pass the handle as it is: writing `.view()`/`.modify()`
    on it adds nothing.
  - A routine may return a token taken from `me` or from a parameter the caller
    keeps owning (a token parameter):
    `return me.inner.view()`, or `peek(h: Viewing[Holder])` returning
    `h.inner.view()`. Every return takes it from the same parameter, never from a
    local, a temporary, or an entity parameter the routine owns (RF-S600). The
    caller's token has that argument as source.
  - An entity element read into a variable is a read token on the element:
    `var b = boxes[0]` holds `boxes.view_at(index: 0)`. Anything that can add to
    or remove from `boxes` ends it. Write through `boxes[0].n = 5` or take
    `boxes.modify_at(index: 0)`.
  - The lock tokens (`Consulting`/`Amending`) are always opened with `using`.
  - **`Viewing[T]` / `Modifying[T]`** — read / write intent on a directly-owned
    entity. Produced by `a.view()` / `a.modify()`.
  - **`Consulting[T]` / `Amending[T]`** — read / write intent on the inner value of
    a `Guarded[T, TPolicy]`, lock-guarded by the policy `TPolicy`. Produced by `s.consult()` /
    `s.amend()`, always via a `using` block; `s.try_amend()` is the NON-BLOCKING
    acquire (a hand-written method returning the token, used with `using … fallback`
    — NOT a recovery variant, so no `try` keyword). Two handles at once:
    `using multiamend(first: xs[i], second: xs[j]) as (a, b)` (address-ordered holds, never nested `amend()`s;
    the same entity twice is an `AliasedTokensError`). `using r as (a, b)` names the items of the tuple `r` gives.
  - **Two tokens that may be the same entity.** Two token parameters (or shared handles) on the same kind of
    container may be the same container, so holding an element of one (`var p = x.modify_at(index: 0)`,
    `var b = xs[0]` on an entity element, to the end of its block) while the other changes shape is RF-S640.
    `using multimodify(first: x, second: y) as (a, b)` hands out two write tokens it checked to be different
    entities (the same one twice is an `AliasedTokensError`), and inside it holding an element of `a` while `b`
    grows is allowed. `multimodify(first: x, second: y)` with `y` a `Viewing` hands out a write token and a read
    token (`each v in b { a.add(value: v) }` is then allowed). `multiamend` is the same for two `Guarded` handles.
    Handing one container to two parameters of one call when one of them is written
    (`extend(into: xs, from: xs)`) is RF-S623.
- **RC wrappers** (opt-in shared ownership, reference-counted):
  - **`Retained[T]`** — single-thread strong handle (`Retained(from: steal e)`, `=` makes one more holder);
    forwards direct access to the retained entity.
  - **`Guarded[T, TPolicy]`** — multi-thread strong handle (atomic, `=` makes one more holder);
    reaching its inner value goes through a `Consulting`/`Amending` token (RF-S649 on the handle), except
    `Guarded[T, ReadOnly]`, which has no lock and is read directly (`g.name`, `g.label()`, reads only).
  - A binding from outside an `if`/`when`/loop is taken with `steal` on every path going on past it or on none
    (RF-S616); a loop turn may not take it and go on (only where the loop ends).
  - `steal` on a `Retained`/`Guarded` gives the entity inside (`var t: Table = steal table`) when it is the last
    strong handle, crashing with `SharedHandleStealError` otherwise; weak handles can't be stolen.
    Every `Atomic` routine is `@readonly` (each change synchronizes itself): `stats.open.fetch_add(delta: 1)`
    works through a read token or a `ReadOnly` Guarded.
  - **`Tracked[T]`** (single-thread) / **`Witnessed[T, TPolicy]`** (multi-thread) — weak handles.
- **`Hijacked[T]`** — a non-owning raw handle (no-op destruct); the stdlib's
  internal buffer/pointer mechanism, used inside `danger`.
- Records never use tokens or `as_entity` — those are entity concepts.
- `danger` blocks / `dangerous` routines mark **only** operations that can
  ACTUALLY cause memory-unsafety — UB, a memory race, use-after-free,
  double-free, or a memory leak. Ordinary code (arithmetic, collection ops,
  channels, failable calls) is **never** wrapped in `danger`; it is not a
  catch-all for "risky" or "advanced". A `dangerous` routine can only be called
  inside a `danger` block.
- No lifetime syntax; safety comes from single ownership + marked transfers
  (`steal`) + scope-bound access tokens.

```razorforge
using c.view() as v          # Viewing[Counter], read-only, dead at block end
    show(f"count = {v.value}")
using c.modify() as m        # Modifying[Counter], write intent
    m.increment()
```

- **Identity comparison `===` / `!==`** — "are these two the SAME object?", distinct
  from value equality `==`. Valid only on reference-carrying operands: an `entity` or
  a forwarding wrapper (`Viewing`/`Modifying`/`Consulting`/`Amending`/`Retained`/
  `Guarded`/`Tracked`/`Witnessed`). A value type (record/scalar) is a build error
  (**RF-S440**) — values have no identity; use `==`. `Hijacked` is excluded (its `==`
  is already identity — you `peek` to see its value). It is a primitive pointer
  compare, never a `.eq()` call, so it is not overloadable. For single-owner entities
  `a === b` is trivially `false` between distinct bindings; it earns its keep where
  aliasing exists — two `Retained[T]` (or two `Viewing`/`Modifying` tokens) that may
  point at the same entity.

```razorforge
var r1 = Retained(from: steal a)   # Retained[Node]
var r2 = r1                        # second handle to the SAME node (one more holder)
show(r1 === r2)              # true  — same object
show(a === make_other())    # false — different objects
```

> **Decided direction (2026-09-22, NOT yet built — do not assume the builder does this today).**
> Full rationale in `internal-wiki/record-entity-finite-representation.md`.
> (The `bundle` part below the note is BUILT.)
> - **A recursive record is a build error; recursion belongs to entity.** Surface rule: *a record is a
>   copied value, an infinitely-sized value cannot be copied, so a record cannot contain itself.*
>   `record Node { next: Node }` → error (make it `entity Node`, whose `next: Node?` auto-indirects to a
>   nullable pointer; unique ownership makes the structure a tree = acyclic = deterministic teardown).
> - Generic param kinds: bare `[TKey, TValue]` = `AnyType`; annotate `T: record` / `T: entity` only to constrain.
>   No copyability bound (record ⟺ copyable). No `Boxed`/`Owned`/`Unique` wrapper — the ownership
>   wrappers below name only *departures* from the bare-entity default (unique ownership is unnamed).
> - Entity allocation is stack/inline by default; heap is induced by a wrapper (`Retained`/…) or recursion,
>   not a marked concern.

**`record` vs `bundle` (built, RazorForge only).** A `record` is a copied value, so it may hold neither an
entity nor a type parameter that could be one (**RF-S409**): a copy would make two of the one entity. Fix it
by declaring the type `entity`, holding a handle that copies (`Retained[E]`), keeping the parameter to values
(`needs RecordType T`), or declaring it `bundle`. A `bundle` is the generic aggregate whose kind each
instantiation decides: `bundle Pair[T]` with `T = S64` is a copyable value; with an entity `T` it is
single-owner (moved with `steal`, RF-S413 on a copy), has no identity (`===` is an error), is laid out in
place, is never copied by `=`, and has `duplicate()` only when every member is Copyable (RF-S150 naming the member
otherwise). A bundle needs type parameters (**RF-S445**) and cannot contain itself (**RF-S446**; a recursive
structure is an entity, as for records, RF-S960).
In a generic body, a value of an unconstrained type parameter may be an entity, so keeping a second copy of
it (`var y = x`, `return me.value`, a tuple of it) is RF-S413 on the template's own line: `steal` it, keep the
parameter to values (`needs RecordType T`), or work on it in place.
`needs RecordType T` accepts only values: a bundle instance holding an entity does not satisfy it. Several
kinds on one line repeat the kind: `needs RecordType TKey, RecordType TValue`. Stdlib bundles: `Maybe`, `Array`,
`SplitArray`, `DictEntry` (tuples and variants follow their elements the same way). `Range[T]`,
`Atomic[T]` and `SplitArray`'s element iterator are `needs RecordType`. `PriorityQueue[TPriority, TElement]`
asks only `TPriority obeys Comparable`. A queue is not walked directly: `pq.unordered()` and `pq.ordered()` copy its
`(priority, element)` pairs (storage order / smallest priority first, `needs RecordType TElement`), `pq.drain()`
empties it into them smallest first (`each (priority, element) in pq.drain()`). An
`Array`'s iterator reads each element in place, so `each r in arr` works on an array of entities (the binding
only reads; the array keeps owning each element). Suflae has no
`bundle` (an SF entity is shared, so a record holding one copies the handle).

## 8. Generics and protocols

```razorforge
routine largest[T obeys Comparable](items: Viewing[List[T]]) -> T
    ...

record Pair[TFirst, TSecond]
    first: TFirst
    second: TSecond

protocol Iterable[T]
relates Iter obeys Iterator[T]        # associated type slot
    routine iter() -> Me/Iter          # `/` projects an associated type

entity List[T] obeys Iterable[T]
relates ListEmitter[T] as Iter        # associated type binding
```

- Constraint syntax: `T obeys SomeProtocol`. `Me` is the self type.
- Const generics: `Array[T, COUNT]`.
- **Type arguments use `[...]`, never `<...>` — by design; do not propose `<>`.**
  `f < T > (x)` is already a valid chained comparison in RF, so angle brackets
  would force a parse-time guess (C#'s disambiguation rules, Rust's turbofish) plus
  `>>`/`>>>` token splitting. `List[S32]` and `xs[i]` share one parse shape
  (`postfix [ args ]`); whether the thing before `[` is a type or a value is
  settled by name lookup after parsing (`BracketReclassifyPass`), not by a
  heuristic. A generic argument is a type-level `getitem`.
- Everything monomorphizes; there is **no runtime dispatch** of any kind.
- A protocol member declared `@readonly` (`eq`, `cmp`, `hash`, the arithmetic operators, `count`, `iter`,
  `getitem`, `duplicate`, …) is implemented `@readonly` too, and its body is checked to change nothing:
  an implementation without it is RF-S771. Suflae has neither `@readonly` nor `@reshaping`.

## 9. Text and formatting

```razorforge
show(f"x={x} and pi≈{pi}")    # f-string interpolation
show(f"debug: {value:?}")     # :? = diagnose (debug) format spec
show(f"{n.hex()} {x.fixed(2)} {name.pad_end(10)} {n.represent().zero_pad(5)}")
```

The only f-text format specs are `=`, `?` and `=?`. Everything else is a member routine you call
inside the braces:
- **Integers** (`S8`..`S256`, `U8`..`U256`, `Integer`): `to_text(radix:)` (2 to 36, lowercase
  digits, `-` for negatives, crashes on another radix) and the shorthands `hex()` / `bin()` / `oct()`.
  No `0x` prefix; `.to_uppercase()` for `FF`.
- **Reals** (`B16`..`B128`, `D32`..`D128`, `Decimal`): `fixed(places)` — exactly `places`
  digits after the point, integer part in full, rounded half to even from the exact value
  (`2.675.fixed(2)` is `2.67`). `to_text(sig_digits:)` on
  `B16`/`B32`/`B64` is the significant-digit (`%g`) form. A binary float's plain text (`show(x)`,
  `f"{x}"`, `represent()`) is the SHORTEST decimal that reads back as the same value, so
  `x.represent().to[B64]()` returns `x` bit for bit on `B16`..`B128` (`0.1 + 0.2` shows
  `0.30000000000000004`, `0.1` shows `0.1`). Scientific form starts below `1e-04` and at `1e+04` /
  `1e+07` / `1e+16` / `1e+34` for `B16` / `B32` / `B64` / `B128`; a whole value drops the `.0`.
- **Text**: `pad_start(width)` / `pad_end(width)` / `center(width)` (fill defaults to a space) and
  `zero_pad(width)`, which keeps a leading `-`/`+` in front (`"-5".zero_pad(4)` is `-005`).

`Text` is UTF-32, a record (value type). `Bytes` is a record for raw byte
sequences with UTF-8 iteration helpers. A `b'x'` byte-letter literal has type
`Byte`; `b"..."` has type `Bytes`.

- **`Text` slicing** uses a range subscript: `text[a til b]` / `text[a to b]`
  (subscript forces `U64` indices), open-ended `text[a til ^0]` to the end.
  Beyond the basics, `Text` carries a rich pure-RF method set (search, split,
  case, padding) — e.g. `count_of`, `find!`/`find_last!`, `split_once!`,
  `replace_first`, `capitalize`, `center`, `eq_ignore_case`. Verify exact names
  in `Standard/RazorForge/Core/Types/Text.rf`.
- **`Bytes` `obeys Comparable, Hashable`** — usable as a `Dict` key and sortable.
  It supports range slicing (`bs[a til b]`), sub-sequence `contains`/`find!`/
  `split`, and hex `to_hex`/`from_hex!`. Container `represent` quotes elements
  by type (Text/Bytes → `"…"`, Char/Byte → `'…'`).

## 10. Collections quick reference

Verify exact signatures in `Standard/RazorForge/Collections/` — highlights
that differ from other languages:

- `list.add_last(value: v)`, `list.remove_first!()`, `list.remove_at!(0)` —
  removal is failable (empty/out-of-range throws); `try list.remove_first()`
  returns `Maybe`.
- `set.add(value: v)` returns Bool, `@discardable`: no `drop` needed.
- `dict[k] = v` puts a value: it replaces the value of a key that is there and adds the key otherwise (there is
  no `dict.add`). Reading `dict[k]` is failable under the hood, so use `try dict.getitem(key: k)` (or `try dict[k]`) when you want `Maybe[TValue]`.
- Indexing `coll[i]` is failable under the hood (`getitem!`); back-indexing
  is `coll[^1]` (last element).
- **Range slicing returns an owned COPY**: `xs[a til b]` (or `xs[a to b]`) on a
  `List`/`CircularList` yields a new `List`/`CircularList`, on `Array[T, COUNT]` a `List[T]`
  (slice length is a runtime value, so it cannot be a fixed `Array`). Mutating
  the slice never touches the original. Open-ended `xs[a til ^0]` slices to the
  end. Element type must be `Copyable` (slicing a `List[Entity]` is a compile
  error). For a lazy, no-copy window use the iterator combinator
  `xs.skip(a).take(n)` instead — copy-vs-view is spelled by which you call.
- `List[T]`, `Dict[TKey, TValue]`, `Set[T]`, `CircularList[T]`, and sorted collections are
  entities. Do not pass a container as a bare parameter when read-only access is
  enough; declare the parameter `Viewing[List[T]]` and pass the container
  (`largest(items: xs)`). Bind `var v = items.view()` when the token needs a name.
- `SortedList`/`SortedSet` have **no positional indexing** — rank access is
  `get_by_rank!(...)`.
- Iterator adapters (from `IterTools`): `select`, `where`, `zip`, `enumerate`,
  `chain`, `distinct`, `select_many`, `min_by`, … — lazily evaluated, chainable,
  lambdas like `x => x % 2 == 0`. An adapter points at the collection it was made
  from, so it is used in the statement that makes it (`each`, a terminal call such
  as `.to[List]()`, or an argument). Keeping one in a variable or field, or returning
  it, is RF-S644. A range (`var r = 0 til 10`) points at nothing and can be kept.
- Ranges use word operators: `1 to 5` inclusive, `1 til 5` exclusive. Without
  `by` a range counts UP: when the end is below the start it is EMPTY (`2 to 1`
  yields nothing, `(2 to 1).has(1)` is false). `by N` sets the step
  (`1 to 10 by 2`), and a negative step counts down: `10 to 1 by -1`,
  `10 to 0 by -2`, also on unsigned types (`3 to 0 by -1` over `U64` yields 3 2 1 0).
  A step held in a variable decides the direction by its sign. A literal
  `by 0` is RF-S516; a zero step held in a variable crashes with
  `RangeStepZeroError` when the range is iterated. `has`/`lacks` follow the
  same rule (`(10 to 0 by -2).has(4)` is true). Slicing with a counting-down range
  reverses: `text[4 to 0 by -1]`.
- `List`, `Set`, `Dict` live in **`Core`** (always available — never suggest
  `using Collections`). Only these three canonical collections have literal
  syntax of their own (`[1, 2]`, `{1, 2}`, `{1: 10}`); a specialized container
  (`SortedSet`, `CircularList`, `Array`, `BitArray`, `BitList`, `PriorityQueue`, …)
  takes the same literal when the variable names its type
  (`var s: SortedSet[S64] = {3, 1, 2}`, `var pq: PriorityQueue[S32, Text] = {1: "high"}`).
  An `Array` is also built from its elements: `Array[3](1, 2, 3)`.
- Tuple fields are accessed as `t.item0`, `t.item1`, … (NOT `t.0`/`t.first`);
  destructure with `var (q, r) = pair`. `(a, b) = (b, a)` swaps places (locals, member variables, entities
  included: values move, nothing is copied, no `steal`); any other tuple on the left is a build error.

**Mutating a collection while `each`-looping it is banned** (RF-S625): calling a
`@reshaping` mutator (`add`/`remove`/…) on the variable being iterated is a
build-time error — after a structural change the loop can no longer trust its
next element. Finish the loop, then mutate. A map's `d[k] = v` (and `d[k] += 1`) counts: it may add the key,
so it is banned inside `each` over `d` even when the key is already there. A list's `xs[i] = v` only replaces
an element and stays allowed.
The ban also covers a mutation hidden behind a call
(`each x in xs { grow(xs: xs.modify()) }`): the builder works out which
parameters each routine can add to or remove from, following its calls, and
rejects handing `xs` to one. Handing `xs` to a routine value, or changing another
token, `Retained` or `Tracked` handle to the same kind of container
(`each v in from { into.add(value: v) }`), is rejected too, because the builder
cannot tell whether it is the same container. Take both with
`using multimodify(first: into, second: from) as (a, b)` to loop over `b` and change `a`.
Every stdlib collection marks its adding/removing routines `@reshaping`.

**Writes into an element land in the container.** A call, field write, or index
write on an ENTITY element (`grid[0].add_last(value: 1)`, `boxes[0].n = 5`,
`grid[0][1] = 20`) acts on the element inside the container, not on a copy: the
builder reaches it through the container's `modify_at`/`view_at` token (List,
CircularList, Dict). In that same statement nothing may add to or remove from
the container, directly or through a call (RF-S639), since that could move the
element. A VALUE element works the same way (`m[i][j] = v`, `pts[k].x = v`):
the builder writes through a copy and stores it back.

**An entity element or field cannot be taken by a plain read.** `boxes[0]` and
`p.a` read an entity their container still owns, so putting one anywhere that
owns what it holds (`x = boxes[0]`, `p.b = boxes[0]`,
`take(b: boxes[0])`, `holder.add_last(value: boxes[0])`, `return boxes[0]`,
`(boxes[0], 1)`) is RF-S413: it would make two owners of one entity (a tuple
owns its items, so a variable goes in with `steal` too: `(steal z, 1)`). Take an
element out with a removing routine (`boxes.remove_at(index: 0)`), copy it
explicitly when its type is `Copyable` (`grid[0].duplicate()`), or work on it
where it sits. A new variable (`var x = boxes[0]`) is the exception: it holds a
read token on the element (see access tokens). Calling the reading routines
yourself copies a VALUE or handle element (`=`: a copy, one more holder for a
handle). A single-owner ENTITY element is never copied behind your back: there
is no copying `getitem`/`first()`/`last()` for it. Read it in place
(`boxes[0]`), or make a new one on purpose with `boxes[0].duplicate()` (the
entity must `obeys Copyable`, else RF-S150). Element
calls and field access (`cells[0].v = 5`, `grid[0].add_last(value: 1)`) still act
in place through `modify_at`/`view_at`, `var c = cells[0]` is still a read
token, and passing an entity element to an owning parameter
(`keep(c: cells[1])`) is still RF-S413 even when Copyable: write
`keep(c: cells[1].duplicate())`.

## 10b. Filesystem and paths (`IO/File`, `IO/FileSystem`)

Filesystem operations are **methods on two value types** in `import IO/File`:
`File` (a named regular file — byte content) and `Directory` (a named directory —
a container of entries). Both are cheap values wrapping a path (`at: Text`);
constructing one touches nothing. The *pure, OS-free* path-string math lives
separately in `import IO/FileSystem` as free routines (RF has no `Path` type;
paths are plain `Text`). This is distinct from the *async* file-content sessions
in §14 (also `IO/File`). Gotchas that differ from Python `os`/`pathlib`:

- **`File` (`File(at:)`):** `exists`/`is_file`, `size!`, `metadata!` (→
  `FileMetadata`), `extension`/`name`/`stem`, `parent` (→ **`Directory`**),
  `with_extension`, `absolute!`/`canonical!` (→ `File`), `read_text`/`write_text`,
  `set_size!`, `can_read`/`can_write`/`can_execute`, `set_readonly!`, `copy_to!` /
  `move_to!` / `move_to_if_absent!` (→ `Bool`, false if dest existed) / `delete!`
  / `touch!` (all take/return `File`). **Copy is `copy_to!`, NOT `copy`** — a bare
  `copy` collides with the structural `copy` derive verb.
- **`Directory` (`Directory(at:)`):** `exists`/`is_directory`, `metadata!`,
  `name`, `parent` (→ `Directory`), `file(name:)` (→ `File`) / `subdir(name:)` (→
  `Directory`) to build child paths, `absolute!`/`canonical!` (→ `Directory`),
  `can_*`/`set_readonly!`, `create!`/`create_all!`, `delete!` (empty) /
  `delete_all!` (recursive tree), `move_to!` / `move_to_if_absent!`, `copy_to!`
  (recursive tree copy, symlink-safe). Listing: `list!` (→ `List[Text]` bare
  names), `entries!` (→ `List[DirEntry]` with `name`/`is_directory`/`modified` in
  ONE read — prefer it when walking, no per-entry stat on Windows), `walk!` (→
  full descendant paths, recursive, symlink-safe).
- **Process/standard dirs (free routines in `IO/File`):** `current_dir!` /
  `set_current_dir!(directory:)`, `home_dir!`, `temp_dir` (never fails) — all
  return/take `Directory`.
- **Path builders (pure, no OS call — `IO/FileSystem`):** `join_path(a:, b:)`,
  `parent_path`, `file_name`, `file_stem`, `extension`, `split_extension` (→
  `(stem, ext)` tuple), `with_extension(path:, ext:)`, `with_file_name`.
  **`extension()` returns WITHOUT the leading dot** (`"txt"`, not `".txt"`), and
  round-trips through `with_extension`.

Not yet present (do not generate): symlink ops (`is_symlink`/`read_link!`/
`symlink!`/`hard_link!`) and `glob`.

## 11. CLI and project manifest

```
razorforge new [folder]             # start a project: config.toml, main.rf, .gitignore, .vscode/ (keeps existing files)
razorforge check [entry]            # check the program without building it
razorforge build [entry]            # build a native executable, don't run
razorforge run [entry]              # build and run
razorforge test <dir-or-file>...    # run programs against <name>.expected / .exit / .error / .input
razorforge fmt [--check] <paths>... # format in the canonical layout
razorforge lint <paths>...          # report code not in the canonical layout (style errors)
razorforge lsp                      # language server, for an editor
razorforge help | version
```

These are the same commands `suflae` and `tessera` have. A bare file is not a command
(`razorforge hello.rf` is an error that points to `razorforge run hello.rf`). The builder's own
inspection commands (`codegen [entry] [out.ll]`, `parse`, `tokenize`) still work but are not listed.

**There are no build flags.** All configuration lives in `config.toml`:

```toml
[package]
name = "my-app"

[target]
executable = "MainModule"   # entry MODULE name (the `module` decl, not a file path)
library = ["../shared"]     # external dependency DIRECTORIES (optional)
mode = "debug"              # debug -O0 | release -O2 | release-time -O3 | release-space -Os
dump-ast = false            # optional: write .rf.desugared beside generated IR
sa-timing = false           # optional: print semantic-analysis phase timings
show-build-stages = false   # optional: print build/check stage banners
```

With no entry file argument, the CLI walks up from the cwd to find
`config.toml` — `cd` into a project and `razorforge run` works.

## 12. Reading build errors

Format: `error[RF-S###]: file:line:col: message` plus a source excerpt with a
caret. Families: `RF-G###` grammar/parse, `RF-S###` semantic, `RF-W###`
warning. A diagnostic in a Suflae file spells the same number with `SF-`
(`SF-S436`). Frequent ones when porting habits from other languages:

| Code         | Usual cause                           | Fix                                                |
|--------------|---------------------------------------|----------------------------------------------------|
| RF-S510/S512 | positional args in a multi-param call | name every argument                                |
| RF-S413      | entity assigned without transfer      | add `steal`                                        |
| RF-S753      | failable call left unhandled          | recover with `try`/`check` + `when`                 |
| RF-S010      | literal out of range for target type  | use the right constant (`U8_MAX`)                  |
| RF-G010      | a number names its type (`5_u8`)      | `var x: U8 = 5`, or let the place it goes type it  |
| RF-W007      | ignored result                        | `drop expr`                                        |
| RF-W008      | an Agent nothing ever starts          | `start_now()` it, or `retrieve()`/`gather()` it    |
| RF-W009      | a started Agent nothing collects      | `retrieve()` it, or `drop job` to stop it          |
| RF-S632      | `to[Agent]` argument unsafe to hand over (a token / `Retained` only with `start_here()`, kept in the routine) | `steal` the entity, pass `Atomic`/`Guarded`/…, or `start_here()` |
| RF-S623      | one container handed to two parameters of one call, one of them written | copy the read one first, or pass two containers |
| RF-S640      | an element of one token held while another token on the same kind of container changes shape | finish with the held element first, or take both with `using multimodify(first: x, second: y) as (a, b)` |
| RF-S642      | `to[Agent]` on something that is not a routine, or over generic-parameter-typed values | convert a routine, with concrete types |
| RF-S645      | `x.to()` with nothing that gives the target type | write it: `.to[S64]()`, or give the variable a type |
| RF-S646      | no conversion between the two types (or to its own type) | convert to a type that has one, or drop the `.to` |
| RF-S647      | `steal place as old` leaves a non-optional place empty | add `inject <new value>` |
| RF-S649      | value of a locked `Guarded` reached on the handle | `using g.amend() as v` / `consult()`, or the `ReadOnly` policy |
| RF-S648      | `steal … as …` on something that is not a place | take from a variable, member variable or element |
| RF-G216      | `and`/`or`, or `&`/`\|`/shifts, mixed bare | add parentheses: `a or (b and c)`, `(a & b) \| c` |
| RF-S516      | `by 0` on a range                     | a non-zero step, negative to count down            |
| RF-G055/G112 | brace-style or inline-`if` habits     | 4-space indent blocks; conditionals top-level only |

## 13. A complete worked example

```razorforge
module Demo/Inventory
import IO/Console

record Item
    name: Text
    qty: S64

routine find_qty!(items: Viewing[List[Item]], name: Text) -> S64
    each item in items
        if item.name == name
            return item.qty
    throw AbsentValueError()

routine start()
    var items = List[Item]()
    items.add_last(value: Item(name: "bolt", qty: 40))
    items.add_last(value: Item(name: "nut", qty: 0))

    var q = try find_qty(items: items.view(), name: "bolt")
    when q
        is None => show("not found")
        else as n  => show(f"bolt qty: {n}")

    show("DONE")
    return
```

## 14. Concurrency

Any routine can run concurrently, and a routine declares nothing about where it runs. The keyword
**`f.to[Agent](args)`** makes a routine concurrent work: `download.to[Agent](url: u)` computes the arguments NOW
(and a receiver that is a value, as in `p.total.to[Agent]()`), defers the call into an `Agent[T]`, and starts
nothing. A verb on the Agent starts it and chooses where it runs. There is no `spawn`/`async` keyword.

```razorforge
import IO/Console

routine add(a: S64, b: S64) -> S64
    return a + b

routine start()
    var job = add.to[Agent](a: 1, b: 2)    # Agent[S64]: arguments computed, nothing runs yet
    job.start_now()                    # runs on the runtime's pool of threads
    # ... other work ...
    show(f"{job.retrieve()}")          # waits for the value
    var jobs = [add.to[Agent](a: 1, b: 1), add.to[Agent](a: 2, b: 2)]
    show(f"{jobs.gather()}")           # starts both, waits for all, results in input order
    return
```

- The routine is named, not called (like `f.to[Callable]()`), and the conversion takes the routine's own
  arguments. Only that routine is the work: `f.to[Agent](x).retrieve()` makes the Agent and collects it at once,
  and in `p.twice().total.to[Agent]()` the receiver `p.twice()` is computed now. `agent f(x)` is the old spelling
  (RF-G220, `migrate --step agent`).
- **Recovery is where the Agent is collected:** `try job.retrieve()` / `check job.retrieve()`. To recover inside
  the work, write a routine that uses `try` and make an Agent of it.
- An Agent of a routine that returns nothing is `Agent[None]`. The type can be inferred:
  `var jobs = [work.to[Agent](), work.to[Agent]()]` then `jobs.gather()`.

Verbs (methods on `Agent[T]`, all failable):

- `start_now()` — starts now on the runtime's pool of threads (M:N). The default for most work.
- `start_here()` — starts now on THIS thread beside its other `start_here()` jobs. They take turns only
  where one waits (`retrieve`, `waitfor`, a channel), so they can share values without a lock, and a job
  that blocks the thread stops them all. Inside a `start_now()` job it is an `AgentStartError`, because
  such a job can move between threads.
- `start_apart()` — starts now on an OS thread of its own, for work that blocks or computes for long.
- `retrieve() -> T` — waits for the value. An Agent nothing started is started with `start_now()` first.
  Inside a job it parks (other jobs keep running), elsewhere it blocks.
- **A failure stays in its Agent.** A failure inside the work does not end the program where the work
  runs, and sibling Agents keep running. It surfaces where the Agent is retrieved: `job.retrieve()`
  rethrows it (bare, it crashes loudly there like any failure), `check job.retrieve()` /
  `try job.retrieve()` recover it. The crash report's trace ends with
  `agent in start()` (the line that made the Agent) and `Thrown again at` names the `retrieve()` line.
- `cancel()` — asks the work to stop. Work nothing started yet never runs. Running work sees the request at
  its next wait (`waitfor`, `retrieve`, a channel) or through `cancellation_requested()`, and stops there
  (a thread of its own cannot be stopped from outside). Retrieving a cancelled Agent waits for it to stop,
  then throws `CancelRequested` (a crashable, no `Error` in the name because cancelling is not a failure),
  recovered with `check`/`try`.
- `job.waitfor(5s).retrieve()` — retrieve with a deadline, throws `TaskTimeoutError` when it passes
  (`try job.waitfor(5s).retrieve()` gives `None` instead). A deadline needs a thread of its own: an Agent
  nothing started yet is started with `start_apart()`, and one already running as a job of
  `start_now()`/`start_here()` is refused with `AgentStartError`.
- Starting an Agent twice, or retrieving it twice, is an `AgentStartError` (a crashable carrying
  `reason: Text`).
- `is_done()`, `is_running()` — racy snapshots for polling.
- On a `List[Agent[T]]`: `gather() -> List[T]` (wait for all, input order, throws the first failure it
  meets in list order), `race() -> T` (the first finisher, throws the winner's failure), `cancel_all()`
  (no `!`, returns nothing: cancels every Agent and waits until each has stopped, dropping what each came
  to, work nothing started yet never runs), `next_finished!() -> Agent[T]` (waits for the first to finish, takes
  it out and returns it, loop it to handle results in finishing order), `gather_all() -> List[Check[T]]` (waits for
  all, each Agent's value or failure in input order), `take_finished() -> List[Agent[T]]` (takes the finished Agents
  out in place and returns them, leaving the rest: rebuilding the list with `where` would drop work still
  running). `gather` and `race` start the Agents nothing started with `start_now()`.
- An Agent is an entity with one owner: `steal` moves it, `var b = a` is RF-S413. When its owner lets go of it
  before a verb took its value (`drop job`, or the end of the scope or list that holds it), the work is cancelled
  and waited for. A started Agent nothing retrieves, drops or hands on is warning RF-W009.
- There is no fire-and-forget verb: start with `start_now()` and collect later with `retrieve()` or
  `gather()`. An Agent thrown away before anything starts it (`f.to[Agent]()` as a statement, or
  `drop f.to[Agent]()`) never runs its work, and is warning RF-W008.
- Free routines: `waitfor(d)` waits `d` (parks inside a job, sleeps elsewhere; `50ms`, `5s`, or
  `Duration.from_milliseconds(ms: n)`), `yield_now()` lets other jobs run, `cancellation_requested()`
  tells a long loop to stop early. Cancellation is cooperative: `waitfor` returns early once it is asked.

```razorforge
routine checked!(n: S64) -> S64
    if n < 0
        throw VerificationFailedError()
    return n

routine relay(n: S64) -> S64
    return checked(n: n)

routine slow(n: S64) -> S64
    var i = 0
    while i < 2000
        if cancellation_requested()
            return -1
        waitfor(1ms)
        i += 1
    return n

routine start()
    var bad = relay.to[Agent](n: -1)
    var good = relay.to[Agent](n: 3)
    bad.start_now()
    good.start_now()
    show(f"good: {good.retrieve()}")   # 3, the failure beside it changes nothing here
    when check bad.retrieve()
        is S64 as v => show(f"bad: {v}")
        is Crashables as e => show(f"bad failed: {e.crash_message()}")
    var running = slow.to[Agent](n: 1)
    running.start_now()
    running.cancel()
    when check running.retrieve()
        is CancelRequested => show("cancelled")
        else => show("finished")
    return
```

**What `to[Agent]` may hand to the work (RF-S632).** Each value `f.to[Agent](...)` computes now must be safe to
hand to work that may run on another thread: a `steal`-moved bare entity, trivially copyable value data,
or a shareable manager handle (`Atomic`, `Guarded`, `Witnessed`). A `Roamed`
handle is promoted. Anything else (a bare entity by copy, a record owning `Retained`/`Tracked`/
`Viewing`/`Modifying`) is rejected. An `Atomic[T]` parameter is always by reference, so a routine taking
one works on the caller's cell, and sharing one counter across agents just works:

```razorforge
routine bump(cell: Atomic[S64], times: S64)
    each i in 0 til times
        drop cell.fetch_add(delta: 1)
    return

routine start()
    var counter = Atomic[S64](initial: 0)
    var jobs = [bump.to[Agent](cell: counter, times: 1000), bump.to[Agent](cell: counter, times: 1000)]
    drop jobs.gather()
    show(f"{counter.load()}")          # 2000
    return
```

Async file I/O (parks a job while waiting, blocks elsewhere), in `IO/File`:
`read_text(path: Text) -> Text` and `write_text(path: Text, content: Text) -> S64`. Prefer these to
opening a `FileHandle` inside a job.

**Channels** (`Core`, no import) — typed producer/consumer queues:
- `make_channel[T](capacity: U64) -> (Sender[T], Receiver[T])` — single consumer. `capacity` 0 =
  rendezvous (send waits for a taker); N = bounded buffer (send blocks when full = backpressure).
- `make_shared_channel[T](capacity: U64) -> (Sender[T], SharedReceiver[T])` — multiple consumers
  COMPETE for each item (fan-out, not broadcast).
- `sender.send(item: x)` moves `x` in; FAILABLE (`ChannelClosedError` if closed / no consumers).
  `sender.send_if_room(item: x) -> Bool` never waits: `true` when it went in, `false` when the channel was full
  (the item is destroyed), `ChannelClosedError` like `send`.
  `sender.duplicate()` clones a producer for fan-in; `sender.close()` / `is_closed()`.
- `Receiver`/`SharedReceiver` are `Iterable[T]` — drain with `each x in rx` (ends when closed + empty).
  `rx.close()` closes from the receiving side: buffered items are destroyed unreceived, a later `send` throws
  `ChannelClosedError`, and a sender waiting for room wakes with it (an early stop travels up a pipeline).

```razorforge
var (tx, rx) = make_channel[S64](capacity: 4)
tx.send(item: 10)
tx.send(item: 20)
tx.close()
each n in rx
    show(f"{n}")
```

**Not implemented yet** (do not generate these — they do not exist): async networking
(sockets/HTTP/WebSocket). It is on the roadmap, not in the language today.

## 14b. Process signals (`Signals`)

`import Signals` to react to the OS asking the process to stop:

```razorforge
import Signals

routine on_stop()                    # a NAMED routine — lambdas are expression-only,
    show("cleaning up")                # so a multi-statement handler cannot be a lambda
    return

routine start()
    when_interrupted(handler: on_stop)   # Ctrl-C / SIGINT
    when_terminated(handler: on_stop)    # SIGTERM / console-close
    ...                                  # your own loop — see below
    return
```

- **Registering a handler SUPPRESSES the default termination.** The process no longer
  dies on Ctrl-C / SIGTERM; your handler decides. Registering does NOT keep the
  program alive — it must run its own loop; the handler typically flips a flag the
  loop checks, then the loop exits. (SIGKILL / OOM-killer cannot be caught.)
- **The handler runs on a dedicated dispatch thread**, not the main thread — keep it
  small (set a flag, log, save) and thread-aware. Multiple handlers per class all run,
  in registration order.
- The handler type is **`Callable[(), None]`** — a no-argument, no-return routine value.
  (`()` is the empty parameter tuple; `None` is the void return. The parameter list is
  always tuple-notation: `()`, `(T,)`, `(A, B)`.)
- **Carrying state — the context overload.** Because the handler runs on another thread,
  shared state it touches must be a thread-safe, storable handle: a `Guarded[T, TPolicy]`
  (RazorForge multi-thread RC) or a `Roamed[T]`. Pass it with the copy verb and the
  handler receives it back on every fire — no capture, no global:

  ```razorforge
  routine on_stop(box: Guarded[Flags, Exclusive])   # or Roamed[T]
      ...
  when_interrupted(handler: on_stop, context: flags)   # `=` gives the handler one more holder
  ```

  The registration retains the handle for the process lifetime (a `Roamed` context is
  also promoted to its atomic escaped mode). One overload set accepts either handle
  type — the builder picks by the argument type.

## 15. Foreign functions and conditional compilation

- **Foreign routines are realm-qualified**, not an `external` keyword (there is no
  `external` keyword). Declare a C function as `routine C::name(...)` and an LLVM
  intrinsic as `routine LLVM::name(...)`. `dangerous routine C::name(...)` works
  (the `dangerous` prefix precedes). Call sites use the same qualifier:
  `C::labs(n: x)`. External calls are positional.
- **`@blocking`** on a `C::` routine marks a call that may block its thread for long (a read from a pipe, a
  sleep, a network wait). Inside a job started with `start_now()` or `start_here()` such a call runs on one of
  the runtime's I/O threads while the job parks, so its thread goes on with other jobs, and anywhere else it is
  an ordinary call. A variadic or failable foreign routine cannot be marked. To put your own long work on an
  OS thread, start it with `start_apart()`.
- **`@link("SDL2")`** on a `C::` extern names the C library that resolves its
  symbols (Rust `#[link]` style); merged with `[target] c_libraries` in the
  manifest into clang `-l` flags. Library search paths (`-L`) live in the
  manifest's `library_paths`.
- **`@target(...)`** = file-granularity conditional compilation (RF-only; Suflae
  is never gated). Place it as a leading annotation BEFORE `module`:
  `@target(os: "windows")`, `@target(os: "linux", "macos")`,
  `@target(arch: "arm64")`. Keys are AND-ed; comma-separated values within a key
  are OR-ed; keys are `os` (windows/linux/macos) and `arch` (x64/x86_64,
  arm64/aarch64). There is no block-level `#if` — platform code splits into
  separate files (`foo_windows.rf` / `foo_linux.rf`), one type per file.

## 16. Keyword inventory

Every reserved word the tokenizer recognizes. Words marked **†** are **RF-only**
(Suflae does not reserve them — see SUFLAE-FOR-AI §2). Everything else is shared by
both realms. There is NO `for`, `external`, `async`, `spawn`, `let`, `const`, `fn`,
`class`, `struct`, `enum`, `match`, `trait`, or `impl` keyword — if you reach for one,
you are writing another language.

A keyword can still be a name when written between backticks (`` `check` ``), and so can words with one space
between them (`` `total count` ``); `` `count` `` and `count` are the same name.

- **Declarations**: `routine` `entity` `record` `bundle`† `choice` `flags` `crashable`
  `variant` `protocol`
- **Bindings**: `var` `preset` `lateinit`
- **Visibility / receiver**: `secret` `posted` `common`
- **Self**: `me` `Me`
- **Protocols & constraints**: `obeys` `needs` `relates` `everywhere`
- **Control flow**: `if` `elseif` `else` `then` `when` `is` `isnot` `loop` `with`
  `while` `each` `break` `continue` `return` `throw` `pierce` `todo` `becomes` `until`
- **Iteration / range / ownership**: `in` (loops only) `to` `til` `by` `steal`†
- **Module system**: `import` `module`
- **Other statements**: `using` `as` `define` `pass` `given` `drop`
- **Logical operators**: `and` `or` `not`
- **Literals**: `true` `false` `None` `none`
- **Concurrency**: `f.to[Agent](args)`
- **Danger**†: `danger` (block) `dangerous` (modifier)
- **Buildtime reflection**†: `expand` (loop) — the ONLY reflection keyword.

The word operators `to` `til` `by` `is` `isnot` `in` `as` `and` `or` are keywords only where an
operator stands, right after something that ends an expression (`0 to 10`, `a and b`, `each x in xs`). Anywhere
else they are names: `var to = 3`, `routine step(by: S64, to: S64)`, `by + to`. Two places keep them keywords: a
`when` arm that starts with `is`/`isnot`, and `else as p`. `not` starts an expression, so it is always a keyword.
`inject` is a keyword only after `steal … as …`.

The reflection **sources** `openmemvarof` `allmemvarof` `branchof` `caseof` (there is no
`memvarof`) and the metadata **accessors** `nameof` `orderof` `placeof` `sizeof` `typeof`
`typeidof` `valueof` `visibilityof` are **buildtime builtin intrinsics, NOT reserved
keywords** — they tokenize as ordinary identifiers and are recognized in SA only when
`import BuilderExpansion` is in effect (bare `nameof(m)` or `$`-spliced `me.$nameof(m)`),
each reading a buildtime property off the active `expand` handle or a type. Without that
import, using `expand` or any source/accessor is a build error (RF-S952).

`$` (wired-routine marker / `${…}` buildtime splice) and `!` (failable marker) are
**structural sigils on a name, not keywords** — the name stays bare (RoutineInfo
carries the flags). See §1b.

---

When generating RazorForge: start from the closest fixture in
`tests/Fixtures/Stdlib/`, keep arguments named, end every routine with
`return`, and prefer `when` over nested conditionals.
