<p align="center">
  <img src="branding/razorforge.svg" alt="RazorForge logo" width="112">
</p>

<h1 align="center">RazorForge</h1>

<p align="center"><strong>Make programming sharp again.</strong></p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.4.0-informational.svg" alt="Version 0.4.0">
  <img src="https://img.shields.io/badge/status-early%20alpha-orange.svg" alt="Status: early alpha">
  <img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT">
</p>

<p align="center">
  <a href="https://razorforge.lumi-dev.xyz/">Documentation</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="RAZORFORGE-FOR-AI.md">Reference for AI assistants</a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="https://github.com/dj-lumiere/Suflae">Suflae</a>
</p>

RazorForge (`.rf`) is a natively compiled language built around precision: in what a program means,
in how it fails, and in the numbers it computes. It is meant for application work where you want
native code and predictable memory behavior: command-line tools, services, games, and data and
numeric tools. It is not a kernel or bare-metal language; programs link against a runtime library,
and there is no freestanding mode.

```razorforge
import IO/Console

entity Resource
    tag: S64

routine consume(r: Resource)
    show(f"consuming tag={r.tag}")
    return
    # r is destroyed here, exactly once

routine parse_digit!(c: Character) -> S64
    unless "0123456789" have c
        absent                      # fail without an error object
    return S64(from_text: Text(from: c))

routine start()
    var b = Resource(tag: 7)
    consume(r: steal b)             # ownership moves; using `b` afterwards is a build error

    var d = try parse_digit(c: 'x') # `try` recovers the failure as Maybe[S64]
    when d
        is None => show("not a digit")
        else n  => show(f"digit {n}")

    show(200u8 +^ 100u8)            # clamping add: 255
    return
```

> **Early alpha.** The builder, runtime, and standard library work, and every commit passes about
> 1,500 unit tests and 240+ end-to-end programs (RazorForge and Suflae) on Windows, Linux, and
> macOS in CI. APIs will still change between releases, and you will find bugs.

## What it is like

**Single ownership, no borrow checker.** Containment is ownership. An entity has one owner, every
transfer is marked with `steal`, and cleanup runs deterministically at scope exit. To use an entity
you don't own, you take a scope-bound access token (`view()` / `modify()`), which needs no lifetime
syntax and cannot outlive the call or `using` block it appears in. When you want sharing, you opt
into reference counting with `Retained[T]`, `Guarded[T, P]`, or the weak `Tracked[T]` /
`Witnessed[T]`.

**Failure is loud by default, and recovery takes one keyword.** A routine that can `throw` or go
`absent` carries `!` on its declaration, never at its call sites. A bare call that fails crashes the
program with a message and a stack trace (exit status 82). To recover, put a keyword in front of the
call: `try` gives `Maybe[T]`, `grab` gives `Check[T]` (which keeps the error), and `lookup` gives
`Lookup[T]`. There are no exceptions to declare or catch.

**Numbers mean what they say.** `+ - *` are checked and crash on overflow; wrapping (`+%`) and
clamping (`+^`) are separate operators you choose. Integers go from `S8`/`U8` to `S256`/`U256`, and
next to the binary floats `B16`–`B128` sit decimal floats `D32`/`D64`/`D128` and arbitrary-precision
`Integer`, `Decimal`, and `Real`. The float math library is correctly rounded, and a float prints as
the shortest text that reads back as the same value.

**Markers go where a danger is silent.** Ownership transfers are marked (`steal`), overflow behavior
is chosen per operator, and the few operations that can actually break memory safety live in
`danger` blocks. Everything else stays quiet: no lifetime annotations, no `unsafe` around ordinary
code.

**Calls read on their own.** Multi-parameter calls name their arguments (`gcd(a: 252, b: 105)`), an
ignored `Bool` result takes an explicit `discard`, and blocks are indentation, four spaces each.

## Quick start

### From a release package

Prebuilt packages for win-x64, linux-x64, and osx-arm64 are on the
[releases page](https://github.com/dj-lumiere/RazorForge/releases). Each one bundles the LLVM
toolchain it needs, so unpack it, put it on your `PATH`, and run:

```bash
razorforge buildandrun hello.rf
```

### From source

RazorForge is built from four repositories checked out side by side: the builder core
([Anvila](https://github.com/dj-lumiere/Anvila)), the native runtime
([Ingrid](https://github.com/dj-lumiere/Ingrid)), and the two language front ends (this repository
and [Suflae](https://github.com/dj-lumiere/Suflae)).

You need the .NET 10 SDK, LLVM 22 (`clang` and `opt` on `PATH`), CMake 3.20+, and Ninja on Windows.

```bash
mkdir LumiFoundry && cd LumiFoundry
git clone https://github.com/dj-lumiere/Anvila.git
git clone https://github.com/dj-lumiere/Ingrid.git
git clone https://github.com/dj-lumiere/RazorForge.git
git clone https://github.com/dj-lumiere/Suflae.git

# The native runtime builds against libuv and libco, which are not vendored.
git clone --depth 1 https://github.com/libuv/libuv.git Ingrid/native/libuv
git clone --depth 1 https://github.com/higan-emu/libco.git Ingrid/native/libco

dotnet build RazorForge/RazorForge.csproj      # also builds the native runtime
dotnet test RazorForge/tests/RazorForge.Tests.csproj   # optional
```

### Hello, world

```razorforge
# hello.rf
import IO/Console

routine start()
    show("Hello from RazorForge!")
    return
```

```bash
./RazorForge/bin/Debug/net10.0/RazorForge buildandrun hello.rf
# Windows: .\RazorForge\bin\Debug\net10.0\RazorForge.exe buildandrun hello.rf
```

Every routine ends with an explicit `return`; scope teardown is anchored there. A single-file
program may also skip `routine start()` and put its statements at the top level (script mode), and
`module` is optional: without it, the module path comes from the file's location.

### Using an AI assistant?

RazorForge is not in any model's training data yet, so assistants tend to guess Rust- or
Python-flavored syntax that does not build. Point yours at
[`RAZORFORGE-FOR-AI.md`](RAZORFORGE-FOR-AI.md), which also ships in every release package. It is a
compact list of where those guesses go wrong, with pointers to the CI-verified programs in
[`tests/Fixtures/Stdlib/`](tests/Fixtures/Stdlib/).

## Command line

```
razorforge buildandrun [entry-file]       Build, link, and run
razorforge build [entry-file]             Build a native executable for this OS (no run)
razorforge check [entry-file]             Type-check only
razorforge codegen [entry-file] [out.ll]  Stop at LLVM IR
razorforge parse <source-file>            Parse and show an AST summary
razorforge tokenize <source-file>         Show the tokens
razorforge validate-stdlib [language]     Check the standard library's routine bodies
razorforge --lsp                          Run the language server over stdio
razorforge help | version
```

There are no build flags. All build configuration lives in the `[target]` section of a
`config.toml` manifest:

```toml
[package]
name = "my-app"

[target]
executable = "MainModule"          # entry module (by module path, not file path)
library = ["../shared-utils"]      # directories whose modules join the import search space
mode = "debug"                     # debug -O0 | release -O2 | release-time -O3 | release-space -Os
```

With no entry file, the command looks for `config.toml` in the current directory and its parents,
so `cd` into a project and run `razorforge buildandrun`.

## What works today

- **Builder pipeline:** tokenizing, parsing, name and type resolution, desugaring, semantic
  analysis, type-aware lowering, monomorphization, LLVM IR, and a native executable. Only code
  reachable from `start()` is built. Diagnostics read `error[RF-S###]: file:line:col: message` with a
  source excerpt.
- **Memory model:** single-ownership entities with deterministic `destroy`, `steal` transfers,
  scope-bound access tokens, the `Retained` / `Guarded` / `Tracked` / `Witnessed` reference-counted
  wrappers, and `danger` blocks.
- **Error handling:** failable routines (`throw` / `absent`), the `try` / `grab` / `lookup` recovery
  keywords, and `when` pattern matching with exhaustiveness checks.
- **Numerics:** `S8`–`S256`, `U8`–`U256`, `B16`–`B128` (plus `BF16` for storage), `D32` / `D64` /
  `D128`, arbitrary-precision `Integer` / `Decimal` / `Real`, complex `C64` / `C128` / `C256`,
  quaternions, vectors, and SIMD `Vector[T, N]`, with checked, wrapping, and clamping arithmetic.
- **Collections:** `List`, `Dict`, `Set`, `CircularList`, `BitList`, `PriorityQueue`, the sorted
  collections, `SplitList` (struct of arrays), fixed-size `Array[T, N]`, and lazy iterator adapters
  (`select`, `where`, `zip`, `enumerate`, …). Changing a collection while an `each` loop walks it is
  a build error, including when the change is hidden behind a call.
- **Text:** UTF-32 `Text`, f-strings, `Bytes` with UTF-8 helpers, and range slicing
  (`text[a til b]`).
- **Generics and protocols:** type parameters with protocol constraints (`needs T obeys P`), const
  generics (`Array[T, N]`), associated types. Everything monomorphizes; there is no runtime dispatch.
- **Concurrency:** stackful coroutines (`suspended routine`) and OS threads (`threaded routine`)
  behind one `Agent[T]` handle, `gather` / `race`, typed channels with backpressure, async file I/O,
  and OS signal handlers. Async networking is not implemented yet.
- **Interop and build:** realm-qualified `C::` foreign routines and linking against C libraries,
  file-level conditional compilation (`@target`), and buildtime reflection (`expand`).
- **Tooling:** a language server (diagnostics, hover, go-to-definition, rename, completion, inlay
  hints, …), with setup scripts for VS Code (`setup-vscode`) and a Rider plugin
  ([`rider-plugin/`](rider-plugin/)).

### Platforms

| Platform                    | Status                                 |
|-----------------------------|----------------------------------------|
| Windows x86-64              | Working, tested in CI on every commit  |
| Linux x86-64                | Working, tested in CI on every commit  |
| macOS ARM64 (Apple Silicon) | Working, tested in CI on every commit  |
| Linux ARM64, macOS x86-64   | Target definitions exist, not tested   |

On x86-64, programs target x86-64-v3 (Intel Haswell 2013+, AMD Excavator 2015+, every Ryzen),
because the correctly rounded float math relies on hardware FMA. CPUs without AVX2 and FMA,
including low-end Pentium, Celeron, and Atom parts, cannot run the output.

## RazorForge and Suflae

[Suflae](https://github.com/dj-lumiere/Suflae) (`.sf`) is RazorForge's sibling. It shares the
grammar, the standard library, and the loud failures, and hides the ownership machinery: its
entities are shared reference-counted handles with a cycle collector, bare numbers are exact
`Integer` and `Decimal`, and it has thread-safe module `global`s. A `.sf` file can import `.rf`
modules, so one program can mix the two. Pick RazorForge when you want control over ownership and
widths, Suflae when you would rather not think about either.

## Documentation

The documentation lives at [razorforge.lumi-dev.xyz](https://razorforge.lumi-dev.xyz/):

- [Hello World](https://razorforge.lumi-dev.xyz/Hello-World) ·
  [Data Types](https://razorforge.lumi-dev.xyz/Data-Types) ·
  [Pattern Matching](https://razorforge.lumi-dev.xyz/Pattern-Matching)
- [Memory Model](https://razorforge.lumi-dev.xyz/Memory-Model) ·
  [Error Handling](https://razorforge.lumi-dev.xyz/Error-Handling) ·
  [Danger Blocks](https://razorforge.lumi-dev.xyz/Danger-Blocks)
- [Collections](https://razorforge.lumi-dev.xyz/Collections) ·
  [Numeric Types](https://razorforge.lumi-dev.xyz/Numeric-Types) ·
  [Generics](https://razorforge.lumi-dev.xyz/Generics) ·
  [Protocols](https://razorforge.lumi-dev.xyz/Protocols)
- [C Subsystem](https://razorforge.lumi-dev.xyz/C-Subsystem) ·
  [Build System](https://razorforge.lumi-dev.xyz/Build-System) ·
  [Design Philosophy](https://razorforge.lumi-dev.xyz/Design-Philosophy)

The programs in [`tests/Fixtures/Stdlib/`](tests/Fixtures/Stdlib/) run on every commit and are
diffed against their `.expected.txt` output, so they double as working examples for nearly every
language feature and standard library API.

## This repository

```
RazorForge/
├── src/                  # The RazorForge front end: lexer, language rules, `razorforge` command line
├── Standard/             # The standard library (.rf), used by RazorForge and Suflae builds
├── tests/                # RazorForge.Tests: unit tests and end-to-end fixtures
│   └── Fixtures/Stdlib/  #   programs with expected output
├── scripts/              # Packaging and maintenance scripts
├── rider-plugin/         # Rider plugin
├── RazorForge.tmbundle/  # TextMate grammar
├── RAZORFORGE-FOR-AI.md  # Reference for AI assistants
└── CHANGELOG.md          # Release notes for RazorForge and Suflae
```

The builder itself (parser, analysis, lowering, LLVM emission) is
[Anvila](https://github.com/dj-lumiere/Anvila), and the C runtime is
[Ingrid](https://github.com/dj-lumiere/Ingrid).

## Roadmap

**Shipped** (latest release: RazorForge 0.4.0)

- Native builds on Windows and Linux x86-64 and macOS ARM64, with prebuilt packages
- Ownership model, failable routines with recovery keywords, generics, collections, 256-bit
  integers, decimal and 128-bit floats
- Coroutines and threads behind `Agent[T]`, channels, async file I/O, signals
- Language server with VS Code and Rider integration

**Next**

- A fast edit-and-rerun loop
- Async networking (TCP, then HTTP)
- Linux ARM64
- Package management

**Later**

- Native debug info (DWARF / PDB)
- A WASM backend
- A self-hosting builder

## Contributing

Bug reports, feature suggestions, documentation fixes, and code are all welcome. A good start is to
build from source, run the tests, and read a few programs in `tests/Fixtures/Stdlib/`. Report bugs
at [github.com/dj-lumiere/RazorForge/issues](https://github.com/dj-lumiere/RazorForge/issues).

## License

MIT; see [`LICENSE`](LICENSE). Third-party components are listed in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
