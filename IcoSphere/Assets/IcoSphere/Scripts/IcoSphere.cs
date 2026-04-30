using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IcoSphere {
    public class IcoSphere : MonoBehaviour {
        [SerializeField, Range(0, 5)] private int recursion = 3;
        [SerializeField] private Material mat;
        [SerializeField] private ComputeShader computeShader;
        [SerializeField] private float camRadius = 1.0f;
        [SerializeField] private float sphereRadius = 1.0f;
        [SerializeField] private float lineWidth = 0.00005f;

        private bool supportsComputeShaders;
        private Camera cam;
        private Mesh mesh;
        private int triNum;
        private ComputeBuffer allBuf;
        private ComputeBuffer visibleBuf;
        private ComputeBuffer rayBuf;
        private ComputeBuffer drawHexBuf;
        private ComputeBuffer argsBuf;
        private const string kernelName = "Main";
        private int kernelId;
        private float instanceRadius;
        private readonly uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

        public float SphereRadius => sphereRadius;

        // 对于单个三角形, 需要知道的信息有3个顶点坐标值, 还有毗邻的3个三角形中心坐标值
        // -----v0----
        // \c20/ \c01/
        //  \ / t \ /
        //  v2-----v1
        //    \c12/
        //     \ /
        // 然后为了最大化利用数据, xyz对应具体坐标, w对应序号(Int32)
        [StructLayout(LayoutKind.Sequential)]
        public struct InstanceData {
            public uint id; // 三角形id
            public Vector4 v0;
            public Vector4 v1;
            public Vector4 v2;
            public Vector4 c01;
            public Vector4 c12;
            public Vector4 c20;
            public Vector4 col;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RayData {
            public uint tid; // 三角形id
            public uint vid; // 顶点id
            public Vector3 o; // 射线起点
            public Vector3 d; // 射线方向
            public float u; // 重心坐标u
            public float v; // 重心坐标v
            public float t; // 射线参数t (交点到原点的距离)
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct DrawHexData {
            public uint id; // 顶点id
            public Color col;
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
                Vector3 camPos = cam.transform.position;
                computeShader.SetVectorArray("_FrustumPlanes", PlanesToVector4(frustumPlanes));
                computeShader.SetFloat("_MaxDistance", cam.farClipPlane);
                computeShader.SetVector("_CamPos", camPos);
                computeShader.SetFloat("_InstanceRadius", instanceRadius);
                computeShader.SetInt("_MaxNum", triNum);

                // 射线检测
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                computeShader.SetVector("_RayOrigin", ray.origin);
                computeShader.SetVector("_RayDir", ray.direction);

                // 执行剔除
                visibleBuf.SetCounterValue(0);
                int threadGroups = Mathf.CeilToInt(triNum / 64.0f);
                computeShader.Dispatch(kernelId, threadGroups, 1, 1);
                ComputeBuffer.CopyCount(visibleBuf, argsBuf, sizeof(uint));

                // 使用足够大的包围盒，确保所有相机都能看到
                float maxDistance = cam.farClipPlane;
                Bounds renderBounds = new(camPos, new Vector3(maxDistance * 2, maxDistance * 2, maxDistance * 2));

                // 材质参数设置
                mat.SetFloat("_Radius", sphereRadius);
                mat.SetFloat("_LineWidth", lineWidth * sphereRadius);

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

            Pack pack = Pack.Read(recursion);

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

        private RayData[] NewDefaultRayData() {
            RayData[] result = new RayData[1];
            result[0].tid = (uint)triNum;
            return result;
        }

        private DrawHexData[] NewDefaultDrawHexData() {
            DrawHexData[] result = new DrawHexData[1];
            result[0].id = (uint)triNum;
            result[0].col = Color.clear;
            return result;
        }

        private void NewBufs(Pack p) {
            int n = p.tris.Length;
            triNum = n;
            List<InstanceData> data = new(n);
            for (uint i = 0; i < n; ++i) {
                data.Add(NewInstanceData(p, i));
            }
            int stride = Marshal.SizeOf(typeof(InstanceData));
            allBuf = ComputeBufManager.NewBuf(n, stride);
            allBuf.SetData(data);
            visibleBuf = ComputeBufManager.NewBuf(n, stride, ComputeBufferType.Append);
            rayBuf = ComputeBufManager.NewBuf(n, Marshal.SizeOf(typeof(RayData)));
            rayBuf.SetData(NewDefaultRayData());
            drawHexBuf = ComputeBufManager.NewBuf(n, Marshal.SizeOf(typeof(DrawHexData)));
            drawHexBuf.SetData(NewDefaultDrawHexData());
            argsBuf = ComputeBufManager.NewBuf(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            args[0] = mesh.GetIndexCount(0); // Index Count Per Instance
            args[1] = 0; // Instance Count
            args[2] = mesh.GetIndexStart(0); // Start Index Location
            args[3] = mesh.GetBaseVertex(0); // Base Vertex Location
            args[4] = 0; // Start Instance Location
            argsBuf.SetData(args);

            kernelId = computeShader.FindKernel(kernelName);
            if (kernelId < 0) {
                throw new Exception("Failed to find kernel '" + kernelName + "'");
            }

            // 输入
            computeShader.SetBuffer(kernelId, "_AllInstancesData", allBuf);
            mat.SetBuffer("_AllInstancesData", allBuf);

            computeShader.SetBuffer(kernelId, "_DrawHexData", drawHexBuf);

            // 输出
            computeShader.SetBuffer(kernelId, "_VisibleInstancesData", visibleBuf);
            mat.SetBuffer("_VisibleInstancesData", visibleBuf);

            computeShader.SetBuffer(kernelId, "_RayResult", rayBuf);
            mat.SetBuffer("_RayResult", rayBuf);

            Debug.Log($"Buffers created successfully: {n} instances");
        }

        // 对于单个三角形, 需要知道的信息有3个顶点坐标值, 还有毗邻的3个三角形中心坐标值
        // -----v0----
        // \c20/ \c01/
        //  \ / t \ /
        //  v2-----v1
        //    \c12/
        //     \ /
        private InstanceData NewInstanceData(Pack p, uint i) {
            float r = sphereRadius;

            Tri t = p.tris[i];
            Int32 v0 = t[0];
            Int32 v1 = t[1];
            Int32 v2 = t[2];
            Vector3 p0 = p.verts[v0] * r;
            Vector3 p1 = p.verts[v1] * r;
            Vector3 p2 = p.verts[v2] * r;
            Vector3 c01 = p.ctrs[i * 3 + 0] * r;
            Vector3 c12 = p.ctrs[i * 3 + 1] * r;
            Vector3 c20 = p.ctrs[i * 3 + 2] * r;

            return new() {
                id = i,
                v0 = new Vector4(p0.x, p0.y, p0.z, v0),
                v1 = new Vector4(p1.x, p1.y, p1.z, v1),
                v2 = new Vector4(p2.x, p2.y, p2.z, v2),
                c01 = new Vector4(c01.x, c01.y, c01.z, p.adjTris[i][0]),
                c12 = new Vector4(c12.x, c12.y, c12.z, p.adjTris[i][1]),
                c20 = new Vector4(c20.x, c20.y, c20.z, p.adjTris[i][2]),
                col = Color.gray
            };
        }

        private void FreeBuf(ref ComputeBuffer buf) {
            if (buf != null) {
                ComputeBufManager.ScheduleRelease(buf);
                buf = null;
            }
        }

        private void FreeBufs() {
            FreeBuf(ref allBuf);
            FreeBuf(ref visibleBuf);
            FreeBuf(ref rayBuf);
            FreeBuf(ref drawHexBuf);
            FreeBuf(ref argsBuf);
        }

        private Vector4[] PlanesToVector4(Plane[] p) {
            Vector4[] result = new Vector4[6];
            for (int i = 0; i < 6 && i < p.Length; i++) {
                result[i] = new Vector4(p[i].normal.x, p[i].normal.y, p[i].normal.z, p[i].distance);
            }
            return result;
        }

        public void SetRayHexColorToShader(Color col) {
            mat.SetColor("_RayHexCol", col);
        }

        public void DrawHexColorToComputeShader(Color col) {
            RayData[] outRayData = new RayData[1];
            rayBuf.GetData(outRayData);

            DrawHexData[] inDrawHexData = new DrawHexData[1];
            inDrawHexData[0].id = outRayData[0].vid;
            inDrawHexData[0].col = col;
            drawHexBuf.SetData(inDrawHexData);
        }
    }
}
