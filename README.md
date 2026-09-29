<img src="assets/logo.svg" width="96" alt="Tessera logo">

# Tessera

**Tessera is a structured SSA language built from explicit operations.**

**Documentation: [tessera.lumi-dev.xyz](https://tessera.lumi-dev.xyz/)**

Tessera is an attempt to keep what makes IR honest, one explicit step at a time, while taming what makes it painful
to write by hand. Nothing happens between the lines: phi nodes become block parameters, raw integers become typed
values, and hand-rolled loops over memory become collections.

A tessera is one tile of a mosaic. A Tessera program is put together the same way: each level is made of whole
pieces of the level below.

| Level | What it is |
|-------|------------|
| **operation** | One explicit step: a named method (`%a.add(%b)`), a load (`%p.load()`), a store (`%p.store(%v)`), a stack slot (`claim`), a call. There are no operators, so every operation says what it does and what it costs. |
| **block** | A straight run of operations. Values come in as block parameters (no phi nodes), and the block ends in exactly one terminator: `jump`, `branch`, `when`, or `return`. |
| **routine** | A set of blocks with one entry. Control moves between its blocks only through terminators, and every value is SSA. |
| **module** | A namespace for routines, types, and constants (`Standard::Format`). It's declared in the source, not tied to files. |
| **solution** | The code compiled together to make one application. |

```tessera
record IndexPair
    left  : USize
    right : USize

routine two_sum(%list: Ptr<List<S32>>, %target: S32) -> Option<IndexPair>
    block entry():
        jump search_left(0)

    block search_left(%left_idx: USize):
        branch %left_idx.ge(%list.length.load())
            ? return(.Absent)
            : search_right(%left_idx, %left_idx.add(1))

    block search_right(%left_idx: USize, %right_idx: USize):
        branch %right_idx.ge(%list.length.load()) ? search_left(%left_idx.add(1)) : continue
        %left_val  : S32 = %list.get(%left_idx)
        %right_val : S32 = %list.get(%right_idx)
        branch %left_val.add(%right_val).eq(%target)
            ? return(.Present(IndexPair { left: %left_idx, right: %right_idx }))
            : search_right(%left_idx, %right_idx.add(1))
```

Two blocks make the two loops of an O(n²) search. Each loop's state travels as block parameters, `continue` goes on
with the next line when the inner loop isn't done, and the list is held by pointer: `%list.length` is the address of
a field, read with `load`, while `get` takes the pointer itself.

What sets it apart:

- **No operators.** Every step is a named call, so `add` and `add_wrap`, or `shr` on a signed and an unsigned value,
  never look alike.
- **Block parameters instead of phi nodes.** A loop passes its state forward (`search_right(%left_idx,
  %right_idx.add(1))`) instead of collecting it from predecessors.
- **`=` only binds.** Memory is read and written by `load` and `store` calls, and every binding states its type.
- **Sum types are variants.** `Option` and `Result` are variants, read with a `when` whose arms bind the payload.
- **Nothing hidden.** No implicit conversions, destructors, exceptions, vtables, or allocations: every runtime
  operation is written in the source.

> **Status:** the language is still changing, and the compiler works but is incomplete. Modules scope name lookup
> (`import`, qualified paths, `private` / `internal`; `Standard::Core` is always imported), but a solution manifest,
> interface modules, and the same name in two modules aren't there yet: every file on the command line, plus the
> stdlib, forms one solution.

## Repository

| Path | Contents |
|------|----------|
| `Tessera/` | The compiler, in C#. It emits LLVM IR text and links through `clang`. |
| `stdlib/` | The standard library, written in Tessera. It's the source of truth for the current design. |
| `tests/` | Golden tests for the compiler. |
| `examples/` | Introductory programs, one idea each: FizzBuzz, binary search, a Caesar cipher, a prime sieve, word count, and more. |
| `playground/` | Larger programs: Dijkstra, a lazy segment tree, SHA-256, a calculator, and more. |
| `Tessera.tmbundle/` | A TextMate grammar for syntax highlighting. |

The language reference lives in the [docs](https://tessera.lumi-dev.xyz): start with
[Introduction](https://tessera.lumi-dev.xyz/Introduction/) and
[Quick Start](https://tessera.lumi-dev.xyz/Quick-Start/).

If you're an AI assistant writing Tessera, read [TESSERA-FOR-AI.md](TESSERA-FOR-AI.md) first: it's a one-page summary of the
rules that are easy to get wrong.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/) and clang 21 or newer on `PATH`.

```sh
dotnet run --project Tessera -- run tests/hello.tess          # build and run a program
dotnet run --project Tessera -- build prog.tess -o prog       # build an executable
dotnet run --project Tessera -- build prog.tess --emit-llvm   # write LLVM IR instead
dotnet run --project Tessera -- test tests playground examples  # run the golden tests
dotnet run --project Tessera -- fmt stdlib tests examples playground          # format the sources
```

`build` and `run` also take `--target <arch-os-abi>` and `-O`. All files on one command line form one solution,
and the stdlib is compiled in with them.

## Tests

Each test in `tests/` is `<name>.tess` with one of:

- `<name>.expected`: the program's stdout
- `<name>.exit`: its exit code (default 0)
- `<name>.error`: text the compile error must contain

The programs in `examples/` and `playground/` carry `<name>.expected` files too, so they run as tests alongside `tests/`.
