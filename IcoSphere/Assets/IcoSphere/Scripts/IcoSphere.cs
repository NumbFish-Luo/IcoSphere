using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IcoSphere {
    // 生成多个三角形mesh组成球体, 使用Compute Shader绘制大量三角形, 避免使用Mesh Renderer组件
    // 参考: http://blog.andreaskahler.com/2009/06/creating-icosphere-mesh-in-code.html
    public class IcoSphere : MonoBehaviour {
        [SerializeField] private Material mat;
        [SerializeField] private ComputeShader computeShader;
        [SerializeField] private float camRadius = 1.0f;
        [SerializeField] private float sphereRadius = 1.0f;
        [SerializeField, Range(0, 4)] private int recursion = 3; // 递归细分次数, 越大面数越多

        private bool supportsComputeShaders;
        private Camera cam;
        private Mesh mesh;
        private int num;
        private ComputeBuffer allBuf;
        private ComputeBuffer visibleBuf;
        private ComputeBuffer argsBuf;
        private const string kernelName = "TriCullInstances";
        private int kernel;
        private float instanceRadius;
        private readonly uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData {
            public Vector3 v1;
            public Vector3 v2;
            public Vector3 v3;
            public Vector4 col;
        }

        private void Awake() {
            supportsComputeShaders = CheckSupportsComputeShaders();
        }

        private void Start() {
            Init();
        }

        private void Update() {
            if (supportsComputeShaders == false) {
                return;
            }

            try {
                Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
                computeShader.SetVectorArray("_FrustumPlanes", PlanesToVector4(frustumPlanes));
                computeShader.SetFloat("_MaxDistance", cam.farClipPlane);
                computeShader.SetMatrix("_CameraLocalToWorld", cam.transform.localToWorldMatrix);
                computeShader.SetFloat("_InstanceRadius", instanceRadius);
                computeShader.SetInt("_MaxNum", num);

                // 执行剔除
                visibleBuf.SetCounterValue(0);
                int threadGroups = Mathf.CeilToInt(num / 64.0f);
                computeShader.Dispatch(kernel, threadGroups, 1, 1);
                ComputeBuffer.CopyCount(visibleBuf, argsBuf, sizeof(uint));

                // 使用足够大的包围盒，确保所有相机都能看到
                Vector3 cameraPos = cam.transform.position;
                float maxDistance = cam.farClipPlane;
                Bounds renderBounds = new(cameraPos, new Vector3(maxDistance * 2, maxDistance * 2, maxDistance * 2));

                Graphics.DrawMeshInstancedIndirect(
                    mesh: mesh,
                    submeshIndex: 0,
                    material: mat,
                    bounds: renderBounds,
                    bufferWithArgs: argsBuf,
                    argsOffset: 0,
                    properties: null,
                    castShadows: UnityEngine.Rendering.ShadowCastingMode.On,
                    receiveShadows: true,
                    layer: 0,
                    camera: null // 不指定相机，让Unity自动处理
                );
            } catch (Exception e) {
                Debug.LogError($"Update error: {e.Message}");
            }
        }

        private void OnDestroy() {
            FreeBufs();
        }

        private void Init() {
            cam = Camera.main;
            mesh = NewTriMesh();
            instanceRadius = mesh.bounds.extents.magnitude * camRadius;
            if (supportsComputeShaders == false) {
                return;
            }
            Pack pack = NewPack(sphereRadius, recursion);
            FreeBufs();
            NewBufs(pack);
        }

        public bool CheckSupportsComputeShaders() {
            if (SystemInfo.supportsComputeShaders == false) {
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

        public static Pack NewPack(float radius, int recursion) {
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
            int n = pack.tris.Count;
            num = n;
            List<InstanceData> data = new(n);
            for (int i = 0; i < n; ++i) {
                Tri packTris = pack.tris[i];
                int v1 = packTris.v1;
                int v2 = packTris.v2;
                int v3 = packTris.v3;
                data.Add(new() {
                    v1 = pack.verts[v1],
                    v2 = pack.verts[v2],
                    v3 = pack.verts[v3],
                    col = Misc.RandomRgb(i)
                });
            }

            int stride = Marshal.SizeOf(typeof(InstanceData));

            allBuf = ComputeBufManager.NewBuf(n, stride, ComputeBufferType.Default);
            allBuf.SetData(data);

            visibleBuf = ComputeBufManager.NewBuf(n, stride, ComputeBufferType.Append);

            argsBuf = ComputeBufManager.NewBuf(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            args[0] = mesh.GetIndexCount(0);
            args[1] = 0;
            args[2] = mesh.GetIndexStart(0);
            args[3] = mesh.GetBaseVertex(0);
            args[4] = 0;
            argsBuf.SetData(args);

            kernel = computeShader.FindKernel(kernelName);
            if (kernel < 0) {
                throw new Exception("Failed to find kernel '" + kernelName + "'");
            }
            computeShader.SetBuffer(kernel, "_AllInstancesData", allBuf);
            computeShader.SetBuffer(kernel, "_VisibleInstancesData", visibleBuf);
            mat.SetBuffer("_VisibleInstancesData", visibleBuf);
            Debug.Log($"Buffers created successfully: {n} instances");
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

        private Vector4[] PlanesToVector4(Plane[] planes) {
            Vector4[] result = new Vector4[6];
            for (int i = 0; i < 6 && i < planes.Length; i++) {
                result[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            }
            return result;
        }
    }
}
