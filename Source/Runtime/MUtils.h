#pragma once
#include <string>
#include <vector>
#include <cstdint>
#include <cstring>

// ── Marshalling utilities for C++ ↔ C# data conversion ─────────────────────
// Mirrors FlaxEngine's MUtils — provides helpers for converting between
// native types and the representations used by the managed interop bridge.
//
// These are used by internal call implementations (EngineInternalCalls.cpp)
// and the generated C++ glue code (Phase 3).

namespace MUtils
{
    // ── String conversion ──────────────────────────────────────────────
    // Managed strings arrive as UTF-8 const char*.  To pass strings back
    // to managed code we return malloc'd UTF-8 buffers that the C# side
    // frees via Marshal.FreeCoTaskMem.

    inline std::string ToStdString(const char* utf8)
    {
        return utf8 ? std::string(utf8) : std::string();
    }

    inline const char* ToManagedString(const std::string& s)
    {
        char* buf = (char*)std::malloc(s.size() + 1);
        if (buf)
        {
            std::memcpy(buf, s.c_str(), s.size() + 1);
        }
        return buf;
    }

    // ── Array conversion ───────────────────────────────────────────────
    // Managed arrays are passed as (pointer, length) pairs.  Use with
    // caution — the pointer is valid only for the duration of the call.

    template<typename T>
    inline std::vector<T> ToVector(const T* data, int length)
    {
        if (!data || length <= 0) return {};
        return std::vector<T>(data, data + length);
    }

    template<typename T>
    struct ManagedArray
    {
        T*      Data   = nullptr;
        int     Length = 0;
    };

    template<typename T>
    inline ManagedArray<T> ToManagedArray(const std::vector<T>& vec)
    {
        if (vec.empty()) return { nullptr, 0 };
        size_t bytes = vec.size() * sizeof(T);
        T* buf = (T*)std::malloc(bytes);
        if (buf)
            std::memcpy(buf, vec.data(), bytes);
        return { buf, (int)vec.size() };
    }
}
