<p align="center">
  <img src="branding/razorforge.svg" alt="RazorForge logo" width="112">
</p>

<h1 align="center">RazorForge</h1>

<p align="center"><strong>Make programming sharp again.</strong></p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.5.0-informational.svg" alt="Version 0.5.0">
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
numeric tools. It is not a kernel or bare-metal language: programs link against a runtime library,
and there is no freestanding mode.

```razorforge
import IO/Console

entity Resource
    tag: S64

routine consume(r: Resource)
    show(f"consuming tag={r.tag}")
    return
    # r is destroyed here, exactly once

failable routine parse_digit(c: Char) -> S64
    if "0123456789".lacks(c)
        throw InvalidDigitError()   # the failure carries the error that says why
    return c.to[Text]().to[S64]()

routine start()
    var b = Resource(tag: 7)
    consume(r: steal b)             # `b` moves into the call, using it afterwards is a build error

    var d = try parse_digit(c: 'x') # `try` recovers the failure as Maybe[S64]
    when d
        is None => show("not a digit")
        else as n => show(f"digit {n}")

    var level: U8 = 200
    show(level +^ 100)              # clamping add: 255
    return
```

> **Early alpha.** The builder, runtime, and standard library work, and every commit passes about
> 1,500 unit tests and 240+ end-to-end programs (RazorForge and Suflae) on Windows, Linux, and
> macOS in CI. APIs will still change between releases, and you will find bugs.

## What we are trying to keep

- **No ceremony.** Nothing you write is there only because the language asks for it.
- **Code is the expression of intent.** What you write says what you mean.
- **What should work, works.** Code that looks like it should work does, and it does what it looks like it does.
- **One acceptable way.** There is a way that works for everyone, even if it isn't everyone's favorite.
- **Nothing goes wrong silently.** A failure is loud, a hang is reported, and no work is left running that nobody will
  ever check.

And for RazorForge, the feeling of finishing something the way you meant it, and of the builder recognizing it.

## What it is like

**Copy or share, and say how you share.** `=` copies a record. An entity is something a copy would get
wrong, because two copies leave you unsure which one is legitimate, or because copying costs too much, so you
share it instead, and RazorForge asks for how long. For a moment: the entity lives in one place, you give a
routine or a block permission (`view()` / `modify()`), and the builder checks that the permission ends with its
call or `using` block. Moving it to another place is written `steal`. For longer: a manager such as `Retained[T]` or `Guarded[T, P]` (or the watching `Tracked[T]` / `Witnessed[T]`)
holds the entity while each part keeps a handle. Cleanup follows from that choice and runs deterministically at
scope exit, with no lifetime syntax to write.

**Failure is loud by default, and recovery takes one keyword.** A routine that can `throw` is declared
`failable routine`, and its call sites look like any other call. A bare call that fails crashes the
program with a message and a stack trace (exit status 82). To recover, put a keyword in front of the
call: `try` gives `Maybe[T]`, and `check` gives `Check[T]` (which keeps the error). There are no
exceptions to declare or catch.

**Numbers mean what they say.** `+ - *` are checked and crash on overflow, while wrapping (`+%`) and
clamping (`+^`) are separate operators you choose. Integers go from `S8`/`U8` to `S256`/`U256`, and
next to the binary floats `B16` to `B128` sit decimal floats `D32`/`D64`/`D128`, the
arbitrary-precision `Integer`, and `Decimal`, a decimal with 34 significant digits that is never infinite or NaN. A bare literal takes its type from where it goes (`level +^ 100`, `var x: B64 = inf`),
and a variable's type is never inferred from later uses. The float math library is correctly rounded, and a float prints as
the shortest text that reads back as the same value.

**Markers go where a danger is silent.** Moves are marked (`steal`), overflow behavior
is chosen per operator, and the few operations that can actually break memory safety live in
`danger` blocks. Everything else stays quiet.

**Calls read on their own.** A call with three or more parameters names its arguments
(`draw_line(x: 3, y: 4, length: 10)`), a result you throw away takes an explicit `drop`, and blocks are
indentation, four spaces each.

## Quick start

### 1. Install

Download the package for your platform from the
[releases page](https://github.com/dj-lumiere/RazorForge/releases):

| Platform              | File                                 |
|-----------------------|--------------------------------------|
| Windows x64           | `razorforge-v<version>-win-x64.zip`    |
| Linux x64             | `razorforge-v<version>-linux-x64.tar.gz` |
| macOS (Apple Silicon) | `razorforge-v<version>-osx-arm64.tar.gz` |

The package is self-contained: the builder, the standard library, the runtime, and the LLVM
toolchain are all inside it. Unpack it anywhere and run the installer from that folder:

```bat
:: Windows: adds the folder to your user PATH
install.cmd
```

```bash
# Linux / macOS: links the commands into ~/.local/bin
./install.sh
```

Open a new terminal and check that it works:

```bash
razorforge version
```

The same package installs Suflae too (`suflae`). Short aliases `rf` and `sf` work as well.

> **Linux:** linking needs the C library's development files. Most machines have them. Otherwise
> install `libc6-dev` (Debian/Ubuntu) or `glibc-devel` (Fedora) once.
>
> **macOS:** linking uses Apple's Command Line Tools. Run `xcode-select --install` once if you have
> never built anything on this Mac. This alpha is not notarized, and `install.sh` clears the
> Gatekeeper quarantine on the unpacked folder for you.

### 2. Hello, world

```razorforge
# hello.rf
import IO/Console

routine start()
    show("Hello from RazorForge!")
    return
```

```bash
razorforge run hello.rf
```

`run` builds a native executable and runs it. `razorforge build hello.rf` stops after
building and leaves `hello.exe` next to the source.

Every routine ends with an explicit `return`, because scope teardown is anchored there. A single-file
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
module_paths = ["../shared-utils"] # folders whose modules join the solution, which imports also search
mode = "debug"                     # debug -O0 | release -O2 | release-time -O3 | release-space -Os
```

With no entry file, the command looks for `config.toml` in the current directory and its parents,
so `cd` into a solution and run `razorforge buildandrun`.

## What works today

- **Builder pipeline:** tokenizing, parsing, name and type resolution, desugaring, semantic
  analysis, type-aware lowering, monomorphization, LLVM IR, and a native executable. Only code
  reachable from `start()` is built. Diagnostics read `error[RF-S###]: file:line:col: message` with a
  source excerpt.
- **Memory model:** copy or share. Records are copied, entities are shared: for a moment through
  scope-bound permissions (`view()` / `modify()`), for longer through the
  `Retained` / `Guarded` / `Tracked` / `Witnessed` sharing managers. Entities move with `steal` and end
  deterministically (`destruct`), and `danger` blocks mark memory-unsafe code.
- **Error handling:** `failable` routines (`throw`), the `try` / `check` recovery keywords, and `when`
  pattern matching with exhaustiveness checks. A variant shows the value it holds (`40`, or `none` when
  empty), and choices compare with `==` and by their order (`<`, `>=`).
- **Numerics:** `S8` to `S256`, `U8` to `U256`, `B16` to `B128` (plus `BF16` for storage), `D32` / `D64` /
  `D128`, arbitrary-precision `Integer`, 34-digit `Decimal`, complex `C64` / `C128` / `C256`,
  quaternions, vectors, and SIMD `Vector[T, N]`, with checked, wrapping, and clamping arithmetic.
- **Collections:** `List`, `Dict`, `Set`, `CircularList`, `BitList`, `PriorityQueue`, the sorted
  collections, `SplitList` (struct of arrays), fixed-size `Array[T, N]`, and lazy iterator adapters
  (`select`, `where`, `zip`, `enumerate`, …). Changing a collection while an `each` loop walks it is
  a build error, including when the change is hidden behind a call.
- **Text:** UTF-32 `Text`, f-strings, `Bytes` with UTF-8 helpers, and range slicing
  (`text[a til b]`). Ranges (`to` inclusive, `til` exclusive, `by` for the step) work over any ordered
  type, such as integers, `Char`, decimals and floats.
- **Generics and protocols:** type parameters with protocol constraints (`needs T obeys P`), const
  generics (`Array[T, N]`), associated types. Everything monomorphizes, so there is no runtime dispatch.
- **Concurrency:** any routine becomes concurrent work with `f.to[Agent](args)`, an `Agent[T]` that
  runs on the runtime's thread pool (`start_now()`), takes turns on the current thread (`start_here()`)
  or gets an OS thread of its own (`start_apart()`). Also `gather` / `race`, typed channels with
  backpressure, async file I/O, and OS signal handlers. Async networking is not implemented yet.
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
grammar, the standard library, and the loud failures, and shares every entity for you: its
entities are shared reference-counted handles with a cycle collector, bare numbers are exact
`Integer` and `Decimal`, and it has thread-safe module `global`s. A `.sf` file can import `.rf`
modules, so one program can mix the two. Pick RazorForge when you want to choose how each thing is
shared and how wide each number is, Suflae when you would rather not think about either.

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
[Anvila](https://github.com/dj-lumiere/Anvila), and the shared library under the standard library is
[Ingrid](https://github.com/dj-lumiere/Ingrid).

## Roadmap

**Shipped** (RazorForge 0.5.0)

- Native builds on Windows and Linux x86-64 and macOS ARM64, with prebuilt packages
- Copy-or-share memory model, failable routines with recovery keywords, generics, collections, 256-bit
  integers, decimal and 128-bit floats
- Concurrent work behind `Agent[T]`, channels, async file I/O, signals
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

## Building from source

You only need this to work on the builder or the standard library. RazorForge is built from five
repositories checked out side by side: the builder core
([Anvila](https://github.com/dj-lumiere/Anvila)), the shared library
([Ingrid](https://github.com/dj-lumiere/Ingrid)), the two language front ends (this repository and
[Suflae](https://github.com/dj-lumiere/Suflae)), and [Tessera](https://github.com/dj-lumiere/Tessera),
whose builder compiles part of the native runtime.

You need the .NET 10 SDK, LLVM 22 (`clang` and `opt` on `PATH`), CMake 3.20+, and Ninja on Windows.

```bash
mkdir LumiFoundry && cd LumiFoundry
git clone https://github.com/dj-lumiere/Anvila.git
git clone https://github.com/dj-lumiere/Ingrid.git
git clone https://github.com/dj-lumiere/RazorForge.git
git clone https://github.com/dj-lumiere/Suflae.git
git clone https://github.com/dj-lumiere/Tessera.git

dotnet build RazorForge/RazorForge.csproj               # also builds the native runtime
dotnet test RazorForge/tests/RazorForge.Tests.csproj    # optional
```

The built command is `RazorForge/bin/Debug/net10.0/RazorForge` (`RazorForge.exe` on Windows).

## Contributing

Bug reports, feature suggestions, documentation fixes, and code are all welcome. A good start is to
[build from source](#building-from-source), run the tests, and read a few programs in `tests/Fixtures/Stdlib/`. Report bugs
at [github.com/dj-lumiere/RazorForge/issues](https://github.com/dj-lumiere/RazorForge/issues).

## License

MIT, see [`LICENSE`](LICENSE). Third-party components are listed in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
