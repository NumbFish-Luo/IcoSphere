using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IcoSphere {
    // 杂项工具
    public static class Misc {
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

        public static A[] ToArr<K, V, A>(this Dictionary<K, V> dict, Func<K, V, A> newA) {
            if (dict == null) {
                return null;
            }
            A[] a = new A[dict.Count];
            int i = 0;
            foreach (KeyValuePair<K, V> kv in dict) {
                a[i++] = newA(kv.Key, kv.Value);
            }
            return a;
        }

        public static Dictionary<K, V> ToDict<K, V, A>(this A[] a, Func<A, K> getK, Func<A, V> getV) {
            if (a == null) {
                return null;
            }
            Dictionary<K, V> dict = new();
            foreach (A aa in a) {
                dict.Add(getK(aa), getV(aa));
            }
            return dict;
        }

        // 参数precisionLv为精度等级, 消除颜色过于接近的问题
        // precisionLv = 0, 对应0~255(不是256)
        // precisionLv = 1, 对应0~128
        // precisionLv = 2, 对应0~64
        // 之后会将数值再次返回0~255再得出结果
        public static uint ColorToHexRgb(Color col, int precisionLv = 0) {
            uint max = 0xFF;
            uint r = (uint)(col.r * max);
            uint g = (uint)(col.g * max);
            uint b = (uint)(col.b * max);
            // a忽略
            int p = precisionLv;
            if (precisionLv > 0) {
                // 先右移抹除部分精度
                r >>= p;
                g >>= p;
                b >>= p;
                // 然后左移恢复原本大小
                r <<= p;
                g <<= p;
                b <<= p;
            }
            return (r << 16) | (g << 8) | b;
        }

        public static Color HexRgbToColor(uint hexRgb) {
            uint r = (hexRgb >> 16) & 0xFF;
            uint g = (hexRgb >> 8) & 0xFF;
            uint b = hexRgb & 0xFF;
            return new(r / 255f, g / 255f, b / 255f, 1f);
        }

        // 世界坐标转经纬度
        // 经度 (Longitude): (-1.0, 1.0] * pi
        // 纬度  (Latitude): (-0.5, 0.5] * pi
        public static Vector2 ToLonLat(Vector3 p) {
            p = p.normalized;
            return new(Mathf.Atan2(p.z, p.x), Mathf.Asin(p.y));
        }

        public static Vector2 ToLonLatUv(Vector3 p) {
            Vector2 lonLat = ToLonLat(p) / Mathf.PI;
            float x = lonLat.x;
            float y = lonLat.y;
            return new((x + 1.0f) * 0.5f, y + 0.5f);
        }

        public static void KillAllChildren(this Transform tf) {
            int n = tf.childCount;
            for (int i = 0; i < n; ++i) {
                UnityEngine.Object.Destroy(tf.GetChild(i).gameObject);
            }
        }

        public static void Write(this BinaryWriter bw, Vector4 v) {
            bw.Write(v.x);
            bw.Write(v.y);
            bw.Write(v.z);
            bw.Write(v.w);
        }

        public static Vector4 ReadVec4(this BinaryReader br) {
            return new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        }
    }
}
