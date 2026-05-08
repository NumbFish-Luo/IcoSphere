using System;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    // 四叉树, 本质为节点: https://zhuanlan.zhihu.com/p/552748937
    // 或许应该创建一个QuadTreeManager来提取出来静态变量, 以提高复用性?
    public class QuadTree {
        // -------- 静态变量 --------
        // 当前帧所有叶节点
        public static Queue<QuadTree> currentAllLeaves;

        // 下一帧所有叶节点
        private static Queue<QuadTree> nextAllLeaves;

        // 可用的物理地址队列
        public static Queue<int> physicEmptyIndexQueue;

        // 某节点需要加载贴图资源时回调, 因为这种加载一般不坐在树结构内
        private static Action<QuadTree> onLoadData;

        // 当前帧已经细分的次数
        private static int splitCount;

        // 每帧可细分的最大次数, 与splitCount一起, 避免在同一帧加载太多导致卡顿, 实际是一种简单又高性能的分帧机制
        // 分帧加载机制替代异步加载, 极大简化了维护 (如果用异步的加载, 相邻部分加载完成替换索引会出现脏数据等问题)
        private const int eventFrameSplitCountMax = 1;

        // -------- 成员变量 --------
        // 四叉树最最基础的数据, 记录这个格子坐标和尺寸
        public int x;
        public int z;
        public int size;

        // 描述四叉树树结构关系的引用, 类似Transform
        public QuadTree[] children;
        public QuadTree parent;

        // 判断是否是叶节点
        public bool IsLeaf => children == null;

        // 当前帧parent是否被合并过了, 因为遍历某节点的4个子节点顺序是不可控的, 避免出现一个子节点判断应该合并, 但其他子节点却判断为细分出现矛盾
        public bool parentMerged;

        // 当前节点的物理贴图 (Texture2DArray) 索引, 用他来渲染自己覆盖的区域
        public int physicTexIndex = -1;

        // -------- 静态函数 --------
        private static int DequeuePhysicIndex() => physicEmptyIndexQueue.Count == 0 ? -1 : physicEmptyIndexQueue.Dequeue();

        private static void ResetPhysicIndex(QuadTree node) {
            if (node.physicTexIndex > -1) {
                physicEmptyIndexQueue.Enqueue(node.physicTexIndex);
            }
        }

        // 创建根节点
        // 一般手动创建根节点, 然后通过规则让他自己内部去细分或合并
        // 也常在这里做些初始化或静态数据创建
        public static QuadTree CreateRoot(int rootSize, int physicIndexCount, Action<QuadTree> onLoadData) {
            // 注册全局回调
            QuadTree.onLoadData = onLoadData;

            // 新建全局数据
            currentAllLeaves = new Queue<QuadTree>();
            nextAllLeaves = new Queue<QuadTree>();
            physicEmptyIndexQueue = new Queue<int>();

            // 准备好物理地址队列
            for (int i = 0; i < physicIndexCount; ++i) {
                physicEmptyIndexQueue.Enqueue(i);
            }

            QuadTree root = new() {
                // 取出物理地址队列给根节点 (先进后出, 这里取出的是0)
                physicTexIndex = DequeuePhysicIndex(),

                // 尺寸设置
                size = rootSize,

                // x和z不设置, 默认0
                // 不论真实场景如何, 四叉树内部都是从(0, 0)点开始往x+, z+方向区计算的
                // 外部的实际情况可根据offset调整, 不在内部考虑外界的特殊性
                x = 0,
                z = 0,

                // 默认值
                children = null,
                parent = null,
                parentMerged = false
            };

            // 存入现在唯一的叶节点 (同时为根节点)
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

            // 交换两个元素的语法糖, 元组交换
            (nextAllLeaves, currentAllLeaves) = (currentAllLeaves, nextAllLeaves);
        }

        // -------- 成员函数 --------
        private void UpdateState(Vector2 camPos) {
            if (parentMerged) {
                return;
            }

            if (parent != null) {
                int parent_lodSize = parent.CalculateLodSize(camPos);
                bool allBrothersAreLeaf = true;
                for (int i = 0; i < 4; i++) {
                    if (parent.children[i].IsLeaf == false) allBrothersAreLeaf = false;
                }
                if (parent.size <= parent_lodSize && allBrothersAreLeaf) {
                    parent.Merge();
                    return;
                }
            }

            int lodSize = CalculateLodSize(camPos);

            //当前尺寸刚好符合  lod需要的尺寸 自己保持为叶子 不动
            if (size == lodSize) {
                nextAllLeaves.Enqueue(this);
            }
            //当前尺寸 大于 lod需要的尺寸 需要细分出4个子对象
            else if (size > lodSize) {
                //不马上细分 而是合并完了再细分 这样 同时存在的叶子数就比较小 否则需要更多的对象数量
                if (splitCount++ < eventFrameSplitCountMax && physicEmptyIndexQueue.Count >= 3) {
                    Split();

                } else {
                    nextAllLeaves.Enqueue(this);
                }
            } else {
                nextAllLeaves.Enqueue(this);
            }
        }

        // 细分node 给自己增加4个子node 但自己不算做叶子 所以不放队列
        private void Split() {
            ResetPhysicIndex(this);

            children = new QuadTree[4];

            //为了可读性 这里坐标设置 不写循环里, 也不做成对象池， 后面最好做下
            children[0] = new QuadTree() { x = x, z = z };
            children[1] = new QuadTree() { x = x + size / 2, z = z };
            children[2] = new QuadTree() { x = x, z = z + size / 2 };
            children[3] = new QuadTree() { x = x + size / 2, z = z + size / 2 };
            for (int i = 0; i < 4; i++) {
                children[i].parent = this;
                children[i].size = size / 2;
                children[i].physicTexIndex = DequeuePhysicIndex();
                nextAllLeaves.Enqueue(children[i]);
                onLoadData(children[i]);

            }
        }

        // 合并node  队列放入parent 不放自己，并跳过后面3个同级node计算 也就不会放入队列
        private void Merge() {
            nextAllLeaves.Enqueue(this);

            //var tempParent = parent;
            for (int i = 0; i < 4; i++) {
                ResetPhysicIndex(children[i]);
                children[i].parent = null;
                children[i].parentMerged = true;

            }
            physicTexIndex = DequeuePhysicIndex();
            onLoadData(this);
            children = null;

            //Node[] brothers = parent.children;

            //parent.children = null;

            //nextAllLeaves.Enqueue(parent);
            //onLoadData(parent);
            //var tempParent = parent;
            //for (int i = 0; i < 4; i++)
            //{
            //    resetPhysicIndex(brothers[i]);
            //    brothers[i].parent = null;
            //}
            //tempParent.physicTexIndex = getPhysicIndex();

            //currentAllLeaves.Dequeue();
            //currentAllLeaves.Dequeue();
            //currentAllLeaves.Dequeue();
        }

        private int CalculateLodSize(Vector2 camPos) {
            var dis = CalculateClosestPoint(camPos, new Vector2(x + size / 2, z + size / 2), new Vector2(size / 2, size / 2));
            dis = Mathf.Max(1, Mathf.Sqrt(dis));
            // int lod = Mathf.Max(0, (int)(Mathf.Log(dis, 2) + 0.5));
            int lod = Mathf.Max(0, (int)(Mathf.Log(dis, 2) + 0.5));

            return 1 << lod;
        }

        float CalculateClosestPoint(Vector2 pos, Vector2 centerPos, Vector2 aabbExt) {
            // compute coordinates of point in box coordinate system
            Vector2 closestPos = pos - centerPos;

            // project test point onto box
            float fSqrDistance = 0;
            for (int i = 0; i < 2; i++) {
                float fDelta;
                if (closestPos[i] < -aabbExt[i]) {
                    fDelta = closestPos[i] + aabbExt[i];
                    fSqrDistance += fDelta * fDelta;
                    closestPos[i] = -aabbExt[i];
                } else if (closestPos[i] > aabbExt[i]) {
                    fDelta = closestPos[i] - aabbExt[i];
                    fSqrDistance += fDelta * fDelta;
                    closestPos[i] = aabbExt[i];
                }
            }
            return fSqrDistance;
        }
    }
}
