<p align="center">
  <img src="RazorForge/branding/razorforge-suflae-icon.svg" alt="RazorForge and Suflae" width="112">
</p>

<h1 align="center">RazorForge &amp; Suflae</h1>

<p align="center"><strong>Two languages, one builder.</strong></p>

<p align="center">
  <img src="https://img.shields.io/badge/license-MIT%20%7C%20Apache--2.0-blue.svg" alt="License">
  <img src="https://img.shields.io/badge/status-early%20alpha-orange.svg" alt="Status">
</p>

RazorForge (`.rf`) and Suflae (`.sf`) are two natively compiled languages that share one grammar,
one standard library, and one builder. They part ways on how much of the machinery you see.

<table>
<tr>
<td width="50%" valign="top">

<img src="RazorForge/branding/razorforge.svg" alt="RazorForge logo" width="56">

**RazorForge** · *Make programming sharp again.*

You own every entity and hand it over with `steal`. Cleanup is deterministic, overflow behavior
is chosen per operator, and the few operations that can break memory sit in `danger` blocks.

[More below](#razorforge) · [Docs](https://razorforge.lumi-dev.xyz/)

</td>
<td width="50%" valign="top">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="Suflae/branding/suflae-dark.svg">
  <img src="Suflae/branding/suflae.svg" alt="Suflae logo" width="56">
</picture>

**Suflae** · *Make programming sweet again.*

Entities are shared handles with a cycle collector, bare numbers are exact `Integer` and
`Decimal`, and a file can be just its statements. The fixed-width types are there when you ask.

[More below](#suflae) · [Docs](https://suflae.lumi-dev.xyz/)

</td>
</tr>
</table>

Both compile to native code through LLVM and link against the same runtime library. A `.sf` file
can import `.rf` modules and use their types, so one program can mix the two. Neither is a kernel
or bare-metal language, and there is no freestanding mode.

> RazorForge (0.4) and Suflae (0.1) are early alpha. The builder, runtime, and standard library
> work, and about 1,500 unit tests and 230+ end-to-end snapshot fixtures in both languages pass in
> CI on every commit. The languages are still young, though: APIs will change, and you will find
> bugs.

## What they share

1. **Failure is loud by default, and recovery takes one keyword.** A routine that `throw`s or goes
   `absent` is failable. A bare call to it crashes with a message. To recover, put a keyword in
   front of the call: `try parse(...)` gives `Maybe[T]`, `grab parse(...)` gives `Check[T]` (which
   keeps the error), and `lookup parse(...)` gives `Lookup[T]`. There are no exceptions to declare
   or catch.
2. **Calls are readable by default.** Multi-parameter calls use named arguments, and ignoring a
   return value takes an explicit `discard`.
3. **One standard library.** Collections, `Text`, the numeric types, concurrency, and file I/O are
   the same code in both languages.

```razorforge
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
    var g = gcd(a: 252, b: 105)  # multi-parameter calls name their arguments
    return
```

## RazorForge

<img src="RazorForge/branding/razorforge.svg" alt="RazorForge logo" width="72">

RazorForge is built around precision: in what a program means, in how it fails, and in the numbers
it computes. It is meant for application work where you want native code and predictable memory
behavior: CLIs, services, games, and data and numeric tools.

1. **Single ownership without a borrow checker.** Containment is ownership, every transfer is
   marked with `steal`, and you reach an entity you don't own through scope-bound access tokens.
   Cleanup is deterministic and use after move is rejected, with no lifetime annotations and no
   garbage collector.
2. **Numbers mean what they say.** Overflow is checked unless you choose wrapping (`+%`) or
   clamping (`+^`). Decimal floats (`D32`/`D64`/`D128`) and arbitrary-precision `Integer`/`Decimal`
   sit next to the binary floats, and the float math is correctly rounded.
3. **Markers go where a danger is silent.** Ownership transfers are marked with `steal`, and the few
   operations that can actually break memory safety live in `danger` blocks.

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

## Suflae

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="Suflae/branding/suflae-dark.svg">
  <img src="Suflae/branding/suflae.svg" alt="Suflae logo" width="72">
</picture>

Suflae is for building applications without managing memory by hand. It keeps RazorForge's grammar,
standard library, and loud failures, and hides the ownership machinery.

1. **Entities are shared.** A Suflae `entity` is a reference-counted handle. It switches to
   thread-safe counting when it escapes to another task, and a cycle collector reclaims reference
   cycles. There is no `steal`, no access tokens, and no `danger`.
2. **Numbers are exact by default.** A bare `42` is an arbitrary-precision `Integer` and a bare
   `3.14` is a base-10 `Decimal`. The fixed-width types are still there when you name them.
3. **A file can be just its statements.** Top-level statements need no `start()`, and `global`
   gives you module-level state that parallel tasks can safely touch.

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

The REPL, runtime reflection, and hot reload aren't there yet. See
[`SUFLAE-FOR-AI.md`](SUFLAE-FOR-AI.md) for the current state.

## Quick start

### Prerequisites

- .NET SDK 10.0+
- LLVM 20+ (`clang` and `opt` on PATH)
- CMake 3.20+ and a C compiler (for the native runtime)

### Build from source

```bash
git clone https://github.com/dj-lumiere/razorforge-suflae.git
cd razorforge-suflae

dotnet build        # builds the builder AND the native runtime (via CMake)
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

```suflae
# hello.sf
import IO/Console

show("Hello from Suflae!")
```

```bash
./bin/Debug/net10.0/RazorForge buildandrun hello.rf
./bin/Debug/net10.0/RazorForge buildandrun hello.sf
# Windows: .\bin\Debug\net10.0\RazorForge.exe buildandrun hello.rf
```

The file extension picks the language. Release packages also ship a `suflae` command: a bare
`suflae hello.sf` builds and runs the file, like `python hello.py`.

`module` is optional in both languages. Without it, the module path comes from the file's location.
A single-file program can skip `routine start()` and put its statements at the top level (script
mode), as `hello.sf` does.

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
  analysis, type-aware lowering, monomorphization, LLVM IR, native binary, for both languages.
  Diagnostics come in `error[RF-S###]: file:line:col` format with source excerpts, spelled
  `SF-S###` in a Suflae file. Only code
  reachable from `start()` is built.
- **Memory model:** in RazorForge, single-ownership entities with deterministic `destroy`, explicit
  `steal` transfer, scope-bound access tokens, `Retained`/`Guarded`/`Tracked`/`Witnessed` reference
  counting, and `danger` blocks for the few operations that can actually break memory safety. In
  Suflae, shared entities with biased reference counting, promotion to thread-safe counting on
  escape, and a cycle collector.
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
  is a buildtime error in RazorForge and a runtime error in Suflae.
- **Text:** a UTF-32 `Text` type, f-string interpolation with format specs, `Bytes` with UTF-8
  iteration, and range slicing (`text[a til b]`).
- **Generics and protocols:** type parameters with protocol constraints (`needs T obeys P`),
  const generics (`Array[T, N]`), associated types, and monomorphization.
- **Concurrency:** stackful coroutines (`suspended routine`) and OS threads (`threaded routine`)
  behind one `Agent[T]` handle, `gather`/`race`, typed channels with backpressure, async file I/O,
  and OS signal handlers (`Signals`). Async networking (sockets, HTTP) is not implemented yet.
- **Interop and build:** realm-qualified `C::` foreign routines, linking against external C
  libraries, file-level conditional compilation (`@target`), buildtime reflection (`expand`), and
  Suflae files importing RazorForge modules.
- **Tooling:** a built-in language server for both languages (diagnostics, hover, go-to-definition,
  rename, completion, inlay hints, …) with a VS Code extension and a Rider plugin.
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

## Documentation

Each language has its own documentation site, built from `RazorForge-Wiki/` and `Suflae-Wiki/`.

**RazorForge** · [razorforge.lumi-dev.xyz](https://razorforge.lumi-dev.xyz/)

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

**Suflae** · [suflae.lumi-dev.xyz](https://suflae.lumi-dev.xyz/)

- [Hello World](https://suflae.lumi-dev.xyz/Hello-World) ·
  [Entities](https://suflae.lumi-dev.xyz/Entities) ·
  [Numeric Types](https://suflae.lumi-dev.xyz/Numeric-Types)
- [Error Handling](https://suflae.lumi-dev.xyz/Error-Handling) ·
  [Collections](https://suflae.lumi-dev.xyz/Collections) ·
  [Concurrency Model](https://suflae.lumi-dev.xyz/Concurrency-Model)
- [Choosing a Language](https://suflae.lumi-dev.xyz/Choosing-Language) ·
  [RazorForge Interop](https://suflae.lumi-dev.xyz/RazorForge-Interop)

The end-to-end fixtures in `tests/Fixtures/Stdlib/*.rf` and `tests/Fixtures/StdlibSf/*.sf` pass on
every commit, so they also work as a runnable example for nearly every language feature.

## Project structure

```
razorforge-suflae/
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
├── Standard/RazorForge/   # Shared standard library (.rf sources, used by both languages)
├── Standard/Suflae/       # Suflae-side standard library (.sf sources)
├── native/                # C runtime + vendored libs (libco, libuv)
├── tests/                 # Unit tests + end-to-end snapshot fixtures
├── vscode-extension/      # VS Code extension
├── rider-plugin/          # Rider plugin
├── RazorForge-Wiki/       # RazorForge documentation sources
└── Suflae-Wiki/           # Suflae documentation sources
```

## Roadmap

### Shipped (latest release: RazorForge 0.4.0 · Suflae 0.1.0)

- [x] Builder, LLVM, and native pipeline on Windows/Linux x86-64 and macOS ARM64
- [x] RazorForge: ownership model, failable routines with recovery keywords, generics,
  collections, 256-bit integers, decimal and 128-bit floats
- [x] Suflae: shared entities with cycle collection, exact default numbers, script mode, `global`
- [x] Concurrency: coroutines and threads behind `Agent[T]`, channels, async file I/O, signals
- [x] Language server with VS Code and Rider integrations
- [x] Prebuilt release packages (win-x64, linux-x64, osx-arm64)

### Next

- Suflae REPL, and a fast edit-and-rerun loop for RazorForge
- Async networking (TCP, then HTTP), then higher-level protocols
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

More in the Design Philosophy pages for [RazorForge](https://razorforge.lumi-dev.xyz/Design-Philosophy)
and [Suflae](https://suflae.lumi-dev.xyz/Design-Philosophy).

## Contributing

Bug reports, feature suggestions, documentation fixes, and code are all welcome. A good place to
start is running the fixture suite (`dotnet test`) and reading `tests/Fixtures/Stdlib/` and
`tests/Fixtures/StdlibSf/` for working examples of both languages.

## License

Dual-licensed under MIT and Apache-2.0. You can choose either.

## Community

- GitHub: [github.com/dj-lumiere/razorforge-suflae](https://github.com/dj-lumiere/razorforge-suflae)
- Issues: [Report bugs or request features](https://github.com/dj-lumiere/razorforge-suflae/issues)
- Docs: [razorforge.lumi-dev.xyz](https://razorforge.lumi-dev.xyz/) · [suflae.lumi-dev.xyz](https://suflae.lumi-dev.xyz/)
