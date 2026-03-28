using UnityEngine;

namespace IcoSphere {
    // 顶点序号缓存key
    public readonly struct VertCache {
        public readonly int p1;
        public readonly int p2;
        public readonly int p3;
        public readonly int t1;
        public readonly int t2;

        public VertCache(int p1, int p2, int t1, int t2) {
            this.p1 = Mathf.Min(p1, p2);
            this.p2 = Mathf.Max(p1, p2);
            p3 = -1;
            this.t1 = (p1 < p2) ? (t1) : (t2 - t1);
            this.t2 = t2;
        }

        public VertCache(int p1, int p2, int p3) {
            this.p1 = Mathf.Min(p1, p2, p3);
            this.p3 = Mathf.Max(p1, p2, p3);
            this.p2 = p1 ^ p2 ^ p3 ^ this.p1 ^ this.p3;
            t1 = -1;
            t2 = -1;
        }
    }
}
