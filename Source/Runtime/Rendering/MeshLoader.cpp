#include "MeshLoader.h"
#include <fstream>
#include <sstream>
#include <string>
#include <unordered_map>
#include <cmath>
#include <algorithm>

// ── Simple 3-component float vector ────────────────────────────────────────
struct float3 { float x = 0, y = 0, z = 0; };

static float3 cross3(float3 a, float3 b)
{
    return { a.y * b.z - a.z * b.y,
             a.z * b.x - a.x * b.z,
             a.x * b.y - a.y * b.x };
}

static float3 normalize3(float3 v)
{
    float len = sqrtf(v.x * v.x + v.y * v.y + v.z * v.z);
    if (len < 1e-7f) return { 0, 1, 0 };
    return { v.x / len, v.y / len, v.z / len };
}

// ── OBJ face corner: indices into the per-type arrays ──────────────────────
struct FaceCorner { int vi, ni; }; // vertex index, normal index (-1 = absent)

// Parse one face corner token: "v", "v/vt", "v/vt/vn", "v//vn"
static FaceCorner ParseCorner(const std::string& token)
{
    FaceCorner fc{ 0, -1 };
    auto s1 = token.find('/');
    fc.vi = std::stoi(token) - 1;
    if (s1 != std::string::npos)
    {
        auto s2 = token.rfind('/');
        if (s2 != s1 && s2 + 1 < token.size())
            fc.ni = std::stoi(token.substr(s2 + 1)) - 1;
    }
    return fc;
}

bool LoadOBJ(const char* path,
             std::vector<MeshVertex>& outVertices,
             std::vector<uint32_t>&  outIndices)
{
    std::ifstream file(path);
    if (!file.is_open()) return false;

    std::vector<float3> pos, nrm;
    std::vector<FaceCorner> faceCorners; // every 3 = one triangle
    bool hasNormals = false;

    std::string line;
    while (std::getline(file, line))
    {
        if (line.empty() || line[0] == '#') continue;
        std::istringstream ss(line);
        std::string tok;
        ss >> tok;

        if (tok == "v")
        {
            float3 p; ss >> p.x >> p.y >> p.z;
            pos.push_back(p);
        }
        else if (tok == "vn")
        {
            float3 n; ss >> n.x >> n.y >> n.z;
            nrm.push_back(n);
            hasNormals = true;
        }
        else if (tok == "f")
        {
            // Triangulate polygon fan: (0,1,2), (0,2,3), ...
            std::vector<FaceCorner> poly;
            std::string fv;
            while (ss >> fv) poly.push_back(ParseCorner(fv));
            for (size_t i = 1; i + 1 < poly.size(); i++)
            {
                faceCorners.push_back(poly[0]);
                faceCorners.push_back(poly[i]);
                faceCorners.push_back(poly[i + 1]);
            }
        }
    }

    if (pos.empty() || faceCorners.empty()) return false;

    // ── Compute smooth vertex normals if OBJ has none ──────────────────────
    std::vector<float3> smoothNormals(pos.size());
    if (!hasNormals)
    {
        for (size_t i = 0; i < faceCorners.size(); i += 3)
        {
            auto& p0 = pos[faceCorners[i + 0].vi];
            auto& p1 = pos[faceCorners[i + 1].vi];
            auto& p2 = pos[faceCorners[i + 2].vi];
            float3 e1{ p1.x - p0.x, p1.y - p0.y, p1.z - p0.z };
            float3 e2{ p2.x - p0.x, p2.y - p0.y, p2.z - p0.z };
            float3 fn = cross3(e1, e2);
            for (int j = 0; j < 3; j++)
            {
                auto& sn = smoothNormals[faceCorners[i + j].vi];
                sn.x += fn.x; sn.y += fn.y; sn.z += fn.z;
            }
        }
    }

    // ── Deduplicate corners into a shared vertex buffer ─────────────────────
    // Key: pack (vi, ni) into a uint64 for O(1) lookup
    std::unordered_map<uint64_t, uint32_t> cache;
    outVertices.clear();
    outIndices.clear();

    for (auto& [vi, ni] : faceCorners)
    {
        uint64_t key = ((uint64_t)(uint32_t)vi << 32) | (uint32_t)ni;
        auto it = cache.find(key);
        if (it != cache.end())
        {
            outIndices.push_back(it->second);
            continue;
        }

        MeshVertex mv{};
        mv.x = pos[vi].x; mv.y = pos[vi].y; mv.z = pos[vi].z;

        if (hasNormals && ni >= 0 && ni < (int)nrm.size())
        {
            mv.nx = nrm[ni].x; mv.ny = nrm[ni].y; mv.nz = nrm[ni].z;
        }
        else
        {
            float3 n = normalize3(smoothNormals[vi]);
            mv.nx = n.x; mv.ny = n.y; mv.nz = n.z;
        }

        uint32_t idx = (uint32_t)outVertices.size();
        outVertices.push_back(mv);
        cache[key] = idx;
        outIndices.push_back(idx);
    }

    return !outVertices.empty();
}
