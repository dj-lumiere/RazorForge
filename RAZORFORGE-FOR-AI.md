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
   The declaration states the result type: a `return` with a value in a routine
   without `->` (or `-> None`) is RF-S301, and so is a bare `return` in a routine
   with `-> T`. The value never sets the result type.
3. **Conditional expressions are top-level only.** `if c then a else b` is the
   whole value of `var x =` (also `preset`, `global`), a plain `x =`, or `return`.
   Anywhere else it is RF-G112 *by design*: inside parentheses, an argument
   (`f(x: if c then a else b)`), an operand (`(if c then a else b) + 1`, `x += if ...`),
   a subscript, an f-text slot, a branch or condition of another conditional
   (`else if ...` too), a `when` arm's value, and directly after `try`/`check`.
   Use `when` or a named intermediate instead.
4. **`//` is integer floor division; `/` is true division.**
5. **Default arithmetic is checked** — `+ - *` throw on overflow. Wrapping
   variants: `+% -% *%`. Clamping (saturating): `+^ -^ *^`. Floating-point
   types (B16/B32/B64/B128/D32/D64/D128) have two tiers only: checked `+ - * / **`
   (a result that is not finite crashes) and unchecked `+! -! *! /! **!` = raw
   IEEE 754 (±infinity and NaN pass through; no `danger` block needed, unlike
   integers). Decimal and C64/C128/C256 have only `+ - * / **`.
   `Simd.Vector[T, LANES]` is unchecked only: its lane-wise `+ - * /`, `sum`,
   `dot` give the raw IEEE 754 lanes (a per-lane check would cost the SIMD gain).
   Division by zero crashes on every number type, arbitrary precision included.
   On the floats, `Decimal` and the complex types `x / 0` with `x` not
   zero throws `DivisionByZeroError` and `0 / 0` throws `NumericDomainError`
   (integer `//` and `%` by zero throw `DivisionByZeroError`).
   There is no arbitrary-precision binary float: `B128` is the widest, and exact
   arithmetic is `Int` and the decimals.
6. **Data exists to be shared, and record and entity are the two ways.** The question that picks one: may each
   place that shares the value hold its own copy? Yes: a `record`, shared by handing out copies (`=` copies).
   No, copies would be a problem: an `entity`, whose one legitimate version is the single source of truth, so it
   follows that the entity lives in one place. Others reach it by permission for a while (`view()`/`modify()`
   tokens, the builder checks them) or through a manager for longer (`Retained`, `Guarded`, ...). Keeping the
   same entity in a second place with neither is RF-S413 ("would share one entity between two places with no
   manager", `var s = obj.field_entity`). `steal` hands it to what keeps it from now on: `consume(r: steal b)`.
   A manager type (`FileManager`, `PartitionManager`, `StorageManager`, `CpuEventManager`) is always an `entity`
   (a copy would keep its own record and diverge), a service (`Clock`, `CpuClock`, `Environment`, `Entropy`) a
   `record` with no member variables (body `nothing`, routines `common`, called on the type). The console is the
   free routines `show` / `ask` / `alert`. The builder does not check this by name: the stdlib follows it.
7. **A routine that can fail is declared `failable`** (`failable routine parse(...)`, and
   `failable dangerous routine` / `failable common routine`: `failable` goes first, after a visibility).
   `routine parse!(...)` is RF-G222 (fix and `migrate --step failable` write the keyword), and call sites never
   write anything (`parse(...)`, `try parse(...)`). A BARE call
   crashes LOUDLY on failure. To RECOVER, prefix the whole expression with a
   keyword and match the carrier with `when`:
   `try EXPR` → `Maybe[T]` (present | absent, no error kept);
   `check EXPR` → `Check[T]` (T | the caught `Crashables`).
   A routine with no value to give throws `AbsentValueError()`, an ordinary
   stdlib crashable: `throw AbsentValueError()`. To tell "not found" apart from
   other failures, match it by type: `is AbsentValueError => ...` on a `check`.
   **Failures rise only through failable routines.** A `throw` in a `failable` routine gives the failure to the
   caller: a bare call crashes loudly, `try`/`check` in front receive it, and a bare call inside another failable
   routine passes it on to that routine's caller. A `throw` in a routine that is NOT failable crashes on the spot
   with the crashable's message (status 82, like an unrecovered failure): use it for a broken invariant, never for a
   condition a caller could handle. A routine that is not failable is a wall: a failure inside it (its own `throw`,
   a checked operator, a bare call of a failable routine) crashes there, even under `try`/`check` above its caller
   (`try audit()` with `audit` failable calling a plain `share` that divides by zero crashes). Checked operators,
   subscripts and conversions are failable routines, so `try a + b`, `check a // b`, `-x` of the minimum,
   `try xs[9]` (a subscript out of range, a missing key), `check big.to[S8]()` and `"x".to[S64]()` are received.
   `try`/`check` in front of an expression that reaches no failable call is RF-S760 (remove the keyword, or declare
   the routine `failable`). Evaluation stops at the first failure, which becomes the carrier's (`try`: absent,
   `check`: the error). A failure inside a `destruct` (a `destruct` is never `failable`, RF-S815, and runs at a
   scope's end where nothing takes a failure) crashes even under `try`/`check`, and native runtime fatal errors,
   out of memory and running out of stack are fatal everywhere.
   **Routine values.** A lambda whose body can fail (a checked operator, a subscript, a call of a failable routine)
   and a failable routine named as a value are routine values that may fail, of a type written
   `failable Callable[(T,), U]`. They go only to a parameter or member variable of such a type: handing one to a
   plain `Callable[...]` is RF-S761 (mark the parameter `failable`, or hand it a value that cannot fail: `+%`/`*%`
   wrapping, `+^` clamping, float arithmetic, a routine that is not failable). A call that hands a routine value
   that may fail to a `failable Callable` parameter can fail through it, so
   `try apply(f: x => 10 // x, x: 0)` gives `none` and a bare one crashes loudly. The stdlib routines that take a
   routine value take it as `failable Callable[...]` (`select`, `where`, `take_while`, `skip_while`, `distinct_by`,
   `select_many`, `any`, `all`, `accumulate`, `get_count`, `min_by`, `max_by`, `sort_by`, `sorted_by`). An adapter
   holds the lambda in a `failable Callable` member variable and calls it later: a call that works on such an
   adapter (its terminal, `s.to[List]()`, or the `each` loop over it) can fail through it, so
   `try xs.select(transform: x => 10 // x).to[List]()` is received, and an `each` loop over such an adapter passes
   the lambda's failure on in a failable routine and crashes anywhere else. The handlers of `CpuEventManager`
   (`Callable[(), None]?`) are not marked: a handler's failure crashes the program.
   **`todo`** is a statement for code not written yet (`routine parse(bytes: Bytes) -> Header` with the body
   `todo`): running it crashes with `NotImplementedError` naming the routine, even under `try`/`check`. It does not make
   the routine failable and ends its path, so a body that is only `todo` needs no `return`. The build warns
   once per routine that still has one (RF-W303). `nothing` is an empty body on purpose, a different thing.
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
   crashable's own routines. **A crashable holds a few values** (numbers, a `Text`); it
   has no size limit. **Failure molds.** Every flow (each thread, and each fiber or
   coroutine a Work runs on) has one failure mold, reserved when the flow starts, as big
   as the program's largest crashable. `throw` writes the crashable into the mold of the
   flow it runs on (its member variables copied at that moment), and the failure passes up
   through the routines it fails as its type id and failure number only. The `check` that
   receives it copies the crashable out of the mold into the `Check` it gives, so a `Check`
   bound to a name keeps its own error past the next failure, and a failure on one fiber
   never overwrites another's. `try` copies nothing. One failure flies per flow at a time:
   errors are never stacked (to keep several, keep several `Check` values). A kept error
   thrown again (`throw e`, a Work's error at `retrieve`) goes back into the mold of the
   flow that throws it. A caught error is then held IN the carrier (a `Crashables` is the
   crashable's type id, the failure number, and the crashable itself), so a `Check` takes
   no memory of its own, and a call on a `Crashables` is a switch the builder writes on
   the type id over the crashables the program throws: nothing about it is looked up at
   run time.
   **The list a routine throws** is collected by the builder (its own `throw`s and those
   of the failable routines it calls). `@throws([NotFoundError, ParseError])` above a
   failable routine fixes it, for a routine others build on: a crashable the routine can
   throw that the list leaves out is RF-S712, and the annotation on a routine that isn't
   failable (or naming a type that isn't a crashable) is RF-S713. The language server marks
   a bare call of a failable routine with a `may stop` hint.
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
   The trace holds the program's routines only: a standard library routine has no frame on it, and a crash inside one
   reports the line where the program called it. A routine LLVM inlined still has its frame. `[debug] trace-stdlib =
   true` (for work on the library) keeps the library's frames too. On Linux x86-64 a build made ahead of time reads
   the trace from the stack when a crash asks for it (every routine keeps a frame record, the builder writes a table
   of return addresses), so while the program runs it only keeps the frame records. Elsewhere, and in the debug JIT, the program
   keeps a shadow stack as it runs.
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
   in System) set last: one atomic value, so any thread may set it and the last call
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
   `@discardable` returns a by-product (`Set.add`/`Set.remove`/`Dict.remove`'s Bool, `Unbroken.fetch_*`'s old value,
   `File.write_text`'s count): its result may be dropped silently, `seen.add(value: v)`. `drop x` on a local
   binding the routine owns (a local, an entity parameter handed over with `steal`) tears it down there and ends
   the name like `steal x` (RF-S615 after, RF-S616 on some paths only, tokens from it end, RF-S643); a member
   variable, a token, `me` or a global is RF-S421.
   **Notes and fixes (RF and SF).** A NOTE is a likely slip that builds as written: `check`, `lint` (SF files) and
   the language server show it (`note[RF-W###]`), a build never does. `until false` gets RF-W304 with the fix
   "Write 'loop'". The body of `until true` gets RF-W057 and, like the code after an
   unconditional `return`/`break` (RF-S786, still an error), is shown FADED (LSP `Unnecessary` tag, range through its
   last line). Diagnostics carry an optional `Fix` (title + 1-based `TextEdit`s): the language server offers it as a
   quick fix (sent in `diagnostic.data`), and `check --json` writes every error/warning/note as a JSON array (`file`,
   `line`, `column`, `severity`, `code`, `message`, `endLine?`, `fix?`). Fixes so far: RF-G211 removes the call-site
   `!`, RF-G212 writes `xs.has(x)`, RF-W007 prepends `drop`, RF-S767 converts two named operands to the type holding
   both. Named Bool conditions get no loop note.
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
  `secret` (type/module-private: seen in its module and the modules under it). `open` is the unwritten default. Giving a `secret` or `posted` member
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
  The flags value with no member on is `none` where the flags type is expected (`var marks: CellMarks = none`,
  `marks == none`, a preset or record preset). A `CellMarks?` slot takes `none` as the absent Maybe. There is no
  `all_off()`. Choice values (`OK: 200`) and flags bits (`MINE: 0`, 0 to 63) are written on every member or on none
  (then 0, 1, 2, ... in order): some with and some without is RF-S417.
  A `choice` auto-provides `Equatable`/`Comparable`/`Hashable` — no `obeys` and no
  method bodies needed.
- **`Me` (capital) = the receiver's TYPE (the Self type); `me` (lowercase) = the
  receiver INSTANCE.** Do not conflate them.
- The generic-constraint keyword is **`needs`** (e.g. `needs T obeys P`), **not
  `where`** — `where` is not RF syntax. A constraint's protocol type arguments are
  checked too: a `List[Text]` given where `Emittable[S64]` is taken is RF-S150.
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
  same name (`routine List[Work[T]].gather() -> List[T]`: that `T` is the pattern's).
- Say **"buildtime dispatch" / "runtime dispatch"**, not "static / dynamic".
  Everything monomorphizes; RF's default is buildtime dispatch, no runtime dispatch.
- varargs parameter notation is **`name...: Type`** (the `...` follows the param
  NAME, before the colon); it binds to a `List[Type]`.
- **RazorForge has NO "borrow" concept** (that is Rust). Do not use the word
  "borrow" in prose or API names, nor "owner"/"ownership" for entities. Sharing is expressed with records
  (copies) and entities: permission (**access tokens**, below), managers (RC wrappers) and `steal`.
- builder-synthesized per-type routines are **wired routines** (`represent`,
  `diagnose`, `serialize`, the constructor, `eq`, `compare`, `hash`, …) — named with **NO
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
  routines in the file cannot see it (RF-S444). Pass it as an argument. A
  `global` is read-only, so state that changes goes in an `Unbroken`/`Guarded` global.
- Routines you define are still their own scopes and still end with `return`.

`module <Path>` is optional. Without it the module path is derived from the file's
location relative to `config.toml`: each folder and the file name are PascalCased and
joined with `/` (`tools/text utils.rf` → `Tools/TextUtils`). Declare `module` to
override that. Imports use `/` separators.

Files in one folder that declare the same `module` are one module and build together, with no
import between them. A file there with its own `routine start()` is another program and is not
built with yours, so a draft that does not build yet can sit next to the program. A file there with
top-level statements and no `routine start()` is built and is RF-S443, since only the entry file
may be a script. Only files that are part of the build report their errors. A failed `build` keeps
the last executable that built.

**Two import forms, distinct meaning:** `import Foo/Bar` is a **module** import
(`Bar` is a submodule of `Foo`). `import Foo.bar` is a **member** import (`bar` is
a type/routine/preset in module `Foo`). An import brings the module it names, never the
modules under it (`import IO` doesn't bring `IO/Console`). `System` and every module under
it are seen everywhere without an import.

**The standard library's modules.** A standard library module is its folder
(`Standard/RazorForge/System/Text/` is `System/Text`), and a `module` line that names another
module is a build error. `System` is the base: the built-in types, `List` / `Dict` / `Set`,
the protocols and the errors, with `System/Numerics` (every number type and its math,
`Int`, `Decimal`), `System/Text` (`Text`, `Bytes`, formatting), `System/Memory` (the
wrappers and controllers, `ByteSize`), `System/Temporal` (`Duration`, `Moment`, `Clock`),
`System/Concurrency`, `System/Serialization`, `System/Cpu` (`CpuEventManager`) and `System/CSubSystem` (C types
and strings).
The rest takes an import: `Collections`, `IO/Console`, `IO/Environment`, `IO/File`, `IterTools`, `Math3D`,
`Random`, `Simd`, `Subprocess`, `Text/Patterns`, `BuilderQuery`, `BuilderExpansion`.
`migrate --step modules` drops imports of `Core` and `Numerics` and rewrites `Core.X` to
`System.X`.

**Globals (RazorForge).** `global name: Type = init` (type and initializer both required):
initialized in dependency order when `start()` begins (a cycle is RF-S436, one reading itself too:
`global A: S64 = A + 1`, no `start()` is RF-S438), torn down at every exit of `start()` in the opposite order. A
global the `destruct` of another global's type reads (or the destruct of an entity it holds) is initialized before
it and torn down after it, whatever order they are written in, and two whose destructs read each other are RF-S436. Every thread reaches it, so the binding is
never assigned again: shared changing state is an `Unbroken` (`hits.fetch_add(delta: 1)`) or a
`Guarded` (`modifying cache as c`), any other global is only read (writes are RF-S251 /
RF-S256, `modifying G` on a global entity that is not a `Guarded` is RF-S256, `steal G` in any routine is
RF-S421). A global never holds a token (RF-S601) or a single-thread handle (`Retained`, `Tracked`, at any depth:
RF-S632). A module-level `var` is an error (RF-S435). Constants the build knows are `preset`.

## 3. Types

- Signed ints: `S8 S16 S32 S64 S128 S256` · unsigned: `U8 U16 U32 U64 U128 U256`
- Binary floats: `B16 B32 B64 B128` · decimals: `D32 D64 D128`
- `BF16` (bfloat16, ML weights/tensors): bit patterns (`from_bits`/`to_bits`/bytes), `b.to[B32]()`/`b.to[B64]()`
  widen exactly, `x.to[BF16]()` (from `B32`/`B64`) narrows with ONE round-to-nearest-even (from `B64` directly,
  never through `B32`), `represent`/`t.to[BF16]()`, `==`/`<`. Arithmetic (`obeys Numeric`, the B16 surface:
  `+ - * /`, `sqrt`, `floor`..., `mul_add`, `next_up`...) works on the B32 values and rounds once (correctly rounded for
  `+ - * /`/`sqrt`), and the library functions (`exp`, `ln`, `sin`, `pow`, `atan2`, `hypot`, `sin_pi`...) call
  Tessera's correctly rounded BF16 routines through Ingrid (`ingrid_bf16_*`, bits as `U16`). No literals of its own.
- Hex float literals (binary floats ONLY): `0x1.8p3` = hex mantissa × 2^exponent (= 12), `var eps: B32 = 0x1p-23`,
  `0x1.fffffep+127` where a `B32` goes. The `p` exponent is required (`0x1.8` is not a float). The value must fit the type
  EXACTLY: too many significant bits or a bit below the smallest subnormal is RF-S018, not a rounding. On a
  `D32`/`D64`/`D128`/`Decimal` type it is RF-S017. It takes a binary float context, else `B64`
  (in Suflae too).
- Arbitrary precision: `Int` (16 bytes, two words: a value in the `S64` range is held in the value itself, with no heap and no engine call for `+ - *`, compares, shifts, bitwise, `//` and `%`; a value outside it shares a read-only list of 64-bit limbs between copies, and a literal outside the range is laid down once by the builder and shared by each use). `Decimal` is a finite-only decimal with 34 significant digits (decimal128, canonical, never inf/NaN): `+ - *` are exact and crash with `NumericOverflowError` when the result needs more than 34 digits, `/` and the math routines round to the nearest, and every rounding a `Decimal` does (`/`, text and integers or floats with more digits, `round()`, `fixed(places:)`, literals) is half up: x goes to the n (at the digit kept) with `n - 0.5 <= x < n + 0.5`, that is `floor(x + 0.5)` (`2.5` to `3`, `-2.5` to `-2`, `-7.125` to 2 places `-7.12`, `(-0.5).round()` is `0`), `pow`/`powi`/`sqrt` and the transcendentals included, while `D32`/`D64`/`D128` keep IEEE 754's ties to even, cohorts and `-0` (a number takes them from context, `var big: Int = 10`). A `Decimal` has one form per value: every result is normalized (no trailing zeros in the coefficient, a zero is always `+0`, never `-0`: `(-0.5).fixed(0)` is `0`, `"-0".to[Decimal]()` is `0`), so `1.0` and `1.00` are the same bits and `Decimal` is `Hashable` (a `Set`/`Dict` key). Its text drops the zeros after the point and writes an integer of up to 34 digits in full (`100`, `1.5e3` shows `1500`, `1e40` shows `1E+40`). A negative literal is one signed value (`-1.0000000000000000000000000000000005` rounds half up to `-1`). Operators keep the type's one rule; a routine that rounds on purpose takes a `RoundingRule` (`HALF_UP`, `HALF_EVEN`, `HALF_AWAY`, `FLOOR`, `CEIL`, `TRUNC`, leaving it out gives the type's rule: `HALF_UP` for `Decimal`, `HALF_EVEN` for `D32`/`D64`/`D128`): `x.round(places: 2, rule: RoundingRule.FLOOR)` (`places` default 0, so `D64.round()` is ties to even), `x.fixed(places: 2, rule: ...)`, and on `Decimal` `a.divide(by: b, places: 2, rule: ...)`, `a.add(you: b, places: 2)`, `a.subtract(you: b, places: 2)`, `a.multiply(by: b, places: 10)` (computed exactly, rounded once, so they round where `+ - *` fail, and throw `NumericOverflowError` only when even the rounded value needs more than 34 digits). There is no global rounding state. `a.rem(you: b)` on `Decimal` is the Euclidean remainder, as integer `%` is: computed exactly, `0 <= r < |b|`, never negative and never `-0` (`-7` by `2` is `1`, `7` by `-2` is `1`, `5.5` by `-2` is `1.5`, `-5.5` by `2` is `0.5`, `1E+30` by `7` is `1`), throws `DivisionByZeroError` for a zero `b` and `NumericOverflowError` when the remainder needs more than 34 digits (`-1e-40` by `7`). `Decimal` has no `%` or `//`, and `D32`/`D64`/`D128` keep the IEEE `rem`. No arbitrary-precision binary float or
  complex: `B128` and `C256` are the widest.
- Complex (total-bit): `C64` (2×B32) `C128` (2×B64) `C256` (2×B128). Imaginary literal is the width-less
  `i` (also `_i`): `3 + 4i`. Width comes from context (a `C64`/`C256` slot), and with no context it
  defaults to `C128`. Plain `3` fills the real part, `4i` the imaginary.
  `* / abs norm reciprocal` round each part once from the exact value (no overflow on the way when the result
  fits). `sqrt`, `ln`, the inverse functions, `tan`/`tanh` take principal branches with C99 Annex G signed-zero cuts
  (`sqrt` of `-4 - 0i` is `0-2i`), within a few ulps. `exp_unchecked`/`sinh_unchecked`/`cosh_unchecked` give inf past
  the range, the checked forms crash. All of it is Ingrid's (`Ingrid/tessera/complex.te`).
- Geometry (`import Math3D`, `B32` elements): `Vector2D`/`Vector3D`/`Vector4D`, the quaternions `Q128` (B32 parts)
  and `Q256` (B64 parts), and the matrices `Matrix2R2C`, `Matrix3R3C`, `Matrix4R4C`, stored column-major as
  `Simd.Vector[B32, N]` columns (`vec2`/`vec3`/`vec4` build one, `Matrix2R2C(c0: vec2(4.0, 2.0), c1: vec2(7.0, 6.0))`
  is `[[4, 7], [2, 6]]`). `m.get(row:, col:)` is one element, `m[row]` a row, `m.transform(v:)` is `m · v`, `*` the
  matrix product. `inverse()` and `solve(b:)` (A x = b) are Gaussian elimination with partial pivoting and are
  failable: `SingularMatrixError` when a pivot is exactly 0 (there is no near-singular tolerance), and an inf or NaN
  during the computation crashes like checked float math. `inverse_unchecked()` is the raw IEEE result. There is no
  n×n matrix type.
- `Bool`, `Text` (UTF-32 string), `Char`, `Byte`, `Bytes`
- `Duration`, `ByteSize` (with literal forms), `Moment`/`LocalMoment` temporals. A `ByteSize` displays as its byte
  count grouped in threes by `_` (`{4kib}` is `4_096B`, never split into units, `kb_size()`/`mib_size()`/... convert),
  and diagnoses as `ByteSize(bytes: 4096)`. Duration `//` and `%` follow the
  integer rule (the remainder is never negative: `-10s // 3s` is `-4`), and overflow or `// 0s` crashes.
  `Moment(year:, …)` crashes with `InvalidMomentError` on a part out of range (month 13, February 30, hour 25).
  `Duration` and `Moment` are both `S64` seconds + `U32` nanoseconds. The time is read from `Clock`, called on the
  type: `Clock.now()` (a `Moment`, follows the system clock), `Clock.monotonic()` (a `Duration` that only goes
  forward, for measuring and deadlines), `CpuClock.now()` (processor time used, a `Duration`). There is no
  `Moment.now()` or `LocalMoment.now_utc()`.
- `Char.to_uppercase`/`to_lowercase`/`is_uppercase`/`is_lowercase` follow Unicode (simple one-to-one mapping,
  `ß` stays `ß`). `lines()` and `ask_lines()` start no new line after a final line ending.
- Build errors where the emitter used to fail: a construction leaving out a member with no default (RF-S455, names
  it) or giving unnamed values that are not one per member (`Size(3)` for two members, RF-S455), `Int(1)` (RF-S646:
  write `var n: Int = 1`, or `x.to[Int]()` for a variable), a slice of a `List` of entities, a protocol as a value or element type (`List[Named]`), a member write
  through an element read from a `Viewing` token, a variant built from a non-member type. `Check[T]` has
  `unwrap!()` (`check f() !!`), `strip_out()` works for entities, `br"..."` builds, a value assigned to a `T?`
  variable becomes present (`port ??= 8080`), and `()` works as a type argument in an expression
  (`List[Callable[(), S64]]()`).
- A variant holding only values copies with `=` (one that may hold an entity is RF-S413). A file of only `var` lines
  is a script. The overflow message is "Integer overflow error: The value does not fit in the target type."
- A field-by-field construction infers type arguments (`Pair(first: 1, second: "one")` is `Pair[S64, Text]`). A type
  obeying `Emittable[T]` gets its `Emitter` from what its `emitter()` returns (no `relates` needed), so IterTools adapters
  walk it. A bundle holding an entity moves with `steal` (no copy step). An element token from `each` converts like
  its value (`w.to[S64]()`). `CpuEventManager.when_interrupted`/`when_terminated` link and run.
- A bare number that fits two members of a variant (`S32` and `S64`) is RF-S201: state its type in a declaration.
  An empty flags value shows as `none`, a one-item tuple as `(1,)`. A global record's member can't be assigned
  (RF-S257, like the global itself). `try sv.to[List[S64]]()` recovers a bad element too. Every collection reads
  back with `sv.to[T]()` (`Dict`, `Set`, `SortedDict`, `SortedList`, `SortedSet`, `RotatableList`), a dictionary's keys
  from their text. A type holding a `secret` member variable, at any depth, can't be read back: RF-S813.
- Inside a `when` arm whose name reaches the variant's own value (`is Upload as u` on an entity branch, or a
  `Retained`/`Hijacked`/`Maybe` branch), giving the matched variable a new value is RF-S264: assign it after the
  `when`. A value branch's name is a copy, so assigning there is fine.
- `each x in steal xs` takes a `List` apart (`take_each()`): the loop variable holds each element, a `break` ends the
  rest with the walk, and using `xs` after the loop is RF-S615 naming the loop. Other collections can't be taken
  apart that way (a build error says so).
- A routine written outside its type may name the slots its own way (`routine Pair[TLeft, TRight].swapped() ->
  Pair[TRight, TLeft]` for `record Pair[TFirst, TSecond]`): the names bind by position.
- A `Moment` shows as `2026-03-05 09:00:00` (each part zero-padded), a `LocalMoment` adds `Z` or `+09:00`.
- `ask_line()` gives a blank line as `""` and crashes with `EndOfInputError` (`IO/Errors`) at the end of the input, as
  does `ask_word()`: read until the end with `when check ask_line()` + `is EndOfInputError => break`. An `ask_line()`
  right after `ask_word()` skips the line end the word left.
- Collections: `List[T]`, `Dict[TKey, TValue]`, `Set[T]`, `RotatableList[T]`, `BitList`,
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
  and where a `Decimal`, `Int` or integer type goes it is RF-S020. There are no infinity/NaN presets and no
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
choice of those; RF-S258 if it fails or has no such value, RF-S436 if its value names itself, directly or through
other presets). A preset made by a construction is one read-only value
everywhere (an entity preset has one identity). (`lateinit var x: T` defers initialization: storage is allocated at
the declaration — entities get a real zeroed block, the constructor not run. Assign before reading: a read (a token
from it included) where some path hasn't assigned it is RF-S202, a `return` path doesn't count and a loop is left with
what its `break`s assigned. Writing a member (`r.x = 1`) counts as giving it its value.)

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
  `U8`); a negative index crashes with `NegativeIndexError`. The named routines (`x.shift_left(bits:)`, `xs.getitem(index:)`)
  keep their parameter types.
- `//` floor division, `/` true division, `%` remainder
- An operator is a call of its routine: `/` `truediv`, `//` `floordiv`, `%` `mod`, `<=>` `compare`, `<<`
  `shift_left`, `>>` `arith_shift_right`, `>>>` `logical_shift_right` (the others: `add`, `eq`, `lt`, `bitand`, ...).
- Float and decimal math names: natural log `ln`, `ln_1p`, `log2`, `log10`, `log2_1p`, `log10_1p`, `exp_m1`,
  `exp2_m1`, `exp10_m1`, `sin_pi` / `cos_pi` / `tan_pi`, `asin_pi` / `acos_pi` / `atan_pi`, `atan2_pi`,
  `rsqrt` (1 / sqrt(x) rounded once, on every binary float, the D types and `Decimal`: `x <= 0` fails like the other
  checked math, `Decimal.rsqrt` of `x <= 0` is `NumericDomainError`), `sin_cos`, `gamma`, `ln_gamma`, `mul_add`
  (fused `me * b + c`), each with its `_unchecked` form, `powi(n)` (a whole power) and `midpoint` (`(a + b) / 2`,
  no overflow), rounded once on the D types and `Decimal` like the others (an exact D-type power keeps the exponent
  `n` times x's: `2.0.powi(2)` is `4.00`, `10.powi(3)` is `1000`, `x.powi(0)` is `1`),
  and `mod_trunc` (remainder of a truncated division, sign of `me`), `rem` (IEEE remainder, quotient rounded to
  nearest even, on `Decimal` the Euclidean remainder above), `fract` (`me - trunc(me)`), `is_sign_negative` (sign bit, true for -0), `total_compare` (IEEE
  totalOrder). Float/decimal `max`/`min` return the other operand when one is NaN (NaN only when both are);
  `maximum`/`minimum` propagate NaN. `migrate --step names` rewrites calls and declarations written with the C
  names (`expm1`, `log`, `fma`, `fmod`, `remainder`, `fmax`, `frac`, `signbit`, `total_order`, `cmp`, `ashl`, ...).
- `abs()` on signed ints is failable (`abs!()` throws on MIN); the force-unwrap
  idiom is `try x.abs()!!`
- Equality `==`/`!=` compares VALUES (lowers to `.eq()`); identity `===`/`!==`
  compares whether two references are the SAME object (entities, handles, and `Addr`
  only — RF-S440 on value types and access tokens). See §7.
- **`==` and hashing are automatic, `Comparable` is opt-in.** Every record and every entity has `==`/`!=`
  with no `obeys` line when each member variable has `==` (the builder writes `eq` comparing them in
  order, a hand-written `eq` takes its place), so two distinct `Player(name: "Ada")` entities are `==` and
  not `===`. A member variable without `==` (a `Callable[...]` routine value) means the type has none: `==` on it is RF-S065 and passing it where `needs T obeys Equatable` is asked is RF-S150,
  both naming that member variable. A record also hashes automatically (a `Set` element / `Dict` key) when
  every member variable hashes, else RF-S150 at the use names the member variable. An entity hashes only
  with `obeys Hashable` (its member variables can change while a `Set` holds it): `Set[Player]` without it is
  RF-S150. `Text`, `Bytes`, `Int` and `Decimal` hash their value, so equal values hash equal however
  they were built. `D32`/`D64`/`D128` are `Hashable` too and hash the value, not the digits they keep: `1.0` and
  `1.00`, `+0` and `-0` hash the same, and every NaN hashes the same. An optional `T?` has `==` and a hash when `T` does (two absent ones are equal, an absent and
  a present one differ). An entity's builder-written `==` keeps the pairs it is comparing for the running task
  (`rf_eq_enter`), and meeting one again counts as equal, so structures that lead back to themselves compare by
  shape and end, and its hash stops at an entity it is already inside of. `Comparable` (`<`, sorting) still needs
  `obeys Comparable`.

**Conversions: `x.to[T]()`, the one way to convert.**

```razorforge
var small = count.to[U8]()        # number to number
var big = count.to[Int]()      # Int/Decimal convert on to B32/B64 (nearest value)
var n = "42".to[S64]()             # text parsing (also Int, Decimal, B64, ...)
var address = a.to[U64]()          # an Addr's number, and 'x'.to[Text]() makes one-character text
var raw = status.to[S32]()         # a choice's value (also to[S64]()), perms.to[U64]() a flags bitmask
var wide: U64 = count.to()         # T left out: the declared type gives it
var fits: U8? = try count.to()     # none when it does not fit
var low = count.to_wrap[U8]()      # the low 8 bits (300 -> 44), never fails
var near = count.to_clamp[U8]()    # the nearest U8 (300 -> 255), never fails
var why: Check[S64] = check line.to()
```

**A byte's and a character's numbers are named routines, not conversions.** `b.value()` is a `Byte`'s `U8`
(`b.value().to[S64]()` for another type), `Byte(v)` makes one from a `U8` (never fails). `c.codepoint()` is a
`Char`'s `U32`, `Char.from_codepoint(n: v)` (a `failable common routine`, takes a `U32`) fails on a surrogate
(0xD800..0xDFFF) or above 0x10FFFF. `c.to_byte()` is an ASCII character's byte (failable, throws for non-ASCII).
`c.digit_value()` (`base: 10` default, `base: 16` also takes `a-f`/`A-F`) is a failable `U8`, ASCII only, throws
`InvalidDigitError` for a character that is no digit of the base. `b.to[U8]()`, `c.to[U32]()`, `v.to[Byte]()`,
`n.to[Char]()` are build errors (RF-S646) naming these. A `Byte` is eight bits with no arithmetic: it obeys
`Bitwiseable` (`&`, `|`, `^`, `~`, result `Byte`) and has `<<` and `>>>`. `>>` on a `Byte` is a build error
pointing to `>>>`, `b + 1` points to `Byte(b.value() + 1)`. A bare number where a `Byte` goes is a `Byte`
(`b & 0x7F`, `b == 0x41`), outside 0..255 a build error. `migrate --step bytes` rewrites `v.to[Byte]()` to
`Byte(v)` and `c.byte_value()` to `c.codepoint().to[U8]()`, and lists `n.to[Char]()` and `digit_value()` compared
with -1.

- Widening never fails. A value that does not fit `T`, or text that does not read as a `T`, crashes
  loudly (`IntegerOverflowError`, `InvalidValueError`, ...). Recover with `try` (→ `T?`) or `check`
  (→ `Check[T]`, holding the error).
- A conversion to a narrower float crashes when a finite value rounds to an infinity
  (`NumericOverflowError`): `B64.to[B32]()`, `to[B16]()`, `to[BF16]()`, `B32.to[B16]()` / `to[BF16]()`,
  `B128.to[B64]()` / `to[B32]()` / `to[B16]()`, `D32`/`D64`/`D128` to a binary float whose range they pass
  (`D64.to[B64]()`, `D32.to[B32]()`, ...), and the integers whose largest values pass a float's range
  (`S32`..`U256` to `B16`, `U128`/`S256`/`U256` to `B32`). An infinity stays infinite and a NaN stays a NaN.
  Each has `to_unchecked[T]()`, the raw rounded value (an infinity past the range); a value in range is the
  same in both. `try big.to[B32]()` recovers. A conversion that can't overflow has only `to[T]()`.
- `[T]` may be left out where the surrounding code says the type: a declared variable type, the
  routine's return type (`return count.to()`), a parameter (`takes(n: x.to())`), and through
  `try`/`check`. Where nothing gives it (`var n = count.to()`, mid-chain `count.to().add(1)`) it is
  RF-S645: write `.to[S64]()`.
- A pair with no conversion is RF-S646 at the call ("There is no conversion from 'Text' to 'Bool'.").
  Converting a value to its own type is RF-S646 too (use the value as it is), and so is `.to[...]()` on a
  value of a type parameter: conversions are written per concrete type, so take the parameter as the
  target type or convert where the type is known.
- A number type built from one number with `T(x)` (`U8(x)` with an `S64` `x`, `S64(1.5)`, `Decimal(2)`) is RF-S655
  in a program file: it reads the bits of `x` and gives another number (`U8(300)` is 44, `Decimal(2)` is
  `2E-6176`). The message points to `x.to[T]()`, and for a literal to a declaration of the type
  (`var b: U8 = 200`), where the builder checks that it fits. Inside `danger` (and in the standard library and
  builder-written bodies) `T(x)` still reads the bits. `migrate --step numbers` rewrites `T(x)` to `x.to[T]()`
  between binary integer and float types, types `var b = U8(200)` as `var b: U8 = 200`, and lists the rest
  (decimal types, `BF16`, a literal inside an expression).
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
  conversion that can fail is declared `failable routine Text.to[S64]() -> S64`. A routine written for every
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

until i >= 10             # stops when i >= 10, tested before each round
    i = i + 1

loop                      # doesn't end by itself: break, return or throw leaves it
    n = n + 1
    if n >= 3
        break
```

Loops are `until c` and `loop` (plus `each` and `expand`). `until c` stops when `c` becomes true, tested before each
round: write the condition that ends the loop. `loop` doesn't end by itself: only a `break`, `return` or `throw`
leaves it, so the lines after a `loop` without a `break` never run (RF-S786) and a routine whose last statement is
such a loop needs no `return` after it. `while` is not a keyword: a loop written with it is RF-G224, and
`migrate --step until` rewrites it (`while true` → `loop`, `while not c` → `until c`, an integer comparison turned
around, `while i < n` → `until i >= n`, a float comparison and anything else → `until not (c)`, listed). An `until`,
`each` or `loop` may end with a `then` block. It runs once whenever the loop is left for the line after it: when the loop ends by itself
and after its own `break` (a `break` of a loop inside only leaves that loop). A `return` or `throw` leaves the
routine and skips it. A loop whose `then` ends in `return` always returns, so the routine needs nothing after it.
Loops have no `else` (RF-G219): the case where the loop would not run even once is an `if` before the loop.
A loop may carry values from one round to the next: `until n <= 1 with n: U64 = start, steps: U64 = 0`
(`each x in xs with sum: S64 = 0` and `loop with i: S64 = 0` too). Each carried value is bound anew every round, from what the round's
`continue with n: after, steps: steps + 1` passes (every carried name once, in any order, all computed before
any is stored, so `continue with x: y, y: x % y` swaps). The names are visible in the condition, the body and
the `then` block, and nowhere after the loop. A body that carries values must not run off its end: every path
ends in `continue with`, `break` or a `return` (RF-G217), a bare `continue` in such a loop is RF-G218, as is
`continue with` in a loop without `with`. `break` takes no values. The idiom replaces a `var` that only the
loop changes:

```razorforge
routine collatz_steps(start: U64) -> U64
    until n <= 1 with n: U64 = start, steps: U64 = 0
        var after = if n % 2 == 0 then n // 2 else 3 * n + 1
        continue with n: after, steps: steps + 1
    then
        return steps
```

`when` is the pattern match:

```razorforge
when m
    is None => show("absent")
    else as p  => show(f"present: x={p.x}")   # else with binding: the one type left (here the value)

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
    is Monster => nothing                        # an arm testing a member variable doesn't cover the type
```

The same record pattern works after `if`: `if cell is Monster(hp: 0) as m`.

`else as name` is allowed only when one type is left after the earlier arms, and `name` has that type: the value
of a `Maybe` after `is None`, the last member type of a variant, or (on a `Check`) every error left, taken as one
type. When more than one type is left it is `RF-S359`: write an arm for each.

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
An arm may also be `throw ...` (`else => throw InvalidValueError(message: word)`): it gives no value, so the other
arms decide the type. In a `failable` routine the failure goes to the caller, where `try` / `check` take it like
any other, and in a routine that is not failable it crashes on the spot.

## 6. Routines

```razorforge
routine add(a: S64, b: S64) -> S64
    return a + b

routine Point.magnitude() -> B64        # method: Type.name, receiver is `me`
    return (me.x * me.x + me.y * me.y).to[B64]().sqrt()

failable routine get_text(n: S64) -> Text      # can throw
    when n
        == 0 => throw DivisionByZeroError()
        else => return "ok"

dangerous routine raw_poke(p: Addr)     # callable only inside danger blocks
    ...
    return
```

- Call with named args: `add(a: 1, b: 2)`. Recover a failable call with a keyword:
  `try get_text(n: 0)` → `Maybe[Text]`/`Text?`; `check get_text(n: 0)` → `Check[Text]`
  (keeps the `Crashables`). A bare `get_text(n: 0)` crashes loudly on failure.
- **Wired routines** are builder-synthesized lifecycle/operator hooks (NO sigil):
  the constructor `T(...)`, `destruct`, `copy`, `eq`, `compare`, `represent` (to-text), `diagnose`
  (debug text), `getitem!`/`setitem` (indexing), `emitter`/`emit` (walking), `add`
  etc. (operator overloads). Wired-ness is inferred from protocol conformance.
- Failure inside a failable routine: `throw SomeError(...)`. A routine with no
  value to give throws `AbsentValueError()`.
- Failability is on the DECLARATION only (`failable routine S64.from_digit_bytes_at(...)`,
  `failable routine Celsius(from: Text)`). Calls never mark it:
  `S64.from_digit_bytes_at(bytes: bs)`, `x.divmod(other: m)`, `t.to[Celsius]()`.
  `foo!(...)`, `x.foo!(...)` and `T![..](...)` are RF-G211; `foo(args)!` is a parse
  error too. A constructor is declared `routine T(...)` / `failable routine T(...)`
  (`routine T.create(...)` is an ordinary member routine named `create`), and
  overload resolution picks the failable or plain one for you.
- **Annotations are a fixed set.** `@readonly`, `@writable`, `@reshaping`, `@discardable`, `@positional`,
  `@infix`, `@inline`, `@noinline`, `@no_optimize`, `@crash_only`, `@untraced`, `@blocking`, `@layout(...)`, `@link(...)`,
  `@target(...)`, `@test`/`@case`/`@skip`/`@time_limit` (routine tests, below) and the
  library's own (`@llvm`, `@llvm_ir`, `@rf`, `@innate`, `@generated`, `@override`, `@overridable`,
  `@self_locking`). Any other name is RF-G221 (a misspelling names the close one). `@noinline` keeps a routine
  out of its callers (LLVM `noinline`, its own body still optimized), `@no_optimize` also turns its optimization off
  (`noinline optnone`), and `@inline` with `@noinline` on one routine is RF-G227. There is no
  `@deprecated`, no `@initonly` and no `@bench` yet.
- **Routine tests.** `razorforge test` builds a file once per `@test` routine, with a `start()` that calls it in
  place of the program's own: once per `@case(input: (a, b), output: r)` (arguments in parameter order, the result
  compared with `==`), or once with no arguments when there is no case. `@skip("reason")` reports it skipped,
  `@time_limit(1s)` stops it (units `ns us ms s m h d w`), `@memory_limit(64kib)` fails it when the program holds
  more memory than that at any point (units `b kb mb gb kib mib gib`, counted by the runtime from RF_MEMORY_LIMIT). Lines read `ok    file::routine`. The program itself is still tested when the file
  has a `start()` or `.expected`/`.exit`/`.error`/`.input` files.
- Bare routine names are first-class values: `select(transform: double)`
  (free routines only).

## 7. Records vs entities (the memory model)

```razorforge
record Point          # VALUE type: copied, no identity, no destructor
    x: S64
    y: S64
# construct memberwise, strictly named:
var p = Point(x: 3, y: 4)

entity Resource       # ONE legitimate version, so it lives in one place; deterministic destruct
    tag: S64

var b = Resource(tag: 7)
consume(r: steal b)   # handed to what keeps it now; using b afterwards = build error
# destruct runs exactly once, at the end of the scope that keeps it (anchored at `return`)
```

- A container, a member variable or a manager that holds an entity is where it is kept; `steal` marks each
  hand-over to such a place.
- A name `steal` emptied takes a new value with `=` (`keep(i: steal a)`, then `a = Item(n: 2)`, on every path or on
  some): the new value is torn down once at the end of the scope (or before the next `=`), and the value that left
  is not torn down there.
- A `return` ends the routine's own variables: a local handed over inside the returned value moves without
  `steal` (`return Shelf(book: book)`, `return (a, b)`, `return xs`). `steal` on such a local there is RF-W259 (quick
  fix and `migrate --step return-steal` remove it). A parameter handed over in a `return` still takes `steal`.
- A written `destruct` (a plain `routine T.destruct()`, never `dangerous`: RF-S807, never `failable`: RF-S815) tears down what the entity
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
  Returning a bare entity hands it to the caller (`var a = make()` keeps it, torn down at scope exit). A routine
  handing back an entity that stays where it is (a container's element) returns an access token, never the bare
  entity: the entity would otherwise be kept in two places, and torn down twice.
- **Access tokens** (RF's answer to "borrow" — never call them borrows). A token
  remembers the object it was taken from (its SOURCE), and every rule below is
  checked at build time:
  - Use one inline, keep it in a variable (`var v = a.view()`,
    `var m = a.modify()`, re-pointable to another object of the same type), or
    open it for a block with `viewing a as v` / `modifying a as m`. A token is never part of another value: a field,
    a variant branch, or a type argument (`List[Viewing[T]]`, `Maybe[Viewing[T]]`, a tuple holding one, a generic type
    given one, `var xs = [a.view()]`, a variant written in place `Viewing[Cell] | S64`, a global) is RF-S601, written
    or inferred. A generic routine whose type parameter a call makes a token (`same(x: l.view())` with
    `routine same[T](x: T)`) is RF-S601 too: pass what you need from the token, or give the routine a
    `Viewing[T]` parameter and pass the entity. The one value that carries a token for a moment
    is what `try` gives on a routine returning a token (`try xs.view_at(index: i)`, an `each` step, `enumerate`'s
    `(index, token)` pair): unpack it with `when` right there. Binding that `Maybe` to a name is RF-S601. A lambda
    never captures a token (RF-S606).
  - The source is free to change. Replacing it (`a = …`, `h.inner = …`), moving
    it out (`steal a`), leaving its scope, or calling a routine that replaces it
    (`h.reset()` whose body assigns `me.inner`, followed through its calls) ENDS
    the token. Using an ended token is RF-S643, reported at the use. A change that
    cannot reach the source (`h.count = 1`, `h.bump()` touching only `count`)
    does not end it.
  - A call that can replace the source of a token handed to that same call is
    RF-S639 (`h.replace_with(t: h.inner.view())`).
  - A name a pattern binds to an entity in place is a token on the matched place: `else as c` of `when s.item`,
    `is Cell as c`, a member name in a record pattern (`if ch is Chest(item: as c)` reads `ch.item`), and the element
    an `each` loop over entities hands out (from the walked container). Replacing that place (`s.item = none`,
    `ch.item = ...`, `xs[0] = ...`), moving it out (`steal ch.item as old`) or dropping it ends the name, and a later
    use is RF-S643. A value element or member is a copy and is not affected.
  - Two names that may be the same entity without the builder knowing it are treated as maybe the same: two token
    parameters (and `me`) on the same entity type, and two `Retained` handles on one (a copy of a handle, a handle
    that came from a call, a field or a parameter: one made right there with `Retained(from: ...)` and only reached
    through is its own). A change through one ends the tokens taken from the same part of the other (RF-S643), unless
    a `modifying a, b` block over both checked that they differ. Handing one call two tokens taken from the same
    entity through differently named token variables (`var m2 = m`, then `both(a: m, b: m2)`) with one of them
    written is RF-S623, like handing it the same container twice.
  - A token parameter takes the entity itself: `total(items: xs)` passes
    `xs.view()` for `Viewing[T]` (a temporary works too), `grow(items: xs)` passes
    `xs.modify()` for `Modifying[T]`. A record value or a value of a type parameter
    is handed over as the address of the place it is in, and a handle or a wider
    token over a `T` is narrowed the same way: `Retained[T]`, `Roamed[T]` or `Modifying[T]`
    to `Viewing[T]`, and `Retained[T]` or `Roamed[T]` to `Modifying[T]`. Pass the handle as it is: writing `.view()`/`.modify()`
    on it adds nothing.
  - A routine may return a token taken from `me` or from a parameter the caller
    keeps owning (a token parameter):
    `return me.inner.view()`, or `peek(h: Viewing[Holder])` returning
    `h.inner.view()`. Different returns may use different token parameters, and an
    `if`/`when` expression may choose between them:
    `shorter(x: Viewing[List[S64]], y: Viewing[List[S64]]) -> Viewing[List[S64]]`
    returning `x` or `y` builds. Never from a local, a temporary, or an entity
    parameter the routine owns (RF-S600). The builder works out, per routine, the
    parameters (and `me`) the token may come from, through calls too
    (`return shorter(x: p, y: q)` may come from `p` or `q`). The caller's token
    has every argument handed to one of them as a source: replacing, stealing or
    ending any of them ends it (RF-S643 at a later use), and for an element token
    a change that can add to or remove from any of them does too.
  - An entity element read into a variable is a token on the element with the
    container's permission: `var b = boxes[0]` holds `boxes.modify_at(index: 0)`
    when `boxes` can be changed there (`b.n += 1` changes the element in the
    list), and `boxes.view_at(index: 0)` when `boxes` is reached read-only (a
    `Viewing` parameter, a `@readonly` routine's `me`). Anything that can add to
    or remove from `boxes` ends it.
  - **`Viewing[T]` / `Modifying[T]`** — the only two tokens: read / write permission, wherever
    they come from. `a.view()` / `a.modify()` on an entity give them directly.
  - **Access blocks: `viewing` / `modifying`** (keywords, RF only). `viewing x as v` calls `x.view_enter()` before
    the block, binds the `Viewing[...]` it returns to `v`, and calls `x.view_exit()` on every way out (its end,
    `return` (whose value is computed first), `throw`, `break`, `continue`); `modifying` is `modify_enter()` /
    `modify_exit()` and a `Modifying[...]`. Any type with the pair is a target: `Guarded` (takes the access: waits
    while another job holds it), `FileStorage` (maps the file), a type of your own. `view_enter()` must return a
    `Viewing[...]` and `modify_enter()` a `Modifying[...]`, with the matching exit (RF-S654 otherwise). An access
    token (`modifying into, viewing from`: only checked to differ from the other tokens and entities, `viewing`
    narrows a write token) or an entity variable of the routine without the pair is a target the builder handles
    itself (its token, `AliasedTokensError` when two names are one entity). `Retained` / `Roamed` have no pair: as a
    target they are RF-S651 (use them directly, they go where a token goes). A weak handle (`Tracked`, `Witnessed`,
    `Watched`) is RF-S651 too: take a strong one with `hold()` first (failable, `AbsentValueError` when the entity is
    gone): `Tracked.hold()` → `Retained`, `Witnessed.hold()` → `Guarded` (then open it), `Watched.hold()` → `Roamed`.
    Several targets: `viewing rates, modifying payer, payee` (a verb carries over to the targets after it until
    another verb comes, each target takes its own `as`, a target without `as` keeps its own name inside the block,
    where that name is the token; a target with no name of its own, `xs[0]`, or a call, needs `as`). `modifying a, b`
    is `modifying a` with `modifying b` inside: entered in the order written, exited in the opposite order. Two
    blocks naming the same Guardeds in opposite orders can wait on each other: `[debug] deadlock-check` reports it.
    A target made in the head (a call, `viewing FileManager.storage[Byte](at: f) as v`) is made once for the block.
    Naming one target twice in a block is RF-S650, a target the block can't open that way RF-S651 (`viewing` an
    `Exclusive`, `modifying` a `ReadOnly` or a `Viewing`, an entity inside another, a type without the pair). Inside
    a block the targets' tokens are known to be different entities, so holding an element of one while another
    grows, or walking one while another grows, is allowed (RF-S640 / RF-S625 otherwise point here).
  - **`try viewing` / `try modifying`**: `try viewing x as v` is `try x.view_enter()`: inside the block `v` is a
    `Maybe` of the token, present when the block entered `x`, and the body branches with `when v` (`is None => ...`,
    `else as t => ...`). Only an entered target is exited. A block has no `else` (RF-G226). With several targets each
    name is its own `Maybe`. A Guarded's enter fails with `ReenterDuringAccessError` when this job has the access
    open already (a routine called from inside the block that opened it), so `try` gives none there and a plain
    block crashes. `try` on a target the block doesn't enter (a token, an entity without the pair) is RF-S611.
    `using ... fallback` is gone (a grammar error that names this form).
  - **A Guarded's access for one call**: `g.view()` / `g.modify()` written as a call argument (or the receiver of a
    member call, `g.view().count()`, or a member variable read through it, `g.view().total`) enters `g` for that
    statement (`view_enter()` / `modify_enter()` before, the exit after): `record_into(entries: log.modify())`.
    Several in one statement are entered in the order written. Anywhere else (kept in a variable, inside a condition
    or a loop head) is RF-S652. `view()` is for `MultiRead`/`ReadOnly`, `modify()` for `Exclusive`/`MultiRead`
    (RF-S160 otherwise). `g.view()` on a `Guarded[T, ReadOnly]` takes nothing and is an ordinary `Viewing[T]`
    anywhere.
  - **Opening it again.** Opening a `Guarded` (or an alias of it) inside a block of the same routine that has it
    open is RF-S630, for reading and for writing alike (waiting would never end, going in would see the block's work
    half done): pass the token instead. Opened again from a called routine, the enter fails with
    `ReenterDuringAccessError` (the runtime knows which job holds which access): a crash, or none under `try`. The token a block opens on a
    `Guarded` never goes to a job (RF-S632): hand the job the `Guarded`. Passing the same write token twice in
    one call is RF-S603.
  - **`using` is for resources only** (types obeying `Enterable`). `using x.view() as v` is RF-S612, and
    `migrate --step access` rewrites the old forms: `using g.modify() as m` into `modifying g as m`,
    `using a.view() as x, b.modify() as y` into one block, `using g.try_modify() as m` + `fallback` (and
    `try modifying g as m` + `else`) into `try modifying g as m_entered` with `when m_entered` / `is None =>` (the old
    `else`) / `else as m =>` (the old body), `using multimodify(first: a, second: b) as (x, y)` into
    `modifying a as x, b as y` (listed for a look: a second argument that is read only is written `viewing`), a
    Guarded's old `consult()` / `amend()` and `Consulting` / `Amending` into `view()` / `modify()` and `Viewing` /
    `Modifying`, and `LockPolicy` / `ReentrantLockError` into `AccessPolicy` / `ReenterDuringAccessError`. A `try`
    block with several targets is listed to rewrite by hand.
  - **The AccessManager** (the runtime's, shared with Suflae and Tessera) knows which job holds which access and
    which one waits for what. A `Guarded`'s `GuardManager` keeps its own entity's readers and writer and turns to
    the AccessManager only when a job has to wait. `[debug] deadlock-check` (on in a `debug` build, off in the
    release modes, `= false` / `= true` in config.toml overrides): before a job waits, the chain of holders is
    followed, and a cycle crashes with `DeadlockError` naming the jobs and where each took its access.
    `[debug] access-order = true` (off by default, costs time on every access): two accesses taken in opposite
    orders crash with `AccessOrderError` before they ever wait on each other.
  - **Two tokens that may be the same entity.** Two token parameters (or shared handles) on the same kind of
    container may be the same container, so holding an element of one (`var p = x.modify_at(index: 0)`,
    `var b = xs[0]` on an entity element, to the end of its block) while the other changes shape is RF-S640.
    `modifying x as a, y as b` (or `modifying into, viewing from`) checks that they are different entities, and
    inside it holding an element of `a` while `b` grows is allowed.
    Handing one container to two parameters of one call when one of them is written
    (`extend(into: xs, from: xs)`) is RF-S623.
- **RC wrappers** (managers for sharing an entity for longer, reference-counted):
  - **`Retained[T]`** — single-thread strong handle (`Retained(from: steal e)`, `=` makes one more holder);
    forwards direct access to the retained entity.
  - **`Guarded[T, TPolicy]`** — multi-thread strong handle (atomic, `=` makes one more holder); `TPolicy` is an
    `AccessPolicy`: `MultiRead` (several readers or one writer), `Exclusive` (one user at a time, `modifying` only),
    `ReadOnly` (no writer). Reaching its inner value goes through a `viewing` / `modifying` block or a `g.view()` /
    `g.modify()` argument (RF-S649 on the handle), except `Guarded[T, ReadOnly]`, which nothing may change and is
    read directly (`g.name`, `g.label()`, reads only).
  - A binding from outside an `if`/`when`/loop is taken with `steal` on every path going on past it or on none
    (RF-S616); a loop turn may not take it and go on (only where the loop ends).
  - `steal` on a `Retained`/`Guarded` gives the entity inside (`var t: Table = steal table`) when it is the last
    strong handle, crashing with `SharedHandleStealError` otherwise; weak handles can't be stolen.
    Every `Unbroken` routine is `@readonly` (each change synchronizes itself): `stats.open.fetch_add(delta: 1)`
    works through a read token or a `ReadOnly` Guarded.
  - **`Tracked[T]`** (single-thread) / **`Witnessed[T, TPolicy]`** (multi-thread) — weak handles.
- **`Hijacked[T]`** — a raw handle that manages nothing (no-op destruct); the stdlib's
  internal buffer/pointer mechanism, used inside `danger`.
- **`Addr`** — an address with no pointee type, LLVM `ptr` (C's `void*`): what the allocator and native code
  take and hand back. It names a place, so two compare with `===` / `!==` (`==` on an `Addr` is RF-S061 and
  points there), `Addr.hollow()` is the address 0 (`is_hollow()` / `is_not_hollow()`). It moves by a
  `ByteSize` with the wrapping operators only: `a +% 16b`, `a -% size`, and `b -% a` is the distance from `a` to
  `b` as a `ByteSize` (all three wrap around and check no bounds, obeying `WrappingOffsetAddable` /
  `WrappingOffsetSubtractable`). Plain `+` / `-` on an `Addr` are errors that point to `+%` / `-%`, and it has no
  ordering. `x.to[Addr]()` / `a.to[U64]()` convert, `Hijacked[T](from: a)` reads it as a `T`. `Address` and
  `CPtr` are both `Addr` now (`migrate --step addr` renames them, rewrites `==` / `!=` between known addresses to
  `===` / `!==`, rewrites `a.offset(bytes: 8)` on a known address to `a +% 8b`, and lists what it cannot type).
- **`T.hollow()`** — every type has it (builder-made): the value with every bit zero (`0`, `false`, a choice's first
  member, an empty handle; for an entity a new entity whose member variables are all zero, a collection with no
  memory). It is `dangerous` (only inside `danger`): the zero value can break the type's promise. `Addr.hollow()` is
  its own routine and is NOT dangerous. `is_hollow()` exists only on the nine handle types (`Retained`, `Tracked`,
  `Guarded`, `Witnessed`, `Roamed`, `Watched`, `Hijacked`, `Viewing`, `Modifying`) and `Addr`, never dangerous.
  Every `destruct` leaves a hollow value alone (an entity's `destruct` returns at once on a zero reference). The old
  free `hollow[T]()` is `T.hollow()`. Not in Suflae (SF-S447).
- Records never use tokens or `as_entity` — those are entity concepts.
- `danger` blocks / `dangerous` routines mark **only** operations that can
  ACTUALLY cause memory-unsafety — UB, a memory race, use-after-free,
  double-free, or a memory leak. Ordinary code (arithmetic, collection ops,
  channels, failable calls) is **never** wrapped in `danger`; it is not a
  catch-all for "risky" or "advanced". A `dangerous` routine can only be called
  inside a `danger` block (RF-S609). That holds for every way of reaching it: a lambda's body is outside the
  `danger` block the lambda is written in, and an operator, a compound assignment or an f-text insertion whose
  routine is `dangerous` (`p + q` on a dangerous `add`, `f"{p}"` on a dangerous `represent`) is written inside
  `danger` like the call.
- A routine value taken from a dangerous routine is a **dangerous callable** (no keyword): it goes only to a C
  routine's `Callable` parameter (`C::qsort(..., by_value)`), to a dangerous routine's `Callable` parameter, or into a
  local variable (`var f = raw`, which is a dangerous callable from then on). A dangerous routine's own `Callable`
  parameters are dangerous callables in its body. A dangerous callable is called only inside `danger` or a dangerous
  routine. Putting one in a plain `Callable` place (a plain routine's argument, a member variable, a collection, a
  result, a lambda's value) is RF-S609.
- No lifetime syntax; safety comes from the one-legitimate-version rule (an entity lives in one place), marked
  hand-overs (`steal`), scope-bound access tokens and managers.

```razorforge
viewing c as v               # Viewing[Counter], read-only, dead at block end
    show(f"count = {v.value}")
modifying c as m             # Modifying[Counter], write intent
    m.increment()
modifying payer, payee       # payer entered first, then payee: the names are the tokens
    payer.balance -= amount
    payee.balance += amount
try modifying seats as s_entered     # a Maybe: none when this job has seats open already
    when s_entered
        is None =>
            show("busy")
        else as s =>
            s.take(seat: n)
```

- **Identity comparison `===` / `!==`** — "are these two the SAME object?", distinct
  from value equality `==`. Valid only on reference-carrying operands: an `entity`, a
  handle (`Retained`/`Guarded`/`Tracked`/`Witnessed`/`Watched`/`Hijacked`), or an `Addr`.
  A value type (record/scalar) is a build error (**RF-S440**) — values have no identity;
  use `==`. An access token (`Viewing`/`Modifying`) is RF-S440 too: compare the entities
  it came from. It is a primitive pointer compare, never a `.eq()` call, so it is not overloadable.
  For bare entities (each kept in one place) `a === b` is trivially `false` between distinct
  bindings; it earns its keep where aliasing exists — two `Retained[T]` handles that may
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
>   nullable pointer; each node kept in one place makes the structure a tree = acyclic = deterministic teardown).
> - Generic param kinds: bare `[TKey, TValue]` = `AnyType`; annotate `T: record` / `T: entity` only to constrain.
>   No copyability bound (record ⟺ copyable). No `Boxed`/`Owned`/`Unique` wrapper — the managers
>   below name only *departures* from the bare-entity default (kept in one place, unnamed).
> - Entity allocation is stack/inline by default; heap is induced by a wrapper (`Retained`/…) or recursion,
>   not a marked concern.

**`record` vs `bundle` (built, RazorForge only).** A `record` is a copied value, so it may hold neither an
entity nor a type parameter that could be one (**RF-S409**): a copy would make two of the one entity. Fix it
by declaring the type `entity`, holding a handle that copies (`Retained[E]`), keeping the parameter to values
(`needs RecordType T`), or declaring it `bundle`. A `bundle` is the generic aggregate whose kind each
instantiation decides: `bundle Pair[T]` with `T = S64` is a copyable value; with an entity `T` it is
kept in one place like an entity (handed over with `steal`, RF-S413 on a copy), has no identity (`===` is an error), is laid out in
place, is never copied by `=`, and has `duplicate()` only when every member is Copyable (RF-S150 naming the member
otherwise). A bundle needs type parameters (**RF-S445**) and cannot contain itself (**RF-S446**; a recursive
structure is an entity, as for records, RF-S960).
In a generic body, a value of an unconstrained type parameter may be an entity, so keeping a second copy of
it (`var y = x`, `return me.value`, a tuple of it, handing it to a parameter of a type parameter as in
`xs.add_last(value: x)`) is RF-S413 on the template's own line: `steal` it, keep the
parameter to values (`needs RecordType T`), or work on it in place. An entity handed to a parameter written `x: T`
belongs to the routine: one it does not keep (hand over, store, return) is torn down at its exits before it keeps
it, a `throw` included (`try xs.add_at(index: 5, value: Item(n: 3))` that fails tears the `Item` down).
`needs RecordType T` accepts only values: a bundle instance holding an entity does not satisfy it. Several
kinds on one line repeat the kind: `needs RecordType TKey, RecordType TValue`. Stdlib bundles: `Maybe`, `Array`,
`SplitArray`, `DictEntry` (tuples and variants follow their elements the same way). `Range[T]`,
`Unbroken[T]` and `SplitArray`'s element emitter are `needs RecordType`. `PriorityQueue[TPriority, TElement]`
asks only `TPriority obeys Comparable`. A queue is not walked directly: `pq.unordered()` and `pq.ordered()` copy its
`(priority, element)` pairs (storage order / smallest priority first, `needs RecordType TElement`), `pq.drain()`
empties it into them smallest first (`each (priority, element) in pq.drain()`). An
`Array`'s emitter reads each element in place, so `each r in arr` works on an array of entities (the binding
only reads; the array keeps owning each element). Suflae has no
`bundle` (an SF entity is shared, so a record holding one copies the handle).

## 8. Generics and protocols

```razorforge
routine largest[T obeys Comparable](items: Viewing[List[T]]) -> T
    ...

record Pair[TFirst, TSecond]
    first: TFirst
    second: TSecond

protocol Emittable[T]
relates Emitter obeys Emitter[T]       # associated type slot
    routine emitter() -> Me/Emitter    # `/` projects an associated type

entity List[T] obeys Emittable[T]
relates ListEmitter[T] as Emitter     # associated type binding
```

- Constraint syntax: `T obeys SomeProtocol`. `Me` is the self type.
- A member routine of a generic type may be written for some of its instances only, with a kind on the type's
  parameter (`routine List[T].sorted_by(...)` with `needs RecordType T`: it copies the elements). Calling it on an
  instance the kind leaves out (`sorted_by` on a `List` of entities) is RF-S151 at the call. `sorted()` and
  `sorted_by` copy, so a list of entities sorts in place with `sort()` / `sort_by`.
- Const generics: `Array[T, COUNT]`.
- **Type arguments use `[...]`, never `<...>` — by design; do not propose `<>`.**
  `f < T > (x)` is already a valid chained comparison in RF, so angle brackets
  would force a parse-time guess (C#'s disambiguation rules, Rust's turbofish) plus
  `>>`/`>>>` token splitting. `List[S32]` and `xs[i]` share one parse shape
  (`postfix [ args ]`); whether the thing before `[` is a type or a value is
  settled by name lookup after parsing (`BracketReclassifyPass`), not by a
  heuristic. A generic argument is a type-level `getitem`.
- Everything monomorphizes; there is **no runtime dispatch** of any kind.
- A protocol member declared `@readonly` (`eq`, `compare`, `hash`, the arithmetic operators, `count`, `iter`,
  `getitem`, `duplicate`, …) is implemented `@readonly` too, and its body is checked to change nothing:
  an implementation without it is RF-S771. Suflae has neither `@readonly` nor `@reshaping`.

## 9. Text and formatting

```razorforge
show(f"x={x} and pi≈{pi}")    # f-string interpolation
show(f"debug: {value:?}")     # :? = diagnose (debug) format spec
show(f"{n.hex()} {x.fixed(2)} {name.pad_end(10)} {n.represent().zero_pad(5)}")
```
The diagnose form never makes two values look alike (it does not promise to read back as source): a `Text` diagnoses
as `Text(count: 9, "say \"hi\"\t")` with `"`, `\`, line breaks, tabs and other control characters escaped and a
trailing space before the closing quote, a tuple quotes its text items (`System.Tuple[System.S64, System.Text](1, "1")`),
a container's diagnose quotes its text elements with the same escapes (`["a\", \"b"]` is one text), and a variant
names the member type its value has (`S32(1)`, `Text("a")`, `none`), in a container too
(`System.List[System.S32 | S64](count: 2, [S32(1), S64(1)])`).

The only f-text format specs are `=`, `?` and `=?`. Everything else is a member routine you call
inside the braces:
- **Integers** (`S8`..`S256`, `U8`..`U256`, `Int`): `to_text(radix:)` (2 to 36, lowercase
  digits, `-` for negatives, crashes on another radix) and the shorthands `hex()` / `bin()` / `oct()`.
  No `0x` prefix; `.to_uppercase()` for `FF`.
- **Reals** (`B16`..`B128`, `D32`..`D128`, `Decimal`): `fixed(places)` — exactly `places`
  digits after the point, integer part in full, rounded from the exact value by the type's rule
  (half to even, `2.675.fixed(2)` is `2.67`, half up for `Decimal`) or `fixed(places: 2, rule: RoundingRule.FLOOR)`. `to_text(sig_digits:)` on
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
  in `Standard/RazorForge/System/Text/Text.rf`.
- **`Bytes` `obeys Comparable, Hashable`** — usable as a `Dict` key and sortable.
  It supports range slicing (`bs[a til b]`), sub-sequence `contains`/`find!`/
  `split`, and hex `to_hex`/`from_hex!`. Container `represent` quotes elements
  by type (Text/Bytes → `"…"`, Char/Byte → `'…'`).

**Text patterns** (`import Text/Patterns`, one engine for RazorForge, Suflae and Tessera, in Ingrid). A pattern is
built from named pieces, one after another: `preset DATE: TextPattern = TextPattern.of([capture("year",
digit.repeat(4)), text("-"), capture("month", digit.repeat(2))])` (a `preset` pattern is compiled at build time).
- Pieces (`TextPiece`): `text("-")`, `digit`/`letter`/`space`/`word_char` (ASCII), `unicode_digit`/`unicode_letter`/
  `unicode_space`/`unicode_word_char`, `any_char` (line feed included), `one_of("aeiou")`, `none_of(",;")`,
  `char_range(from: 'a', to: 'z')`, anchors `start_of_text`, `end_of_text`, `start_of_line`, `end_of_line`,
  `word_boundary`. The bare ones are presets (no parentheses).
- Repetition: `p.repeat(4)`, `p.repeat(2 to 4)` (`til` excludes the end), `p.repeat_at_least(1)`, `optional(p)`,
  `any_number_of(p)`, `one_or_more(p)`. Every repetition is greedy (takes as many rounds as it can); there is no
  shortest repetition. To take text up to a delimiter, repeat a piece that can't be it (`any_number_of(none_of("<"))`),
  since `any_number_of(any_char)` runs to the last delimiter.
- Alternatives name their rule, there is no default: `either_longer([...])` (the longest match there),
  `either_shorter([...])` (the shortest), the first written winning a tie.
- `capture("name", p)`, `ignoring_case(p)`, and `TextPattern.of(pieces: [...], ignore_case: true)`.
- Searches: `p.matches(text)` (whole text), failable `p.find(text)` (`try p.find(t)` gives `none`), `p.find_all(text)`
  (non-overlapping, `List[TextMatch]`), `p.replace(text: t, with: "#")`, `p.split(text)`. A `TextMatch` has `start()`,
  `end()` (character positions, `U64`), `text()`, failable `group("name")` (`CaptureNameError` for an unknown name,
  `AbsentValueError` for a capture that took no part).
- `BytesPattern` over `Bytes`: `byte(0x89)`, `bytes(b"PNG")`, `any_byte`, `byte_range(0x00 to 0x1F)`, `start_of_bytes`,
  `end_of_bytes`; `BytesMatch.bytes()`. Text and bytes pieces never mix (a type error).
- Failable `TextPattern.from_regex(text)` / `BytesPattern.from_regex` read the usual regex syntax (unnamed groups
  capture as `"1"`, `"2"`, `|` is the longest alternative, `(?i)` `(?m)` `(?s)` at the start). Backreferences,
  lookaround, `*?` and `*+` throw `PatternSyntaxError` with the position; malformed pieces throw `InvalidPatternError`.
- A search takes time linear in the text times the pattern size (no backtracking blow-up).

## 10. Collections quick reference

Verify exact signatures in `Standard/RazorForge/Collections/` — highlights
that differ from other languages:

- Making room up front is failable: `reserve(new_capacity:)` on `List`, `RotatableList`, `BitList`, `PriorityQueue`
  and `SplitList`, and the creators `List[T](capacity:)`, `List[T](count:, fill_value:)`, `RotatableList[T](capacity:)`,
  `BitList(capacity:)`, `PriorityQueue[P, E](capacity:)`, `Set[T](key:, capacity:)` and `Dict[K, V](key:, capacity:)`
  throw `OutOfMemoryError` when the storage is more bytes than a `U64` counts (`requested_bytes` is then `U64_MAX`)
  or the heap has no room for it. The size never wraps around, `check xs.reserve(new_capacity: n)` receives the
  failure, and the collection then stays as it was. A bare call crashes with the error. Growth on the way
  (`add_last` past the capacity) is not failable: running out of memory there crashes.

- `list.add_last(value: v)`, `list.remove_first!()`, `list.remove_at!(0)` —
  removal is failable (empty/out-of-range throws); `try list.remove_first()`
  returns `Maybe`.
- `set.add(value: v)` returns Bool, `@discardable`: no `drop` needed.
- `dict[k] = v` puts a value: it replaces the value of a key that is there and adds the key otherwise (there is
  no `dict.add`). Reading `dict[k]` is failable under the hood, so use `try dict.getitem(key: k)` (or `try dict[k]`) when you want `Maybe[TValue]`.
- Indexing `coll[i]` is failable under the hood (`getitem!`); back-indexing
  is `coll[^1]` (last element).
- **Range slicing returns an owned COPY**: `xs[a til b]` (or `xs[a to b]`) on a
  `List`/`RotatableList` yields a new `List`/`RotatableList`, on `Array[T, COUNT]` a `List[T]`
  (slice length is a runtime value, so it cannot be a fixed `Array`). Mutating
  the slice never touches the original. Open-ended `xs[a til ^0]` slices to the
  end. Element type must be `Copyable` (slicing a `List[Entity]` is a compile
  error). For a lazy, no-copy window use the emitter adapters
  `xs.skip(a).take(n)` instead — copy-vs-view is spelled by which you call.
- `List[T]`, `Dict[TKey, TValue]`, `Set[T]`, `RotatableList[T]`, and sorted collections are
  entities. Do not pass a container as a bare parameter when read-only access is
  enough; declare the parameter `Viewing[List[T]]` and pass the container
  (`largest(items: xs)`). Bind `var v = items.view()` when the token needs a name.
- `SortedList`/`SortedSet` have **no positional indexing** — rank access is
  `get_by_rank!(...)`.
- **Emitters.** What walks a value is an emitter (`Emitter[T]`, one `emit()` at a time); what can be walked is an
  emittable (`Emittable[T]`, it has `emitter()`). `each x in xs` takes `xs.emitter()` for you, and you may call it
  yourself: `var it = xs.emitter()`, then `it.emit()` (failable, `AbsentValueError` at the end) or `each x in it`,
  which goes on from where `it` is. An emitter made from a local container (directly, or from its `view()` /
  `modify()`) is itself a token on the container's elements: a local variable may hold it, and it ends when the
  container may change shape or ends (a later use is RF-S643, `xs.add_last(...)` ends it). It hands out what the
  container's walk hands out (`Viewing` from a read-only head, `Modifying` otherwise). A routine may return one made
  from `me` or from a token parameter (`routine big(xs: Viewing[List[S64]]) -> WhereEmittable[S64, List[S64]]` returning
  `xs.where(...)`), and the caller's result is a token on what it passed there. Keeping it in a field, or returning one
  made from anything else, is RF-S644. `(steal xs).emitter()` takes the list's elements into the emitter (each one
  comes out as the value itself, like `each x in steal xs`), so it holds what it walks and can go anywhere, returned
  from a routine too. A range (`var r = 0 til 10`) points at nothing and can be kept. `r.emitter()` on a
  `Retained[T]` gives a `RetainedEmitter[TElement, T/Emitter, T]` (the element type comes from `T`'s `Emittable`
  conformance) that holds one more reference to the entity, so it is no token: it can be returned, kept in an
  entity's member variable, or used after `r` is given another entity. `each x in r` walks the same way. An emitter
  made from the entity itself (`r.view().emitter()`) is a token like one made from a local container.
- An emitter is an emittable (`Emitter[T]` obeys `Emittable[T]`): terminals and adapters work on it and take the
  values it has not handed out yet (`var it = xs.emitter()`, `drop it.emit()`, then `it.to[List]()`, `it.sum()`,
  `it.where(...)`).
- Emitter adapters (from `IterTools`): `select`, `where`, `zip`, `enumerate`, `chain`, `distinct`, `select_many`,
  `min_by`, … — lazily evaluated, chainable, lambdas like `x => x % 2 == 0`. An adapter points at the container it
  was made from, so it follows the same rule: `var big = xs.where(...)` is a token on `xs`'s elements.
- **Collecting.** An emittable is collected with a conversion that names the collection without its element type:
  `xs.where(...).to[List]()`, `to[Set]()`, `to[Dict]()` (the values are `(key, value)` tuples), and in `Collections`
  `to[SortedList]()`, `to[SortedSet]()`, `to[SortedDict]()` (pairs), `to[RotatableList]()`, `to[BitList]()` (Bools),
  `to[PriorityQueue]()` (`(priority, element)` pairs). `to[Array[T, N]]()` names the size (`xs.to[Array[S64, 3]]()`)
  and is failable: fewer or more than `N` values throw `ElementCountMismatchError`. `to[List]`/`to[Set]`/`to[Dict]`/
  `to[Array]` need `import IterTools`.
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
- `List`, `Set`, `Dict` live in **`System`** (always available — never suggest
  `using Collections`). Only these three canonical collections have literal
  syntax of their own (`[1, 2]`, `{1, 2}`, `{1: 10}`); a specialized container
  (`SortedSet`, `RotatableList`, `Array`, `BitArray`, `BitList`, `PriorityQueue`, …)
  takes the same literal when the variable names its type
  (`var s: SortedSet[S64] = {3, 1, 2}`, `var pq: PriorityQueue[S32, Text] = {1: "high"}`).
  A literal takes its element types from where it goes, as a number does: `bytes.add_range(you: [3, 4])` on a
  `List[U8]` builds a `List[U8]`, `total(xs: [5, 6])` for `xs: Emittable[U8]` builds a `List[U8]`, and
  `{1: "a"}` given to an `Emittable[DictEntry[U16, Text]]` builds a `Dict[U16, Text]`. With nothing to go by,
  `[1, 2]` is a `List[S64]`. The empty set literal is `{}` and the empty dict literal `{:}`
  (`var d: Dict[S32, S32] = {:}`, `steal s[b] as old inject {:}`). A `{}` where a dict is expected is RF-S558.
  An `Array` is also built from its elements: `Array[3](1, 2, 3)`.
- **Showing a collection.** `show(xs)` and `f"{xs}"` write the elements, each in the form it has inside a
  collection (`Text` and `Bytes` quoted `"a"`, `Char` and `Byte` as `'a'`, everything else its `represent`):
  `List`, `Array` and the other sequences `[1, 2, 3]`, sets `{1, 2}`, maps `{1: 10, 2: 20}`, the empty set `{}`
  and the empty map `{:}`. `Dict` and `Set` show in insertion order, the sorted collections in order. A
  `PriorityQueue` shows its `priority: element` pairs in the order they come out, the next one first
  (`[1: "a", 2: "b", 4: "d"]`), from a sorted copy, so the queue does not change. A range shows as it is written:
  `0 to 10`, `0 til 10 by 2`, `10 to 0 by -2`. `f"{xs:?}"` (diagnose) puts the qualified type and the count around
  the same text: `System.List[System.S64](count: 3, [1, 2, 3])`, `System.Dict[System.S64, System.S64](count: 0,
  {:})`, `System.Range[System.S64](0 til 10 by 2)`. A fixed-size `Array`, `BitArray` or `SplitArray` has its count
  in its type: `System.Array[System.S64, 3]([1, 2, 3])`.
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
(`each x in xs { grow(xs: xs.modify()) }`) and a loop over an adapter of the collection
(`each x in xs.where(...)` walks `xs` itself): the builder works out which
parameters each routine can add to or remove from, following its calls, and
rejects handing `xs` to one. Handing `xs` to a routine value, or changing another
token, `Retained` or `Tracked` handle to the same kind of container
(`each v in from { into.add(value: v) }`), is rejected too, because the builder
cannot tell whether it is the same container. Open both with
`modifying into, viewing from` to loop over `from` and change `into`.
A loop over a token walks the container the token is on: `var w = xs.view()` (or `viewing xs as w`) then
`each x in w { xs.add_last(value: x) }` is RF-S625, and so is a change of any container a routine that gave the token
may have taken it from (`var w = shorter(x: a.view(), y: b.view())` keeps both `a` and `b` still).
Every stdlib collection marks its adding/removing routines `@reshaping`.
The same holds while a member routine of a collection calls a lambda it is handed: a lambda that can add to or
remove from that collection (or a token or shared handle that may be it), directly or through a call
(`r.sort_by(compare: (a, b) given (alias) => grow(xs: alias, a: a, b: b))`), is RF-S625 at that call, since the
routine is still working on the elements.

**Writes into an element land in the container.** A call, field write, or index
write on an ENTITY element (`grid[0].add_last(value: 1)`, `boxes[0].n = 5`,
`grid[0][1] = 20`) acts on the element inside the container, not on a copy: the
builder reaches it through the container's `modify_at`/`view_at` token (List,
RotatableList, Dict). In that same statement nothing may add to or remove from
the container, directly or through a call (RF-S639), since that could move the
element. A VALUE element works the same way (`m[i][j] = v`, `pts[k].x = v`):
the builder writes through a copy and stores it back.

**An entity element or field cannot be taken by a plain read.** `boxes[0]` and
`p.a` read an entity their container still holds, so putting one anywhere that
keeps what it holds (`x = boxes[0]`, `p.b = boxes[0]`,
`take(b: boxes[0])`, `holder.add_last(value: boxes[0])`, `return boxes[0]`,
`(boxes[0], 1)`, a lambda giving it back: `teams.select_many(transform: t => t.members)`) is RF-S413: it would share one entity between two places with no manager (a tuple
keeps its items, so a variable goes in with `steal` too: `(steal z, 1)`). Take an
element out with a removing routine (`boxes.remove_at(index: 0)`), copy it
explicitly when its type is `Copyable` (`grid[0].duplicate()`), or work on it
where it sits. A new variable (`var x = boxes[0]`) is the exception: it holds a
token on the element with the container's permission (see access tokens). Calling the reading routines
yourself copies a VALUE or handle element (`=`: a copy, one more holder for a
handle). An ENTITY element is never copied behind your back: there
is no copying `getitem`/`first()`/`last()` for it. Read it in place
(`boxes[0]`), or make a new one on purpose with `boxes[0].duplicate()` (the
entity must `obeys Copyable`, else RF-S150). Element
calls and field access (`cells[0].v = 5`, `grid[0].add_last(value: 1)`) still act
in place through `modify_at`/`view_at`, `var c = cells[0]` is still a read
token, and passing an entity element to an owning parameter
(`keep(c: cells[1])`) is still RF-S413 even when Copyable: write
`keep(c: cells[1].duplicate())`.

## 10b. Files and directories (`IO/File`, `FileManager`), the environment (`IO/Environment`)

**Storage has three layers, in all three languages.** `StorageManager` is one storage device (blocks, partition
table), `PartitionManager` one partition of it, and a file manager the file system on one partition (its file table,
giving files space, every read and write). A partition has exactly ONE file manager (a second one over the same
partition fails loudly). **`FileManager` is the operating system's file manager, the one a program calls** (a
`record`, no protocol): its routines are `common routine`s called on the type
(`FileManager.read_text(at: f)`). It reaches every mounted partition (one OS file manager per partition):
`FileManager.partition(at: dir)` (→ `PartitionManager`: `name`, `storage`, `root`, `file_system`, `size`),
`PartitionManager.all()`, `StorageManager.all()` (`name`, `size`), `storage.partitions()` are read-only queries.
Making partitions and formatting are not in 0.5 (they will be `failable`, not `dangerous`). `File(at:)` and
`Directory(at:)` (in `import IO/File`) only say where: cheap values wrapping a path (`at: Text`), constructing one
touches nothing. There is no `IO/FileSystem` module, no free `read_text`/`temp_dir`/`join_path`, and no `/` path
operator (`/` is division).

- **Path members (no disk):** `File`: `name`, `stem`, `extension` (WITHOUT the leading dot), `parent` (→ `Directory`),
  `with_extension(ext:)`, `with_name(name:)`. `Directory`: `name`, `parent`, `file(name:)` (→ `File`),
  `go_to(name:)` (→ `Directory`, was `subdir`).
- **A file must exist before it is read, written or opened.** `read_text`/`read_bytes`, `write_text`/
  `write_bytes`/`append_text`, `open_read`/`open_write`/`open_append`, `set_size` and a block opening a `storage` fail
  with `NotFoundError` on a missing file. Nothing creates a file silently.
- **Errors (`IO/Errors`):** `NotFoundError` (nothing at the path), `NotEnoughPermissionError` (the OS refuses, a
  read-only file system too), `NotEnoughSpaceError` (the device is full), `StorageFaultError` (the device failed to
  read or write, "this storage device failed to read or write at ..."), and `IOError` for every other reason
  ("already exists", a name with a separator, ...). Each is its own crashable (crashables are flat, so `is IOError`
  does NOT take the other four, `is Crashables` takes them all).
- **Making:** `FileManager.make_file(at: File, replacing: Bool = false) -> File`: an existing file is an `IOError`
  ("already exists") unless `replacing: true`, which empties it (the "write a fresh file every run" case:
  `FileManager.write_text(at: FileManager.make_file(at: f, replacing: true), content: x)`).
  `make_directory(at: Directory, parents: Bool = false) -> Directory`. There is no `touch`.
- **Asking:** `exists(at:)` (File or Directory), `is_file`, `is_directory`, `size`, `metadata` (→ `FileMetadata`),
  `can_read`/`can_write`/`can_execute`, `absolute`/`canonical` (→ the same kind).
- **Changing:** `delete(at:)` (a file, or an empty directory), `delete_all(at: Directory)`,
  `copy(from:, to:, replacing: false)` (File→File or Directory→Directory, directory copy is recursive and
  symlink-safe), `set_readonly(at:, readonly: true)`, `set_size(at:, bytes:)`.
  **`rename(at:, to: name, replacing: false)`** changes the name of a file or directory in place (`to` is a NAME, a
  separator in it is an `IOError`), **`move(at:, into: Directory, replacing: false)`** puts it into another directory
  keeping its name. Both give the new `File`/`Directory` back (`@discardable`). `move` is a copy into `into` then a
  delete of the original: on the same partition the OS renames instead (same result), across partitions it copies
  then deletes. Not atomic: a failed copy deletes nothing, a failed delete leaves both (the error says so). An
  existing destination is an `IOError` unless `replacing: true`. `migrate --step files` rewrites
  `FileManager.move(from:, to:)` into `rename` or `move` when the two paths show which (lists the rest).
- **Listing:** `list(at:)` (→ `List[Text]` bare names), `entries(at:)` (→ `List[DirEntry]` with
  `name`/`is_directory`/`modified` in ONE read, no per-entry stat on Windows), `walk(at:)` (→ full descendant paths,
  recursive, symlink-safe).
- **Sessions:** `open_read` (→ `FileSource`, `pull(max:)`), `open_write`/`open_append` (→ `FileSink`, `push(data:)`), bound by
  `using`.
- **Verbs by job:** `load` / `store` read and write a PLACE (`Memory`, `Box`, `Unbroken`: reading again gives the same
  value). `pull` / `push` move bytes through a FLOW (the `Source` / `Sink` protocols: `FileSource`, `FileSink`,
  `stdin()`, `stdout()`, subprocess pipes). `transmit` / `receive` move one message at a time (channels:
  `Transmitter[T].transmit`, `transmit_if_room`). `migrate --step verbs` rewrites the old `load` / `store` on a
  source or sink, the channel `send` / `send_if_room`, and `Sender`.
- **Storage (`FileStorage[T]`):** what a file manager hands out for random access: a mapped file (mmap /
  MapViewOfFile) seen as fixed-size cells of `T`, `T` = `Byte` (one byte per cell) or `Char` (one Unicode scalar per
  cell, 4 bytes, UTF-32LE on disk, a UTF-8 file is NOT one). NOT `dangerous`. `FileManager.storage[T](at: f)` (the
  whole file) and `storage[T](at:, start:, count:)` (a window, in cells) name the file and map nothing: a block maps
  it. `viewing FileManager.storage[Byte](at: f) as v` maps it for reading only, `modifying ... as m` for writing (a
  file that can't be written fails there with `NotEnoughPermissionError`, a missing one with `NotFoundError`: `try`
  gives none), and the block's end unmaps it. Read `v[i]`, write `m[i] = x` through the token (or
  `Viewing[FileStorage[Byte]]` / `Modifying[...]` parameters). `count()`, `flush()`. It cannot grow the file: size it
  with `set_size` first. An index past the end is `IndexOutOfBoundsError`, a `Char` cell that isn't a scalar
  `InvalidValueError`. `T` is limited by the `StorageCell` protocol (only `Byte` and `Char` obey it).
  `migrate --step files` renames `open_storage` / `open_storage_range` to `storage` and lists the lines.
- **The environment (`import IO/Environment`):** `Environment` reads what the OS gives the program, nothing is set:
  `arguments()` (→ `List[Text]`, the program's own name left out), `variable(name:)` (failable, `AbsentValueError`
  when unset, asked of the OS so Windows names are case-insensitive), `to[Dict[Text, Text]]()` (a snapshot),
  `working_directory()`, `home_directory()`, `temp_directory()` (never fails). On a target without an OS: no
  arguments, no variables. There is no way to change the working directory (`set_current_directory` is gone): keep
  a `Directory` and build paths from it, and a child process gets its own through
  `run_program(file:, args:, working_directory: Directory, env:)` (`Subprocess`).
- **Randomness (`System`):** `Entropy.next_u64()` is the OS's random source (hash keys come from it),
  `Random.from_entropy()` (`Random`) seeds a generator with it.
- **migrate:** `migrate --step files` rewrites `OsFileManager` / `OsEnvironment` / `OsEntropy` → `FileManager` /
  `Environment` / `Entropy`, the directory routines →
  `Environment` (import added), `subdir` → `go_to`, `expose`/`wield`/`open_storage` → `storage` and `Exposed`/`Wielded` →
  `FileStorage` (listing those lines), and lists `set_current_directory`.

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
module_paths = ["../shared"]  # folders whose modules join the solution (optional), a header-less file is named by its place
mode = "debug"              # debug -O0 | release -O2 | release-time -O3 | release-space -Os
dump-ast = false            # optional: write .rf.desugared beside generated IR
sa-timing = false           # optional: print semantic-analysis phase timings
show-build-stages = false   # optional: print build/check stage banners
```

With no entry file argument, the CLI walks up from the cwd to find
`config.toml` — `cd` into a project and `razorforge run` works.

**Moving 0.4 code:** `razorforge migrate <folder>` first indents a file written in steps of 2 spaces in steps of 4
(`migrate --step indent` does only that, text literals keep their lines), renames `razorforge.toml` to
`config.toml` (a `razorforge.toml` alone is a build error that names `config.toml`, and a set `RF_DEADLOCK_DETECT`
prints a warning that names `deadlock-check` under `[debug]`), and then rewrites the old spellings: `try_f(x)` →
`try f(x)`, `check_f(x)` / `lookup_f(x)` → `check f(x)`, `routine T.create(...)` → `routine T(...)` (a member
routine `create` that gives back its own type is RF-W104), `-x ** n` → `(-x) ** n` (the old meaning), and `C64` /
`C128` → `C128` / `C256` in a project with a manifest. It lists what it cannot decide (a range whose start is not a
literal may have counted down). Run the named steps after it and `--step suffix` last. A build error on an old name
(`Result`, `Lookup`, `Indexable`, `RetainController`, `Accessing`, `try_f`, `gather`, `blank`, `IO/FileSystem`,
`Moment.now`, ...) names what is written now.

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
| RF-S760      | `try`/`check` over nothing failable   | remove the keyword, or declare the routine `failable` |
| RF-S761      | failable routine value to a plain `Callable` | write the parameter `failable Callable[...]`  |
| RF-S712      | `@throws` leaves out a crashable the routine throws | add it to the list, or recover it inside |
| RF-S010      | literal out of range for target type  | use the right constant (`U8_MAX`)                  |
| RF-S558      | `{}` (empty set) where a dict is expected | write `{:}`                                    |
| RF-G010      | a number names its type (`5_u8`)      | `var x: U8 = 5`, or let the place it goes type it  |
| RF-W007      | ignored result                        | `drop expr`                                        |
| RF-W008      | a Work nothing ever starts          | `start_now()` it, or `retrieve()`/`gather()` it    |
| RF-W009      | a started Work nothing collects      | `retrieve()` it, or `drop job` to stop it          |
| RF-S601      | a token in a field, a variant, or a type argument (`List[Viewing[T]]`, `Maybe[...]` kept in a name, a tuple) | keep the entity or a manager's handle, unpack `try` of a token with `when` at once |
| RF-S632      | `to[Work]` argument unsafe to hand over (a token / `Retained` only with `start_here()`, kept in the routine) | `steal` the entity, pass `Unbroken`/`Guarded`/…, or `start_here()` |
| RF-S623      | one container handed to two parameters of one call, one of them written | copy the read one first, or pass two containers |
| RF-S640      | an element of one token held while another token on the same kind of container changes shape | finish with the held element first, or open both with `modifying x, y` |
| RF-S611      | `try viewing` / `try modifying` on a target the block doesn't enter (token, entity) | remove `try` |
| RF-S654      | a `view_enter` / `modify_enter` that gives no `Viewing` / `Modifying`, or has no matching exit | return the token, declare the exit |
| RF-S612      | `using` given a non-`Enterable` (also `using g.view()`) | `viewing g as v` / `modifying g as m` |
| RF-S630      | a `Guarded` opened again inside a block of the same routine that has it open | use the outer block's token |
| RF-S650      | one block names a target (or a token name) twice | name it once |
| RF-S651      | a target the block can't open that way | the verb its policy has, `as` for a nameless target |
| RF-S652      | `g.view()` / `g.modify()` not passed to a call, or in a condition | pass it, or open a block |
| RF-S642      | `to[Work]` on something that is not a routine, or over generic-parameter-typed values | convert a routine, with concrete types |
| RF-S645      | `x.to()` with nothing that gives the target type | write it: `.to[S64]()`, or give the variable a type |
| RF-S646      | no conversion between the two types (or to its own type) | convert to a type that has one, or drop the `.to` |
| RF-S647      | `steal place as old` leaves a non-optional place empty | add `inject <new value>` |
| RF-S649      | value of a `MultiRead`/`Exclusive` `Guarded` reached on the handle | `modifying g as v` / `viewing g as v`, or the `ReadOnly` policy |
| RF-S648      | `steal … as …` on something that is not a place | take from a variable, member variable or element |
| RF-G216      | `and`/`or`, or `&`/`\|`/shifts, mixed bare | add parentheses: `a or (b and c)`, `(a & b) \| c` |
| RF-G224      | a loop written `while c`              | `until` with the condition that ends it, or `loop` (`migrate --step until`) |
| RF-G225      | a line written `pass`                 | `nothing`, the line with no work (`migrate --step nothing`) |
| RF-S516      | `by 0` on a range                     | a non-zero step, negative to count down            |
| RF-G055/G112 | brace-style or inline-`if` habits     | 4-space indent blocks; conditionals top-level only |
| RF-G227      | `@inline` and `@noinline` on one routine | keep the one you mean                            |

## 13. A complete worked example

```razorforge
module Demo/Inventory
import IO/Console

record Item
    name: Text
    qty: S64

failable routine find_qty(items: Viewing[List[Item]], name: Text) -> S64
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
**`f.to[Work](args)`** makes a routine concurrent work: `download.to[Work](url: u)` computes the arguments NOW
(and a receiver that is a value, as in `p.total.to[Work]()`), defers the call into a `Work[T]`, and starts
nothing. A verb on the Work starts it and chooses where it runs. There is no `spawn`/`async` keyword.

```razorforge
import IO/Console

routine add(a: S64, b: S64) -> S64
    return a + b

routine start()
    var job = add.to[Work](a: 1, b: 2)    # Work[S64]: arguments computed, nothing runs yet
    job.start_now()                    # runs on the runtime's pool of threads
    # ... other work ...
    show(f"{job.retrieve()}")          # waits for the value
    var jobs = [add.to[Work](a: 1, b: 1), add.to[Work](a: 2, b: 2)]
    jobs.start_now()                   # starts both together
    show(f"{jobs.gather()}")           # waits for all, results in input order
    return
```

- The routine is named, not called (as when it is a routine value, `var f = work`), and the conversion takes the
  routine's own arguments. Only that routine is the work: `f.to[Work](x).retrieve()` makes the Work and collects it at once,
  and in `p.twice().total.to[Work]()` the receiver `p.twice()` is computed now.
- **Recovery is where the Work is collected:** `try job.retrieve()` / `check job.retrieve()`. To recover inside
  the work, write a routine that uses `try` and make a Work of it.
- A Work of a routine that returns nothing is `Work[None]`. The type can be inferred:
  `var jobs = [work.to[Work](), work.to[Work]()]` then `jobs.gather()`.

`migrate --step work` rewrites an older `Agent` / `to[Agent]` / `AgentStartError` to `Work` / `to[Work]` /
`WorkStartError`, and a remaining `Agent` is a build error naming `Work`.

Verbs (methods on `Work[T]`, all failable):

- `start_now()` — starts now on the runtime's pool of threads (M:N). The default for most work.
- `start_here()` — starts now on THIS thread beside its other `start_here()` jobs. They take turns only
  where one waits (`retrieve`, `waitfor`, a channel), so they can share values without a lock, and a job
  that blocks the thread stops them all. Inside a `start_now()` job it is a `WorkStartError`, because
  such a job can move between threads.
- `start_apart()` — starts now on an OS thread of its own, for work that blocks or computes for long.
- `retrieve() -> T` — waits for the value. A Work nothing started is started with `start_now()` first.
  Inside a job it parks (other jobs keep running), elsewhere it blocks.
- **A failure stays in its Work.** A failure inside the work does not end the program where the work
  runs, and sibling jobs keep running. It surfaces where the Work is retrieved: `job.retrieve()`
  rethrows it (bare, it crashes loudly there like any failure), `check job.retrieve()` /
  `try job.retrieve()` recover it. The crash report's trace ends with
  `work in start()` (the line that made the Work) and `Thrown again at` names the `retrieve()` line.
- **A Work nobody retrieves** is cancelled and waited for where it is dropped (`drop job`, or the end of the
  scope that holds it, RF-W009 warns). A failure its work came to crashes the program there with the failure's
  message. Call `job.cancel()` first to throw away what it comes to, a failure included.
- `cancel()` — asks the work to stop. Work nothing started yet never runs. Running work sees the request at
  its next wait (`waitfor`, `retrieve`, a channel) or through `cancellation_requested()`, and stops there
  (a thread of its own cannot be stopped from outside). Retrieving a cancelled Work waits for it to stop,
  then throws `CancelRequested` (a crashable, no `Error` in the name because cancelling is not a failure),
  recovered with `check`/`try`.
- `job.waitfor(5s).retrieve()` — retrieve with a deadline, throws `TaskTimeoutError` when it passes
  (`try job.waitfor(5s).retrieve()` gives `None` instead). A deadline needs a thread of its own: a Work
  nothing started yet is started with `start_apart()`, and one already running as a job of
  `start_now()`/`start_here()` is refused with `WorkStartError`.
- Starting a Work twice, or retrieving it twice, is a `WorkStartError` (a crashable carrying
  `reason: Text`).
- `is_done()`, `is_running()` — racy snapshots for polling.
- On a `List[Work[T]]`: `gather() -> List[T]` (wait for all, input order, throws the first failure it
  meets in list order), `race() -> T` (the first finisher, throws the winner's failure), `cancel_all()`
  (not failable, returns nothing: cancels every Work and waits until each has stopped, dropping what each came
  to, work nothing started yet never runs), `next_finished!() -> Work[T]` (waits for the first to finish, takes
  it out and returns it, loop it to handle results in finishing order), `gather_all() -> List[Check[T]]` (waits for
  all, each Work's value or failure in input order), `take_finished() -> List[Work[T]]` (takes the finished Works
  out in place and returns them, leaving the rest: rebuilding the list with `where` would drop work still
  running). `jobs.start_now()` / `jobs.start_here()` / `jobs.start_apart()` start every Work of the list together:
  each one is made ready first (its thread too) and then all are let go at once, so none runs before the others are
  ready; a Work of the list started already is a `WorkStartError`. `gather` and `race` only collect: a Work nothing
  started is started with `start_now()` the same way, all together.
- A Work is an entity kept in one place: `steal` hands it over, `var b = a` is RF-S413. When the place that
  keeps it lets go of it before a verb took its value (`drop job`, or the end of the scope or list that holds it), the work is cancelled
  and waited for. A started Work nothing retrieves, drops or hands on is warning RF-W009.
- There is no fire-and-forget verb: start with `start_now()` and collect later with `retrieve()` or
  `gather()`. A Work thrown away before anything starts it (`f.to[Work]()` as a statement, or
  `drop f.to[Work]()`) never runs its work, and is warning RF-W008.
- Free routines: `waitfor(d)` waits `d` (parks inside a job, sleeps elsewhere; `50ms`, `5s`, or
  `Duration.from_milliseconds(ms: n)`), `yield_now()` lets other jobs run, `cancellation_requested()`
  tells a long loop to stop early. Cancellation is cooperative: `waitfor` returns early once it is asked.

```razorforge
failable routine checked(n: S64) -> S64
    if n < 0
        throw VerificationFailedError()
    return n

routine relay(n: S64) -> S64
    return checked(n: n)

routine slow(n: S64) -> S64
    var i = 0
    until i >= 2000
        if cancellation_requested()
            return -1
        waitfor(1ms)
        i += 1
    return n

routine start()
    var bad = relay.to[Work](n: -1)
    var good = relay.to[Work](n: 3)
    bad.start_now()
    good.start_now()
    show(f"good: {good.retrieve()}")   # 3, the failure beside it changes nothing here
    when check bad.retrieve()
        is S64 as v => show(f"bad: {v}")
        is Crashables as e => show(f"bad failed: {e.crash_message()}")
    var running = slow.to[Work](n: 1)
    running.start_now()
    running.cancel()
    when check running.retrieve()
        is CancelRequested => show("cancelled")
        else => show("finished")
    return
```

**What `to[Work]` may hand to the work (RF-S632).** Each value `f.to[Work](...)` computes now must be safe to
hand to work that may run on another thread: a `steal`-moved bare entity, trivially copyable value data,
or a shareable manager handle (`Unbroken`, `Guarded`, `Witnessed`). A `Roamed`
handle is promoted. Anything else (a bare entity by copy, a record owning `Retained`/`Tracked`/
`Viewing`/`Modifying`) is rejected. The check follows everything the value reaches: an entity's member
variables, a collection's elements (`List[Retained[Cell]]`), a channel end's items (`Receiver[Retained[Cell]]`),
and the `given` names of a lambda (written in the call or bound to a variable first). A routine-value parameter a
routine hands to `to[Work]` (directly, or on to another routine that does) is followed to its callers: handing such a
routine a lambda that captured a `Retained`, `Tracked` or token is RF-S632 ("You hand 'spawn' a lambda that captured
`r` (a `Retained`), and 'spawn' hands its 'f' to a Work..."). A routine value read from a member variable, a
collection or a call's result and handed to a Work could be any lambda of its type kept there earlier, so every lambda
of that routine type that captured a `Retained`, `Tracked` or token is RF-S632 at the lambda ("This lambda captured
`r` ..., and a routine value of its type ... is handed to a Work at line N"). A value holding a
`Retained`, `Tracked` or token at any depth makes a Work of one thread: it starts with `start_here()`, and
`start_now()` / `start_apart()` (or a `retrieve()` that starts it) is RF-S632. An `Unbroken[T]` parameter is always by reference, so a routine taking
one works on the caller's cell, and sharing one counter across jobs just works:

```razorforge
routine bump(cell: Unbroken[S64], times: S64)
    each i in 0 til times
        drop cell.fetch_add(delta: 1)
    return

routine start()
    var counter = Unbroken[S64](initial: 0)
    var jobs = [bump.to[Work](cell: counter, times: 1000), bump.to[Work](cell: counter, times: 1000)]
    drop jobs.gather()
    show(f"{counter.load()}")          # 2000
    return
```

Async file I/O (parks a job while waiting, blocks elsewhere), in `IO/File`:
`FileManager.read_text(at: File) -> Text` and `FileManager.write_text(at: File, content: Text)`. Prefer these to
opening a `FileSource`/`FileSink` inside a job.

**Channels** (`System`, no import) — typed producer/consumer queues:
- `make_channel[T](capacity: U64) -> (Transmitter[T], Receiver[T])` — single consumer. `capacity` 0 =
  rendezvous (transmit waits for a taker); N = bounded buffer (transmit blocks when full = backpressure).
- `make_shared_channel[T](capacity: U64) -> (Transmitter[T], SharedReceiver[T])` — multiple consumers
  COMPETE for each item (fan-out, not broadcast).
- `transmitter.transmit(item: x)` hands `x` over; FAILABLE (`ChannelClosedError` if closed / no consumers).
  `transmitter.transmit_if_room(item: x) -> Bool` never waits: `true` when it went in, `false` when the channel was full
  (the item is destroyed), `ChannelClosedError` like `transmit`.
  `transmitter.duplicate()` clones a producer for fan-in; `transmitter.close()` / `is_closed()`.
- `Receiver`/`SharedReceiver` are `Emittable[T]` — drain with `each x in rx` (ends when closed + empty).
  `rx.close()` closes from the receiving side: buffered items are destroyed unreceived, a later `transmit` throws
  `ChannelClosedError`, and a transmitter waiting for room wakes with it (an early stop travels up a pipeline).

```razorforge
var (tx, rx) = make_channel[S64](capacity: 4)
tx.transmit(item: 10)
tx.transmit(item: 20)
tx.close()
each n in rx
    show(f"{n}")
```

**Not implemented yet** (do not generate these — they do not exist): async networking
(sockets/HTTP/WebSocket). It is on the roadmap, not in the language today.

## 14b. Process events (`CpuEventManager`)

`CpuEventManager` (System/Cpu, no import) holds one handler per kind of event the operating system sends when it
asks the process to stop. Its routines are called on the type:

```razorforge
import IO/Console

global stop_requests: Unbroken[U64] = Unbroken[U64](initial: 0)

routine on_stop()
    show("stopping")
    drop stop_requests.fetch_add(delta: 1)
    return

routine start()
    CpuEventManager.when_interrupted(on_stop)     # Ctrl+C, SIGINT
    until stop_requests.load() != 0
        waitfor(200ms)
    show("cleaned up")
    return
```

- `CpuEventManager.when_terminated(h)`: a request to end (SIGTERM, a console window closed, a logoff or a shutdown
  on Windows). When `h` returns, the process ends at once with the status `set_exit_code` stored last: the cleanup
  that must happen is the handler's (other threads' `destroy` teardown does not run).
- `CpuEventManager.when_interrupted(h)`: SIGINT, Ctrl+C or Ctrl+Break. When `h` returns, the program goes on.
- The handler is a **`Callable[(), None]`** (a named routine or a lambda, taking nothing). It runs on a thread of
  its own, so `show`, locks and allocation are fine in it. The state it changes is a `global` (`Unbroken` /
  `Guarded`): there is no context argument.
- **One slot per kind.** Setting a handler returns the one before as `Callable[(), None]?`, `none` when the
  system's action held the slot (the result may be dropped silently). Setting it again puts it back, and `none` gives
  the event back to the system. A library that adds its own handling calls the one it got back from its handler.
- An event of a kind whose handler is still running takes the system's action: a second Ctrl+C ends a program whose
  handler is stuck.
- A forced end (SIGKILL, the OOM killer, Windows ending the program after a timeout) can't be caught, and a fault
  ends the program as a crash.
- The `Signals` module is gone: `migrate --step cpu-events` drops `import Signals` and writes
  `CpuEventManager.when_interrupted(h)`, and lists the calls that passed a `context:`.

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
- **C strings.** `CStrView` / `CWStrView` are C's `char*` / `wchar_t*`, never freed by RazorForge (a string C
  returns, and the type of every C string parameter). `text.to[CStr]()` / `text.to[CWStr]()` copy a `Text` into
  new memory with a zero at the end (UTF-8, and UTF-16 on Windows or code points elsewhere for the wide one), torn
  down at the end of the scope. A `CStr` argument to a `CStrView` parameter is passed as `arg.view()` by the
  builder, and `steal arg` as `arg.leak()`, so C keeps memory that is never torn down (for C functions that keep
  the pointer, such as `putenv`). `Bytes(ptr: view)` / `view.to[Text]()` copy a C string back.
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
- **Control flow**: `if` `elseif` `else` `then` `when` `is` `isnot` `until` `loop` `with`
  `each` `break` `continue` `return` `throw` `todo` `becomes`
- **Iteration / range / hand-over**: `in` (loops only) `to` `til` `by` `steal`†
- **Module system**: `import` `module`
- **Other statements**: `using` `as` `define` `nothing` `given` `drop`
- **Access blocks**: `viewing` `modifying` (and `try viewing` / `try modifying`, whose names are Maybes)
- **Logical operators**: `and` `or` `not`
- **Literals**: `true` `false` `None` `none`
- **Concurrency**: `f.to[Work](args)`
- **Danger**†: `danger` (block) `dangerous` (modifier)
- **Buildtime reflection**†: `expand` (loop) — the ONLY reflection keyword.

The word operators `to` `til` `by` `is` `isnot` `in` `as` `and` `or` are keywords only where an
operator stands, right after something that ends an expression (`0 to 10`, `a and b`, `each x in xs`). Anywhere
else they are names: `var to = 3`, `routine step(by: S64, to: S64)`, `by + to`. Two places keep them keywords: a
`when` arm that starts with `is`/`isnot`, and `else as p`. `not` starts an expression, so it is always a keyword.
`inject` is a keyword only after `steal … as …`.

The reflection **sources** `openmemvarof` `allmemvarof` `branchof` `caseof` `protocolof` (the protocols a type
obeys, the ones they refine included, each once: `Comparable` brings `Equatable`, `nameof(p)` each name; there is
no `memvarof`) and the metadata **accessors** `nameof` `orderof` `placeof` `sizeof` `typeof`
`typeidof` `valueof` `visibilityof` are **buildtime builtin intrinsics, NOT reserved
keywords** — they tokenize as ordinary identifiers and are recognized in SA only when
`import BuilderExpansion` is in effect (bare `nameof(m)` or `$`-spliced `me.$nameof(m)`),
each reading a buildtime property off the active `expand` handle or a type. Without that
import, using `expand` or any source/accessor is a build error (RF-S952).

`$` (`${…}` buildtime splice) is a structural sigil, not a keyword. Failability is the `failable` keyword on the
declaration, and the name stays bare (RoutineInfo carries `IsFailable`). See §1b.

---

When generating RazorForge: start from the closest fixture in
`tests/Fixtures/Stdlib/`, keep arguments named, end every routine with
`return`, and prefer `when` over nested conditionals.
