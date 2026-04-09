export module IcoSphere.HexAbuts;

import <iostream>;
import <string>;
import <vector>;
import <algorithm>;
import <map>;
import Mesh.Abut;

using std::ostream;
using std::vector;
using std::map;
using std::move;
using std::cerr;
using std::endl;
using std::string;
using std::vformat;
using std::make_format_args;
using std::pair;
using Mesh::Abut;

namespace IcoSphere {
    // 六边形或者五边形的毗邻数据
    // 使用时以中心顶点序号v为下标往外扩散连接周围的vn
    // 然后划分出来的三角形为tn
    //   --v4---v5--
    //    / \ t4/ \
    // \ / t3\ /t5 \ /
    //  v3----v----v0
    // / \ t2/ \t0 / \
    //    \ /t1 \ /
    //   --v2---v1--
    // 实际内部使用std::map<int32_t, Abut>管理数据, key为v-vn边, val为共享这条边的三角形
    export class HexAbuts{
    private:
        map<int32_t, Abut> abuts;
    public:
        typedef pair<int32_t, Abut> Kv;
        typedef vector<Kv> Kvs;

        // 最多6个毗邻数据
        static inline const size_t KVS_MAX_SIZE = 6;

        // map的构造函数不是constexpr
        HexAbuts() noexcept {}

        HexAbuts(const Kvs& kvs) {
            abuts = map<int32_t, Abut>();
            for (const Kv& kv : kvs) {
                abuts.insert(kv);
            }
        }

        Abut operator[](int32_t v) {
            return abuts[v];
        }

        bool Push(int32_t v, int32_t t) {
            if (abuts.contains(v)) {
                return abuts[v].Push(t);
            } else if (abuts.size() < 6) {
                Abut a = Abut{};
                a.Push(t);
                abuts[v] = move(a);
                return true;
            } else {
                cerr << "错误! 试图加入第7个顶点, v: " << v << ", t: " << t << endl;
                return false;
            }
        }

        Kvs ToKvs() const {
            Kvs kvs = {};
            for (const Kv& kv : abuts) {
                kvs.push_back(kv);
            }
            while (kvs.size() < KVS_MAX_SIZE) {
                kvs.push_back({ -1, {} });
            }
            std::sort(kvs.begin(), kvs.end(), [](const Kv& l, const Kv& r) {
                return l.first < r.first;
            });
            return kvs;
        }

        string ToStr() const {
            string str = "";
            Kvs kvs = ToKvs();
            for (const Kv& kv : kvs) {
                int32_t v = kv.first;
                Abut a = kv.second;
                int32_t t0 = a[0];
                int32_t t1 = a[1];
                str += vformat("({}: {}, {}); ", make_format_args(v, t0, t1));
            }
            return str;
        }

        friend ostream& operator<<(ostream& out, const HexAbuts& a) {
            return out << a.ToStr();
        }
    };
}
