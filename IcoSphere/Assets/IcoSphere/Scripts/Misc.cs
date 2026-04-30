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
