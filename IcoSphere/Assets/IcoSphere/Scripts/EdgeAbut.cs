namespace IcoSphere {
    // 记录相邻的三角面下标, 使用方法是用Dictionary<Edge, Abut>储存和索引
    public readonly struct EdgeAbut {
        public readonly Edge e;
        public readonly Abut a;

        public EdgeAbut(Edge e, Abut a) {
            this.e = e;
            this.a = a;
        }
    }
}
