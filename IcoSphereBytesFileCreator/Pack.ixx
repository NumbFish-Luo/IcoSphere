module;
#define BYTE_ADDR_CONST(DATA_ADDR) reinterpret_cast<const char*>(DATA_ADDR)
#define BYTE_ADDR(DATA_ADDR) reinterpret_cast<char*>(DATA_ADDR)
export module IcoSphere.Pack;

import <iostream>;
import <vector>;
import <string>;
import <fstream>;
import Math.Vec3;
import Mesh.Tri;
import Mesh.Abut;
import IcoSphere.HexAbuts;

using std::cout;
using std::cerr;
using std::endl;
using std::vector;
using std::string;
using std::format;
using std::ifstream;
using std::ofstream;
using Math::Vec3;
using Mesh::Tri;
using Mesh::Abut;

namespace IcoSphere {
    export class Pack {
    private:
        vector<HexAbuts::Kvs> abutsData; // 拆解成数组后的毗邻数据

        void AbutsToData() {
            size_t n = abuts.size();
            abutsData = vector<HexAbuts::Kvs>(n);
            for (int32_t i = 0; i < n; ++i) {
                abutsData[i] = abuts[i].ToKvs();
            }
        }

        void DataToAbuts() {
            size_t n = abutsData.size();
            abuts = vector<HexAbuts>(n);
            for (int32_t i = 0; i < n; ++i) {
                abuts[i] = HexAbuts(abutsData[i]);
            }
        }

        Vec3 CalcCtr(int32_t t) {
            const Tri& tt = tris[t];
            int32_t v0 = tt[0];
            int32_t v1 = tt[1];
            int32_t v2 = tt[2];
            const Vec3& p0 = verts[v0];
            const Vec3& p1 = verts[v1];
            const Vec3& p2 = verts[v2];
            return (p0 + p1 + p2) / 3.0f;
        }

    public:
        vector<Vec3> verts;
        vector<Tri> tris;
        vector<HexAbuts> abuts; // 数量与verts一致
        vector<Vec3> ctrs; // 毗邻三角形中心坐标数组, 数量为tris的3倍, ctr是center的缩写
        vector<Tri> adjTris; // 毗邻三角形序号数组, 数量与tris一致, 每个Tri存储(t01, t12, t20)

        // 推算毗邻数据
        void CalcAbuts() {
            // 数量与verts一致
            size_t nv = verts.size();
            size_t nt = tris.size();
            abuts = vector<HexAbuts>(nv);
            for (int32_t t = 0; t < nt; ++t) {
                //     v0:a0
                //      / \
                //     / t \
                // v2:a2---v1:a1
                const Tri& tt = tris[t];
                int32_t v0 = tt[0];
                int32_t v1 = tt[1];
                int32_t v2 = tt[2];
                HexAbuts& a0 = abuts[v0];
                HexAbuts& a1 = abuts[v1];
                HexAbuts& a2 = abuts[v2];
                a0.Push(v1, t);
                a0.Push(v2, t);
                a1.Push(v0, t);
                a1.Push(v2, t);
                a2.Push(v0, t);
                a2.Push(v1, t);
            }

            AbutsToData();
        }

        // 推算毗邻三角形中心点坐标, 需要在准备好abuts之后才能执行
        void CalcCtrs() {
            size_t nt = tris.size();
            ctrs = vector<Vec3>(nt * 3);
            adjTris = vector<Tri>(nt);
            for (int32_t t = 0; t < nt; ++t) {
                // 先获取目标三角形
                const Tri& tt = tris[t];
                // 获取顶点序号
                int32_t v0 = tt[0];
                int32_t v1 = tt[1];
                int32_t v2 = tt[2];
                // 获取相应的毗邻数据
                Abut a01 = abuts[v0][v1];
                Abut a12 = abuts[v1][v2];
                Abut a20 = abuts[v2][v0];
                // 查找到对应的毗邻三角形
                // -----v0----
                // \t20/ \t01/
                //  \ / t \ /
                //  v2-----v1
                //    \t12/
                //     \ /
                int32_t t01 = a01[0];
                if (t01 == t) {
                    t01 = a01[1];
                }
                int32_t t12 = a12[0];
                if (t12 == t) {
                    t12 = a12[1];
                }
                int32_t t20 = a20[0];
                if (t20 == t) {
                    t20 = a20[1];
                }
                size_t t3 = (size_t)t * 3;
                ctrs[t3 + 0] = CalcCtr(t01);
                ctrs[t3 + 1] = CalcCtr(t12);
                ctrs[t3 + 2] = CalcCtr(t20);
                adjTris[t] = { t01, t12, t20 };
            }
        }

        string AbutsToStr() const {
            string str = "";
            size_t m = abuts.size();
            for (size_t i = 0; i < m; ++i) {
                str += format("{}: {}\n", i, abuts[i].ToStr());
            }
            return str;
        }

        // 二进制模型文件格式
        // Header (5 * int32_t):
        //   [vertsSize] [trisSize] [abutsSize] [ctrsSize] [adjTrisSize]
        // Body:
        //   verts  : vertsSize   * Vec3          (3 * float)
        //   tris   : trisSize    * Tri           (3 * int32_t)
        //   abuts  : abutsSize   * HexAbuts::Kvs (6 * 3 * int32_t)
        //   ctrs   : ctrsSize    * Vec3          (3 * float)
        //   adjTris: adjTrisSize * Tri           (3 * int32_t)
        bool Save(const char* filePath) const {
            // 打开文件
            ofstream file{ filePath, std::ios::binary };
            if (!file) {
                cerr << "save file == null: " << filePath << endl;
                return false;
            }

            // 写入数据头
            vector<int32_t> header = {
                (int32_t)verts.size(),
                (int32_t)tris.size(),
                (int32_t)abutsData.size(),
                (int32_t)ctrs.size(),
                (int32_t)adjTris.size()
            };
            file.write(BYTE_ADDR_CONST(header.data()), 5 * sizeof(int32_t));

            // 写入字节
            file.write(BYTE_ADDR_CONST(verts.data()), verts.size() * sizeof(Vec3));
            file.write(BYTE_ADDR_CONST(tris.data()), tris.size() * sizeof(Tri));
            for (const HexAbuts::Kvs& kvs : abutsData) {
                file.write(BYTE_ADDR_CONST(kvs.data()), HexAbuts::KVS_MAX_SIZE * sizeof(HexAbuts::Kv));
            }
            file.write(BYTE_ADDR_CONST(ctrs.data()), ctrs.size() * sizeof(Vec3));
            file.write(BYTE_ADDR_CONST(adjTris.data()), adjTris.size() * sizeof(Tri));

            // 关闭文件
            file.close();
            return true;
        }

        bool Read(const char* filePath) {
            // 打开文件
            ifstream file{ filePath, std::ios::binary };
            if (!file) {
                cerr << "read file == null: " << filePath << endl;
                return false;
            }

            // 读取数据头
            vector<int32_t> header = { 0, 0, 0, 0, 0 };
            file.read(BYTE_ADDR(header.data()), 5 * sizeof(int32_t));

            // 读取字节
            const size_t vertsSize = (size_t)header[0];
            verts = vector<Vec3>(vertsSize);
            file.read(BYTE_ADDR(verts.data()), vertsSize * sizeof(Vec3));

            const size_t trisSize = (size_t)header[1];
            tris = vector<Tri>(trisSize);
            file.read(BYTE_ADDR(tris.data()), trisSize * sizeof(Tri));

            const size_t abutsDataSize = (size_t)header[2];
            abutsData = vector<HexAbuts::Kvs>(abutsDataSize);
            for (size_t i = 0; i < abutsDataSize; ++i) {
                HexAbuts::Kvs& kvs = abutsData[i];
                const size_t n = HexAbuts::KVS_MAX_SIZE;
                kvs = HexAbuts::Kvs(n);
                file.read(BYTE_ADDR(kvs.data()), n * sizeof(HexAbuts::Kv));
            }

            const size_t ctrsSize = (size_t)header[3];
            ctrs = vector<Vec3>(ctrsSize);
            file.read(BYTE_ADDR(ctrs.data()), ctrsSize * sizeof(Vec3));

            const size_t adjTrisSize = (size_t)header[4];
            adjTris = vector<Tri>(adjTrisSize);
            file.read(BYTE_ADDR(adjTris.data()), adjTrisSize * sizeof(Tri));

            // 转换毗邻数据
            DataToAbuts();

            // 关闭文件
            file.close();
            return true;
        }
    };
}
