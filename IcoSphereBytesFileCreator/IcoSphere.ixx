export module IcoSphere.IcoSphere;

import <iostream>;
import <cmath>;
import <string>;
import <map>;
import <vector>;
import Math.Vec3;
import Mesh.Tri;
import IcoSphere.Pack;
import IcoSphere.VertCache;

using std::cout;
using std::endl;
using std::map;
using std::vector;
using std::string;
using std::format;
using std::move;
using std::acos;
using std::sin;
using Math::Vec3;
using Mesh::Tri;
using IcoSphere::Pack;
using IcoSphere::VertCache;

namespace IcoSphere {
    // 生成由大量三角形组成的球体数据包
    // 参考: http://blog.andreaskahler.com/2009/06/creating-icosphere-mesh-in-code.html
    export class IcoSphere {
    private:
        static int GetSplitPoint(map<VertCache, int32_t>& cache, Pack& pack, int32_t v0, int32_t v1, int32_t t0, int32_t t1) {
            VertCache key{ v0, v1, t0, t1 };
            auto it = cache.find(key);
            if (it != cache.end()) {
                return it->second;
            }

            // not in cache, calculate it
            Vec3 p0 = pack.verts[v0];
            Vec3 p1 = pack.verts[v1];
            float theta = acos(p0.Dot(p1));
            float t = (t0 * 1.0f) / t1;
            Vec3 ps = sin((1 - t) * theta) / sin(theta) * p0 + sin(t * theta) / sin(theta) * p1;

            // add vertex makes sure point is on unit sphere
            pack.verts.push_back(ps);
            int32_t i = (int32_t)(pack.verts.size() - 1);
            cache[key] = i;
            return i;
        }

        static int GetTriMidPoint(map<VertCache, int32_t>& cache, Pack& pack, int32_t v0, int32_t v1, int32_t v2) {
            VertCache key{ v0, v1, v2 };
            auto it = cache.find(key);
            if (it != cache.end()) {
                return it->second;
            }

            // not in cache, calculate it
            Vec3 p0 = pack.verts[v0];
            Vec3 p1 = pack.verts[v1];
            Vec3 p2 = pack.verts[v2];
            Vec3 ps = ((p0 + p1 + p2) / 3.0f).Norm();

            // add vertex makes sure point is on unit sphere
            pack.verts.push_back(ps);
            int32_t i = (int32_t)(pack.verts.size() - 1);
            cache[key] = i;
            return i;
        }

    public:
        static inline const float GOLDEN_RATIO = (1.0f + sqrt(5.0f)) * 0.5f;

        static string GetFilePath(int recursion) {
            return format("Output/pack_arr_{}.bytes", recursion);
        }

        static Pack NewPackAndSave(int recursion) {
            Pack pack{};

            // 尝试从二进制文件中读取数据
            int readRecursion = recursion;
            for (; readRecursion >= 0; --readRecursion) {
                if (pack.Read(GetFilePath(readRecursion).c_str()) == true) {
                    break;
                }
            }

            // 如果读不到最基础的迭代0数据, 则生成迭代0数据, 并保存二进制文件
            if (readRecursion < 0) {
                pack = {};

                // create 12 vertices of a icosahedron
                const float t = GOLDEN_RATIO;
                pack.verts = {
                    { -1,  t,  0 }, { 1, t, 0 }, { -1, -t,  0 }, {  1, -t,  0 },
                    {  0, -1,  t }, { 0, 1, t }, {  0, -1, -t }, {  0,  1, -t },
                    {  t,  0, -1 }, { t, 0, 1 }, { -t,  0, -1 }, { -t,  0,  1 }
                };
                for (Vec3& v : pack.verts) {
                    v = v.Norm();
                }

                // create 20 triangles of the icosahedron
                pack.tris = {
                    { 0, 11, 5 }, { 0,  5,  1 }, {  0,  1,  7 }, {  0, 7, 10 }, { 0, 10, 11 }, // 5 faces around point 0
                    { 1,  5, 9 }, { 5, 11,  4 }, { 11, 10,  2 }, { 10, 7,  6 }, { 7,  1,  8 }, // 5 adjacent faces
                    { 3,  9, 4 }, { 3,  4,  2 }, {  3,  2,  6 }, {  3, 6,  8 }, { 3,  8,  9 }, // 5 faces around point 3
                    { 4,  9, 5 }, { 2,  4, 11 }, {  6,  2, 10 }, {  8, 6,  7 }, { 9,  8,  1 }  // 5 adjacent faces
                };

                // 推算毗邻数据
                pack.CalcAbuts();

                // 保存二进制文件
                pack.Save(GetFilePath(0).c_str());

                // 然后设置迭代数为0而不是负数
                readRecursion = 0;
            }

            // 曲面细分每个迭代并保存二进制文件
            for (int i = readRecursion; i < recursion; ++i) {
                map<VertCache, int32_t> cache{};
                vector<Tri> trisNew{};

                for (Tri tri : pack.tris) {
                    int32_t v0 = tri[0];
                    int32_t v1 = tri[1];
                    int32_t v2 = tri[2];

                    // 生成9个小三角形
                    //        v0
                    //       / \
                    //     c1---a0
                    //     / \ / \
                    //   c0---o---a1
                    //   / \ / \ / \
                    // v2--b1---b0--v1
                    int32_t a0 = GetSplitPoint(cache, pack, v0, v1, 1, 3);
                    int32_t a1 = GetSplitPoint(cache, pack, v0, v1, 2, 3);
                    int32_t b0 = GetSplitPoint(cache, pack, v1, v2, 1, 3);
                    int32_t b1 = GetSplitPoint(cache, pack, v1, v2, 2, 3);
                    int32_t c0 = GetSplitPoint(cache, pack, v2, v0, 1, 3);
                    int32_t c1 = GetSplitPoint(cache, pack, v2, v0, 2, 3);
                    int32_t o = GetTriMidPoint(cache, pack, v0, v1, v2);

                    trisNew.push_back({ v0, a0, c1 });
                    trisNew.push_back({ c1, a0,  o });
                    trisNew.push_back({ a0, a1,  o });
                    trisNew.push_back({ c1,  o, c0 });
                    trisNew.push_back({  o, b0, b1 });
                    trisNew.push_back({  o, a1, b0 });
                    trisNew.push_back({ c0,  o, b1 });
                    trisNew.push_back({ a1, v1, b0 });
                    trisNew.push_back({ c0, b1, v2 });
                }

                pack.tris = move(trisNew);
                pack.CalcAbuts();
                pack.Save(GetFilePath(readRecursion + 1).c_str());
            }

            return pack;
        }
    };
}
