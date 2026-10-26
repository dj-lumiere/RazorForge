# Changelog

All notable changes to RazorForge are documented here. This project follows
[Keep a Changelog](https://keepachangelog.com/) conventions. Suflae keeps its own changelog.

## [0.5.0] — unreleased

0.5 changes the language surface in many places. This section lists what differs from 0.4.0. Read **Breaking
changes** before you move a 0.4 project.

To move a project, run `razorforge migrate <folder>` first. It applies every default rewrite listed below,
skips files whose `config.toml` already says `razorforge-version = "0.5.0"`, and marks the manifest when it is done.
The named steps (`razorforge migrate --step <name> <folder>`) run one rewrite on any source. Where no step exists,
the build error says what to write instead.

`migrate` first indents a 0.4 file in steps of 4 spaces and renames `razorforge.toml` to `config.toml`, then
rewrites it. Run the named steps after it and `--step suffix` last: the suffix step needs analysis, which reads a
file only once its other old spellings are gone.

### Breaking changes

**Source layout and tools**
- Indentation is a multiple of 4 spaces. Any other indent is `RF-G053`. 0.4 sources used 2 spaces: default `migrate`
  doubles the leading spaces of each line of such a file first (`migrate --step indent` does only that, and leaves
  the lines inside a text literal as they are).
- The project manifest is `config.toml` (it was `razorforge.toml`). A `razorforge.toml` with no `config.toml` next
  to it is a build error, and default `migrate` renames the file. `RF_DEADLOCK_DETECT` is `deadlock-check` under
  `[debug]`: when the variable is still set, every command prints a warning that names the key.
- The commands are `check`, `build`, `run`, `test`, `fmt`, `lint`, `lsp`, `new`, `migrate` and `docs`. A bare file
  name is not a command: the error shows `razorforge run <file>`.
- Every runtime crash exits with code 82.
- x86-64 programs target x86-64-v3.

**Failure and recovery**
- A routine that can fail says so on its declaration: `failable routine parse(...)`. `routine parse!(...)` is
  `RF-G222` (`migrate --step failable`). A call never writes `!`: `f!(x)` and `job.retrieve!()` are `RF-G211`.
- Recovery is a keyword at the call: `try f(x)` gives a `Maybe[T]`, `check f(x)` gives a `Check[T]`. The generated
  `try_f` / `check_f` / `lookup_f` routines are gone. Default `migrate` writes `try f(x)` for `try_f(x)` (and
  `try r.f(x)` for `r.try_f(x)`) and `check f(x)` for `check_f(x)` and `lookup_f(x)`, and the build error on an old
  name names the keyword.
- Failures rise only through failable routines. A `throw` in a routine that is not `failable` crashes there with the
  crashable's message, so `pierce` is gone and is an ordinary name: `migrate --step pierce` writes `throw` for each
  `pierce` in a routine that is not failable and lists the ones in failable routines, where a `throw` goes to the
  caller instead.
- A routine that is not failable is a wall: a failure inside it crashes there, even under a `try` or `check` above
  its caller. Declare the routine `failable` to let its failures go up.
- `try` or `check` in front of an expression that reaches no failable call is `RF-S760`. Remove the keyword, or
  declare the routine `failable`.
- A lambda that can fail, or a failable routine named as a value, goes only to a parameter or member variable of type
  `failable Callable[...]`: handing one to a plain `Callable[...]` is `RF-S761`. The call that hands it over can fail
  through it (`try xs.select(transform: x => 10 // x).to[List]()`). The standard library routines that take a
  routine value (`select`, `where`, `any`, `all`, `sort_by`, `min_by`, ...) take it as `failable Callable[...]`.
- `Result[T]` is `Check[T]`. `Lookup[T]` is gone. The build errors say so.
- `absent` is gone: a routine with nothing to give throws, `throw AbsentValueError()` (default `migrate`).
- The `Crashable` protocol is the `Crashables` record. Every crashable writes its own `crash_message()` (`RF-S704`).
- `MemoryAllocationError` is `OutOfMemoryError` (default `migrate`, `migrate --step type-names`).
- `Integer` is `Int` (default `migrate`, `migrate --step type-names`). There is no alias: `Integer` is a build
  error that names `Int`. An `Int` is 16 bytes: a value in the `S64` range is held in the value itself, and a
  value outside it shares a read-only list of limbs between copies. A literal outside the `S64` range is laid down
  once by the builder and shared by each use. `List[Int]` takes 16 bytes per element (40 before).

**Syntax**
- A loop is `until c` (it stops when `c` becomes true) or `loop` (it ends only with `break`, `return` or `throw`).
  `while` is not a keyword: `while c` is `RF-G224` (`migrate --step until` turns integer comparisons around and
  lists the rest). A loop takes no `else` (`RF-G219`). A loop's `then` block runs after `break` too.
- A line with no work is `nothing`. `pass` is `RF-G225` (`migrate --step nothing`). A type declaration with an empty
  body is `RF-S211` (write `nothing` under the header).
- A number takes its type from where it goes or from its declaration (`var x: U8 = 5`). Type suffixes (`0_s64`,
  `1.5f32`, `10n`, `2.5dn`) are `RF-G010` (`migrate --step suffix`). `inf` and `nan` are literals, so `inf_b64` and
  the `B*_INFINITY` / `B*_NAN` presets are gone.
- Binary floats are `B16`, `B32`, `B64`, `B128` (the build error names the `B` type for an `F` name). Complex types
  are named by their total bits: 0.4's `C64` is `C128` and 0.4's `C128` is `C256`, and `C64` is now two `B32`
  parts. Default `migrate` renames them in a project it moves (a file with no `config.toml` above it gets them
  listed instead, because the old names still build). Quaternions are `Q128` and `Q256` (the build error on `Q32`
  / `Q64` names them).
- Containment is a call on the container: `xs.has(x)`, `xs.lacks(x)`, `d.has_key(k)`, `d.lacks_key(k)`,
  `perms.has_any(...)`. The `have` / `lack` operators are gone (`migrate --step have`), `contains` /
  `notcontains` are `has` / `lacks` (default `migrate`), `x in xs` as a test is `RF-G212`, and `notin` is gone.
- `x but y` is `x.without(y)`, `unless c` is `if not (c)` (default `migrate`). `disobeys` is gone: write
  `not (x obeys P)`.
- A pattern binds with `as`: `is T as x`, `else as p` (default `migrate`). `else as x` needs exactly one type left
  (`RF-S359`).
- `Character` is `Char` and `Routine[...]` is `Callable[...]` (default `migrate`).
- Operator levels: `and` and `or` share one level, and so do the bitwise operators, so mixing them needs
  parentheses (`RF-G216`). A range binds looser than arithmetic and tighter than comparisons. `**` binds tighter
  than a leading `-`: `-2 ** 2` is `-4`. Default `migrate` writes `(-x) ** n` for each `-x ** n`, which keeps what
  the line computed in 0.4, and lists each one.
- A range counts down only with a negative step: `10 to 1` is empty, write `10 to 1 by -1`. Default `migrate` adds
  the step where both ends are literals and lists the ranges whose start is not a literal.
- A chained comparison goes one way. `!=` or `<=>` after another comparison is a parse error.
- There is no `<<<`: `<<` fills with zeros.
- A constructor is `routine T(...)`. `routine T.create(...)` declares an ordinary member routine named `create`.
  Default `migrate` rewrites it to `routine T(...)` (and a call `T.create(...)` to `T(...)`), and a member routine
  `create` that gives back its own type is warning `RF-W104`.
- A `var` that hides a parameter or an outer local is `RF-S008`. An unknown annotation is `RF-G221`.
- A memberwise construction that sets `secret` or `posted` member variables works only inside their module
  (`RF-S403`, `RF-S404`).
- The right side of a compound assignment is checked like a binary expression (`Integer += S64` is `RF-S502`).
- `===` and `!==` take entities and handles. An access token is `RF-S440`, like a value.
- `destroy` is `destruct` (default `migrate`). The builder calls it: a program that calls `x.destruct()` gets
  `RF-S812`, which points to `drop x`. `dangerous routine T.destruct` is `RF-S807` (default `migrate` removes
  `dangerous`). A `failable` `destruct` is `RF-S815`.
- `discard` is `drop` (default `migrate`). A dropped result needs `drop` (`RF-W007`) unless its routine is
  `@discardable`.
- The flags value with no member on is `none` (`var marks: CellMarks = none`, also in presets and record presets).
  `all_off()` is gone, and its build error names `none`.
- Choice values (`OK: 200`) and flags bits (`MINE: 0`) are written on every member or on none, and with none the
  members count 0, 1, 2, ... in order. Some with and some without is `RF-S417`, a flags bit past 63 is `RF-S419`,
  and two members on one bit is `RF-S429`.
- A local handed over inside a `return` (`return Shelf(book: book)`) moves without `steal`. `steal` there is a
  warning, `RF-W259`, whose quick fix removes it (`migrate --step return-steal`).
- The same routine declared twice in one standard library or program file is a build error (`RF-S406`).

**Sharing and access**
- `Assignable` is gone. `=` copies a record, and copying a handle makes one more handle to the same entity. Default
  `migrate` removes `.share()` calls.
- `Accessing` and `Controlling`, with their `access()` and `control()` verbs, are gone. Use the handle directly, or
  `view()` / `modify()`. The build errors on the old names say so.
- A `Guarded` value is opened with an access block: `viewing g as v` to read and `modifying g as m` to write. The
  block calls the target's `view_enter()` / `modify_enter()` before it (the token they give is the `as` name) and
  `view_exit()` / `modify_exit()` on every way out, so any type with those pairs is a block target (`Guarded`,
  `FileStorage`, a type of your own). Using the block on a `Retained`, a `Roamed` or a weak handle is `RF-S651` (use
  it directly, or `hold()` first). `try viewing g as v` gives `v` as a `Maybe` of the token, which the body takes
  apart with `when`. `modifying a, b` enters in the written order and leaves in the reverse one. `consult()`,
  `amend()`, `Consulting`, `Amending`, `multiamend` and the `using` access blocks are gone: a `using` over an access
  is `RF-S612`, and `migrate --step access` rewrites the 0.4 forms.
- `LockPolicy` is `AccessPolicy`, `ReentrantLockError` is `ReenterDuringAccessError` (`migrate --step access`),
  and the `[debug]` switches are `access-order` and `deadlock-check`.
- `RetainController`, `GuardController` and `RoamController` are `RetainManager`, `GuardManager` and
  `RoamManager`. `FrozenController` is gone.
- A `Guarded[T, ReadOnly]` is read directly. Reaching a locking `Guarded` without an access block is `RF-S649`.
- Keeping an entity in a second place without a manager is `RF-S413`. Hand it over with `steal`, on every path or
  on none (`RF-S616`). A name `steal` emptied takes a new value with `=`, and the builder tears that value down at
  the end of the scope.
- Changing a container's shape while an `each` loop or an element statement uses it is a build error (`RF-S625`,
  `RF-S639`), and so is holding an element while another token changes a container of the same kind (`RF-S640`) or
  passing one container twice in a call (`RF-S623`). An `each` over a token (`var w = a.view()`, `viewing a as w`, a
  token a routine gives back) guards the containers it is on: `a.add_last(...)` inside `each x in w` is `RF-S625`.
- `Array[T, N]()` with a `T` that has no zero value is `RF-S641`.
- A lazy adapter is used in the statement that makes it. The elements of a loop over a read token are read-only.
- A routine that returns a token may return one from `me` or from any of its token parameters. The caller's token
  ends when any of the arguments it came from changes shape or ends (`RF-S643`).
- A Work that receives a token or a `Retained` starts with `start_here()` (`RF-S632`).
- A token is never a type argument: `List[Viewing[T]]`, `Maybe[Viewing[T]]` kept in a name, a tuple or a generic type
  holding one, written or inferred (`var xs = [a.view()]`), is `RF-S601`. What `try` gives on a routine that returns a
  token is unpacked with `when` where it is made. A lambda capturing a token is `RF-S606`, and `steal` on a token is
  `RF-S604`.
- More paths are build errors. A token in a variant written in place (`Viewing[Cell] | S64`), a global of a token
  type, and a generic routine whose type parameter a call makes a token (`same(x: l.view())`) are `RF-S601`. In a
  generic body, handing a value of a type parameter to a parameter of a type parameter (`xs.add_last(value: x)`)
  takes `steal` (`RF-S413`). `RF-S632` follows a Work's argument into an entity's member variables, a collection's
  elements, a channel end's items, a lambda's captures and a routine value handed on to a Work, and a global holding
  `Retained` or `Tracked` is `RF-S632`. `modifying G` on a global entity that is not a `Guarded` is `RF-S256`, `steal G`
  is `RF-S421`. An `each` over an adapter (`xs.where(...)`) guards `xs` against reshaping (`RF-S625`), and so does a
  lambda that reshapes the container its routine walks. A binding taken in a loop turn that reaches `continue` is
  `RF-S616`.
- A routine value taken from a `dangerous` routine is a dangerous callable. It goes only to a `Callable` parameter of
  a C routine or of a `dangerous` routine, or to a local (which is a dangerous callable from then on), and it is
  called only inside a `dangerous` routine or a `danger` block. Anywhere else it is `RF-S609`. A lambda body is
  outside the `danger` block it is written in, and an operator or f-text insertion whose routine is `dangerous` needs
  `danger` (`RF-S609`).
- A started Work nobody retrieves is cancelled where it is dropped, and a failure its work came to crashes the
  program there. `job.cancel()` throws the failure away.

**Types and the C boundary**
- `Address` and `CPtr` are `Addr`. Two addresses compare with `===`, and an address moves by a `ByteSize` with
  `+%` / `-%` (`migrate --step addr`).
- `CStr` and `CWStr` are C strings RazorForge made, cleaned up at scope exit. A pointer from C is a `CStrView` /
  `CWStrView`. `CStr` at the C boundary is `RF-S814`.
- `blank[T]()` is gone and `hollow[T]()` is `T.hollow()`, a `dangerous` routine every type has. The handle types and
  `Addr` have `is_hollow()`.
- `Byte.to_hex()` writes two digits. The widest signed integer is `S256`. `Vector[T, N]` arithmetic does not check
  for overflow.

**Standard library**
- A module is its folder. The base module is `System` (it was `Core`) and every file sees it and the modules under
  it (`System/Numerics`, `System/Text`, `System/Memory`, `System/Concurrency`, `System/Cpu`, ...) without an import.
  Importing a parent does not import its children (`migrate --step modules`).
- Math routines use the shared names: `ln`, `exp_m1`, `sin_pi`, `ln_gamma`, `mul_add`, `mod_trunc`,
  `rem`, `fract`, `compare`, `floordiv`, `shift_left`, `arith_shift_right`, `total_compare`, `is_sign_negative`
  (`migrate --step names`).
- Float operations and math routines crash on an inf or NaN result. The raw result is `+! -! *! /! **!` and the
  `_unchecked` routines. A narrowing float conversion that would turn a finite value infinite fails (raw:
  `to_unchecked`).
- A conversion is `x.to[T]()`: `S32(from: x)` and `x.S32()` are rewritten by default `migrate`, and a missing
  conversion is `RF-S458`. Collecting is a conversion too: `xs.List()` and `xs.Set()` are `xs.to[List]()` and
  `xs.to[Set]()`.
- What walks a value is an emitter: `Iterable[T]` is `Emittable[T]`, 0.4's `Emittable[T]` (the iterator) is
  `Emitter[T]`, and `iter()` is `emitter()`. The collection iterators are named `ListEmitter`, `DictEmitter`, ...,
  and the adapters `WhereEmittable`, `SelectEmittable`, .... The build error on an old name names the new one.
- `Indexable` is `ViewingIndexable` and `MutableIndexable` is `ModifyingIndexable` (the build error names the new
  name, and so does the one on `RetainController`, `GuardController`, `PQEntry`, `Moment.now`, `blank`, `hollow`,
  `gather` and `race`).
- `Signals` is `CpuEventManager` in `System/Cpu`, with one handler slot per event kind (`migrate --step
  cpu-events`, a call that passed a context is listed).
- The time is read with `Clock.now()`, `Clock.monotonic()` and `CpuClock.now()` (they replace `Moment.now` and
  `LocalMoment.now_utc`). A `Moment` is written with two digits for each part after the year:
  `2026-01-15 12:30:00` (0.4 wrote `2026-1-15 12:30:0`).
- A collection shows itself the way its literal looks. A `ByteSize` shows its byte count grouped by `_` with a `B`
  (`{4kib}` is `4_096B`): `kb_size()`, `mib_size()` and the others convert.
- `FileManager` does every file system task, and `IO/FileSystem` is gone (the import error names `FileManager`).
  Reading or writing a missing file fails, `make_file` fails on an existing file unless you pass `replacing: true`,
  and there is no `touch`. The working, temporary and home directories and the environment variables are
  `Environment`'s, random bytes are `Entropy`'s.
- `ask_line` and `ask_word` fail with `EndOfInputError` at the end of input.
- `Dict.items()` gives `(key, value)` tuples: `each (k, v) in d.items()` (default `migrate` lists the `entries()`
  loops). `d[k] = v` adds a missing key, and `d.add(key:, value:)` is rewritten to it.
- `PriorityQueue` holds `(priority, element)` tuples and has `unordered`, `ordered` and `drain`. `PQEntry` is
  gone.
- `Duration` `//` and `%` follow the integer rules, and overflow or division by zero crashes.
- `Decimal` is a decimal128 with 34 significant digits and one form per value: every result is normalized, so `1.0`
  and `1.00` are the same value, and a zero is never `-0` (`(-0.5).fixed(0)` is `0`). `+`, `-` and `*` are exact or
  crash with `NumericOverflowError`. Every rounding a `Decimal` does (`/`, `round()`, `fixed()`, text, literals,
  `pow`, `sqrt` and the other math routines) is half up: `2.5` goes to `3` and `-2.5` to `-2`. `D32`, `D64` and `D128`
  keep IEEE 754 ties to even.
- `Decimal`'s `rem` is the Euclidean remainder, like integer `%`: `0 <= r < |b|` (`-7` by `2` is `1`).
- `Matrix4R4C.inverse()` is failable: a singular matrix throws `SingularMatrixError` (0.4 gave the zero matrix).
  The inverse is Gaussian elimination with partial pivoting, so a result that isn't exact can differ from 0.4's in
  its last bits. `inverse_unchecked()` gives the raw result (infinities or NaN for a singular matrix).
- Concurrency: `suspended` and `threaded` are gone. Any routine runs concurrently through `f.to[Work](args)`
  (default `migrate` adds `.to[Work]` to the calls). A piece of handed-off work is a `Work[T]` (it was `Agent[T]`,
  `AgentStartError` is `WorkStartError`): `migrate --step work` rewrites the old names, and a remaining `Agent` is a
  build error that names `Work`. Start a Work with `start_now()`, `start_here()` or `start_apart()`. `execute()` is
  gone (default `migrate` writes `start_now()`). `gather()` and `race()` are routines on the Work list:
  `jobs.gather()`. When the last holder of a Work lets it go, the work is cancelled and waited for.
- Channels: `Sender[T]` is `Transmitter[T]`, `send` is `transmit` and `send_if_room` is `transmit_if_room`
  (`migrate --step verbs`).
- `Atomic[T]` is `Unbroken[T]` (a value whose one change is never broken partway), and its marker protocol
  `AtomicType` is `UnbrokenType`. The routines keep their names. Default `migrate` and `migrate --step type-names`
  rewrite the old names, and a remaining old name is a build error that names the new one.
- `FileManager`, `PartitionManager`, `StorageManager` and `CpuEventManager` are entities.

### Added

**Language**
- Script mode: the loose statements of the entry file are its `start()`. Loose statements in an imported file are
  `RF-S443`.
- `global`: module-level state, initialized in order before `start()` runs and torn down at its end. A global
  binding is read-only, so state that changes is an `Unbroken` or a `Guarded`. Globals of one name in two modules
  stay apart.
- `bundle`: a generic group whose kind is chosen per instance.
- Loops carry values: `until c with total: S64 = 0`, `continue with total: v`, and `then` sees them.
- A `when` that gives a value ends a block arm with `becomes value`. One arm can match several values
  (`== 4, 6, 9, 11 =>`). An arm can be `throw ...`.
- `==` compares values. Records and entities have `==` and `!=` without `obeys Equatable` when every member
  variable has `==` (a written `eq` replaces it), so two entities with equal member variables are `==` and not
  `===`. Records hash the same way, and an entity hashes only with `obeys Hashable`. A `T?` has `==` and a hash when
  `T` does. Entities whose member variables form a cycle compare and hash by shape. A missing `==` or hash is a
  build error that names the member variable that lacks it.
- Backtick names (`` `check` ``, `` `total count` ``), tuple swap `(a, b) = (b, a)`, record patterns, inline
  variants `A | B`, `define` type aliases, generic variants, `@infix` routines.
- Named and short arguments can mix: `area(width, height: 4)`.
- `todo` statement.
- Presets are computed at buildtime, including presets that call routines.
- Hex float literals (`0x1.8p3`) for binary floats.
- `@throws([...])` lists what a routine throws and the builder checks it (`RF-S712`, `RF-S713`).
- A crashable has no size limit. A `Check` bound to a name keeps its own error after later failures, and a failure
  on one fiber never overwrites another's.
- Kind constraints `needs RecordType T` and `needs EntityType T`.
- A file without a `module` header takes its module path from its place under `module_paths`.
- Collection literals take their element type from the expected type, like numbers do.
- `T.hollow()` for every type, inside `danger`.

**Standard library**
- `x.rsqrt()` is 1 / sqrt(x) rounded once (not `1 / x.sqrt()`, which rounds twice) on `B16`, `BF16`, `B32`, `B64`,
  `B128`, `D32`, `D64`, `D128` and `Decimal`. `x <= 0` crashes like the other checked float math (raw:
  `rsqrt_unchecked`), and `Decimal.rsqrt` of `x <= 0` is a `NumericDomainError`.
- `powi(n)` on `D32`, `D64`, `D128` and `Decimal` is rounded once, not once per multiplication (`1.1.powi(-7)` on a
  `D64` is `0.5131581182307068`, it was `...069`). An exact `D` power keeps the exponent `n` times `x`'s (`2.0.powi(2)`
  is `4.00`, it was `4.000`) and `x.powi(0)` is `1` (it was `1.0`). `midpoint` is `(a + b) / 2` rounded once, so two
  finite values have a finite midpoint (`D64_MAX.midpoint(D64_MAX)` is `D64_MAX`, it was `inf`) and the sum is not
  rounded first (on a `D64`, `9999999999999999.midpoint(9999999999999998)` is `9999999999999998`, it was `1.00000000000000E+16`).
- `D32`/`D64`/`D128` `is_subnormal()` and `fpclassify()` count every finite nonzero value below the smallest normal
  one (`5e-390` on a `D64`), not only those at the smallest exponent.
- `Decimal.to[B32]()` and `to[B64]()` crash with `NumericOverflowError` past the float's range, as documented
  (they gave an infinity).
- `C64`, `C128` and `C256`: `*`, `/`, `reciprocal()`, `norm()` and `abs()` round each part once from the exact value.
  On a `C128`, `(0.1+0.1i) * (0.1+0.3i)` is `-0.02+0.04i` (it was `-0.019999999999999997+0.04i`), `(0.1+0.1i) /
  (0.1+0.3i)` is `0.4-0.2i` (it was `0.39999999999999997-0.2i`) and the modulus of `0.1+0.1i` is `0.1414213562373095`
  (it was `0.14142135623730953`). A squared modulus past the range no longer crashes when the result fits:
  `(1+1i) / (1e-200+0i)` is `1e+200+1e+200i` and the modulus of `1e200+1e200i` is `1.414213562373095e+200` (both
  crashed).
- The complex square root, logarithms and inverse functions give their principal values, and a `-0` part picks the
  side of a branch cut: the square root of `-4 - 0i` is `0-2i` (it was `0+2i`), `acosh` of `-2+0i` is
  `1.3169578969248166+3.141592653589793i` (the real part was negative), `atan` of `2i` is
  `1.5707963267948966+0.5493061443340549i` (the real part was negative), and `asin`/`atanh` of `2+0i` take the side
  above the cut. `tan`, `tanh` and the inverse functions no longer lose digits to cancellation, and `sin`, `cos`,
  `sinh`, `cosh` and `exp` are made from the correctly rounded real functions, so every elementary function is within
  a few units in the last place. The other complex results that use them (`ln`, `log2`, `log10`, `pow`) can change in
  the last digit.
- `exp_unchecked`, `sinh_unchecked` and `cosh_unchecked` of a complex number give an infinity past the range (they
  crashed): `exp_unchecked` of `100+0i` on a `C64` is `inf+0i`.
- Reading a complex number from text that is not one fails with `InvalidValueError` naming the whole text and the
  complex type (it named the part and its float type), and a NaN part reads as the positive quiet NaN.
- A complex number's `hash` is the same for `+0` and `-0` parts, as `==` treats them.
- `C256.to[C64]()` rounds each part once (it went through `B64`, rounding twice).
- `AccessManager`: `viewing` / `modifying` blocks, deadlock detection (`[debug] deadlock-check`, on in debug
  builds) and `access-order`.
- Emitters: a program may call `emitter()` and keep the emitter in a local, where it is a token on the container
  (`RF-S643` after the container changes shape, `RF-S644` in a field or a return). `(steal xs).emitter()` takes the
  elements and can go anywhere, and `r.emitter()` on a `Retained` holds one more reference to the entity. An emitter
  is an emittable, so terminals and adapters work on it.
- An emittable collects into any collection with `to[...]`: `to[List]`, `to[Set]`, `to[Dict]` (of `(key, value)`
  pairs), `to[SortedList]`, `to[SortedSet]`, `to[SortedDict]`, `to[RotatableList]`, `to[BitList]`,
  `to[PriorityQueue]` (of `(priority, element)` pairs), and `to[Array[T, N]]` (`ElementCountMismatchError` when the
  count differs).
- `Math3D` has `Matrix2R2C` and `Matrix3R3C` beside `Matrix4R4C`, with the same routines, `vec2` and `vec3`, a 2D
  `rotation`, `rotation_x`, `rotation_y`, `rotation_z` and `from_quaternion` on `Matrix3R3C`, and `scaling`. All three
  have `inverse()`, `inverse_unchecked()` and `solve(b:)` (A x = b), computed by Gaussian elimination with partial
  pivoting. `inverse()` and `solve(b:)` throw `SingularMatrixError` when a pivot is exactly 0, and an infinity or NaN
  during the computation crashes like the other checked float operations.
- `RotatableList[T]`: a list that adds and removes at both ends and rotates.
- `jobs.start_now()`, `jobs.start_here()` and `jobs.start_apart()` on a list of Works start every Work in it and let
  them all go together, so none runs before the others are ready. `gather` and `race` start the ones not started yet
  the same way.
- `RoundingRule` (`HALF_UP`, `HALF_EVEN`, `HALF_AWAY`, `FLOOR`, `CEIL`, `TRUNC`) for the routines that round on
  purpose: `x.round(places:, rule:)`, `x.fixed(places:, rule:)`, and on `Decimal` `divide(by:, places:, rule:)`,
  `add`, `subtract` and `multiply` with `places:` and `rule:` (computed exactly, rounded once). Leaving out `rule` gives
  the type's own rule.
- `Decimal`, `D32`, `D64` and `D128` are `Hashable`: equal values hash the same whatever their digits or cohort.
- `TextPattern` and `BytesPattern`: patterns built from named pieces, matched in time linear in the input, with
  `from_regex`, `matches`, `find`, `find_all`, `replace` and `split`.
- `FileManager` with `FileStorage[T]` (a mapped file), `rename`, `move`, `go_to`, and I/O errors by reason.
  `Environment` (arguments, environment variables, directories) and `Entropy`.
- `Clock`, `CpuClock`, `CpuEventManager`, `ThreadLocalManager`, `Watched` handles.
- `@test` routines and the `test` command, with `@case`, `@skip`, `@time_limit` and `@memory_limit`.
- `ask[T]()`, `set_exit_code`, `to_wrap[T]()`, `to_clamp[T]()`, `to_text(radix:)`, `hex()`, `bin()`, `oct()`,
  `fixed(places)`.
- `T(from: SerialValue)` construction and collections read from serial values.
- Work lists: `cancel()`, `next_finished`, `gather_all`, `take_finished`, `cancel_all`. A failure stays inside its
  Work until it is retrieved.
- `List.take_each`, `List.swap`, `RotatableList.add_range`, `lacks` beside every `has`, `KeyContainer`.
- `eq` and `cmp` between different types. The builder writes `ne` for each `eq` and `lacks` for each `has`.
- B16, B32, B64 and B128 math routines are correctly rounded on every platform, and printed floats are the
  shortest decimal that reads back to the same bits. `BF16` does arithmetic.
- `Char` case mapping follows Unicode 17.0.
- A shift amount or an index can be any integer type. A negative one crashes (`NegativeIndexError`).
- C interop: routine values for C callbacks, `@layout(C)` / `@layout(packed)` / `@layout(align)`, `@link` with
  automatic DLL placement, and `@blocking` for `C::` routines.

**Tools**
- A resident build daemon and a JIT mode for the edit loop: `[target] mode = "debug-jit"`, `use-daemon` and
  `incremental`.
- `new`, `migrate`, `docs`, `fmt`, `lint`, `test`, `lsp` and `check --json`.
- The language server: diagnostics, formatting, hover, go to definition, rename and quick fixes.
- Rider plugin and VS Code extension.
- `[target] backend = "tessera"` (opt-in).
- Windows trace builds write CodeView and PDB files.
- A crash trace holds the program's routines only: a standard library routine keeps no frame, and a crash
  inside one reports the line where the program called it. `[debug] trace-stdlib = true` keeps the library's
  frames too (for work on the library).
- Diagnostics are written in the second person and point to the new spelling of a retired word.
- `migrate --step indent` (indent in steps of 4 spaces). Default `migrate` and the `have` and `until` steps read a
  file whose numbers still have type suffixes, and leave the suffixes for `--step suffix`.

### Fixed

- A write to a value element (`m[i][j] = v`, `pts[k].x = v`) changed a temporary copy.
- A variant's payload was not zero-filled, so optimized builds could lose the arm's value.
- The cycle collector skipped the effects of a written `destroy`.
- B64 to B16 narrowing gave wrong values on Windows.
- `Text(from: CStr)` turns invalid UTF-8 into U+FFFD. Integer parsing reports `IntegerOverflowError` before it
  overflows.
- A channel that waits on itself, or waits with no Work left to send, is reported as a deadlock.
- A plain value passed to a `T?` parameter is wrapped as present.
- Decimal arithmetic matches IEEE 754 bit for bit, and `fmod` / `remainder` on D32, D64 and D128 are exact.
- A stack overflow crashes with exit code 82 and reports the call depth and stack size.
- A crash inside the standard library points to the caller's line.
- A user type named like a generic parameter (`record T`) no longer breaks that generic.
- A byte literal with a hex escape (`b'\x07'`) read as the backslash.
- A syntax error inside a value `when` no longer reports the rest of the routine as top-level statements.
- `S128_MIN.to[B128]()` and `S256_MIN.to[B128]()` crashed. `B128.rem` crashed on operands near the top of the
  range.
- Soundness holes are closed: a global that reads itself in its initializer, a preset defined from itself and two
  globals whose `destruct`s read each other are `RF-S436`. A name bound by a pattern or an `each` loop ends with the
  place it came from. `ByteSize` multiplication checks for overflow. An entity a routine receives through a bare
  generic parameter and does not keep is torn down by that routine, on failure paths too, so it no longer leaks.
- A range that reaches the ends of its type runs to the end and stops there.

### Removed
- `Assignable`, `FrozenController`, `Accessing`, `Controlling`, `Consulting`, `Amending`, `multiamend`,
  `LockPolicy`, `ReentrantLockError`.
- `Address` and `CPtr` (now `Addr`).
- `using` access blocks, `using ... fallback`.
- `Result[T]`, `Lookup[T]`, `Crashable`, `absent`, `discard`, `pierce`, and the `try_` / `check_` / `lookup_`
  routines.
- `unless`, `but`, `disobeys`, `notin`, `have`, `lack`, `while`, `pass`, `suspended`, `threaded`.
- `Agent.execute()`.
- `Iterable` (now `Emittable`), `iter()` (now `emitter()`), and the `List()` / `Set()` terminals (now `to[...]`).
- The `Signals` module and `IO/FileSystem`.
- Arbitrary-precision `Real` and `Complex`, the `?.` operator.
- `F256`, `F512`, `U192`, `U384` and the other in-between widths.
- Number type suffixes, `inf_bNN`, the `B*_INFINITY` / `B*_NAN` presets.
- `blank[T]()`.
- Flags' `all_off()` (write `none`).

## [0.4.0] — 2026-08-28 · RazorForge 0.4.0 · Suflae 0.1.0

_Covers everything since `v0.3.0` (234 commits). RazorForge 0.4 is largely an API-surface cleanup
release — read the **Breaking Changes** section before upgrading._

### ⭐ Suflae 0.1.0 — the first Suflae release

Suflae (`.sf`) is now a working language sharing RazorForge's grammar and stdlib, with its own
ownership runtime and an approachable surface. It ships alongside RazorForge 0.4 via the `suflae` /
`sf` CLI.

- **Realm-scoped Core.** RazorForge and Suflae each have their own `module Core`, distinguished by
  **realm**: a `.sf` file gets the Suflae-realm Core analyzed in SF mode, a `.rf` file the
  RazorForge-realm Core in RF mode; both coexist in one compilation. Cross-realm references bridge via
  `RF::` / `SF::` (e.g. `RF::Core.List`).
- **Entities are `Roamed[T]`.** An SF `entity` is a biased-refcounted, cycle-collected handle
  (auto-promote-on-escape + task-keyed lock). Recursive lowering handles nested entities, entity
  fields, params/returns, and nullability (`E?` = a nullable `Roamed[E]`, no double-`Maybe`).
- **SF-realm Core stdlib** under `Standard/Suflae/`: entity collection wrappers (List, Dict, Set,
  Deque, PriorityQueue, Sorted{List,Dict,Set}, SplitList, BitList) delegating to `RF::Core` via an
  auto-forwarding pass; value types (Text/Bytes/Integer/Real/…) are shared, not wrapped.
- **Approachable surface:** `Integer`/`Decimal` are the default number types; fixed-width types
  (`S32`/`U64`/`F128`/…) and their literal suffixes are import-gated behind `import Numerics`
  (**RF-S636**); `@dangerous`/unsafe free-routine calls are rejected (**RF-S800**).
- **Script mode.** Top-level statements and `var` decls in a `.sf` file fold into an implicit
  `start()`; running a `.sf` directly builds and executes the file, `python hello.py`-style.
- **Concurrency:** SF `suspended` routines are enabled (lazy Agent model, below).
- **Testing:** a `.sf` fixture harness with an RF↔SF equivalence lock (shared fixtures must produce
  byte-identical output) runs in the main test suite.

### ⚠️ Breaking Changes (RazorForge)

**Renamed types, protocols & terminology**
- RC wrappers: `Shared` → **`Guarded`**, `Watched` → **`Witnessed`**; controllers `ShareController` →
  **`GuardController`**, `ImmutableSharedController` → **`FrozenController`** (Retained/Tracked/Roamed
  unchanged).
- Access tokens: `Inspecting` → **`Consulting`**, `Claiming` → **`Amending`**, `Referring` →
  **`Accessing`** (and the read-only coercion `refer()` → **`access()`**, symmetric with
  `Controlling`/`control()`).
- Capability protocol `Storable` → **`Assignable`**; reflection vocabulary `Field` →
  **`MemberVariable`**, `Method` → **`MemberRoutine`**.
- Annotation `@migratable` → **`@reshaping`**.
- Buildtime reflection: keyword `armof` → **`branchof`**; `memvarof` split into **`openmemvarof`** /
  **`allmemvarof`**; `expand` is now the only reflection keyword (sources/accessors are
  `BuilderExpansion`-gated intrinsics).

**Reference-counting surface**
- **Construction verbs abolished.** `T.roam()`, `T.retain()`, and `T.share[P]()` (entity→wrapper
  "constructors" masquerading as verbs) are removed — construct a wrapper with
  `Wrapper(from: steal n)` (e.g. `Roamed(from: n)`, `Retained(from: steal x)`).
- Copy/lifecycle verbs renamed to a canonical vocabulary: RC copy verb `store` → **`share`**;
  conversion verbs `track`/`watch` → **`observe`**, `recover` → **`hold`**; controller counts
  `increase/decrease_strong_count` → **`hold`/`unhold`**, weak → **`observe`/`unobserve`**.
- The public `release()` verb is removed from all RC wrappers — `destroy()` **is** the decrement
  (canonical surface: `share` / `observe` / `hold` / `access` / `control` / `destroy`).

**Removed features**
- `Representable` / `Diagnosable` / `Serializable` protocols removed — `represent` / `diagnose` /
  `serialize` are now **universal built-ins**, not opt-in capabilities (declare with `override`).
- `FastDict` / `FastSet` removed from the collections module.
- The `Blank` unit type is replaced by **`None`**.
- The `$` wired-sigil surface is removed (wired-ness is inferred from protocol conformance); the dead
  `?T` prefix concept is gone.

**Changed semantics**
- **`Copyable` is decoupled from `Storable`/`Assignable`** into orthogonal capability axes; `Copyable`
  auto-derives via a `needs P everywhere` gate; RC wrappers no longer imply it (a duplicate is an
  explicit `.share`).
- Failability: the `!` marker is no longer part of a routine's name — failability is an attribute
  (`IsFailable`), and `foo` / `foo!` are the same routine.
- Three-rules parameter model: record params borrow, `retain` moves to the destination
  (constructor / store-primitive), the caller tears down rvalue args.

### Added (RazorForge)

**Language surface**
- `text[a til b]` range/slice indexing via `getitem(range: Range[U64])`; `Range[T]` is a record with a
  `RangeEmittable[T]` iterator, and range/slice indexing works across collections.
- Single-hole `_` lambda sugar; named-argument punning (field-init shorthand).
- SoA collections `SplitList` / `SplitArray`; `is <Name>Type` builder-group constraints.
- Zero-initialization routines `blank()` / `hollow()`.
- Carrier lowering: `Try`/`Check`/`Lookup` failable variants lower to `Maybe`/`Result`/`Lookup`
  values (the internal `#carrier` representation is eliminated program-wide; payload is a single
  `CPtr` slot).
- **Reference-identity operators `===` / `!==`** — "same object?", distinct from value equality `==`.
  A primitive pointer compare (not overloadable, never lowered to `.eq()`), valid on reference-carrying
  operands only — an `entity` or a forwarding wrapper (`Viewing`/`Modifying`/`Consulting`/`Amending`/
  `Retained`/`Guarded`/`Tracked`/`Witnessed`/`Roamed`); a value type is **RF-S440** (use `==`).
  `Hijacked` is excluded (its `==` is already identity). Works in both realms.
- **`###` doc-comments** with reStructuredText-style field lists — `:param name:`, `:typeparam Name:`,
  `:returns:`, `:throws:`, `:absent:`, `:note:`, `:see:` — captured onto declarations and surfaced by the
  language server (see **Tooling**).

**Standard library**
- **`Signals` module** (`when_interrupted` / `when_terminated`): register handlers for OS interrupt /
  terminate signals, dispatched off a dedicated thread with a suppress-default option;
  `Guarded`/`Roamed` context overloads. (Empty-tuple type `()` now spells `Routine[(), None]`; generic
  overloads resolve by argument type.)

**Numerics**
- Quaternions `Q32` / `Q64`; vector types `Vector2D` / `Vector3D` / `Vector4D`.
- Native SIMD `Vector[T, N]` with elementwise arithmetic and reductions.

**Concurrency**
- **Lazy Agent model.** A `suspended` call builds an inert coroutine that a verb starts: `retrieve`
  awaits one, `gather`/`race` launch a set, `execute` detaches for fire-and-forget. **RF-W008** warns
  on a never-launched agent.

**Buildtime & reflection**
- `expand` / `${…}` reflection: `openmemvarof` / `allmemvarof`, `branchof` / `caseof`, and `*of`
  metadata intrinsics (`nameof`/`orderof`/`typeof`/`typeidof`/`valueof`/`placeof`/`sizeof`/
  `visibilityof`) with repr-C offset/size folding.
- `serialize` universalized; derive classification is opt-in and constraint-driven
  (`needs P everywhere` / `obeys P`). Buildtime-value const-generics (`${…}`).

**Build & FFI**
- File-granularity conditional compilation via the `@target` annotation (parsed before `module`, read
  pre-parse, editor-highlightable); `@target` supports multi-value keys (`os: "linux", "macos"`) and
  gates stdlib file loading.
- Link external C libraries via `[target] c_libraries` / `library_paths` (clang `-l`/`-L`) and the
  `@link("lib")` source annotation on `C::` externs.
- Platform-width C FFI types: `CLong` / `CULong` / `CWChar` (per-target) and `CWStr` (`wchar_t*`).
- Float-struct ABI (Phase 3): per-eightbyte SSE/INTEGER (SysV x86-64), HFA (AAPCS64), GP-reg-by-size
  (Win-x64).
- `RoutineRealm {RF, SF, C, LLVM}`; foreign calls migrated from `external("C"|"llvm")` to
  realm-qualified `C::` / `LLVM::` routines (strict realm enforced at call sites — **RF-S460**).
- Versions sourced from the csproj `AssemblyMetadata` via `BuildInfo`; `builder_version()` is
  language-specific.

**Tooling — editor / language server**
- **Built-in language server** (`RazorForge --lsp`, stdio JSON-RPC): live diagnostics, hover (types,
  routine signatures, and `###` doc-comments including `:param:`/`:returns:`/… fields — for stdlib and
  external-module symbols too), go-to-definition (routines cross-file; variables/parameters
  scope-precise), binding-precise references and rename, signature help, completion (type-filtered
  `receiver.` members hiding `secret` fields, realm-scoped `C::`/`LLVM::`, resolve), document & workspace
  symbols, inlay hints (inferred `var` types), code actions, and semantic tokens — including a **dead-use
  grey-out** that marks a value read after its ownership was moved out by `steal`. Analysis reuses a
  cached stdlib snapshot for microsecond per-keystroke re-analysis.
- **Editor integrations:** a VS Code extension and a Rider (JetBrains LSP API) plugin under
  `rider-plugin/`; TextMate grammars gain annotation highlighting.

- **Object-level liveness on `RoamController`:** weak observation + tombstone (a `Watching` handle,
  `is_alive()` / `is_destroyed()`), cycle-collect and multithread safe.
- Opt-in escaped-lock **deadlock detector** (`RF_DEADLOCK_DETECT`): a contended `Roamed` lock cycle
  aborts loudly with the offending task ids instead of hanging.
- `RangeState`/cycle-collector renamed to meaning-based `RoamState {LIVE/SCANNING/GARBAGE/SUSPECTED}`;
  internal `cc_` prefix → `cyclic_`.
- New diagnostics: **RF-S622** (aggregate-steal holes `steal l[i]` / `steal o.field`), **RF-S262**
  (ambiguous module-scoped type reference); keeping a store-less value read from a container/aggregate
  is rejected.
- `RuntimeContract` is the single source of truth for `represent`/`diagnose`/`serialize` and
  wrapper-name literals. `dump-ast` reworked to capture the exact pre-LLVM-codegen AST.
- Name canonicalization: routine names are structurally bare (owner / member / receiver / generic
  args are separate structured fields, not parsed from the name string).

### Fixed

- **Cycle-collector UAF** under concurrency (rwlock stop-the-world) + stack-overflow
  (recursion → explicit worklist); roam-trace now descends into open-addressing (Dict/Set) and
  bare-entity container fields so elements are collectable.
- **Agent double-free** — task lifetime inverted to a worker↔`retrieve` self-reap rendezvous.
- Several **over-prune / heap-corruption crashes** from user types whose names collided with a generic
  parameter (`record T`/`U`/`N`): generic-parameter identity is now keyed by positional slot, not name
  (channel-buffer under-allocation, `pick!` param mismatch, derive-template shadowing, cross-module
  signature-cache contamination — all fixed with regression fixtures).
- **Teardown correctness:** emit destroys *before* a terminator (spilling return/throw values to a
  temp) so temporaries are freed (was a leak); skip unreachable fall-through destroys.
- **Collections RC:** `Array` store/copy cascades element-wise (fixes `Array[Text]` double-free);
  keeping a value read from a container copies it (fixes heap-value `getitem` double-free);
  `Array[Entity]` no longer wrongly assignable.
- **Record layout** uses natural member alignment (not size) so `SizeBytes`/`placeof`/ABI match the
  emitted LLVM (C) layout; codegen resolves Text/Bytes fields by name, not hardcoded index.
- `crash_message` returns `Text` via the sret ABI (a by-value return garbled the crash report).
- SF entity field **reassignment** no longer crashes; SF `Set`/`Dict` literals into an entity slot
  stay bare (roamed at binding); SF-wrapper self-recursion killed via module/realm-distinct resolution.
- Wrong-arity explicit type args on a constructor give a clean **RF-S102** (was a crash).

### Internal / Removed

- `RcRetainLoweringPass` deleted — redundant retain bumps caused double-frees; scope-exit teardown and
  the type's own copy DERIVE handle RC lifecycle.
- Codegen was moved off stdlib-encroachment: monomorphization completes before codegen (no codegen
  type-substitution), and Roamed lock/promote/projection + roam-hook refs moved to dedicated lowering
  passes. The 10 pipeline-stage source folders and builder phases are renumbered to execution order.

### Performance

- **Warm-compile / daemon groundwork** (milestone-1): warm-state capture/restore proving the
  stdlib-reuse ceiling (SA 5.2s → 31ms), a variant-reuse gate (warm analyze 5.3s → 1.17s), and a
  codegen-correct warm path (emits the identical routine set as a cold build). The remaining dominant
  cost (variant regeneration/reanalysis over the stdlib each compile) is profiled and documented.

---

_Older releases (`v0.0.1-alpha` … `v0.3.0`) predate this changelog._
