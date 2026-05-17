export module IcoSphere.PosVert;

import <iostream>;
import Math.Vec3;

using Math::Vec3;

namespace IcoSphere {
    export class PosVert {
    public:
        Vec3 p;
        int32_t v;

        constexpr PosVert() noexcept : p(), v(-1) {}
        constexpr PosVert(const Vec3& p, int32_t v) noexcept : p(p), v(v) {}
    };
}
