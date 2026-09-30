cmake_minimum_required(VERSION 3.20)
project(RazorForgeNative C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)

# Export all symbols on Windows (generates .lib import library)
set(CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS ON)

# Platform-specific library suffix
if(WIN32)
    set(LIB_SUFFIX ".dll")
    set(LIB_PREFIX "")
elseif(APPLE)
    set(LIB_SUFFIX ".dylib")
    set(LIB_PREFIX "lib")
else()
    set(LIB_SUFFIX ".so")
    set(LIB_PREFIX "lib")
endif()

# Include directories
include_directories(include)

# ============================================================================
# Third-party libraries — Core (always needed)
# ============================================================================

# (No arbitrary-precision C library: decNumber, TLFloat and LibBF are all retired. D32/D64/D128,
#  B128, Integer and Real are pure-RF software engines (Integer and Real are a port of LibBF's
#  algorithms), B128 text parsing/formatting is pure RF (FloatConvert.rf), and the builder encodes
#  B128/Integer literals in managed C# (NumericLiteralParser.EncodeB128). Algorithm credit retained
#  in docs/licenses.)

# ============================================================================
# Third-party libraries (the runtime links libco and libuv; a stdlib module that needs another C
# library adds it here when it is written)
# ============================================================================

# libco - Stackful coroutine/fiber backend
# https://github.com/rxi/libco or compatible fork (public domain / ISC-like depending on fork)
include(cmake/libco.cmake)

# libuv - Cross-platform async I/O and event loop
# https://github.com/libuv/libuv (MIT)
include(cmake/libuv.cmake)


# ============================================================================
# Compiler-RT shim for 128-bit integer operations (Windows)
# The runtime uses __int128 (rf_divide.c) which requires __udivti3 etc. from compiler-rt/libgcc
# ============================================================================
if(WIN32)
    add_library(compiler_rt_shim STATIC
        runtime/compiler_rt_shim.c
    )
    # Ensure these symbols are exported
    if(CMAKE_C_COMPILER_ID STREQUAL "Clang" OR CMAKE_C_COMPILER_ID STREQUAL "GNU")
        target_compile_options(compiler_rt_shim PRIVATE -fvisibility=default)
    endif()
endif()

# ============================================================================
# Runtime library for memory management, error handling, and math
# ============================================================================
add_library(razorforge_runtime SHARED
    runtime/runtime_init.c
    runtime/cstring_runtime.c
    runtime/console_runtime.c
    runtime/duration_wait.c
    runtime/crash_runtime.c
    runtime/shared_locks.c
    runtime/memory.c
    runtime/stacktrace.c
    runtime/moment.c
    runtime/math_functions.c
    runtime/rf_divide.c
    runtime/text_functions.c
    runtime/file_functions.c
    runtime/task_runtime.c
    runtime/concurrency_context.c
    runtime/coro_runtime.c
    runtime/async_io.c
    runtime/channel_runtime.c
    runtime/signal_runtime.c
    runtime/process_signals.c
    runtime/random.c
    runtime/emutls_runtime.c
    runtime/ffi_callback_test.c
    runtime/types.h
)

target_include_directories(razorforge_runtime PUBLIC include)

# Link math library
if(UNIX AND NOT APPLE)
    target_link_libraries(razorforge_runtime m)
endif()

# Link DbgHelp for stack trace symbol resolution on Windows
if(WIN32)
    target_link_libraries(razorforge_runtime dbghelp)
endif()

# On Windows, compile the compiler-rt shim (128-bit integer division builtins) straight into the
# runtime DLL. Linking it as a static library would only pull its object in when something in the
# DLL itself references __udivti3 etc., and then the builtins would be missing from the export
# table the in-process ORC JIT resolves against.
if(WIN32)
    target_sources(razorforge_runtime PRIVATE runtime/compiler_rt_shim.c)
    if(CMAKE_C_COMPILER_ID STREQUAL "Clang" OR CMAKE_C_COMPILER_ID STREQUAL "GNU")
        set_source_files_properties(runtime/compiler_rt_shim.c PROPERTIES COMPILE_OPTIONS -fvisibility=default)
    endif()
endif()

# Link libco if available
if(HAVE_LIBCO)
    target_compile_definitions(razorforge_runtime PRIVATE HAVE_LIBCO)
    target_link_libraries(razorforge_runtime rf_libco)
endif()

# Link libuv if available
if(HAVE_LIBUV)
    target_compile_definitions(razorforge_runtime PRIVATE HAVE_LIBUV)
    target_link_libraries(razorforge_runtime uv_a)
endif()


# Set output directory
set_target_properties(razorforge_runtime PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
    LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
    ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
)

# ============================================================================
# Math library (standalone, for static linking)
# ============================================================================
add_library(razorforge_math STATIC
    runtime/math_functions.c
    runtime/rf_divide.c
)

target_include_directories(razorforge_math PUBLIC include)

if(UNIX AND NOT APPLE)
    target_link_libraries(razorforge_math m)
endif()

# On Windows, link the compiler-rt shim for 128-bit integer operations
if(WIN32)
    target_link_libraries(razorforge_math compiler_rt_shim)
endif()

set_target_properties(razorforge_math PROPERTIES
    ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
)

# ============================================================================
# Install targets
# ============================================================================
install(TARGETS razorforge_runtime razorforge_math
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)

install(FILES include/razorforge_math.h
    DESTINATION include
)

install(FILES include/razorforge_runtime.h
    DESTINATION include
)

# Print configuration summary
message(STATUS "")
message(STATUS "RazorForge Native Configuration:")
message(STATUS "  ── Modules ──")
message(STATUS "  libco (green-thread contexts):    ${HAVE_LIBCO}")
message(STATUS "  libuv (async I/O loop):           ${HAVE_LIBUV}")
message(STATUS "  ── Platform ──")
message(STATUS "  Platform: ${CMAKE_SYSTEM_NAME}")
message(STATUS "  Compiler: ${CMAKE_C_COMPILER_ID} ${CMAKE_C_COMPILER_VERSION}")
message(STATUS "")
