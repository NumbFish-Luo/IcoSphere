using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    // 三角形顶点序号
    public struct Tri {
        public int v1;
        public int v2;
        public int v3;

        public Tri(int v1, int v2, int v3) {
            this.v1 = v1;
            this.v2 = v2;
            this.v3 = v3;
        }
    }
}
