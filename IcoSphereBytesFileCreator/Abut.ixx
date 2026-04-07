export module Mesh.Abut;

import <iostream>;
import <array>;

using std::cerr;
using std::endl;
using std::array;

namespace Mesh {
    export class Abut {
    private:
        array<int32_t, 2> t = { -1, -1 };
    public:
        constexpr Abut() noexcept : t{ -1, -1 } {}
        constexpr Abut(int32_t t0, int32_t t1) noexcept : t{ t0, t1 } {}

        constexpr int32_t operator[](size_t i) const {
            return t[i];
        }

        constexpr bool Push(int32_t tIn) noexcept {
            if (t[0] < 0) {
                t[0] = tIn;
                return true;
            } else if (t[1] < 0) {
                t[1] = tIn;
                return true;
            } else {
                cerr << "错误! 试图传入第3个三角形, t[0]: " << t[0] << ", t[1]: " << t[1] << ", tIn: " << tIn << endl;
                return false;
            }
        }
    };
}
