using UnityEngine;

namespace IcoSphere {
    // 记录相邻的三角面下标, 使用方法是用Dictionary<Edge, Abut>储存和索引
    public struct Abut {
        public int t1;
        public int t2;

        public Abut(int t1 = -1, int t2 = -1) {
            this.t1 = t1;
            this.t2 = t2;
        }

        public static Abut New => new() { t1 = -1, t2 = -1 };

        public void Push(int t) {
            if (t1 < 0) {
                t1 = t;
            } else if (t2 < 0) {
                t2 = t;
            } else {
                Debug.LogError($"试图传入第三个三角形: t1: {t1}, t2: {t2}, t: {t}");
            }
        }
    }
}
