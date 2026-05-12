using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace IcoSphere {
    // Runtime Virtual Texture
    public class Rvt : MonoBehaviour {
        QuadTree root;
        RenderTexture clipRTAlbedoArray;
        RenderTexture clipRTNormalArray;
        public int rootSize = 1024;
        public Vector3 terrainOffset;
        public ComputeShader indexGenerator;
        public RenderTexture indexRT;
        private VirtualCapture virtualCapture;

        void Start() {

            virtualCapture = GetComponent<VirtualCapture>();
            indexRT = new(rootSize, rootSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            indexRT.Create();
            indexGenerator.SetTexture(0, "Result", indexRT);

            int mipmapCount = (int)Mathf.Log(indexRT.width, 2);
            print(mipmapCount);

            clipRTAlbedoArray = new(VirtualCapture.virtualTextArraySize, VirtualCapture.virtualTextArraySize, 0, RenderTextureFormat.ARGB32) {
                volumeDepth = 256 + 128,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            clipRTAlbedoArray.Create();

            clipRTNormalArray = new(VirtualCapture.virtualTextArraySize, VirtualCapture.virtualTextArraySize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                volumeDepth = clipRTAlbedoArray.volumeDepth,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            clipRTNormalArray.Create();

            root = QuadTree.CreateRoot(rootSize, clipRTAlbedoArray.volumeDepth, OnLoadNodeData);
            Shader.SetGlobalInt("VT_RootSize", rootSize);
        }

        void OnDestroy() {
            if (indexRT != null) indexRT.Release();
            if (clipRTAlbedoArray != null) clipRTAlbedoArray.Release();
            if (clipRTNormalArray != null) clipRTNormalArray.Release();
        }

        void Update() {
            Shader.SetGlobalTexture("_VT_AlbedoTex", clipRTAlbedoArray);
            Shader.SetGlobalTexture("_VT_NormalTex", clipRTNormalArray);
            Shader.SetGlobalTexture("_VT_IndexTex", indexRT);

            Profiler.BeginSample("updateAllLeavesState");
            QuadTree.UpdateAllLeavesState(new Vector2(Camera.main.transform.position.x - terrainOffset.x, Camera.main.transform.position.z - terrainOffset.z));
            Profiler.EndSample();
        }

        private void OnLoadNodeData(QuadTree item) {
            Profiler.BeginSample("onLoadNodeData");

            virtualCapture.VirtualCapture_MRT(new Vector2(item.x + item.size / 2.0f, item.z + item.size / 2.0f), item.size, out RenderTexture albedoRT, out RenderTexture normalRT);
            for (int i = 0; i < 4; i++) {
                Graphics.CopyTexture(albedoRT, 0, i, clipRTAlbedoArray, item.physicTexIndex, i);
                Graphics.CopyTexture(normalRT, 0, i, clipRTNormalArray, item.physicTexIndex, i);
            }

            indexGenerator.SetVector("value", new Vector4(item.physicTexIndex, item.x, item.z, item.size));

            // 只处理 mipmap0 , 也可以选择写入每一级mipmap 根据实际开销对比 选择创建mipmaps开销 还是选择 shader采样的缓存命中低
            int rectSize = item.size;
            indexGenerator.SetInt("offsetX", item.x);
            indexGenerator.SetInt("offsetZ", item.z);
            indexGenerator.Dispatch(0, rectSize, rectSize, 1);
            Profiler.EndSample();

        }

#if UNITY_EDITOR
        void OnDrawGizmos() {
            if (root == null) {
                return;
            }
            Gizmos.color = Color.green;
            int leafCount = 0;
            foreach (QuadTree item in QuadTree.currentAllLeaves) {
                Gizmos.DrawWireCube(terrainOffset + new Vector3(item.x + item.size / 2.0f, 0, item.z + item.size / 2.0f), new Vector3(1, 0, 1) * item.size);
                UnityEditor.Handles.Label(terrainOffset + new Vector3(item.x + item.size / 2.0f, 0, item.z + item.size / 2.0f), item.physicTexIndex + "");
                leafCount++;
            }
            print("leafCount:" + leafCount);
            print("freeIndexCount:" + (clipRTAlbedoArray.volumeDepth - QuadTree.physicEmptyIndexQueue.Count));
        }
#endif
    }
}
