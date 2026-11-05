#!/usr/bin/env bash
# Revare packaging script (Unix host -> host-RID package).
#
# Produces: dist/revare-v<version>-<rid>.tar.gz (+ sha256 in checksums.txt)
# where <rid> is the host's RID: linux-x64, osx-arm64, or osx-x64.
#
# IMPORTANT: packages the HOST platform only. The native runtime (revare_runtime)
# is built by the host toolchain, and `dotnet publish -r <foreign-rid>` would silently
# bundle the host's native binaries into a foreign-platform archive. Build the Windows
# package on Windows with scripts/package.ps1.
#
# Prerequisites: .NET 10 SDK; clang + cmake + ninja (native runtime build); the
# vendored native sources under native/ (CI fetches them; dev checkouts have them).
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

case "$(uname -s)" in
    Linux)  RID=linux-x64 ;;
    Darwin) if [[ "$(uname -m)" == "arm64" ]]; then RID=osx-arm64; else RID=osx-x64; fi ;;
    *)
        echo "package.sh packages Linux/macOS hosts; use scripts/package.ps1 on Windows." >&2
        exit 1
        ;;
esac
VERSION="${VERSION:-$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Revare/Revare.csproj | head -1)}"
NAME="revare-v${VERSION}-${RID}"
OUT="dist/${NAME}"

echo "=== Revare ${VERSION} -> ${NAME} ==="
rm -rf "$OUT" "dist/${NAME}.tar.gz"
mkdir -p dist

echo "=== build the Lapila builder ==="
# Ingrid's runtime has code written in Lapila (Ingrid/runtime-lapila): its native build finds the Lapila
# builder at Lapila/bin/Release/net10.0/lapila.dll.
dotnet build Lapila/Lapila.csproj -c Release --verbosity minimal

echo "=== publish the Lapila builder (NativeAOT) ==="
# Lapila ships as one native executable with its standard library, boards and Ingrid's Lapila library beside it
# (the layout StdlibDir and FindIngrid look for from the executable's folder). Its own archive: the Revare
# package carries the Lapila backend inside the builders and needs no separate lapila executable.
LAPILA_VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Lapila/Lapila.csproj | head -1)"
LAPILA_NAME="lapila-v${LAPILA_VERSION}-${RID}"
LAPILA_OUT="dist/${LAPILA_NAME}"
rm -rf "$LAPILA_OUT" "dist/${LAPILA_NAME}.tar.gz" "dist/_lapila-publish"
dotnet publish Lapila/Lapila.csproj -c Release -r "$RID" -o "dist/_lapila-publish" --verbosity minimal
mkdir -p "$LAPILA_OUT"
cp "dist/_lapila-publish/lapila" "$LAPILA_OUT/"
cp -a Lapila/Standard "$LAPILA_OUT/Standard"
cp -a Lapila/boards "$LAPILA_OUT/boards"
cp -a Ingrid/lapila "$LAPILA_OUT/Ingrid"
cp Lapila/LICENSE Lapila/README.md "$LAPILA_OUT/"
rm -rf "dist/_lapila-publish"
# The packaged executable builds and runs a program from a folder away from every checkout.
LAPILA_SMOKE=$(mktemp -d)
cat > "$LAPILA_SMOKE/smoke.lpa" <<'EOF'
import Hosted

routine start() -> None
    Console.show("packaged lapila works")
    return
EOF
LAPILA_STAGE=$(mktemp -d)
cp -a "$LAPILA_OUT/." "$LAPILA_STAGE/"
if ! (cd "$LAPILA_SMOKE" && "$LAPILA_STAGE/lapila" run smoke.lpa 2>&1 | grep -q "packaged lapila works"); then
    echo "packaged lapila smoke failed" >&2
    exit 1
fi
rm -rf "$LAPILA_SMOKE" "$LAPILA_STAGE"
tar -C dist -czf "dist/${LAPILA_NAME}.tar.gz" "$LAPILA_NAME"
if command -v sha256sum >/dev/null 2>&1; then
    (cd dist && sha256sum "${LAPILA_NAME}.tar.gz" >> checksums.txt)
else
    (cd dist && shasum -a 256 "${LAPILA_NAME}.tar.gz" >> checksums.txt)
fi
echo "Packaged: dist/${LAPILA_NAME}.tar.gz"

echo "=== build Ingrid's native runtime ==="
(cd Ingrid/native && bash build.sh)

echo "=== dotnet publish revare + mielia (NativeAOT, one directory) ==="
# Both command lines share Anvila and the runtime, so they publish into the same directory. NativeAOT: each command line is
# one native executable, so a one-shot build does not spend its first second starting the .NET runtime and compiling the
# builder. The native runtime, the standard libraries and the LLVM tools are copied below, publish does not carry them.
dotnet publish Revare/Revare.csproj -c Release -r "$RID" -p:PublishAot=true \
    -o "$OUT" --verbosity minimal
dotnet publish Mielia/Mielia.csproj -c Release -r "$RID" -p:PublishAot=true \
    -o "$OUT" --verbosity minimal
# The prebuilt standard library snapshot is made and read by managed builders only (PbrfSerializer.IsSupported).
rm -rf "$OUT/Standard/.prebuilt"

echo "=== dotnet publish the managed daemons (self-contained, $OUT/daemon) ==="
# The NativeAOT command line starts the daemon from here when it is there (BuilderLocation.DaemonFolder). Managed code
# under the JIT analyzes about 1.6 times as fast as the NativeAOT build once the daemon is warm, and the daemon runs for
# the whole session. Self-contained, so no .NET runtime needs to be installed. It reads the standard library, the tools
# and Ingrid from the folder above (BuilderLocation.HomeEnvVar), so the large files both folders would hold are links.
dotnet publish Revare/Revare.csproj -c Release -r "$RID" --self-contained true \
    -o "$OUT/daemon" --verbosity minimal
dotnet publish Mielia/Mielia.csproj -c Release -r "$RID" --self-contained true \
    -o "$OUT/daemon" --verbosity minimal
for shared in libLLVM.so libLLVM.dylib libLLVMSharp.so libLLVMSharp.dylib lapila-stdlib Standard; do
    if [ -e "$OUT/daemon/$shared" ] && [ -e "$OUT/$shared" ]; then
        rm -rf "${OUT:?}/daemon/$shared"
        ln -s "../$shared" "$OUT/daemon/$shared"
    fi
done

echo "=== native runtime artifacts (installed layout) ==="
# The installed layout — and the toolchain's own P/Invoke probing + buildandrun's prebuilt-layout
# detection — expect them FLAT next to the executables, matching the dev bin/ output.
for sub in Ingrid/native/build/bin Ingrid/native/build/lib; do
    if [ -d "$sub" ]; then
        cp -a "$sub"/. "$OUT/"
    fi
done

echo "=== Ingrid's Lapila library (installed layout) ==="
# Every build links ingrid_lapila.bc (made from ingrid_lapila.ll) from next to the executable. An installed layout
# has no Lapila sources to compile it from, so the package ships both: the packaged builder compiles them here,
# finding Ingrid/lapila up the tree.
"$OUT/Revare" emit-ingrid
test -f "$OUT/ingrid_lapila.ll"
test -f "$OUT/ingrid_lapila.bc"

echo "=== prune dev-only artifacts ==="
rm -rf "$OUT/Revare-Wiki" "$OUT/Mielia-Wiki"
find "$OUT" -name '*.pdb' -delete
cp Revare/LICENSE Revare/README.md Revare/THIRD-PARTY-NOTICES.md "$OUT/"

# Short alias next to the canonical binary. (Users preferring `forge` can alias it
# themselves — the name is deliberately not shipped to avoid colliding with
# Foundry's `forge`.)
ln -sf Revare "$OUT/rv"

# Mielia has its own command line; `mielia` and `mi` point at it.
ln -sf Mielia "$OUT/mielia"
ln -sf Mielia "$OUT/mi"

echo "=== bundle self-contained LLVM toolchain ==="
# The builder resolves <package>/toolchain/bin before PATH (ResolveToolchainTool),
# and uses that toolchain's ld.lld/ld64.lld for linking. Linux still needs the
# host's libc dev files (crt1.o); macOS needs the Command Line Tools SDK stubs —
# both documented in QUICKSTART.md.
LLVM_VERSION=22.1.7
CACHE=dist/_cache
mkdir -p "$CACHE"
case "$RID" in
    linux-x64)  LLVM_ASSET="LLVM-${LLVM_VERSION}-Linux-X64" ;;
    osx-arm64)  LLVM_ASSET="LLVM-${LLVM_VERSION}-macOS-ARM64" ;;
    *)          LLVM_ASSET="" ;;
esac
if [[ -n "$LLVM_ASSET" ]]; then
    TARBALL="$CACHE/${LLVM_ASSET}.tar.xz"
    if [[ ! -f "$TARBALL" ]]; then
        curl -L -o "$TARBALL" "https://github.com/llvm/llvm-project/releases/download/llvmorg-${LLVM_VERSION}/${LLVM_ASSET}.tar.xz"
    fi
    LLVM_ROOT="$CACHE/${LLVM_ASSET}"
    if [[ ! -d "$LLVM_ROOT/bin" ]]; then
        tar -xf "$TARBALL" -C "$CACHE"
    fi

    TC="$OUT/toolchain"
    mkdir -p "$TC/bin" "$TC/lib/clang"
    # -L: follow symlinks so `clang` is the real binary, not a dangling link.
    for tool in clang opt llvm-link lld; do
        cp -L "$LLVM_ROOT/bin/$tool" "$TC/bin/"
    done
    # lld dispatches on argv[0]; clang looks for ld.lld (ELF) / ld64.lld (Mach-O)
    # next to itself when given -fuse-ld=lld.
    (cd "$TC/bin" && ln -sf lld ld.lld && ln -sf lld ld64.lld)
    # Shared libraries the tools load (no-ops for static builds).
    cp -a "$LLVM_ROOT"/lib/libLLVM*.so* "$TC/lib/" 2>/dev/null || true
    cp -a "$LLVM_ROOT"/lib/libclang-cpp*.so* "$TC/lib/" 2>/dev/null || true
    cp -a "$LLVM_ROOT"/lib/libLLVM*.dylib "$TC/lib/" 2>/dev/null || true
    cp -a "$LLVM_ROOT"/lib/libclang-cpp*.dylib "$TC/lib/" 2>/dev/null || true

    # The official Linux LLVM binaries are not fully static. Carry every
    # non-baseline dependency reported by ldd beside the bundled LLVM libraries
    # and make package creation fail if any dependency remains unresolved.
    if [[ "$RID" == "linux-x64" ]]; then
        is_baseline_linux_lib() {
            case "$(basename "$1")" in
                linux-vdso.so.*|ld-linux-x86-64.so.*|libc.so.*|libm.so.*|libpthread.so.*|libdl.so.*|librt.so.*|libresolv.so.*)
                    return 0
                    ;;
                *)
                    return 1
                    ;;
            esac
        }

        copy_linux_tool_deps() {
            local tool="$1"
            LD_LIBRARY_PATH="$LLVM_ROOT/lib:$TC/lib:${LD_LIBRARY_PATH:-}" ldd "$tool" |
                awk '
                    /=>/ && $3 ~ /^\// { print $3 }
                    /^[[:space:]]*\// { print $1 }
                ' |
                while read -r dep; do
                    [[ -n "$dep" && -e "$dep" ]] || continue
                    if is_baseline_linux_lib "$dep"; then
                        continue
                    fi
                    cp -L "$dep" "$TC/lib/$(basename "$dep")"
                done
        }

        for tool in clang opt llvm-link lld; do
            copy_linux_tool_deps "$LLVM_ROOT/bin/$tool"
        done
    fi
    # clang resource dir: compiler-rt builtins (--rtlib=compiler-rt) live here.
    cp -a "$LLVM_ROOT/lib/clang/${LLVM_VERSION%%.*}" "$TC/lib/clang/"
    find "$TC/lib/clang" -type d -name include -exec rm -rf {} + 2>/dev/null || true

    if [[ "$RID" == "linux-x64" ]]; then
        for tool in "$TC/bin/clang" "$TC/bin/opt" "$TC/bin/lld" "$TC/bin/ld.lld"; do
            if LD_LIBRARY_PATH="$TC/lib:${LD_LIBRARY_PATH:-}" ldd "$tool" | grep -q "not found"; then
                LD_LIBRARY_PATH="$TC/lib:${LD_LIBRARY_PATH:-}" ldd "$tool" >&2 || true
                echo "Bundled LLVM tool has unresolved shared-library dependencies: $tool" >&2
                exit 1
            fi
        done
    fi
fi

echo "=== add install script + quickstart + AI reference ==="
cp Revare/scripts/package-assets/install.sh Revare/scripts/package-assets/QUICKSTART.md Revare/REVARE-FOR-AI.md "$OUT/"
chmod +x "$OUT/install.sh"

echo "=== smoke test: self-contained buildandrun (system toolchain hidden) ==="
"$OUT/Revare" version
RV_ABS="$(cd "$OUT" && pwd)/Revare"
SMOKE_DIR=$(mktemp -d)
cat > "$SMOKE_DIR/smoke.rvr" <<'EOF'
module PackageSmoke

import IO/Console

routine start()
    show("packaged revare works")
    return
EOF
# Stage a copy of the package OUTSIDE the repo (buildandrun's dev-checkout
# detection walks up from the exe and would find the repo's native/build tree
# from dist/), then run from the smoke dir with a minimal PATH — the end-user
# situation. The bundled toolchain must carry the build; only the OS linker
# prerequisites (libc dev files / CLT) come from the system.
STAGE_DIR=$(mktemp -d)
cp -a "$OUT/." "$STAGE_DIR/"
RV_ABS="$STAGE_DIR/Revare"
SMOKE_OUT=$( cd "$SMOKE_DIR" && PATH="/usr/bin:/bin" "$RV_ABS" buildandrun smoke.rvr 2>&1 ) || {
    echo "$SMOKE_OUT"
    echo "self-contained buildandrun smoke failed"
    rm -rf "$STAGE_DIR"
    exit 1
}
rm -rf "$STAGE_DIR"
if ! grep -q "packaged revare works" <<< "$SMOKE_OUT"; then
    echo "$SMOKE_OUT"
    echo "self-contained buildandrun smoke failed (wrong output)"
    exit 1
fi
echo "self-contained buildandrun OK"
rm -rf "$SMOKE_DIR"

echo "=== archive + checksum ==="
tar -C dist -czf "dist/${NAME}.tar.gz" "$NAME"
# macOS has no sha256sum; shasum -a 256 emits the same format.
if command -v sha256sum >/dev/null 2>&1; then
    (cd dist && sha256sum "${NAME}.tar.gz" >> checksums.txt)
else
    (cd dist && shasum -a 256 "${NAME}.tar.gz" >> checksums.txt)
fi
echo "Packaged: dist/${NAME}.tar.gz"
