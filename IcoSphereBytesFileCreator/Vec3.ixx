export module Math.Vec3;

import <iostream>;
import <string>;
import <format>;
import <cmath>;

using std::ostream;
using std::string;
using std::format;
using std::sqrt;

namespace Math {
    export class Vec3 {
    public:
        float x = 0.0f;
        float y = 0.0f;
        float z = 0.0f;

        constexpr Vec3(float x = 0.0f, float y = 0.0f, float z = 0.0f) noexcept : x(x), y(y), z(z) {}

        constexpr Vec3 operator+(const Vec3& v) const noexcept {
            return Vec3(x + v.x, y + v.y, z + v.z);
        }

        constexpr Vec3 operator-(const Vec3& v) const noexcept {
            return Vec3(x - v.x, y - v.y, z - v.z);
        }

        constexpr Vec3 operator*(float s) const noexcept {
            return Vec3(x * s, y * s, z * s);
        }

        constexpr Vec3 operator/(float s) const {
            return Vec3(x / s, y / s, z / s);
        }

        constexpr Vec3& operator+=(const Vec3& v) noexcept {
            x += v.x;
            y += v.y;
            z += v.z;
            return *this;
        }

        constexpr Vec3& operator-=(const Vec3& v) noexcept {
            x -= v.x;
            y -= v.y;
            z -= v.z;
            return *this;
        }

        constexpr Vec3& operator*=(float s) noexcept {
            x *= s;
            y *= s;
            z *= s;
            return *this;
        }

        constexpr Vec3& operator/=(float s) {
            x /= s;
            y /= s;
            z /= s;
            return *this;
        }

        constexpr Vec3 operator-() const noexcept {
            return Vec3(-x, -y, -z);
        }

        constexpr bool operator==(const Vec3& v) const noexcept = default;

        constexpr float LenSqr() const noexcept {
            return x * x + y * y + z * z;
        }

        float Len() const noexcept {
            return sqrt(LenSqr());
        }

        Vec3 Norm() const {
            float lenSqr = LenSqr();
            if (lenSqr == 0.0f) {
                return Vec3(0.0f, 0.0f, 0.0f);
            }
            float invLen = 1.0f / sqrt(lenSqr);
            return Vec3(x * invLen, y * invLen, z * invLen);
        }

        constexpr float Dot(const Vec3& v) const noexcept {
            return x * v.x + y * v.y + z * v.z;
        }

        constexpr Vec3 Cross(const Vec3& v) const noexcept {
            return Vec3(
                y * v.z - z * v.y,
                z * v.x - x * v.z,
                x * v.y - y * v.x
            );
        }

        constexpr Vec3 DirectMul(const Vec3& v) const noexcept {
            return Vec3(x * v.x, y * v.y, z * v.z);
        }

        constexpr Vec3 DirectDiv(const Vec3& v) const {
            return Vec3(x / v.x, y / v.y, z / v.z);
        }

        string ToStr(int precision = 2) const {
            return format("({:.{}f}, {:.{}f}, {:.{}f})", x, precision, y, precision, z, precision);
        }

        friend constexpr Vec3 operator*(float s, const Vec3& v) noexcept {
            return Vec3(v.x * s, v.y * s, v.z * s);
        }

        friend ostream& operator<<(ostream& out, const Vec3& v) {
            return out << v.ToStr();
        }
    };
}
