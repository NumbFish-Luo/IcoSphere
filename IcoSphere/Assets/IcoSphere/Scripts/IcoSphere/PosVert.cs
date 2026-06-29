using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    /// <summary>
    /// 世界坐标和顶点序号
    /// </summary>
    public class PosVert {
        public readonly Vector3 p;
        public readonly Int32 v;

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public PosVert() {
            v = -1;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="p">世界坐标</param>
        /// <param name="v">顶点序号</param>
        public PosVert(Vector3 p, Int32 v) {
            this.p = p;
            this.v = v;
        }

        /// <summary>
        /// 二分搜索
        /// </summary>
        /// <param name="pv">要搜索的数组</param>
        /// <param name="target">要搜索的目标坐标</param>
        /// <returns>目标所在下标</returns>
        public static int BinarySearch(PosVert[] pv, Vector3 target) {
            PosVert finder = new(target, -1);
            return Array.BinarySearch(pv, finder, new PosVertComparer());
        }
    }

    /// <summary>
    /// 世界坐标和顶点序号结构体的比较算法
    /// </summary>
    public class PosVertComparer : IComparer<PosVert> {
        /// <summary>
        /// 比较算法
        /// </summary>
        /// <param name="l">左侧数据</param>
        /// <param name="r">右侧数据</param>
        /// <returns>比较结果 (-1, 0, 1)</returns>
        public int Compare(PosVert l, PosVert r) {
            if (l.p.x != r.p.x) {
                return l.p.x.CompareTo(r.p.x);
            }
            if (l.p.y != r.p.y) {
                return l.p.y.CompareTo(r.p.y);
            }
            return l.p.z.CompareTo(r.p.z);
        }
    }
}
