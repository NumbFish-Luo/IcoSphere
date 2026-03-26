using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IcoSphere {

    public class IcoSphereComputeShader : MonoBehaviour {
        [SerializeField] private Material mat;
        [SerializeField] private ComputeShader computeShader;
        [SerializeField] private float camRadius = 1.0f;
        [SerializeField] private float sphereRadius = 1.0f;
        [SerializeField, Range(0, 3)] private int recursion = 2; // 递归细分次数, 越大面数越多

        private Camera cam;
        private Mesh mesh;
        private ComputeBuffer allBuf;
        private ComputeBuffer visibleBuf;
        private ComputeBuffer argsBuf;
        private int kernel;
        private float instanceRadius;
        private readonly uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData {
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
            public Vector4 color;
        }

        private void Start() {
            Init();
        }

        private void OnDestroy() {
            FreeBufs();
        }

        private void Init() {
            cam = Camera.main;
            mesh = NewTriMesh();
            instanceRadius = mesh.bounds.extents.magnitude * camRadius;
            if (CheckSupports() == false) {
                return;
            }
            Pack pack = NewData(sphereRadius, recursion);
            FreeBufs();
            NewBufs(pack);
        }

        public bool CheckSupports() {
            if (!SystemInfo.supportsComputeShaders) {
                Debug.LogWarning("Compute shaders not supported");
                return false;
            }
            return true;
        }

        private Mesh NewTriMesh() {
            const float pi = Mathf.PI;
            const float a0 = pi / 2.0f;
            const float a1 = 11.0f * pi / 6.0f;
            const float a2 = 7.0f * pi / 6.0f;
            float c0 = Mathf.Cos(a0);
            float s0 = Mathf.Sin(a0);
            float c1 = Mathf.Cos(a1);
            float s1 = Mathf.Sin(a1);
            float c2 = Mathf.Cos(a2);
            float s2 = Mathf.Sin(a2);
            Mesh m = new() {
                name = "Tri",
                vertices = new Vector3[3] {
                    new(c0, s0),
                    new(c1, s1),
                    new(c2, s2)
                },
                uv = new Vector2[3] {
                    new(c0, s0),
                    new(c1, s1),
                    new(c2, s2)
                },
                triangles = new int[3] { 0, 1, 2 }
            };
            m.RecalculateNormals(); // 自动计算法线，实现光照效果
            m.RecalculateBounds();
            return m;
        }

        public static Pack NewData(float radius, int recursion) {
            Pack pack = new();

            // create 12 vertices of a icosahedron
            float t = Misc.GOLDEN_RATIO;
            pack.verts = new() {
                new(-1,  t,  0), new(1, t, 0), new(-1, -t,  0), new( 1, -t,  0),
                new( 0, -1,  t), new(0, 1, t), new( 0, -1, -t), new( 0,  1, -t),
                new( t,  0, -1), new(t, 0, 1), new(-t,  0, -1), new(-t,  0,  1)
            };
            pack.uvs = new();
            pack.cols = new();
            for (int i = 0; i < pack.verts.Count; i++) {
                Vector3 norm = pack.verts[i].normalized;
                pack.verts[i] = norm;
                pack.uvs.Add(SphereToUV(norm));
                pack.cols.Add(Color.white);
            }

            // create 20 triangles of the icosahedron
            pack.tris = new() {
                new(0, 11, 5), new(0, 5, 1),  new(0, 1, 7),   new(0, 7, 10), new(0, 10, 11), // 5 faces around point 0
                new(1, 5, 9),  new(5, 11, 4), new(11, 10, 2), new(10, 7, 6), new(7, 1, 8),   // 5 adjacent faces
                new(3, 9, 4),  new(3, 4, 2),  new(3, 2, 6),   new(3, 6, 8),  new(3, 8, 9),   // 5 faces around point 3
                new(4, 9, 5),  new(2, 4, 11), new(6, 2, 10),  new(8, 6, 7),  new(9, 8, 1)    // 5 adjacent faces
            };

            // refine triangles
            Dictionary<VertCache, int> cache = new();
            for (int i = 0; i < recursion; ++i) {
                List<Tri> tris2 = new();
                foreach (Tri tri in pack.tris) {
                    int v1 = tri.v1;
                    int v2 = tri.v2;
                    int v3 = tri.v3;

                    // 生成9个小三角形
                    //        v1
                    //       / \
                    //     c2---a1
                    //     / \ / \
                    //   c1---o---a2
                    //   / \ / \ / \
                    // v3--b2---b1--v2
                    int a1 = GetSplitPoint(cache, pack, v1, v2, 1, 3, false);
                    int a2 = GetSplitPoint(cache, pack, v1, v2, 2, 3, false);
                    int b1 = GetSplitPoint(cache, pack, v2, v3, 1, 3, false);
                    int b2 = GetSplitPoint(cache, pack, v2, v3, 2, 3, false);
                    int c1 = GetSplitPoint(cache, pack, v3, v1, 1, 3, false);
                    int c2 = GetSplitPoint(cache, pack, v3, v1, 2, 3, false);
                    int o = GetTriMidPoint(cache, pack, v1, v2, v3, true);

                    tris2.Add(new(v1, a1, c2));
                    tris2.Add(new(c2, a1, o));
                    tris2.Add(new(a1, a2, o));
                    tris2.Add(new(c2, o, c1));
                    tris2.Add(new(o, b1, b2));
                    tris2.Add(new(o, a2, b1));
                    tris2.Add(new(c1, o, b2));
                    tris2.Add(new(a2, v2, b1));
                    tris2.Add(new(c1, b2, v3));
                }
                pack.tris = tris2;
            }

            // 缩放顶点到目标半径
            for (int i = 0; i < pack.verts.Count; ++i) {
                pack.verts[i] *= radius;
            }

            return pack;
        }

        private void NewBufs(Pack pack) {
            // todo...
        }

        private void FreeBufs() {
            if (allBuf != null) {
                ComputeBufManager.ScheduleRelease(allBuf);
                allBuf = null;
            }

            if (visibleBuf != null) {
                ComputeBufManager.ScheduleRelease(visibleBuf);
                visibleBuf = null;
            }

            if (argsBuf != null) {
                ComputeBufManager.ScheduleRelease(argsBuf);
                argsBuf = null;
            }
        }

        // 分割点为t1/t2
        private static int GetSplitPoint(Dictionary<VertCache, int> cache, Pack pack, int p1, int p2, int t1, int t2, bool randomRgb) {
            VertCache key = new(p1, p2, t1, t2);
            if (cache.TryGetValue(key, out int ret)) {
                return ret;
            }

            // not in cache, calculate it
            Vector3 point1 = pack.verts[p1];
            Vector3 point2 = pack.verts[p2];
            Vector3 pointSplit = Vector3.Lerp(point1, point2, (t1 * 1.0f) / t2).normalized;

            // add vertex makes sure point is on unit sphere
            pack.verts.Add(pointSplit);
            pack.uvs.Add(SphereToUV(pointSplit));
            int i = pack.verts.Count - 1;
            pack.cols.Add(randomRgb ? Misc.RandomRgb(i) : Color.clear);
            cache.Add(key, i);
            return i;
        }

        private static int GetTriMidPoint(Dictionary<VertCache, int> cache, Pack pack, int p1, int p2, int p3, bool randomRgb) {
            VertCache key = new(p1, p2, p3);
            if (cache.TryGetValue(key, out int ret)) {
                return ret;
            }

            // not in cache, calculate it
            Vector3 point1 = pack.verts[p1];
            Vector3 point2 = pack.verts[p2];
            Vector3 point3 = pack.verts[p3];
            Vector3 pointSplit = ((point1 + point2 + point3) / 3.0f).normalized;

            // add vertex makes sure point is on unit sphere
            pack.verts.Add(pointSplit);
            pack.uvs.Add(SphereToUV(pointSplit));
            int i = pack.verts.Count - 1;
            pack.cols.Add(randomRgb ? Misc.RandomRgb(i) : Color.clear);
            cache.Add(key, i);
            return i;
        }

        // 将单位球面上的点转换为UV坐标, 经纬度映射, 默认p为单位向量
        public static Vector2 SphereToUV(Vector3 p) {
            float u = Mathf.Atan2(p.z, p.x) / (2.0f * Mathf.PI) + 0.5f;
            float v = 0.5f + Mathf.Asin(p.y) / Mathf.PI;
            return new Vector2(u, v);
        }
    }
}
