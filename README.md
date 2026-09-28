# Tessera

**Tessera is a structured SSA language built from explicit operations.**

A tessera is one tile of a mosaic. A Tessera program is put together the same way: each level is made of whole
pieces of the level below, and nothing is hidden between them.

| Level | What it is |
|-------|------------|
| **operation** | One explicit step: a named method (`%a.add(%b)`), a load (`:=`), a store (`=`), an `alloca`, a call. There are no operators, so every operation says what it does and what it costs. |
| **block** | A straight run of operations. Values come in as block parameters (no phi nodes), and the block ends in exactly one terminator: `jump`, `branch`, `select`, `switch`, or `return`. |
| **routine** | A set of blocks with one entry. Control moves between its blocks only through terminators, and every value is SSA. |
| **module** | A namespace for routines, types, and constants (`Standard::Format`). It's declared in the source, not tied to files. |
| **solution** | The code compiled together to make one application. |

```tessera
routine sum(%n: S64) -> S64
    block entry():
        jump loop(0, 0)

    block loop(%i: S64, %acc: S64):
        %done: Bool = %i.ge(%n)
        branch %done ? return(%acc) : body(%i, %acc)

    block body(%i: S64, %acc: S64):
        %next_acc: S64 = %acc.add(%i)
        jump loop(%i.add(1), %next_acc)
```

> **Status:** the language is in design, and the spec is still a proposal. The compiler works but is incomplete.
> Modules and solutions are designed but not implemented yet: today every file on the command line, plus the
> stdlib, forms one solution with one namespace.

## Repository

| Path | Contents |
|------|----------|
| `Tessera/` | The compiler, in C#. It emits LLVM IR text and links through `clang`. |
| `stdlib/` | The standard library, written in Tessera. It's the source of truth for the current design. |
| `tests/` | Golden tests for the compiler. |
| `playground/` | Example programs: Dijkstra, a lazy segment tree, SHA-256, a calculator, and more. |
| `Tessera.tmbundle/` | A TextMate grammar for syntax highlighting. |

The language reference lives in the [wiki](https://github.com/dj-lumiere/Tessera/wiki): start with
[Introduction](https://github.com/dj-lumiere/Tessera/wiki/Introduction) and
[Quick Start](https://github.com/dj-lumiere/Tessera/wiki/Quick-Start).

If you're an AI assistant writing Tessera, read [TESSERA-FOR-AI.md](TESSERA-FOR-AI.md) first: it's a one-page summary of the
rules that are easy to get wrong.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/) and clang 21 or newer on `PATH`.

```sh
dotnet run --project Tessera -- run tests/hello.tess          # build and run a program
dotnet run --project Tessera -- build prog.tess -o prog       # build an executable
dotnet run --project Tessera -- build prog.tess --emit-llvm   # write LLVM IR instead
dotnet run --project Tessera -- test tests playground         # run the golden tests
```

`build` and `run` also take `--target <arch-os-abi>` and `-O`. All files on one command line form one solution,
and the stdlib is compiled in with them.

## Tests

Each test in `tests/` is `<name>.tess` with one of:

- `<name>.expected`: the program's stdout
- `<name>.exit`: its exit code (default 0)
- `<name>.error`: text the compile error must contain

The programs in `playground/` carry `<name>.expected` files too, so they run as tests alongside `tests/`.
