namespace IcoSphere {
    // 边数据, 创建时会自动按大小排序
    public readonly struct Edge {
        public readonly int v1;
        public readonly int v2;

        public Edge(int v1, int v2) {
            if (v1 < v2) {
                this.v1 = v1;
                this.v2 = v2;
            } else {
                this.v1 = v2;
                this.v2 = v1;
            }
        }
    }
}
