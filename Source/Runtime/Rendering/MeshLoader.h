#pragma once
#include <vector>
#include <cstdint>

struct MeshVertex
{
    float x, y, z;    // position
    float nx, ny, nz; // normal
};

// Loads a Wavefront OBJ file into an interleaved vertex+normal buffer
// and a 32-bit index buffer.  Returns false on I/O or parse error.
bool LoadOBJ(const char* path,
             std::vector<MeshVertex>& outVertices,
             std::vector<uint32_t>&  outIndices);
