namespace IcoSphere {
    // 四叉树
    public class QuadTree {
        // ---- 基础属性 ----
        public int x;
        public int z;
        public int size;
        public QuadTree[] children;
        public QuadTree parent;

        // 当前帧parent是否被合并过了, 因为遍历子节点顺序是不可控的, 避免出现合并细分矛盾
        public bool parentMerged = false;

        // ---- 节点数据 ----
        // 当前节点的物理贴图 (Texture2DArray) 索引
        public int phyTexIdx = -1;

        // ---- 构造函数 (隐藏) ----
        private QuadTree() { }

        // ---- 静态函数 ----
        public static QuadTree NewRoot(int size, int phyTexIdx) {
            return new() {
                size = size,
                phyTexIdx = phyTexIdx
                // 其他值保持默认即可
            };
        }

        // ---- 成员函数 ----
        public bool IsLeaf => children == null;

        // 细分node, 使用时应该一次性调用4次
        // for (int i = 0; i < 4; ++i) { node.Split(i, ...); }
        public QuadTree Split(int childIdx, int phyTexIdx) {
            int halfSize = size / 2;
            QuadTree child = new() {
                x = x + (childIdx % 2) * halfSize,
                z = z + ((childIdx / 2) % 2) * halfSize,
                parent = this,
                size = halfSize,
                phyTexIdx = phyTexIdx
            };
            children ??= new QuadTree[4];
            children[childIdx] = child;
            return child;
        }
    }
}
