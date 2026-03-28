namespace IcoSphere {
    // 三角形顶点序号
    public readonly struct Tri {
        public readonly int v1;
        public readonly int v2;
        public readonly int v3;

        public Tri(int v1, int v2, int v3) {
            this.v1 = v1;
            this.v2 = v2;
            this.v3 = v3;
        }
    }
}
