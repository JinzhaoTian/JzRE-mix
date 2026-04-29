#pragma once

// ── Platform detection ────────────────────────────────────────────────────────
#if defined(_WIN32) || defined(_WIN64)
    #define JzRE_PLATFORM_WINDOWS 1
    #if defined(_WIN64)
        #define JzRE_PLATFORM_64BIT 1
    #endif
#elif defined(__APPLE__)
    #define JzRE_PLATFORM_MACOS 1
    #include <TargetConditionals.h>
    #if TARGET_OS_IPHONE
        #define JzRE_PLATFORM_IOS 1
    #endif
#elif defined(__linux__) || defined(__unix__)
    #define JzRE_PLATFORM_LINUX 1
    #if defined(__x86_64__) || defined(__aarch64__)
        #define JzRE_PLATFORM_64BIT 1
    #endif
#else
    #error "Unknown platform"
#endif

// ── Compiler detection ────────────────────────────────────────────────────────
#if defined(_MSC_VER)
    #define JzRE_COMPILER_MSVC 1
#elif defined(__clang__)
    #define JzRE_COMPILER_CLANG 1
#elif defined(__GNUC__)
    #define JzRE_COMPILER_GCC 1
#else
    #error "Unknown compiler"
#endif

// ── Architecture ──────────────────────────────────────────────────────────────
#if defined(__x86_64__) || defined(_M_X64) || defined(__aarch64__) || defined(_M_ARM64)
    #define JzRE_ARCH_64BIT 1
#endif

// ── DLL export / import (MSVC vs GCC/Clang visibility) ───────────────────────
#if JzRE_PLATFORM_WINDOWS
    #ifdef JzRE_RUNTIME_EXPORTS
        #define JzRE_EXPORT __declspec(dllexport)
    #else
        #define JzRE_EXPORT __declspec(dllimport)
    #endif
    #define JzRE_IMPORT __declspec(dllimport)
#else
    #define JzRE_EXPORT __attribute__((visibility("default")))
    #define JzRE_IMPORT
#endif

// ── Force inline (portable) ───────────────────────────────────────────────────
#if JzRE_COMPILER_MSVC
    #define JzRE_FORCE_INLINE __forceinline
#elif JzRE_COMPILER_CLANG || JzRE_COMPILER_GCC
    #define JzRE_FORCE_INLINE inline __attribute__((always_inline))
#else
    #define JzRE_FORCE_INLINE inline
#endif

// ── Non-copyable helper ───────────────────────────────────────────────────────
#define JzRE_NONCOPYABLE(T)  T(const T&) = delete; T& operator=(const T&) = delete
#define JzRE_ARRAY_SIZE(a)   (sizeof(a) / sizeof((a)[0]))
