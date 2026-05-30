using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace IcoSphere {
    // 非异步处理的分帧机制四叉树管理器: https://zhuanlan.zhihu.com/p/552748937
    public class QuadTreeManager {
        // ---- 成员变量 ----
        private Queue<QuadTree> nowNodes; // 当前帧节点
        private Queue<QuadTree> nextNodes; // 下一帧节点
        private Queue<int> freePhyTexIdxes; // 可用的物理地址队列, 建议创建约512个长度, 根据内存富余情况增加这个值
        private UnityAction<QuadTree> onLoadData; // 加载贴图资源时回调
        private int splitCount; // 当前帧已经细分的次数

        // ---- 静态变量/常量 ----
        // 每帧可细分的最大次数, 避免在同一帧加载太多导致卡顿
        private const int eventFrameSplitCountMax = 1;

        // ---- 公有函数 ----
        // 创建根节点和数据初始化
        // rootSize: 根节点贴图尺寸, 例如1024
        // phyTexIdxCount: 可用的物理地址队列长度, 例如512, 根据内存富余情况增加这个值
        // onLoadData: 加载贴图资源时回调
        public QuadTree CreateRoot(int rootSize, int phyTexIdxCount, UnityAction<QuadTree> onLoadData) {
            this.onLoadData = onLoadData;
            nowNodes = new Queue<QuadTree>();
            nextNodes = new Queue<QuadTree>();
            freePhyTexIdxes = new Queue<int>();
            for (int i = 0; i < phyTexIdxCount; ++i) {
                freePhyTexIdxes.Enqueue(i);
            }

            // 取出物理地址队列给根节点 (先进后出, 这里取出的是0)
            QuadTree root = QuadTree.NewRoot(rootSize, DequeuePhyTexIdx());
            nowNodes.Enqueue(root);
            return root;
        }

        // 根据相机位置, 每帧更新所有叶节点状态
        public void UpdateNodesState(Vector2 camPos) {
            // 清空下一帧相关数据
            splitCount = 0;
            nextNodes.Clear();

            // 遍历所有当前叶节点执行状态更新
            while (nowNodes.Count > 0) {
                QuadTree node = nowNodes.Dequeue();

                // 优先判断父节点是否可合并, 这个值是由子节点lod算出来的 (Merge函数中计算)
                if (node.parentMerged) {
                    continue;
                }

                // 判断合并, 执行Merge函数
                QuadTree parent = node.parent;
                if (parent != null) {
                    int parentLodSize = QuadTreeMath.CalcLodSize(camPos, parent.x, parent.z, parent.size);
                    bool allBrothersAreLeaf = true;
                    for (int i = 0; i < 4; i++) {
                        if (parent.children[i].IsLeaf == false) {
                            allBrothersAreLeaf = false;
                        }
                    }
                    if (parent.size <= parentLodSize && allBrothersAreLeaf) {
                        Merge(parent, nextNodes, onLoadData);
                        continue;
                    }
                }

                // 判断细分, 执行Split函数
                int size = node.size;
                int lodSize = QuadTreeMath.CalcLodSize(camPos, node.x, node.z, size);
                if (size == lodSize) {
                    // 当前尺寸刚好符合lod需要的尺寸, 自己保持为叶子不动
                    nextNodes.Enqueue(node);
                } else if (size > lodSize) {
                    // 当前尺寸大于lod需要的尺寸, 需要细分出4个子对象
                    // 不马上细分, 而是合并完了再细分, 这样同时存在的叶子数就比较小, 否则需要更多的对象数量
                    if (splitCount++ < eventFrameSplitCountMax && freePhyTexIdxes.Count >= 3) {
                        Split(node, nextNodes, onLoadData);
                    } else {
                        nextNodes.Enqueue(node);
                    }
                } else {
                    nextNodes.Enqueue(node);
                }
            }

            // 交换两个元素的语法糖, 元组交换, 避免new一个空队列产生GC
            (nextNodes, nowNodes) = (nowNodes, nextNodes);
        }

#if UNITY_EDITOR
        // 在Scene视图中绘制四叉树节点
        public void OnDrawGizmos(Vector3 terrainOffset) {
            if (nowNodes == null) {
                return;
            }
            Gizmos.color = Color.green;
            foreach (QuadTree node in nowNodes) {
                Vector3 center = terrainOffset + new Vector3(node.x + node.size / 2.0f, 0, node.z + node.size / 2.0f);
                Gizmos.DrawWireCube(center, new Vector3(node.size, 0, node.size));
                UnityEditor.Handles.Label(center + Vector3.up, node.phyTexIdx.ToString());
            }
        }
#endif

        // ---- 私有函数 ----
        // 取出物理纹理地址
        private int DequeuePhyTexIdx() => freePhyTexIdxes.Count == 0 ? -1 : freePhyTexIdxes.Dequeue();

        // 返还物理纹理地址
        private void EnqueueNodePhyTexIdx(QuadTree node) {
            if (node.phyTexIdx > -1) {
                freePhyTexIdxes.Enqueue(node.phyTexIdx);
            }
        }

        // 合并node, 队列放入parent不放自己, 并跳过后面3个同级node计算, 也就不会放入队列
        private void Merge(QuadTree node, Queue<QuadTree> nextNodes, UnityAction<QuadTree> onLoadData) {
            nextNodes.Enqueue(node);
            for (int i = 0; i < 4; i++) {
                EnqueueNodePhyTexIdx(node.children[i]); // 返还物理纹理地址
                node.children[i].parent = null;
                node.children[i].parentMerged = true;
            }

            // 回收子对象物理索引, 分配自己一个地址索引然后加载这个索引对应的资源
            node.phyTexIdx = DequeuePhyTexIdx();
            onLoadData(node);
            node.children = null;
        }

        // 细分node, 给自己增加4个子node, 但自己不算做叶子, 所以不放队列
        private void Split(QuadTree node, Queue<QuadTree> nextNodes, UnityAction<QuadTree> onLoadData) {
            EnqueueNodePhyTexIdx(node); // 返还物理纹理地址

            // 这里后面最好做下对象池, 避免一直new
            for (int i = 0; i < 4; ++i) {
                QuadTree child = node.Split(i, DequeuePhyTexIdx()); // 分配物理纹理地址
                nextNodes.Enqueue(child);
                onLoadData(child);
            }
        }
    }
}
