#pragma once
#include <cstdint>
#include <cstddef>

// ── Fundamental integer aliases (mirrors FlaxEngine's Core.h style) ────────
using int8   = int8_t;
using int16  = int16_t;
using int32  = int32_t;
using int64  = int64_t;
using uint8  = uint8_t;
using uint16 = uint16_t;
using uint32 = uint32_t;
using uint64 = uint64_t;
using byte   = uint8_t;

// ── Compile-time helpers ────────────────────────────────────────────────────
#define JZRE_NONCOPYABLE(T) T(const T&) = delete; T& operator=(const T&) = delete
#define JZRE_FORCE_INLINE   __forceinline
#define JZRE_ARRAY_SIZE(a)  (sizeof(a) / sizeof((a)[0]))
