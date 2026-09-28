# RazorForge

**Make programming sharp again.**

![License](https://img.shields.io/badge/license-MIT%20%7C%20Apache--2.0-blue.svg)
![Status](https://img.shields.io/badge/status-early%20alpha-orange.svg)

RazorForge is a natively compiled, statically typed programming language built around precision:
in what a program means, in how it fails, and in the numbers it computes.

1. **Single ownership without a borrow checker.** Containment is ownership, every transfer is
   marked with `steal`, and you reach an entity you don't own through scope-bound access tokens.
   Cleanup is deterministic and use after move is rejected, with no lifetime annotations and no
   garbage collector.
2. **Failure is loud by default, and recovery takes one keyword.** A routine that `throw`s or goes
   `absent` is failable. A bare call to it crashes with a message. To recover, put a keyword in
   front of the call: `try parse(...)` gives `Maybe[T]`, `grab parse(...)` gives `Check[T]` (which
   keeps the error), and `lookup parse(...)` gives `Lookup[T]`. There are no exceptions to declare
   or catch.
3. **Numbers mean what they say.** Overflow is checked unless you choose wrapping (`+%`) or
   clamping (`+^`). Decimal floats (`D32`/`D64`/`D128`) and arbitrary-precision `Integer`/`Decimal`
   sit next to the binary floats, and the float math is correctly rounded.

It compiles to native code through LLVM and is meant for application work: CLIs, services, games,
and data and numeric tools where you want native code and predictable memory behavior. It is not a
kernel or bare-metal language. Programs link against the RazorForge runtime library, and there is
no freestanding mode.

Its sibling, **Suflae** (`.sf`), shares the grammar and the standard library but hides the
low-level machinery. Entities are shared reference-counted handles with a cycle collector instead
of single-owner values, and numbers default to `Integer` and `Decimal`. See [Suflae](#suflae).

> RazorForge (0.4) and Suflae (0.1) are early alpha. The builder, runtime, and standard library
> work, and about 1,500 unit tests and 230+ end-to-end snapshot fixtures pass in CI on every
> commit. The languages are still young, though: APIs will change, and you will find bugs.

## A taste of RazorForge

### Failable routines and recovery keywords

```razorforge
module Demo
import IO/Console

# A routine that can `throw` is declared with `!`; call sites don't write it.
routine get_text!(n: S64) -> Text
  when n
    == 0 => throw DivisionByZeroError()
    == 1 => return "hello"
    else => return "world"

routine start()
  var m = try get_text(n: 0)   # `try` recovers -> Maybe[Text]
  when m
    is None => show("absent")
    else v  => show(f"present: {v}")
  show(get_text(n: 1))         # a bare call crashes loudly if it fails
  return
```

### Ownership is explicit

```razorforge
entity Resource
  tag: S64

routine consume(r: Resource)
  show(f"consuming tag={r.tag}")
  return
  # r is destroyed here — exactly once, deterministically

routine start()
  var b = Resource(tag: 7)
  consume(r: steal b)   # ownership transferred; using `b` afterwards is a compile error
  return
```

An entity has a single owner, and handing it over requires `steal` (otherwise you get `RF-S413`).
Access tokens (`view()`/`modify()`, which give `Viewing[T]`/`Modifying[T]`) let you read or write
an entity without taking ownership and without lifetime syntax. When you do want sharing, you opt
into reference counting with `Retained[T]`, `Guarded[T, P]`, or the weak `Tracked[T]`/`Witnessed[T]`.

### Calls are readable by default

```razorforge
# Multi-parameter calls require named arguments — call sites document themselves.
var g = gcd(a: 252, b: 105)
discard seen.add(value: v)   # ignoring a return value is explicit, too
```

## Quick start

### Prerequisites

- .NET SDK 10.0+
- LLVM 20+ (`clang` and `opt` on PATH)
- CMake 3.20+ and a C compiler (for the native runtime)

### Build from source

```bash
git clone https://github.com/dj-lumiere/razorforge-suflae.git
cd razorforge-suflae

dotnet build        # builds the compiler AND the native runtime (via CMake)
dotnet test         # optional: run the test suite
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
./bin/Debug/net10.0/RazorForge buildandrun hello.rf
# Windows: .\bin\Debug\net10.0\RazorForge.exe buildandrun hello.rf
```

`module` is optional. Without it, the module path comes from the file's location. A single-file
program can also skip `routine start()` and put its statements at the top level (script mode).

### Using an AI assistant?

RazorForge and Suflae aren't in any model's training data yet, so assistants tend to guess
Rust- or Python-flavored syntax that doesn't build. Point yours at
[`RAZORFORGE-FOR-AI.md`](RAZORFORGE-FOR-AI.md) and [`SUFLAE-FOR-AI.md`](SUFLAE-FOR-AI.md), which
also ship inside every release package. They are compact lists of where those guesses go wrong,
with pointers to the CI-verified example programs in [`tests/Fixtures/Stdlib/`](tests/Fixtures/Stdlib/)
and [`tests/Fixtures/StdlibSf/`](tests/Fixtures/StdlibSf/).

### CLI reference

```
RazorForge <source-file>                  Parse file and show AST summary
RazorForge parse <source-file>            Parse file and show AST summary
RazorForge tokenize <source-file>         Tokenize file and show tokens
RazorForge codegen [entry-file] [out.ll]  Build up to LLVM IR only (no opt/link)
RazorForge build [entry-file]             Build a native executable (no run)
RazorForge buildandrun [entry-file]       Build, link, and execute
RazorForge check [entry-file]             Type-check only (no codegen)
RazorForge validate-stdlib [rf|sf]        Validate stdlib routine bodies
RazorForge --lsp                          Run the language server (stdio)
RazorForge help                           Show usage
RazorForge version                        Show compiler version
```

A `.sf` entry file is built as Suflae. Release packages also ship a `suflae` command: a bare
`suflae hello.sf` builds and runs the file, like `python hello.py`.

There are no build flags. All build configuration lives in the single `[target]` section of the
`config.toml` manifest:

```toml
[package]
name = "my-app"

[target]
executable = "MainModule"          # entry module (by module path, not file path)
library = ["../shared-utils"]      # external dependency directories (optional)
mode = "debug"                     # debug -O0 | release -O2 | release-time -O3 | release-space -Os
```

Each `library` entry is a directory, relative to the manifest, whose modules join the import search
space, much like requirements.txt. Once the package manager lands, entries will also accept
versioned packages from the package site (for example `"json-utils@1.2.0"`), fetched into a cache
that builds read the same way. If you give no entry file, the CLI looks for `config.toml` in the
current directory and its parents, so you can `cd` into a project and run `razorforge buildandrun`.

## What works today

- **Builder pipeline:** tokenizer, parser, declaration and name resolution, desugaring, semantic
  analysis, type-aware lowering, monomorphization, LLVM IR, native binary. Diagnostics come in
  `error[RF-S###]: file:line:col` format with source excerpts. Only code reachable from `start()`
  is built.
- **Memory model:** single-ownership entities with deterministic `destroy`, explicit `steal`
  transfer, scope-bound access tokens, `Retained`/`Guarded`/`Tracked`/`Witnessed` reference
  counting, and `danger` blocks for the few operations that can actually break memory safety.
- **Error handling:** failable routines (`throw`/`absent`), the `try`/`grab`/`lookup` recovery
  keywords, `Maybe[T]`/`Check[T]`/`Lookup[T]` carriers, and `when` pattern matching. Every crash
  exits with status 82 and prints a message and stack trace to stderr.
- **Numerics:** `S8`–`S256`, `U8`–`U256`, binary floats `B16`–`B128` (plus `BF16` for storage),
  decimal floats `D32`/`D64`/`D128`, arbitrary-precision `Integer`/`Decimal`/`Real`, complex
  `C64`/`C128`/`C256`, quaternions, vectors, and native SIMD `Vector[T, N]`, all with checked,
  wrapping, and clamping arithmetic.
- **Collections:** `List`, `Dict`, `Set`, `CircularList`, `BitList`, `PriorityQueue`, sorted
  collections, `SplitList` (struct-of-arrays), fixed-size `Array[T, N]`, and iterator adapters
  (`select`, `where`, `zip`, `enumerate`, …). Changing a collection while an `each` loop walks it
  is a buildtime error.
- **Text:** a UTF-32 `Text` type, f-string interpolation with format specs, `Bytes` with UTF-8
  iteration, and range slicing (`text[a til b]`).
- **Generics and protocols:** type parameters with protocol constraints (`needs T obeys P`),
  const generics (`Array[T, N]`), associated types, and monomorphization.
- **Concurrency:** stackful coroutines (`suspended routine`) and OS threads (`threaded routine`)
  behind one `Agent[T]` handle, `gather`/`race`, typed channels with backpressure, async file I/O,
  and OS signal handlers (`Signals`). Async networking (sockets, HTTP) is not implemented yet.
- **Interop and build:** realm-qualified `C::` foreign routines, linking against external C
  libraries, file-level conditional compilation (`@target`), and buildtime reflection (`expand`).
- **Tooling:** a built-in language server (diagnostics, hover, go-to-definition, rename,
  completion, inlay hints, …) with a VS Code extension and a Rider plugin.
- **Testing:** every commit runs about 1,500 unit tests plus 230+ end-to-end fixtures, in both
  RazorForge and Suflae, that build, link, and run programs and diff their output against snapshots.

### Platform support

| Platform                    | Status                                       |
|-----------------------------|----------------------------------------------|
| Windows x86-64              | Working (CI-verified on every commit)        |
| Linux x86-64                | Working (CI-verified on every commit)        |
| macOS ARM64 (Apple Silicon) | Working (CI-verified on every commit)        |
| Linux ARM64, macOS x86-64   | Target definitions exist, **not yet tested** |

On x86-64, programs target x86-64-v3 (Intel Haswell 2013+, AMD Excavator 2015+, every Ryzen),
because the correctly rounded float math relies on hardware FMA. Older CPUs, and low-end
Pentium/Celeron/Atom parts without AVX, can't run the output.

## Suflae

Suflae is the easier-going sibling of RazorForge. It has the same grammar and the same standard
library with the low-level machinery hidden, and the same builder handles `.sf` files.

```suflae
entity Point
  x: Integer
  y: Integer

var p = Point(x: 3, y: 4)
var q = p                 # both names refer to the same Point — no `steal`
q.x = 10
show(f"{p.x}")            # 10
show(0.1 + 0.2 == 0.3)    # true — bare numbers are Integer and Decimal
```

A Suflae `entity` is a shared, reference-counted handle. It switches to thread-safe counting when
it escapes to another task, and a cycle collector reclaims reference cycles. There is no `steal`,
no access tokens, and no `danger`.

Numbers are exact by default: a bare `42` is an arbitrary-precision `Integer` and a bare `3.14` is
a base-10 `Decimal`. The fixed-width types are still there when you name them.

Top-level statements need no `start()`, and `global` gives you module-level state that parallel
tasks can safely touch. A `.sf` file can also import `.rf` modules and use their types.

The REPL, runtime reflection, and hot reload aren't there yet. See
[`SUFLAE-FOR-AI.md`](SUFLAE-FOR-AI.md) for the current state and
[suflae.lumi-dev.xyz](https://suflae.lumi-dev.xyz/) for the documentation.

## Documentation

The full documentation is at [razorforge.lumi-dev.xyz](https://razorforge.lumi-dev.xyz/), built
from the sources in `RazorForge-Wiki/`:

- [Hello World](https://razorforge.lumi-dev.xyz/Hello-World) ·
  [Data Types](https://razorforge.lumi-dev.xyz/Data-Types) ·
  [Pattern Matching](https://razorforge.lumi-dev.xyz/Pattern-Matching)
- [Memory Model](https://razorforge.lumi-dev.xyz/Memory-Model): ownership, access tokens, `steal`
- [Error Handling](https://razorforge.lumi-dev.xyz/Error-Handling): failable routines and carriers
- [Collections](https://razorforge.lumi-dev.xyz/Collections) ·
  [Numeric Types](https://razorforge.lumi-dev.xyz/Numeric-Types) ·
  [Generics](https://razorforge.lumi-dev.xyz/Generics) ·
  [Protocols](https://razorforge.lumi-dev.xyz/Protocols)
- [Danger Blocks](https://razorforge.lumi-dev.xyz/Danger-Blocks) ·
  [C Subsystem](https://razorforge.lumi-dev.xyz/C-Subsystem) ·
  [Build System](https://razorforge.lumi-dev.xyz/Build-System)

The end-to-end fixtures in `tests/Fixtures/Stdlib/*.rf` and `tests/Fixtures/StdlibSf/*.sf` pass on
every commit, so they also work as a runnable example for nearly every language feature.

## Project structure

```
RazorForge/
├── src/                   # Builder (C#), folders numbered by pipeline stage
│   ├── 1.Tokenizer/       #   Tokenizing (RazorForge & Suflae)
│   ├── 2.Parser/          #   Parsing
│   ├── 3.Declaration/     #   Declaration collection, name and type resolution
│   ├── 4.Desugaring/      #   Syntactic, type-independent desugaring
│   ├── 5.Verification/    #   Semantic analysis & diagnostics
│   ├── 6.Lowering/        #   Type-aware lowering (operators, f-strings, patterns, teardown)
│   ├── 7.Instantiation/   #   Generic monomorphization, wired-routine synthesis
│   ├── 8.Collection/      #   Demand collection from start()
│   ├── 9.LlvmEmit/        #   LLVM IR emission
│   └── Execution/         #   CLI driver, build pipeline (opt + clang + run), language server
├── Standard/RazorForge/   # Standard library (.rf sources)
├── Standard/Suflae/       # Suflae-side standard library (.sf sources)
├── native/                # C runtime + vendored libs (zstd, sqlite3, pcre2, …)
├── tests/                 # Unit tests + end-to-end snapshot fixtures
├── vscode-extension/      # VS Code extension
├── rider-plugin/          # Rider plugin
├── RazorForge-Wiki/       # RazorForge documentation sources
└── Suflae-Wiki/           # Suflae documentation sources
```

## Roadmap

### Shipped (latest release: RazorForge 0.4.0 · Suflae 0.1.0)

- [x] Builder, LLVM, and native pipeline on Windows/Linux x86-64 and macOS ARM64
- [x] Ownership model, failable routines with recovery keywords, generics, collections, 256-bit
  integers, decimal and 128-bit floats
- [x] Concurrency: coroutines and threads behind `Agent[T]`, channels, async file I/O, signals
- [x] Language server with VS Code and Rider integrations
- [x] Suflae: shared entities with cycle collection, exact default numbers, script mode, `global`
- [x] Prebuilt release packages (win-x64, linux-x64, osx-arm64)

### Next

- Async networking (TCP, then HTTP), then higher-level protocols
- Suflae REPL and a fast edit-and-rerun loop for RazorForge
- Linux ARM64 support
- Package management

### Future

- Self-hosting builder
- Native debug info (DWARF/PDB)
- WASM backend

## Philosophy

- Total development cost over raw runtime performance
- Clear, descriptive words over obscure historical terms
- Explicit markers where a danger is silent (ownership transfers, memory-unsafe operations,
  overflow behavior), and quiet everywhere else
- Honest claims over marketing, in this README too

More in [Design Philosophy](https://razorforge.lumi-dev.xyz/Design-Philosophy).

## Contributing

Bug reports, feature suggestions, documentation fixes, and code are all welcome. A good place to
start is running the fixture suite (`dotnet test`) and reading `tests/Fixtures/Stdlib/` for working
examples of the language.

## License

Dual-licensed under MIT and Apache-2.0. You can choose either.

## Community

- GitHub: [github.com/dj-lumiere/razorforge-suflae](https://github.com/dj-lumiere/razorforge-suflae)
- Issues: [Report bugs or request features](https://github.com/dj-lumiere/razorforge-suflae/issues)
- Docs: [razorforge.lumi-dev.xyz](https://razorforge.lumi-dev.xyz/) · [suflae.lumi-dev.xyz](https://suflae.lumi-dev.xyz/)

## Acknowledgments

RazorForge draws on Rust (memory safety without GC), Python (readability), Zig (explicit control),
and C# (tooling).
