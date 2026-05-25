using UnityEngine;

namespace IcoSphere {
    public static class QuadTreeMath {
        // 计算处理的顺序权重, 如近的优先, lod变大的优先
        // 返回值类似于贴图尺寸, 都是2的幂次数
        // lod表 (精确到小数点后1位):
        // |      dis    | lod | return |
        // | ----------- | --- | ------ |
        // |    < 1.4    |  0  |    1   |
        // |  1.4 ~  2.8 |  1  |    2   |
        // |  2.8 ~  5.6 |  2  |    4   |
        // |  5.6 ~ 11.3 |  3  |    8   |
        // | 11.3 ~ 22.6 |  4  |   16   |
        // |     ...     | ... |  ...   |
        // |  (懒得算了) | ... |  1024  |
        public static int CalcLodSize(Vector2 camPos, int x, int z, int size) {
            int halfSize = size / 2;
            float dis = CalcDistance(camPos, new Vector2(x + halfSize, z + halfSize), halfSize);
            dis = Mathf.Max(1, dis);
            int lod = Mathf.Max(0, (int)(Mathf.Log(dis, 2) + 0.5));
            return 1 << lod;
        }

        // 当相机坐标p在当前区块内时, 结果为0
        // 当相机坐标p在当前区块外时, 结果为sqrt(x * x + y * y)
        // 如果其中一个分量(x或y)在下图所示的s内部, 则视为0, 如p'所示
        // 最终外部能得到的结果是相机坐标p距离这个区块的最近的距离, 如p连接区块顶点的距离d, p'连接区块边的距离d'
        //
        // |<----- [s] ----->|
        // ----------------------
        // |                 |  ^
        // |                 |  |
        // |       [o]       | [s]
        // |                 |  |
        // |                 |  v
        // ------------------------------
        //      ^            |\         ^
        //      |            | \        |
        //   [y', d']        |  \       |
        //      |            |   \      |
        //      v            |   [d]   [y]
        //     [p']          |     \    |
        //                   |      \   |
        //                   |       \  |
        //                   |        \ v
        //                   |<- [x] ->[p]
        private static float CalcDistance(Vector2 p, Vector2 o, int s) {
            int sHalf = s / 2;
            Vector2 po = p - o;
            float sqrDistance = 0;

            // x, y两个维度分别计算
            for (int i = 0; i < 2; ++i) {
                float v = po[i];
                float d = 0.0f;
                // 只判断坐标分量在当前区块外的情况
                if (v < -sHalf) {
                    d = v + sHalf;
                } else if (v > sHalf) {
                    d = v - sHalf;
                }
                sqrDistance += d * d;
            }
            return Mathf.Sqrt(sqrDistance);
        }
    }
}
