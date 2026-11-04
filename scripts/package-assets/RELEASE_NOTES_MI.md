# Mielia v0.1.0 — preview

The first public preview of **Mielia**, Revare's sharing-first sister language. Where Revare is
ownership-first (someone owns a value; sharing is opt-in), Mielia is **sharing-first**: values are shared
by default and their memory is managed for you. Revare's low-level surface — ownership transfer,
manual teardown, iterator/reshaping rules, the danger gate — is deliberately **invisible** in Mielia
unless it earns its place at Mielia's altitude.

This is a **preview**: single-threaded Mielia programs run end-to-end today. Concurrency (a multi-threaded
collector) is the next milestone and is intentionally out of scope here — see **Scope & limits** below.

The builder is the same binary as Revare. Run Mielia with the `mielia` command (or `mi`), or by
handing any `.mie` file to `revare`:

```
mielia hello.mie
mielia version      # Mielia v0.1.0
```

## 🌊 The model: `Roamed`

Every Mielia `entity` is a **shared, memory-managed value**. Under the hood it lowers to `Roamed` — a
**biased reference count** (fast path for the common single-owner case) that **auto-promotes when a value
escapes**, with a **cycle collector** to reclaim reference cycles the count alone can't. You never write
`steal`, `hold`, `destroy`, or a wrapper type — you just share values and let go of them.

- **`entity` vs `record` is value-vs-shared:** a `record` is copied, an `entity` is shared (not copied).
  That is the whole distinction — no ownership ceremony on top.
- **Nullability** is part of the type surface.
- **Display transparency:** a `Roamed` value represents, diagnoses, and compares as its inner value —
  the wrapper is invisible in output and operators.

## 🔢 Numbers

Mielia's defaults are chosen for clarity, not machine width.

- **`Int`** is the default for unsuffixed integer literals (arbitrary width, no silent overflow
  surprises).
- **`Decimal`** is base-10 (not binary `F64`) — the default for fractional literals.
- Both are reused from Revare's standard library; Mielia keeps RA's `S64`/`F64` literal defaults only
  inside the shared stdlib bodies.

## 📚 Standard library

Mielia reuses Revare's `Core` **wholesale** — `List`, `Dict`, `Set`, `Text`, and the rest are the
real RA Core types, resolved through Mielia's world-line so a bare `List` in `.mie` *is* `RA::Core.List`,
roamed per instance and reached through display/operator transparency. There is no forked collections
library to drift.

- The **cycle collector traces into container element buffers** — `List` elements and `Dict`/`Set`
  key/value buffers are visible to collection, so cycles through collections are reclaimed (not leaked as
  opaque leaves).

## 🧪 Equivalence testing

A dedicated `.mie` fixture harness runs Mielia programs end-to-end and **locks RA/MI equivalence**: the
same program expressed in both languages must produce the same output. This is how the shared-stdlib
reuse stays honest.

## 🎯 Scope & limits

This is an honest **0.1 preview**. What works, and what does not yet:

- ✅ **Single-threaded programs run end-to-end** — hello-world through to real programs.
- ✅ Reused RA number model + `Core` stdlib, `entity`→`Roamed` lowering, nullability, single-thread cycle
  collection, container roam-trace.
- ⛔ **Concurrency is not ready.** The cycle collector and mutators can race, so a multi-threaded Mielia
  program can hit use-after-free. Mielia 0.1 is **single-thread only**; a thread-safe collector is the
  headline of the next milestone.
- ⛔ **Runtime object manipulation / reflection** (`ObjectHacker`) and **hot reload / REPL** are planned
  for later versions, not this one.

Mielia hides memory *management*, not memory *finiteness*: out-of-memory and other fatal resource walls
are loud, uncatchable crashes, distinct from recoverable failures you handle with `try_` / `when`.

## ✅ Tests

The `.mie` fixture harness is green and wired into CI alongside Revare's suite (**1,511 unit +
analyzer tests**, **188 stdlib fixtures**), including the RA/MI equivalence locks. CI green on Linux,
macOS, and Windows.
