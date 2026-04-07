export module Mesh.Tri;

import <iostream>;
import <string>;
import <array>;

using std::ostream;
using std::string;
using std::array;
using std::format;

namespace Mesh {
    export class Tri {
    private:
        array<int32_t, 3> v = { -1, -1, -1 };
    public:
        constexpr Tri() noexcept {}
        constexpr Tri(int32_t v0, int32_t v1, int32_t v2) noexcept : v{ v0, v1, v2 } {}

        constexpr int32_t operator[](size_t i) const {
            return v[i];
        }

        string ToStr() const {
            return format("({}, {}, {})", v[0], v[1], v[2]);
        }

        friend ostream& operator<<(ostream& out, const Tri& t) {
            return out << t.ToStr();
        }
    };
}
