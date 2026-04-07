export module IcoSphere.VertCache;

import <cstdint>;
import <algorithm>;
import <tuple>;

using std::min;
using std::max;
using std::tie;

namespace IcoSphere {
    export class VertCache {
    private:
        int32_t v0;
        int32_t v1;
        int32_t v2;
        int32_t t0;
        int32_t t1;
    public:
        // 记录两个点的中间点
        VertCache(int32_t v0, int32_t v1, int32_t t0, int32_t t1) noexcept {
            this->v0 = min(v0, v1);
            this->v1 = max(v0, v1);
            v2 = -1;
            this->t0 = (v0 < v1) ? (t0) : (t1 - t0);
            this->t1 = t1;
        }

        // 记录三个点的中点
        VertCache(int32_t v0, int32_t v1, int32_t v2) noexcept {
            this->v0 = min(min(v0, v1), v2);
            this->v2 = max(max(v0, v1), v2);
            this->v1 = v0 ^ v1 ^ v2 ^ this->v0 ^ this->v2;
            t0 = t1 = -1;
        }

        // 用于map迭代器类型萃取
        auto operator<=>(const VertCache&) const = default;
    };
}
