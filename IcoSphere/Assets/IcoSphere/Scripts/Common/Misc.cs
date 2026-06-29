using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IcoSphere {
    /// <summary>
    /// 杂项工具
    /// </summary>
    public static class Misc {
        /// <summary>
        /// uint整数转随机数
        /// </summary>
        /// <param name="x">输入</param>
        /// <param name="seed">随机数种子</param>
        /// <returns>随机数</returns>
        public static uint IntToRandom(uint x, uint seed) {
            uint hash = x * 0x9e3779b9u + seed;
            hash = (hash ^ (hash >> 15)) * 0x85ebca6bu;
            hash = (hash ^ (hash >> 13)) * 0xc2b2ae35u;
            hash ^= (hash >> 16);
            return hash & 0xFF;
        }

        /// <summary>
        /// uint整数转随机RGB颜色值, 可指定A值
        /// </summary>
        /// <param name="i">输入</param>
        /// <param name="a">指定A值</param>
        /// <returns>随机RGB颜色值</returns>
        public static Color RandomRgb(uint i, float a = 1.0f) {
            Color col;
            col.r = IntToRandom(i, 11) / 255.0f;
            col.g = IntToRandom(i, 45) / 255.0f;
            col.b = IntToRandom(i, 14) / 255.0f;
            col.a = a;
            return col;
        }

        /// <summary>
        /// int整数转随机RGB颜色值, 可指定A值
        /// </summary>
        /// <param name="i">输入</param>
        /// <param name="a">指定A值</param>
        /// <returns>随机RGB颜色值</returns>
        public static Color RandomRgb(int i, float a = 1.0f) {
            return RandomRgb((uint)i, a);
        }

        /// <summary>
        /// 字典转数组
        /// </summary>
        /// <typeparam name="K">Key类型</typeparam>
        /// <typeparam name="V">Value类型</typeparam>
        /// <typeparam name="A">数组类型</typeparam>
        /// <param name="dict">输入字典</param>
        /// <param name="newA">构造数组的函数</param>
        /// <returns>输出数组</returns>
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

        /// <summary>
        /// 数组转字典
        /// </summary>
        /// <typeparam name="K">Key类型</typeparam>
        /// <typeparam name="V">Value类型</typeparam>
        /// <typeparam name="A">数组类型</typeparam>
        /// <param name="a">输入数组</param>
        /// <param name="getK">从数组中获取Key的函数</param>
        /// <param name="getV">从数组中获取Value的函数</param>
        /// <returns>输出字典</returns>
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

        /// <summary>
        /// <para>RGB颜色十六进制整数</para>
        /// <para>参数precisionLv为精度等级, 消除颜色过于接近的问题</para>
        /// <para>precisionLv = 0, 对应0~255(不是256)</para>
        /// <para>precisionLv = 1, 对应0~128</para>
        /// <para>precisionLv = 2, 对应0~64</para>
        /// <para>之后会将数值再次返回0~255再得出结果</para>
        /// </summary>
        /// <param name="col">输入颜色</param>
        /// <param name="precisionLv">精度等级</param>
        /// <returns>十六进制整数</returns>
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

        /// <summary>
        /// 十六进制整数转RGB颜色
        /// </summary>
        /// <param name="hexRgb">十六进制整数</param>
        /// <returns>RGB颜色</returns>
        public static Color HexRgbToColor(uint hexRgb) {
            uint r = (hexRgb >> 16) & 0xFF;
            uint g = (hexRgb >> 8) & 0xFF;
            uint b = hexRgb & 0xFF;
            return new(r / 255f, g / 255f, b / 255f, 1f);
        }

        /// <summary>
        /// <para>世界坐标转经纬度</para>
        /// <para>经度 (Longitude): [-1.0, 1.0] * pi</para>
        /// <para>纬度 (Latitude): [-0.5, 0.5] * pi</para>
        /// </summary>
        /// <param name="p">世界坐标</param>
        /// <returns>经纬度</returns>
        public static Vector2 ToLonLat(Vector3 p) {
            p = p.normalized;
            return new(Mathf.Atan2(p.z, p.x), Mathf.Asin(p.y));
        }

        /// <summary>
        /// <para>世界坐标转经纬度UV</para>
        /// <para>经度UV (Longitude): [0.0, 1.0]</para>
        /// <para>纬度UV (Latitude): [0.0, 1.0]</para>
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public static Vector2 ToLonLatUv(Vector3 p) {
            Vector2 lonLat = ToLonLat(p) / Mathf.PI;
            float x = lonLat.x;
            float y = lonLat.y;
            return new((x + 1.0f) * 0.5f, y + 0.5f);
        }

        /// <summary>
        /// 销毁Transform下所有子节点
        /// </summary>
        /// <param name="tf">目标Transform</param>
        public static void KillAllChildren(this Transform tf) {
            int n = tf.childCount;
            for (int i = 0; i < n; ++i) {
                UnityEngine.Object.Destroy(tf.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// 往二进制写入器中写入Vector4
        /// </summary>
        /// <param name="bw">二进制写入器</param>
        /// <param name="v">要写入的Vector4</param>
        public static void Write(this BinaryWriter bw, Vector4 v) {
            bw.Write(v.x);
            bw.Write(v.y);
            bw.Write(v.z);
            bw.Write(v.w);
        }

        /// <summary>
        /// 从二进制读取器中读取Vector4
        /// </summary>
        /// <param name="br">二进制读取器</param>
        /// <returns>读取到的Vector4</returns>
        public static Vector4 ReadVec4(this BinaryReader br) {
            return new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        }
    }
}
