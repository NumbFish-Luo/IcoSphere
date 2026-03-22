using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public static class Misc {
        public readonly static float GOLDEN_RATIO = (1.0f + Mathf.Sqrt(5.0f)) * 0.5f;

        public static uint IntToRandom(uint x, uint seed) {
            uint hash = x * 0x9e3779b9u + seed;
            hash = (hash ^ (hash >> 15)) * 0x85ebca6bu;
            hash = (hash ^ (hash >> 13)) * 0xc2b2ae35u;
            hash ^= (hash >> 16);
            return hash & 0xFF;
        }

        public static Color RandomRgb(uint i, float a = 1.0f) {
            Color col;
            col.r = IntToRandom(i, 11) / 255.0f;
            col.g = IntToRandom(i, 45) / 255.0f;
            col.b = IntToRandom(i, 14) / 255.0f;
            col.a = a;
            return col;
        }

        public static Color RandomRgb(int i, float a = 1.0f) {
            return RandomRgb((uint)i, a);
        }
    }
}
