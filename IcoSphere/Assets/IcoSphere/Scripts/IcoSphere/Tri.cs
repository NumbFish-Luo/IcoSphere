using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    /// <summary>
    /// 三角形顶点序号数据
    /// </summary>
    public readonly struct Tri {
        private readonly Int32 v0;
        private readonly Int32 v1;
        private readonly Int32 v2;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="v0">顶点序号0</param>
        /// <param name="v1">顶点序号1</param>
        /// <param name="v2">顶点序号2</param>
        public Tri(Int32 v0, Int32 v1, Int32 v2) {
            this.v0 = v0;
            this.v1 = v1;
            this.v2 = v2;
        }

        /// <summary>
        /// 索引顶点序号
        /// </summary>
        /// <param name="idx">索引下标</param>
        /// <returns>顶点序号</returns>
        /// <exception cref="IndexOutOfRangeException">超出范围异常</exception>
        public readonly Int32 this[int idx] {
            get {
                return idx switch {
                    0 => v0,
                    1 => v1,
                    2 => v2,
                    _ => throw new IndexOutOfRangeException()
                };
            }
        }
    }
}
