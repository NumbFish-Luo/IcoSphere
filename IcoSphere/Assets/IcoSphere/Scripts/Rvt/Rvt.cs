using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace IcoSphere {
    // Runtime Virtual Texture
    public class Rvt : MonoBehaviour {
        public class Node {
            public static Queue<Node> currentAllLeaves;
            private static Queue<Node> nextAllLeaves;
            public static Queue<int> physicEmptyIndexQueue;

            private static int GetPhysicIndex() { return physicEmptyIndexQueue.Count == 0 ? -1 : physicEmptyIndexQueue.Dequeue(); }
            private static void ResetPhysicIndex(Node node) { if (node.physicTexIndex > -1) physicEmptyIndexQueue.Enqueue(node.physicTexIndex); }
            private static System.Action<Node> onLoadData;
            private static int splitCount;
            private const int eventFrameSplitCountMax = 1;

            public int x;
            public int z;
            public int size;
            public Node[] children;
            public Node parent;
            public bool IsLeaf => children == null;
            public bool parentMerged;
            public int physicTexIndex = -1;

            public static Node CreateRoot(int rootSize, int physicIndexCount, System.Action<Node> onLoadDataCallback) {
                onLoadData = onLoadDataCallback;
                currentAllLeaves = new Queue<Node>();
                nextAllLeaves = new Queue<Node>();
                physicEmptyIndexQueue = new Queue<int>();

                for (int i = 0; i < physicIndexCount; i++)
                    physicEmptyIndexQueue.Enqueue(i);

                var root = new Node {
                    physicTexIndex = GetPhysicIndex(),
                    size = rootSize
                };
                currentAllLeaves.Enqueue(root);
                return root;
            }

            public static void UpdateAllLeavesState(Vector2 camPos) {
                splitCount = 0;
                nextAllLeaves.Clear();

                while (currentAllLeaves.Count > 0) {
                    var node = currentAllLeaves.Dequeue();
                    node.UpdateState(camPos);
                }

                (nextAllLeaves, currentAllLeaves) = (currentAllLeaves, nextAllLeaves);
            }

            private void UpdateState(Vector2 camPos) {
                if (parentMerged) return;

                if (parent != null) {
                    int parentLodSize = parent.CalculateLodSize(camPos);
                    bool allBrothersAreLeaf = true;
                    for (int i = 0; i < 4; i++)
                        if (!parent.children[i].IsLeaf) allBrothersAreLeaf = false;

                    if (parent.size <= parentLodSize && allBrothersAreLeaf) {
                        parent.Merge();
                        return;
                    }
                }

                int lodSize = CalculateLodSize(camPos);

                if (size == lodSize)
                    nextAllLeaves.Enqueue(this);
                else if (size > lodSize) {
                    if (splitCount++ < eventFrameSplitCountMax && physicEmptyIndexQueue.Count >= 3)
                        Split();
                    else
                        nextAllLeaves.Enqueue(this);
                } else
                    nextAllLeaves.Enqueue(this);
            }

            private void Split() {
                ResetPhysicIndex(this);

                children = new Node[4];
                children[0] = new Node() { x = x, z = z };
                children[1] = new Node() { x = x + size / 2, z = z };
                children[2] = new Node() { x = x, z = z + size / 2 };
                children[3] = new Node() { x = x + size / 2, z = z + size / 2 };

                for (int i = 0; i < 4; i++) {
                    children[i].parent = this;
                    children[i].size = size / 2;
                    children[i].physicTexIndex = GetPhysicIndex();
                    nextAllLeaves.Enqueue(children[i]);
                    onLoadData(children[i]);
                }
            }

            private void Merge() {
                nextAllLeaves.Enqueue(this);

                for (int i = 0; i < 4; i++) {
                    ResetPhysicIndex(children[i]);
                    children[i].parent = null;
                    children[i].parentMerged = true;
                }

                physicTexIndex = GetPhysicIndex();
                onLoadData(this);
                children = null;
            }

            private int CalculateLodSize(Vector2 camPos) {
                float dis = CalculateClosestPoint(camPos, new Vector2(x + size / 2, z + size / 2), new Vector2(size / 2, size / 2));
                dis = Mathf.Max(1, Mathf.Sqrt(dis));
                int lod = Mathf.Max(0, (int)(Mathf.Log(dis, 2) + 0.5));
                return 1 << lod;
            }

            private float CalculateClosestPoint(Vector2 pos, Vector2 centerPos, Vector2 aabbExt) {
                Vector2 closestPos = pos - centerPos;
                float fSqrDistance = 0;
                for (int i = 0; i < 2; i++) {
                    float fDelta;
                    if (closestPos[i] < -aabbExt[i]) {
                        fDelta = closestPos[i] + aabbExt[i];
                        fSqrDistance += fDelta * fDelta;
                    } else if (closestPos[i] > aabbExt[i]) {
                        fDelta = closestPos[i] - aabbExt[i];
                        fSqrDistance += fDelta * fDelta;
                    }
                }
                return fSqrDistance;
            }
        }

        // -------------------- 组件引用 --------------------
        private Node root;
        private RenderTexture clipRTAlbedoArray;
        private RenderTexture clipRTNormalArray;
        private VirtualCapture virtualCapture;

        // -------------------- 可调参数 --------------------
        public int rootSize = 1024;
        public Vector3 terrainOffset;
        public ComputeShader indexGenerator;
        public RenderTexture indexRT;

        void Start() {
            virtualCapture = GetComponent<VirtualCapture>();
            if (virtualCapture == null) {
                Debug.LogError("VT_Terrain 需要 VirtualCapture 组件！");
                return;
            }

            // 创建索引贴图
            indexRT = new RenderTexture(rootSize, rootSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            indexRT.Create();

            // 将 Compute Shader 的 Result 纹理绑定
            if (indexGenerator != null)
                indexGenerator.SetTexture(0, "Result", indexRT);
            else
                Debug.LogError("请为 VT_Terrain 指定 indexGenerator (Compute Shader)！");

            // 创建用于存储 VT 纹理数组的 RenderTexture（作为物理图集）
            int arraySize = 256 + 128;  // 与 VirtualCapture 中保持一致，或通过参数暴露
            clipRTAlbedoArray = new RenderTexture(VirtualCapture.virtualTextArraySize, VirtualCapture.virtualTextArraySize, 0, RenderTextureFormat.ARGB32) {
                volumeDepth = arraySize,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            clipRTAlbedoArray.Create();

            clipRTNormalArray = new RenderTexture(VirtualCapture.virtualTextArraySize, VirtualCapture.virtualTextArraySize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                volumeDepth = arraySize,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            clipRTNormalArray.Create();

            // 初始化四叉树
            root = Node.CreateRoot(rootSize, clipRTAlbedoArray.volumeDepth, OnLoadNodeData);

            // 将全局纹理传递给 Shader (URP)
            Shader.SetGlobalTexture("_VT_AlbedoTex", clipRTAlbedoArray);
            Shader.SetGlobalTexture("_VT_NormalTex", clipRTNormalArray);
            Shader.SetGlobalTexture("_VT_IndexTex", indexRT);
            Shader.SetGlobalInt("VT_RootSize", rootSize);
        }

        void OnDestroy() {
            if (indexRT != null) indexRT.Release();
            if (clipRTAlbedoArray != null) clipRTAlbedoArray.Release();
            if (clipRTNormalArray != null) clipRTNormalArray.Release();
        }

        void Update() {
            if (Camera.main == null) return;
            Vector2 camPos = new(
                Camera.main.transform.position.x - terrainOffset.x,
                Camera.main.transform.position.z - terrainOffset.z
            );
            Profiler.BeginSample("updateAllLeavesState");
            Node.UpdateAllLeavesState(camPos);
            Profiler.EndSample();
        }

        private void OnLoadNodeData(Node node) {
            Profiler.BeginSample("onLoadNodeData");

            // 获取当前地块的中心坐标和尺寸
            Vector2 center = new(node.x + node.size / 2.0f, node.z + node.size / 2.0f);
            int size = node.size;

            // 调用 VirtualCapture 渲染该地块的 albedo 和 normal 到临时 RT
            virtualCapture.VirtualCapture_MRT(center, size, out RenderTexture albedoRT, out RenderTexture normalRT);

            // 将渲染结果复制到纹理数组的对应 slice 中（同时复制 4 个 mip 级别，可根据需求调整）
            for (int i = 0; i < 4; i++) {
                Graphics.CopyTexture(albedoRT, 0, i, clipRTAlbedoArray, node.physicTexIndex, i);
                Graphics.CopyTexture(normalRT, 0, i, clipRTNormalArray, node.physicTexIndex, i);
            }

            // 更新索引贴图（通过 Compute Shader）
            if (indexGenerator != null) {
                indexGenerator.SetVector("value", new Vector4(node.physicTexIndex, node.x, node.z, node.size));
                indexGenerator.SetInt("offsetX", node.x);
                indexGenerator.SetInt("offsetZ", node.z);
                indexGenerator.Dispatch(0, size, size, 1);
                Debug.Log($"Dispatch: {node.physicTexIndex}, {node.x}, {node.z}, {node.size}");
            }

            Profiler.EndSample();
        }

#if UNITY_EDITOR
        // 辅助：在 Scene 视图中绘制四叉树节点（调试用）
        void OnDrawGizmos() {
            if (root == null || Node.currentAllLeaves == null) return;
            Gizmos.color = Color.green;
            foreach (var node in Node.currentAllLeaves) {
                Vector3 center = terrainOffset + new Vector3(node.x + node.size / 2.0f, 0, node.z + node.size / 2.0f);
                Gizmos.DrawWireCube(center, new Vector3(node.size, 0, node.size));
                UnityEditor.Handles.Label(center + Vector3.up, node.physicTexIndex.ToString());
            }
        }
#endif
    }
}
