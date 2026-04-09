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

        private bool supportsComputeShaders;
        private Camera cam;
        private Mesh mesh;
        private int triNum;
        private ComputeBuffer allBuf;
        private ComputeBuffer visibleBuf;
        private ComputeBuffer argsBuf;
        private const string kernelName = "TriCullInstances";
        private int kernel;
        private float instanceRadius;
        private readonly uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

        // 对于单个三角形, 需要知道的信息有3个顶点坐标值, 还有毗邻的3个三角形中心坐标值
        // -----v0----
        // \m20/ \m01/
        //  \ / t \ /
        //  v2-----v1
        //    \m12/
        //     \ /
        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData {
            public Vector3 v0;
            public Vector3 v1;
            public Vector3 v2;
            public Vector3 c01;
            public Vector3 c12;
            public Vector3 c20;
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
                computeShader.SetInt("_MaxNum", triNum);

                // 执行剔除
                visibleBuf.SetCounterValue(0);
                int threadGroups = Mathf.CeilToInt(triNum / 64.0f);
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

        private void NewBufs(Pack p) {
            int n = p.tris.Length;
            triNum = n;
            List<InstanceData> data = new(n);
            for (int i = 0; i < n; ++i) {
                data.Add(NewInstanceData(p, i));
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

        // 对于单个三角形, 需要知道的信息有3个顶点坐标值, 还有毗邻的3个三角形中心坐标值
        // -----v0----
        // \m20/ \m01/
        //  \ / t \ /
        //  v2-----v1
        //    \m12/
        //     \ /
        private InstanceData NewInstanceData(Pack p, int i) {
            float r = sphereRadius;

            Tri t = p.tris[i];
            Int32 v0 = t[0];
            Int32 v1 = t[1];
            Int32 v2 = t[2];

            return new() {
                v0 = p.verts[v0] * r,
                v1 = p.verts[v1] * r,
                v2 = p.verts[v2] * r,
                c01 = p.ctrs[i * 3 + 0] * r,
                c12 = p.ctrs[i * 3 + 1] * r,
                c20 = p.ctrs[i * 3 + 2] * r,
                col = Misc.RandomRgb(i)
            };
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

        private Vector4[] PlanesToVector4(Plane[] p) {
            Vector4[] result = new Vector4[6];
            for (int i = 0; i < 6 && i < p.Length; i++) {
                result[i] = new Vector4(p[i].normal.x, p[i].normal.y, p[i].normal.z, p[i].distance);
            }
            return result;
        }
    }
}
