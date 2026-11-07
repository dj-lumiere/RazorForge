# Mielia v0.5.0

Mielia 0.5.0 is the first release under the name Mielia. Its version number now follows the builder's, so it comes after 0.1.0. The release changes the language surface in many places: the file extension, the command, loops, failure handling, numbers and the standard library. It adds `ObjectHacker` for working on live objects, `Int / Int` giving a `Decimal`, a test runner, a formatter and linter, a language server, and a build daemon with a JIT mode for fast rebuilds.

This page lists the changes you are most likely to meet. The complete list, with every diagnostic code, is in `CHANGELOG.md`.

## Moving from 0.1

Run `mielia migrate <folder>` on your project first. It re-indents 0.1 sources to steps of 4 spaces, renames the old manifest file to `config.toml`, and applies every default rewrite. It skips files whose `config.toml` already says `mielia-version = "0.5.0"` and marks the manifest when it is done. `mielia migrate --step extensions <folder>` renames `.sf` files to `.mie`.

Single rewrites run as `mielia migrate --step <name> <folder>`. Run `--step suffix` last, because it needs analysis and reads a file only after its other old spellings are gone. Where no step exists, the build error tells you what to write instead. Then read the Breaking changes section of `CHANGELOG.md`. Some changes are only listed by `migrate` for a hand check, such as `make_channel` calls and code that calls C.

## Name, files and commands

- The language is Mielia. Sources end in `.mie`, diagnostic codes start with `ME-`, the command is `mielia`, and the manifest key is `mielia-version`.
- The project manifest is `config.toml`.
- Indentation is a multiple of 4 spaces (`ME-G053` otherwise).
- The commands are `check`, `build`, `run`, `test`, `fmt`, `lint`, `lsp`, `new`, `migrate` and `docs`.
- Every runtime crash exits with code 82.

## Syntax

- Loops are `until c` and `loop`. `while` is gone. The loop over a range or a collection is `for x in xs`.
- A line with no work is `nothing`. `pass` is gone. A routine that returns nothing ends without `return`.
- `try f(x)` gives a `T?` and `check f(x)` gives a `Check[T]`. The `try_f` and `check_f` routines are gone.
- Containment is a call on the collection: `xs.has(x)`, `xs.lacks(x)`, `d.has_key(k)`.
- A pattern binds with `as`: `is T as x`.
- A number takes its type from where it goes or from its declaration (`var x: U8 = 5`). Type suffixes are gone. Convert with `x.to[T]()`.
- `S64 / S64` is `ME-S065`. Write `a // b`, or convert both sides to `Decimal`. `Int / Int` is a `Decimal` (`7 / 2` is `3.5`).
- A constructor is `routine T(...)`.
- A member routine of a generic type that you write outside the type must name its parameters on the receiver: `routine Box[T].get()`. `routine Box.get()` is a build error (`ME-S165`).
- `==` compares values and `===` tells whether two names refer to the same entity. Two entities with equal member variables are `==` and not `===`.
- Operators changed in precedence: `and` and `or` share one level, the bitwise operators share one, and `**` binds tighter than a leading `-`.

## Entities and concurrency

- A program does not write `destruct`. An entity ends when the last name that refers to it goes away, or when the cycle collector finds it unreachable.
- Concurrency goes through `Work[T]`: `f.to[Work](args)`, then `start_now()`. Calls on a list of Works include `gather()`, `race()` and `jobs.start_now()`. When the last name that refers to a Work goes away, the work is cancelled and waited for.
- A number several jobs change is a `global` or an entity's member variable, and the builder makes each change safe. There is no `Atomic`.
- Channels are `Mailbox[T]`.
- Changing a collection's shape during a `for` over it crashes with `ReshapingWhileInUseError`, also when the change comes from a lambda that captured the collection.

## Numbers and text

- Binary floats are `B16` to `B128`. Float operations and math routines crash on an inf or NaN result, and the raw forms are `+! -! *! /! **!` and the `_unchecked` routines.
- `Int` replaces `Integer`. An `Int` is 16 bytes, so `List[Int]` takes 16 bytes per element (40 before).
- `Decimal` has one form per value, with 34 significant digits. Every rounding it does is half up. The arbitrary-precision `Real` is gone.
- `rsqrt`, `powi`, `midpoint` and the complex functions round once from the exact value. A few results differ from 0.1 in the last digits.
- `RoundingRule` selects the rule for `round` and `fixed`.

## Standard library

- The base module is `System`, and every file sees it and the modules under it without an import. Fixed-width numbers need no import.
- Conversions are `x.to[T]()`, and collecting is `xs.to[List]()`. What walks a value is an emitter: `Emittable[T]` and `Emitter[T]`. An emittable collects into any collection with `to[...]`.
- `FileManager` does every file system task. `Environment` and `Entropy` replace the old directory, variable and random routines. `rng.next[T]()` replaces `next_u64()` and its relatives.
- New: `TextPattern` and `BytesPattern`, `Math3D` matrices of size 2, 3 and 4 with `inverse()` and `solve(b:)`, `RotatableList`, `Clock` and `CpuClock`.

## ObjectHacker

`import ObjectHacker` works on live objects while the program runs.

- `Unknown` keeps a value of any type. `type_name(of:)` and `type_info(of:)` give a type's name, module, kind, member variables, protocols and routines.
- `peek`, `poke` and `watch` read, change and observe an entity. `when_changed` calls a handler after each change of an object's member variables.
- `call(target:, name:, args:)` calls a member routine by name, and `create(type:, args:)` makes a value. Only routines the build reached are callable: mark a type `@reflectable` to keep every routine it has.
- A record that obeys `Annotation` can be written as an annotation such as `@Memoize(limit: 100)`, and its `around(call:)` runs in place of each call.
- `implant` replaces a member routine for one object, and `remove_implant` gives it back.

## Tools

- A resident build daemon and a JIT mode for the edit loop: `[target] mode = "debug-jit"`, `use-daemon` and `incremental`.
- `new`, `migrate`, `docs`, `fmt`, `lint`, `test` and `lsp`, plus `check --json`.
- `@test` routines with `@case`, `@skip`, `@time_limit` and `@memory_limit`.
- A Rider plugin and a VS Code extension.
- Deadlock detection in debug builds.

## Fixed

Among the fixes: two jobs adding to one `List` lost updates, `total += xs[0]` crashed, a `global` `S64` lost digits, and two jobs using globals and a shared entity in opposite orders hung in a release build. They now stop with a `Deadlock error`. `CHANGELOG.md` lists all of them.

## Known issues

- Rebuilding after a one-file edit is slower than the 250 ms goal. On 2026-11-05 a 20-routine program rebuilt in about 1.1 s with `debug-jit` and the daemon.
- The Windows package is a ReadyToRun build that bundles the .NET runtime. It is not NativeAOT. Linux and macOS packages are NativeAOT, and the macOS package has not been verified yet.
- `[target] backend = "lapila"` is experimental and does not translate every program. For example, `words()` builds there but crashes when it runs.
- The run-time check that catches a collection changing during a `for` is correct on one thread only. When several Works change the same collection while looping, it can miss the change.
- The length returned by the file and environment routines can be wrong if a call is suspended and continued on another thread.
