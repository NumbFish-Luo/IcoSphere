using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
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
        private ComputeBuffer allBuf;
        private ComputeBuffer visibleBuf;
        private ComputeBuffer vertBuf;
        private ComputeBuffer rayBuf;
        private ComputeBuffer drawHexBuf;
        private ComputeBuffer argsBuf;
        private const string kernelMainName = "Main";
        private int kernelMainId;
        private float instanceRadius;
        private readonly uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        private Pack pack;
        private InstanceData[] instanceData;
        private VertData[] vertData;

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
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VertData {
            public Vector4 col; // rgb: 颜色, a: 国家id
            public Vector4 replace; // rgb: 替换色, a: 插值t
        }

        public readonly static Vector4 DEFAULT_COL = new(0.5f, 0.5f, 0.5f, 0.0f);

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

        private void OnEnable() {
#if UNITY_EDITOR
            // Ctrl+R刷新或编译脚本后触发的重置
            UnityEditor.AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
#endif
        }

        private void OnDisable() {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
#endif
        }

        // todo: Material.SetBuffer()设置的绑定关系是非持久的 (non-persistent), 当按下Ctrl+R刷新时, 需要重新绑定数据
        private void OnAfterAssemblyReload() {
            // todo...
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
                computeShader.SetInt("_MaxNum", pack.tris.Length);

                // 射线检测
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                computeShader.SetVector("_RayOrigin", ray.origin);
                computeShader.SetVector("_RayDir", ray.direction);

                // 执行剔除
                visibleBuf.SetCounterValue(0);
                int threadGroups = Mathf.CeilToInt(pack.tris.Length / 64.0f);
                computeShader.Dispatch(kernelMainId, threadGroups, 1, 1);
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
            ResetMat();
        }

        private void Init() {
            cam = Camera.main;
            mesh = NewTriMesh();
            instanceRadius = mesh.bounds.extents.magnitude * camRadius;
            if (supportsComputeShaders == false) {
                return;
            }

            pack = Pack.Read(recursion);
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
            result[0].tid = (uint)pack.tris.Length;
            return result;
        }

        private DrawHexData[] NewDefaultDrawHexData() {
            DrawHexData[] result = new DrawHexData[1];
            result[0].id = (uint)pack.tris.Length;
            result[0].col = Color.clear;
            return result;
        }

        private void NewBufs(Pack p) {
            int n = p.tris.Length;

            // ---- allBuf ----
            instanceData = new InstanceData[n];
            for (uint i = 0; i < n; ++i) {
                instanceData[i] = NewInstanceData(p, i);
            }
            int instanceStride = Marshal.SizeOf(typeof(InstanceData));
            allBuf = ComputeBufManager.NewBuf(n, instanceStride);
            allBuf.SetData(instanceData);

            // ---- visibleBuf ----
            visibleBuf = ComputeBufManager.NewBuf(n, instanceStride, ComputeBufferType.Append);

            // ---- vertBuf ----
            int m = p.verts.Length;
            vertData = new VertData[m];
            for (uint i = 0; i < m; ++i) {
                vertData[i] = NewVertData(p, i);
            }
            int vertStride = Marshal.SizeOf(typeof(VertData));
            vertBuf = ComputeBufManager.NewBuf(m, vertStride);
            vertBuf.SetData(vertData);

            // ---- rayBuf ----
            rayBuf = ComputeBufManager.NewBuf(n, Marshal.SizeOf(typeof(RayData)));
            rayBuf.SetData(NewDefaultRayData());

            // ---- drawHexBuf ----
            drawHexBuf = ComputeBufManager.NewBuf(n, Marshal.SizeOf(typeof(DrawHexData)));
            drawHexBuf.SetData(NewDefaultDrawHexData());

            // ---- argsBuf ----
            argsBuf = ComputeBufManager.NewBuf(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            args[0] = mesh.GetIndexCount(0); // Index Count Per Instance
            args[1] = 0; // Instance Count
            args[2] = mesh.GetIndexStart(0); // Start Index Location
            args[3] = mesh.GetBaseVertex(0); // Base Vertex Location
            args[4] = 0; // Start Instance Location
            argsBuf.SetData(args);

            // ---- kernelMainId ---- 
            kernelMainId = computeShader.FindKernel(kernelMainName);
            if (kernelMainId < 0) {
                throw new Exception("Failed to find kernel '" + kernelMainName + "'");
            }

            // ---- Input: Common ----
            computeShader.SetBuffer(kernelMainId, "_AllInstancesData", allBuf);
            mat.SetBuffer("_AllInstancesData", allBuf);

            computeShader.SetBuffer(kernelMainId, "_VertData", vertBuf);
            mat.SetBuffer("_VertData", vertBuf);

            // ---- Input: Main ----
            computeShader.SetBuffer(kernelMainId, "_DrawHexData", drawHexBuf);

            // ---- Output: Main ----
            computeShader.SetBuffer(kernelMainId, "_VisibleInstancesData", visibleBuf);
            mat.SetBuffer("_VisibleInstancesData", visibleBuf);

            computeShader.SetBuffer(kernelMainId, "_RayResult", rayBuf);
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
                c20 = new Vector4(c20.x, c20.y, c20.z, p.adjTris[i][2])
            };
        }

        private VertData NewVertData(Pack p, uint i) {
            return new() {
                col = DEFAULT_COL,
                replace = Color.clear
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
            FreeBuf(ref vertBuf);
            FreeBuf(ref rayBuf);
            FreeBuf(ref drawHexBuf);
            FreeBuf(ref argsBuf);
        }

        private void ResetMat() {
            mat.SetColor("_RayHexCol", Color.white);
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

        public void DrawHexColorToComputeShader(Color col, uint id) {
            RayData[] outRayData = new RayData[1];
            rayBuf.GetData(outRayData);

            DrawHexData[] inDrawHexData = new DrawHexData[1];
            inDrawHexData[0].id = outRayData[0].vid;
            inDrawHexData[0].col = new Vector4(col.r, col.g, col.b, id);
            drawHexBuf.SetData(inDrawHexData);
        }

        public bool TryGetRayHexCountryId(out uint countryId, out uint hexId, out RayData rayData) {
            countryId = 0;
            hexId = 0;
            rayData = new();

            if (rayBuf == null || vertBuf == null || pack.tris.Length <= 0) {
                return false;
            }

            RayData[] rayResult = new RayData[1];
            rayBuf.GetData(rayResult);
            rayData = rayResult[0];

            if (rayData.tid >= pack.tris.Length || rayData.vid >= pack.tris.Length) {
                return false;
            }

            VertData[] hexData = new VertData[1];
            vertBuf.GetData(hexData, 0, (int)rayData.vid, 1);

            hexId = rayData.vid;
            countryId = (uint)Mathf.RoundToInt(hexData[0].col.w);
            return true;
        }

        // hexRgbIdDict: <hexRgb, id>, 例如<#FF0000, 1>
        public void MappingTex(Texture2D tex, Dictionary<uint, uint> hexRgbIdDict) {
            if (tex.format != TextureFormat.RGBA32) {
                Debug.LogWarning("纹理非RGBA32格式, 建议先转换后再调用");
                Debug.LogWarning("请先阅读README文件修改图片设置");
                return;
            }

            NativeArray<byte> pixelData = tex.GetPixelData<byte>(0); // mip level 0
            int w = tex.width;
            int h = tex.height;
            int n = pack.verts.Length;
            vertData = new VertData[n];
            for (uint i = 0; i < n; ++i) {
                vertData[i] = NewVertData(pack, i);
            }
            // 映射国家颜色值
            for (uint i = 0; i < n; ++i) {
                Vector2 uv = Misc.ToLonLatUv(pack.verts[i]);
                int x = (int)(uv.x * w);
                int y = (int)(uv.y * h);
                x = Mathf.Clamp(x, 0, w - 1);
                y = Mathf.Clamp(y, 0, h - 1);
                int offset = y * w * 4 + x * 4;
                uint r = pixelData[offset];
                uint g = pixelData[offset + 1];
                uint b = pixelData[offset + 2];
                uint hexRgb = (r << 16) | (g << 8) | b;
                vertData[i].col = new Vector4(r / 255.0f, g / 255.0f, b / 255.0f, hexRgbIdDict[hexRgb]);
            }
            FreeBuf(ref vertBuf);
            vertBuf = ComputeBufManager.NewBuf(n, Marshal.SizeOf(typeof(VertData)));
            vertBuf.SetData(vertData);
            computeShader.SetBuffer(kernelMainId, "_VertData", vertBuf);
            mat.SetBuffer("_VertData", vertBuf);
        }

        public void SaveVertBufData(string path) {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            vertBuf.GetData(vertData);
            using BinaryWriter writer = new(File.Open(path, FileMode.Create));
            foreach (VertData d in vertData) {
                writer.Write(d.col);
                // 无需保存.replace
            }
        }

        public bool LoadVertBufData(string path) {
            if (!File.Exists(path)) {
                Debug.LogError("LoadAllBufData: 文件不存在 -> " + path);
                return false;
            }

            using (BinaryReader reader = new(File.OpenRead(path))) {
                int i = 0;
                while (reader.BaseStream.Position < reader.BaseStream.Length) {
                    vertData[i++] = new() {
                        col = reader.ReadVec4(),
                        replace = Color.clear
                    };
                }
            }
            vertBuf.SetData(vertData);
            return true;
        }

        // -------- 方便外部应用的API --------

        // 返回地块总数 (六边形/五边形数量)
        public int GetAreaCount() {
            return pack.verts.Length;
        }

        // 判断areaId是否有效, 用于避免点击、寻路、读档时传入非法id
        public bool IsValidAreaId(int areaId) {
            return areaId >= 0 && areaId < pack.verts.Length;
        }

        // 返回某个地块中心点的世界坐标
        public Vector3 GetAreaCenter(int areaId) {
            return pack.verts[areaId] * sphereRadius;
        }

        // 返回某个地块中心点的世界坐标
        // 注意! 这里返回的是原始坐标数据
        // 如果需要使用, 要自己乘球体半径sphereRadius, 或用GetAreaCenter函数
        public Vector3 GetRawAreaCenter(int areaId) {
            return pack.verts[areaId];
        }

        // 返回某个地块中心点的球面外法线, 用途：让单位、图标、模型能正确贴在球面上
        public Vector3 GetAreaNormal(int areaId) {
            return pack.verts[areaId].normalized;
        }

        // 返回某个地块的相邻地块数量, 六边形是6, 五边形是5 (少数)
        public int GetNeighborCount(int areaId) {
            Abut a = pack.abuts[areaId].A(0);
            return a[0] < 0 ? 5 : 6;
        }

        // 返回某个相邻地块id, neighborIndex的范围是0到GetNeighborCount(areaId) - 1
        public int GetNeighborId(int areaId, int neighborIndex) {
            return pack.abuts[areaId].V(neighborIndex);
        }

        // 获取未按坐标排序好的地块列表
        public Vector3[] GetRawUnsortedAreas() {
            return pack.verts;
        }

        // 获取按坐标排序好的地块列表
        // 注意! 这里PosVert用的是原始坐标数据
        // 如果需要使用, 要自己乘球体半径sphereRadius
        public PosVert[] GetRawSortedAreas() {
            return pack.posVerts;
        }

        // 尝试通过坐标寻找地块id, 如果没寻找到, 则返回-1
        public int FindAreaByPos(Vector3 p) {
            int i = PosVert.BinarySearch(pack.posVerts, p);
            if (i < 0) {
                return -1;
            }
            return pack.posVerts[i].v;
        }

        // 用射线拾取地块
        // 如果命中地块, 返回true, 并输出areaId
        // 如果没有命中地块, 返回false
        public bool TryPickArea(Ray ray, out int areaId) {
            areaId = 0;

            // 先检测射线是否击中球体
            if (Math.GetRayResult(Vector3.zero, sphereRadius, ray.origin, ray.direction, out Vector3 _) == false) {
                return false;
            }

            // 再执行compute shader内容
            computeShader.SetVector("_RayOrigin", ray.origin);
            computeShader.SetVector("_RayDir", ray.direction);
            visibleBuf.SetCounterValue(0);
            int threadGroups = Mathf.CeilToInt(pack.tris.Length / 64.0f);
            computeShader.Dispatch(kernelMainId, threadGroups, 1, 1);

            RayData[] rayBufResult = new RayData[1];
            rayBuf.GetData(rayBufResult);
            areaId = (int)rayBufResult[0].vid;
            return true;
        }

        // 设置单个地块颜色, 用途: 选中高亮、归属变化等
        [Obsolete] public void SetAreaColor(int areaId, Color color) {
            // todo: ...
        }

        // 批量设置多个地块为同一种颜色, 用途：初始化国家颜色、刷新地图模式等
        [Obsolete] public void SetAreaColors(int[] areaIds, Color color) {
            // todo: ...
        }

        // 清除单个地块的特殊颜色, 并恢复默认颜色
        [Obsolete] public void ClearAreaColor(int areaId) {
            // todo: ...
        }
    }
}
