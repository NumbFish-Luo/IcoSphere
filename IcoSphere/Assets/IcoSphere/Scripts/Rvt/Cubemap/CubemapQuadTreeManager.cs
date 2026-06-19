using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace IcoSphere {
    // 管理6个面的四叉树, 处理lod更新, 纹理索引池, 分帧加载
    public class CubemapQuadTreeManager {
        // 每个面独立维护节点队列, 数组长度固定为6
        private readonly Queue<CubemapQuadTree>[] nowNodes; // 当前帧叶子节点
        private readonly Queue<CubemapQuadTree>[] nextNodes; // 下一帧叶子节点
        private CubemapQuadTree[] roots = new CubemapQuadTree[6]; // 长度6，存储每个面的根节点

        // 6面共享资源
        private readonly Queue<int> freePhyTexIdxes; // 可用物理纹理索引池
        private readonly UnityAction<CubemapQuadTree> onLoadData; // 节点纹理加载回调

        // lod控制参数
        private const int MAX_SPLIT_PER_FRAME = 2; // 每帧最多细分次数, 总6个面
        private int totalSplitThisFrame; // 本帧已细分总次数

        // 坐标转换器
        private readonly CubemapToWorldMapper mapper;

        // lod系数, 系数越小越精细, 更远距离才降低细节, 例如当距离 = 1000时, 需要边长 ≈ 1000 * LOD_FACTOR, 节点原始边长若为大于此值就需要细分
        const float LOD_FACTOR = 0.25f;

        // 初始化
        public CubemapQuadTreeManager(int rootSize, float planetRadius, int texArrCapacity, UnityAction<CubemapQuadTree> onLoadData) {
            mapper = new CubemapToWorldMapper(rootSize, planetRadius);
            this.onLoadData = onLoadData;

            // 初始化纹理索引池
            freePhyTexIdxes = new Queue<int>();
            for (int i = 0; i < texArrCapacity; ++i) {
                freePhyTexIdxes.Enqueue(i);
            }

            // 为6个面分别创建队列
            nowNodes = new Queue<CubemapQuadTree>[6];
            nextNodes = new Queue<CubemapQuadTree>[6];
            for (int i = 0; i < 6; ++i) {
                nowNodes[i] = new Queue<CubemapQuadTree>();
                nextNodes[i] = new Queue<CubemapQuadTree>();
            }

            // 创建6个根节点
            for (int face = 0; face < 6; ++face) {
                int idx = DequeuePhyTexIdx(); // 分配物理索引
                CubemapQuadTree root = CubemapQuadTree.NewRoot((CubemapFace)face, 0, 0, rootSize, idx);
                roots[face] = root; // 缓存根节点
                nowNodes[face].Enqueue(root);
                onLoadData?.Invoke(root);
            }
        }

        // 每帧更新所有面的lod
        public void UpdateAllFaces(Vector3 camPos) {
            totalSplitThisFrame = 0;

            Vector3 sphereCenter = Vector3.zero;
            float camDist = (camPos - sphereCenter).magnitude; // 相机到球心的距离
            bool isInsideSphere = camDist < mapper.GetRadius();

            for (int face = 0; face < 6; ++face) {
                bool faceVisible = true;
                // 只有在球体外部才剔除背面
                if (!isInsideSphere) {
                    Vector3 faceNormal = mapper.GetFaceNormal((CubemapFace)face);
                    if (Vector3.Dot(faceNormal, camPos.normalized) < 0) {
                        faceVisible = false;
                    }
                }

                if (!faceVisible) {
                    // 塌缩到根节点并回收所有子节点
                    CollapseFaceToRoot(face);
                    continue;
                }

                // 执行正常的 LOD 更新
                UpdateFace(face, camPos);
            }

            // 交换队列, 无gc
            for (int face = 0; face < 6; ++face) {
                (nextNodes[face], nowNodes[face]) = (nowNodes[face], nextNodes[face]);
            }
        }

        // 更新单个面
        private void UpdateFace(int face, Vector3 camPos) {
            nextNodes[face].Clear();

            while (nowNodes[face].Count > 0) {
                CubemapQuadTree node = nowNodes[face].Dequeue();

                // 父节点已被合并，跳过
                if (node.parentMerged) {
                    continue;
                }

                // 合并检查
                CubemapQuadTree parent = node.parent;
                if (parent != null) {
                    // 计算父节点需要的LOD尺寸 (世界空间边长)
                    float parentWorldSize = mapper.GetNodeWorldSize(parent);
                    bool allSiblingsLeaf = true;
                    for (int i = 0; i < 4; ++i) {
                        if (!parent.children[i].IsLeaf) {
                            allSiblingsLeaf = false;
                        }
                    }

                    float neededWorldSize = ComputeNeededWorldSize(camPos, parent);
                    if (parentWorldSize <= neededWorldSize && allSiblingsLeaf) {
                        Merge(parent, nextNodes[face]);
                        continue;
                    }
                }

                // 细分检查
                float nodeWorldSize = mapper.GetNodeWorldSize(node);
                float needWorldSize = ComputeNeededWorldSize(camPos, node);
                if (Mathf.Approximately(nodeWorldSize, needWorldSize)) {
                    // 尺寸刚好合适，保持叶子
                    nextNodes[face].Enqueue(node);
                } else if (nodeWorldSize > needWorldSize) {
                    // 需要进一步细分
                    if (totalSplitThisFrame < MAX_SPLIT_PER_FRAME && freePhyTexIdxes.Count >= 4) {
                        Split(node, nextNodes[face]);
                        totalSplitThisFrame++;
                    } else {
                        nextNodes[face].Enqueue(node);
                    }
                } else {
                    // 当前节点比需求还小 (理论上不会发生，因为叶子节点)
                    nextNodes[face].Enqueue(node);
                }
            }
        }

        // 将指定面塌缩为根节点叶子, 回收所有子节点纹理索引
        private void CollapseFaceToRoot(int face) {
            CubemapQuadTree root = roots[face];
            if (!root.IsLeaf) {
                ReleaseAllChildren(root);
                root.children = null;
            }
            // 确保根节点拥有有效纹理索引
            if (root.phyTexIdx == -1) {
                root.phyTexIdx = DequeuePhyTexIdx();
                onLoadData?.Invoke(root);
            }

            // 清空当前队列, 只保留根节点
            nowNodes[face].Clear();
            nowNodes[face].Enqueue(root);

            // 清空下一帧队列, 确保交换后nowNodes仍然只有根节点
            nextNodes[face].Clear();
            nextNodes[face].Enqueue(root);
        }

        // 递归释放一个节点的所有子节点, 不释放节点本身
        private void ReleaseAllChildren(CubemapQuadTree node) {
            if (node.IsLeaf) {
                return;
            }
            for (int i = 0; i < 4; ++i) {
                CubemapQuadTree child = node.children[i];
                if (child != null) {
                    ReleaseAllChildren(child); // 先递归释放孙节点
                    EnqueuePhyTexIdx(child.phyTexIdx); // 回收当前子节点的纹理索引
                    child.parent = null; // 断开引用
                    child.parentMerged = false; // 重置标记
                }
            }
            node.children = null;
        }

        // 合并节点
        private void Merge(CubemapQuadTree parent, Queue<CubemapQuadTree> nextQueue) {
            nextQueue.Enqueue(parent);

            // 回收子节点的纹理索引
            for (int i = 0; i < 4; ++i) {
                EnqueuePhyTexIdx(parent.children[i].phyTexIdx);
                parent.children[i].parent = null;
                parent.children[i].parentMerged = true;
            }

            // 为父节点分配新纹理索引并加载数据
            parent.phyTexIdx = DequeuePhyTexIdx();
            onLoadData?.Invoke(parent);
            parent.children = null;
        }

        // 细分节点
        private void Split(CubemapQuadTree node, Queue<CubemapQuadTree> nextQueue) {
            EnqueuePhyTexIdx(node.phyTexIdx); // 回收当前节点的纹理索引

            for (int i = 0; i < 4; ++i) {
                int childIdx = DequeuePhyTexIdx();
                CubemapQuadTree child = node.Split(i, childIdx);
                nextQueue.Enqueue(child);
                onLoadData?.Invoke(child);
            }
        }

        // 根据视点计算节点需要的世界空间边长, lod
        // 逻辑：需要的边长 = 节点中心到相机的距离 * (屏幕像素误差阈值) / 焦距
        // 简化版：需要的边长 = 距离 * 常量系数，使得距离越远需要的边长越大（细节越低）
        private float ComputeNeededWorldSize(Vector3 cameraPos, CubemapQuadTree node) {
            Vector3 worldCenter = mapper.GetNodeWorldCenter(node);
            float distance = Vector3.Distance(cameraPos, worldCenter);
            float needed = distance * LOD_FACTOR;
            // 限制最小为1，最大为根节点世界边长
            float maxWorldSize = mapper.GetRootWorldSize();
            return Mathf.Clamp(needed, 1f, maxWorldSize);
        }

        // 纹理索引池操作
        private int DequeuePhyTexIdx() => freePhyTexIdxes.Count == 0 ? -1 : freePhyTexIdxes.Dequeue();

        private void EnqueuePhyTexIdx(int idx) {
            if (idx >= 0) {
                freePhyTexIdxes.Enqueue(idx);
            }
        }

#if UNITY_EDITOR
        // 调试绘制
        public void OnDrawGizmos(int faceFilter = -1) {
            if (nowNodes == null) return;
            Gizmos.color = Color.green;

            // 六个面分别绘制
            for (int f = 0; f < 6; ++f) {
                if (faceFilter != -1 && f != faceFilter) {
                    continue;
                }
                foreach (CubemapQuadTree node in nowNodes[f]) {
                    Vector3 center = mapper.GetNodeWorldCenter(node);
                    Vector3 size3D = mapper.GetNodeWorldSize3D(node);
                    Gizmos.DrawWireCube(center, size3D);
                    UnityEditor.Handles.Label(center, $"{node.phyTexIdx}\n{node.size}");
                }
            }
        }
#endif
    }
}
