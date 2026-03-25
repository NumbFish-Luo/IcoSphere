using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IcoSphere {
    public class GpuCullingExample : MonoBehaviour {
        [SerializeField] private Mesh mesh;
        [SerializeField] private Material mat;
        [SerializeField] private ComputeShader computeShader;
        [SerializeField] private int num = 100000;
        [SerializeField] private float area = 50f;
        [SerializeField] private float radius = 1.0f;

        private Camera cam;
        private ComputeBuffer allBuf;
        private ComputeBuffer visibleBuf;
        private ComputeBuffer argsBuf;
        private int kernel;
        private float instanceRadius;
        private readonly uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData {
            public Vector4 position;
            public Vector4 color;
        }

        private void Awake() {
            cam = Camera.main;
            Init();
        }

        void OnDestroy() {
            ReleaseAllBuf();
        }

        private void Init() {
            if (!CheckSupports()) {
                return;
            }
            ReleaseAllBuf();
            CreateAllBuf();
        }

        private void CreateAllBuf() {
            instanceRadius = mesh.bounds.extents.magnitude * radius;
            List<InstanceData> data = new(num);

            for (int i = 0; i < num; i++) {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(0f, area / 2f);
                float x = Mathf.Cos(angle) * dist;
                float z = Mathf.Sin(angle) * dist;
                Vector4 pos = new(x, 0, z, 1);
                Color col = new(Random.value, Random.value, Random.value, 1);

                data.Add(new InstanceData {
                    position = pos,
                    color = col
                });
            }

            int stride = Marshal.SizeOf(typeof(InstanceData));

            allBuf = ComputeBufManager.Instance.NewBuf(num, stride, ComputeBufferType.Default);
            allBuf.SetData(data);

            visibleBuf = ComputeBufManager.Instance.NewBuf(num, stride, ComputeBufferType.Append);

            argsBuf = ComputeBufManager.Instance.NewBuf(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

            args[0] = mesh.GetIndexCount(0);
            args[1] = 0;
            args[2] = mesh.GetIndexStart(0);
            args[3] = mesh.GetBaseVertex(0);
            args[4] = 0;
            argsBuf.SetData(args);

            const string kernelName = "CullInstances";
            kernel = computeShader.FindKernel(kernelName);
            if (kernel < 0) {
                throw new System.Exception("Failed to find kernel '" + kernelName + "'");
            }

            computeShader.SetBuffer(kernel, "_AllInstancesData", allBuf);
            computeShader.SetBuffer(kernel, "_VisibleInstancesData", visibleBuf);

            mat.SetBuffer("_VisibleInstancesData", visibleBuf);

            Debug.Log($"Buffers created successfully: {num} instances");
        }

        private void ReleaseAllBuf() {
            if (allBuf != null) {
                ComputeBufManager.Instance.ScheduleRelease(allBuf);
                allBuf = null;
            }

            if (visibleBuf != null) {
                ComputeBufManager.Instance.ScheduleRelease(visibleBuf);
                visibleBuf = null;
            }

            if (argsBuf != null) {
                ComputeBufManager.Instance.ScheduleRelease(argsBuf);
                argsBuf = null;
            }
        }

        private bool CheckSupports() {
            if (!SystemInfo.supportsComputeShaders) {
                Debug.LogWarning("Compute shaders not supported");
                return false;
            }
            return true;
        }

        void Update() {
            if (!CheckSupports()) {
                return;
            }

            try {
                Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
                computeShader.SetVectorArray("_FrustumPlanes", PlanesToVector4(frustumPlanes));
                computeShader.SetFloat("_MaxDistance", cam.farClipPlane);
                computeShader.SetMatrix("_CameraLocalToWorld", cam.transform.localToWorldMatrix);
                computeShader.SetFloat("_InstanceRadius", instanceRadius);

                // 执行剔除
                visibleBuf.SetCounterValue(0);
                int threadGroups = Mathf.CeilToInt(num / 64f);
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
            } catch (System.Exception e) {
                Debug.LogError($"Update error: {e.Message}");
            }
        }

        private Vector4[] PlanesToVector4(Plane[] planes) {
            Vector4[] result = new Vector4[6];
            for (int i = 0; i < 6 && i < planes.Length; i++) {
                result[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            }
            return result;
        }

        public void Cleanup() {
            ReleaseAllBuf();
        }
    }
}
