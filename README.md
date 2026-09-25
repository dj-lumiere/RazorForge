# Disl

**Disl** (pronounced "diesel"), Dalmurii's IR Style Language, is a systems language with the discipline of a compiler
IR: SSA values, block parameters instead of phi nodes, explicit jumps and branches, explicit memory (`alloca`, load,
store), and no operators. Every operation is a named method, so every cost is visible.

```disl
routine sum(%n: I64) -> I64
    block entry():
        jump loop(0, 0)

    block loop(%i: I64, %acc: I64):
        %done: Bool = %i.sge(%n)
        branch %done ? return(%acc) : body(%i, %acc)

    block body(%i: I64, %acc: I64):
        %next_acc: I64 = %acc.add(%i)
        jump loop(%i.add(1), %next_acc)
```

> **Status:** the language is in design, and the spec is still a proposal. The compiler works but is incomplete.

## Repository

| Path | Contents |
|------|----------|
| `DISL/` | The compiler, in C#. It emits LLVM IR text and links through `clang`. |
| `stdlib/` | The standard library, written in Disl. It's the source of truth for the current design. |
| `tests/` | Golden tests for the compiler. |
| `playground/` | Example programs: Dijkstra, a lazy segment tree, SHA-256, a calculator, and more. |
| `Disl.tmbundle/` | A TextMate grammar for syntax highlighting. |

The language reference lives in the [wiki](https://github.com/dalmurii/disl/wiki): start with
[Introduction](https://github.com/dalmurii/disl/wiki/Introduction) and
[Quick Start](https://github.com/dalmurii/disl/wiki/Quick-Start).

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/) and clang 21 or newer on `PATH`.

```sh
dotnet run --project DISL -- run tests/hello.disl          # build and run a program
dotnet run --project DISL -- build prog.disl -o prog       # build an executable
dotnet run --project DISL -- build prog.disl --emit-llvm   # write LLVM IR instead
dotnet run --project DISL -- test tests                    # run the golden tests
```

`build` and `run` also take `--target <arch-os-abi>` and `-O`. All files on one command line form one compilation
unit, and the stdlib is compiled in with them.

## Tests

Each test in `tests/` is `<name>.disl` with one of:

- `<name>.expected`: the program's stdout
- `<name>.exit`: its exit code (default 0)
- `<name>.error`: text the compile error must contain
