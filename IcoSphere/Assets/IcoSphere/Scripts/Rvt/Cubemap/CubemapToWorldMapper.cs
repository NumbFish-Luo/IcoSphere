using UnityEngine;

namespace IcoSphere {
    // 负责立方体面UV坐标 <--> 世界空间三维坐标的转换
    public class CubemapToWorldMapper {
        private readonly float sphereRadius; // 球体半径
        private readonly int rootSize; // 根节点纹理分辨率, 如1024
        private readonly float cubeHalfSize; // 立方体半边长 = radius / sqrt(3), 用于归一化
        private readonly float rootWorldSize; // 缓存根节点的世界尺寸

        public CubemapToWorldMapper(float radius, int rootSize) {
            sphereRadius = radius;
            this.rootSize = rootSize;
            cubeHalfSize = radius / Mathf.Sqrt(3f); // 使立方体顶点恰在球面上
            rootWorldSize = ComputeWorldSizeForRoot(); // 预计算根节点的世界尺寸, 所有面相同
        }

        // 获取球面上节点中心的世界坐标
        public Vector3 GetNodeWorldCenter(CubemapQuadTree node) {
            // 面内UV坐标 (范围0~rootSize) 转标准化坐标 (-1~1)
            float u_norm = (node.u + node.size * 0.5f) / rootSize * 2f - 1f;
            float v_norm = (node.v + node.size * 0.5f) / rootSize * 2f - 1f;
            Vector3 localPos = GetLocalPosFromUV(node.face, u_norm, v_norm);
            return localPos.normalized * sphereRadius;
        }

        // 获取节点在世界空间中的边长, 近似为球面弧长对应的弦长
        public float GetNodeWorldSize(CubemapQuadTree node) {
            // 获取节点四个角点，计算平均边长
            float u0 = (float)node.u / rootSize * 2f - 1f;
            float v0 = (float)node.v / rootSize * 2f - 1f;
            float u1 = (float)(node.u + node.size) / rootSize * 2f - 1f;
            float v1 = (float)(node.v + node.size) / rootSize * 2f - 1f;
            Vector3 p00 = GetLocalPosFromUV(node.face, u0, v0).normalized * sphereRadius;
            Vector3 p01 = GetLocalPosFromUV(node.face, u0, v1).normalized * sphereRadius;
            Vector3 p10 = GetLocalPosFromUV(node.face, u1, v0).normalized * sphereRadius;
            float width = Vector3.Distance(p00, p10);
            float height = Vector3.Distance(p00, p01);
            return (width + height) * 0.5f;
        }

        // 获取节点在世界空间中的包围盒尺寸, 用于Gizmos
        public Vector3 GetNodeWorldSize3D(CubemapQuadTree node) {
            // 粗略估算，实际应为局部切平面内的宽和高，这里用近似立方体
            return Vector3.one * GetNodeWorldSize(node);
        }

        // 获取根节点的世界空间边长, 预计算, 无GC
        public float GetRootWorldSize() => rootWorldSize;

        // 计算根节点的世界尺寸, 内部辅助方法
        private float ComputeWorldSizeForRoot() {
            // 根节点覆盖整个面: u = 0, v = 0, size = rootSize
            // 获取四个角点的世界坐标, 计算平均边长
            float u0 = -1f;
            float v0 = -1f;
            float u1 = 1f;
            float v1 = 1f;
            // 选择任意面计算, 例如face = R, 因为对称性结果相同
            Vector3 p00 = GetLocalPosFromUV(0, u0, v0).normalized * sphereRadius;
            Vector3 p01 = GetLocalPosFromUV(0, u0, v1).normalized * sphereRadius;
            Vector3 p10 = GetLocalPosFromUV(0, u1, v0).normalized * sphereRadius;
            float width = Vector3.Distance(p00, p10);
            float height = Vector3.Distance(p00, p01);
            return (width + height) * 0.5f;
        }

        // 根据面索引和标准化UV坐标 (-1~1) 获取立方体局部坐标 (未归一化)
        private Vector3 GetLocalPosFromUV(CubemapFace face, float u, float v) {
            var pos = face switch {
                CubemapFace.R => new Vector3( 1 * cubeHalfSize, -v * cubeHalfSize, -u * cubeHalfSize),
                CubemapFace.L => new Vector3(-1 * cubeHalfSize, -v * cubeHalfSize,  u * cubeHalfSize),
                CubemapFace.U => new Vector3( u * cubeHalfSize,  1 * cubeHalfSize,  v * cubeHalfSize),
                CubemapFace.D => new Vector3( u * cubeHalfSize, -1 * cubeHalfSize, -v * cubeHalfSize),
                CubemapFace.F => new Vector3( u * cubeHalfSize, -v * cubeHalfSize,  1 * cubeHalfSize),
                CubemapFace.B => new Vector3(-u * cubeHalfSize, -v * cubeHalfSize, -1 * cubeHalfSize),
                _ => Vector3.zero,
            };
            return pos;
        }
    }
}
