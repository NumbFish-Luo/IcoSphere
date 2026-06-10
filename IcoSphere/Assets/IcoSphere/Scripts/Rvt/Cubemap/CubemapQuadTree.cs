namespace IcoSphere {
    // 立方体六面四叉树节点, 记录面索引及其在面上的uv坐标
    // uv是像素坐标, 范围[0, rootSize]
    public class CubemapQuadTree {
        // ---- 基础属性 ----
        public CubemapFace face;
        public int u;
        public int v;
        public int size; // 节点边长, 像素单位
        public CubemapQuadTree[] children;
        public CubemapQuadTree parent;

        // 避免合并与细分的冲突
        public bool parentMerged;

        // ---- 节点数据 ----
        public int phyTexIdx = -1; // 物理纹理数组索引

        // ---- 构造函数 (隐藏) ----
        private CubemapQuadTree() { }

        // ---- 静态函数 ----
        public static CubemapQuadTree NewRoot(CubemapFace face, int u, int v, int size, int phyTexIdx) {
            return new() {
                face = face,
                u = u,
                v = v,
                size = size,
                phyTexIdx = phyTexIdx
            };
        }

        // ---- 成员函数 ----
        public bool IsLeaf => children == null;

        // 细分出指定子节点, childIdx: 0~3
        public CubemapQuadTree Split(int childIdx, int phyTexIdx) {
            int half = size / 2;
            int childU = u + (childIdx % 2) * half;
            int childV = v + (childIdx / 2) * half;
            CubemapQuadTree child = new() {
                face = face,
                u = childU,
                v = childV,
                size = half,
                phyTexIdx = phyTexIdx,
                parent = this
            };
            children ??= new CubemapQuadTree[4];
            children[childIdx] = child;
            return child;
        }
    }
}
