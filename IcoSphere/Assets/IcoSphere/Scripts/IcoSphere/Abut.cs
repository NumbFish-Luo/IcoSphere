using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    /// <summary>
    /// 毗邻三角形序号数据
    /// </summary>
    public readonly struct Abut {
        private readonly Int32 t0;
        private readonly Int32 t1;

        /// <summary>
        /// 构造函数, 传入1对三角形序号进行构造
        /// </summary>
        /// <param name="t0">三角形序号</param>
        /// <param name="t1">三角形序号</param>
        public Abut(Int32 t0, Int32 t1) {
            this.t0 = t0;
            this.t1 = t1;
        }

        /// <summary>
        /// 索引器, 有效值[0], [1]
        /// </summary>
        /// <param name="idx">下标</param>
        /// <returns>对应下标三角形序号</returns>
        /// <exception cref="IndexOutOfRangeException">超出范围异常</exception>
        public readonly Int32 this[int idx] {
            get {
                return idx switch {
                    0 => t0,
                    1 => t1,
                    _ => throw new IndexOutOfRangeException()
                };
            }
        }
    }
}
