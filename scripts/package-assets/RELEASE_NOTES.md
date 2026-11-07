# Revare v0.5.0

Revare 0.5.0 is the first release under the name Revare. It changes the language surface in many places: the file extension, the command, loops, failure handling, sharing and access, and much of the standard library. It also adds script mode, `global` state, a test runner, a formatter and linter, a language server, and a build daemon with a JIT mode for fast rebuilds.

This page lists the changes you are most likely to meet. The complete list, with every diagnostic code, is in `CHANGELOG.md`.

## Moving from 0.4

Run `revare migrate <folder>` on your project first. It re-indents 0.4 sources to steps of 4 spaces, renames `razorforge.toml` to `config.toml`, and applies every default rewrite. `revare migrate --step extensions <folder>` renames `.rf` files to `.rvr`. It skips files whose `config.toml` already says `revare-version = "0.5.0"` and marks the manifest when it is done.

Single rewrites run as `revare migrate --step <name> <folder>`. Run `--step suffix` last, because it needs analysis and reads a file only after its other old spellings are gone. Where no step exists, the build error tells you what to write instead. Then read the Breaking changes section of `CHANGELOG.md`, because some changes (operator levels, `**` against a leading `-`, ranges that count down) are listed by `migrate` for a hand check.

## Name, files and commands

- The language is Revare. Sources end in `.rvr`, diagnostic codes start with `RV-`, the command is `revare`, and the manifest key is `revare-version`.
- The project manifest is `config.toml`.
- Indentation is a multiple of 4 spaces (`RV-G053` otherwise).
- The commands are `check`, `build`, `run`, `codegen`, `test`, `lsp`, `format`, `docs`, `migrate`, `help`, `version` and `new`.
- Every runtime crash exits with code 82.

## Syntax

- Loops are `until c` and `loop`. `while` is gone. The loop over a range or a collection is `for x in xs`.
- A line with no work is `nothing`. `pass` is gone.
- `try f(x)` gives a `T?` and `check f(x)` gives a `Check[T]`. The generated `try_f` and `check_f` routines are gone.
- Containment is a call on the container: `xs.has(x)`, `xs.lacks(x)`, `d.has_key(k)`.
- A pattern binds with `as`: `is T as x`.
- A number takes its type from where it goes or from its declaration (`var x: U8 = 5`). Type suffixes such as `0_s64` are gone. Convert with `x.to[T]()`.
- A constructor is `routine T(...)`.
- A member routine of a generic type that you write outside the type must name its parameters on the receiver: `routine Box[T].get()`. `routine Box.get()` is a build error (`RV-S165`).
- Operators changed in precedence: `and` and `or` share one level, the bitwise operators share one, and `**` binds tighter than a leading `-`.

## Sharing and access

- An entity has one original. Holding it under a second name without an access token or a manager is `RV-S413`. Hand it over with `steal`.
- `Guarded` values are opened with `viewing g as v` to read and `modifying g as m` to write. Any type that has the `view_enter` / `modify_enter` pairs can be the target of such a block.
- `Assignable`, `Accessing` and `Controlling` are gone. `=` copies a record, and copying a handle makes one more handle to the same entity.
- The builder checks more cases where a container changes shape while a `for` loop or an element statement uses it (`RV-S625`, `RV-S639`, `RV-S640`).
- Concurrency goes through `Work[T]`: `f.to[Work](args)`, then `start_now()`, `start_here()` or `start_apart()`. `Atomic[T]` is `Unbroken[T]`, channel ends are `Transmitter[T]`.

## Numbers and text

- Binary floats are `B16` to `B128`. Float operations and math routines crash on an inf or NaN result, and the raw forms are `+! -! *! /! **!` and the `_unchecked` routines.
- `Int` replaces `Integer`. An `Int` is 16 bytes, so `List[Int]` takes 16 bytes per element (40 before).
- `Decimal` has one form per value, with 34 significant digits. Every rounding it does is half up. `D32`, `D64` and `D128` keep ties to even.
- `rsqrt`, `powi`, `midpoint` and the complex functions round once from the exact value. A few results differ from 0.4 in the last digits.
- `RoundingRule` selects the rule for `round` and `fixed`.

## Standard library

- The base module is `System`, and every file sees it and the modules under it without an import.
- Conversions are `x.to[T]()`, and collecting is `xs.to[List]()`. What walks a value is an emitter: `Emittable[T]` and `Emitter[T]`. An emittable collects into any collection with `to[...]`.
- `FileManager` does every file system task. `Environment` and `Entropy` replace the old directory, variable and random routines. `rng.next[T]()` replaces `next_u64()` and its relatives.
- New: `TextPattern` and `BytesPattern`, `Math3D` matrices of size 2, 3 and 4 with `inverse()` and `solve(b:)`, `RotatableList`, `Items` and `Slots`, `Clock` and `CpuClock`.

## Added to the language

- Script mode: the loose statements of the entry file are its `start()`.
- `global` for module-level state, initialized in order before `start()` runs.
- Loops that carry values (`until c with total: S64 = 0`), `becomes value` in a `when` arm, and several values in one arm.
- Records and entities have `==` and `!=` without `obeys Equatable` when every member variable has `==`.
- Backtick names, tuple swap, record patterns, inline variants `A | B`, `define` type aliases, `@infix` routines, and `@throws([...])`.

## Tools

- A resident build daemon and a JIT mode for the edit loop: `[target] mode = "debug-jit"`, `use-daemon` and `incremental`.
- `new`, `migrate`, `docs`, `format`, `test` and `lsp`, plus `check --json` and `check <folder>`.
- `@test` routines with `@case`, `@skip`, `@time_limit` and `@memory_limit`.
- A Rider plugin and a VS Code extension.
- A crash trace lists your program's routines. `[debug] trace-stdlib = true` adds the library's frames.

## Fixed

Among the fixes: a write to a value element (`m[i][j] = v`) changed a temporary copy, a variant's payload was not zero-filled in optimized builds, the cycle collector skipped a written `destroy`, a list built by a failing `check` or `try` leaked, and a user type named `T` broke generics. `CHANGELOG.md` lists all of them.

## Known issues

- Rebuilding after a one-file edit is slower than the 250 ms goal. On 2026-11-05 a 20-routine program rebuilt in about 600 ms with `debug-jit` and the daemon.
- The Windows package is a ReadyToRun build that bundles the .NET runtime. It is not NativeAOT. Linux and macOS packages are NativeAOT, and the macOS package has not been verified yet.
- `[target] backend = "lapila"` is experimental and does not translate every program. The Bool-returning forms of `try` and `check` give a build error there.
- `Stamped` and `PoolManager` change shape in 0.6: you will add an entity with `pool.add(e)`, and the generic `stamp()` goes away.
- A `T?` that holds an entity (`List[S64]?`) or a variant cannot be received by `try` or `check`. The builder reports a build error, because taking the entity out as a value would copy it and a changing call would then be silently wrong.
