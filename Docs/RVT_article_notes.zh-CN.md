# 《大地形的一种简化 RVT》阅读笔记

来源：

- 知乎原文：https://zhuanlan.zhihu.com/p/552748937
- 可访问镜像：https://blog.uwa4d.com/archives/USparkle_RVT.html
- 文中关联仓库：https://github.com/jackie2009/unityRVTTerrain

说明：原文/镜像注明未经授权不要转载，因此这里只保留本地阅读笔记，不复制原文全文和图片。

## 核心思路

文章介绍的是一套面向 Unity 大地形的简化 Runtime Virtual Texture 方案。它没有实现完整虚拟纹理 feedback 流程，而是用更工程化的方式：

1. 用四叉树在平面 XZ 空间划分地形。
2. 当前叶节点代表需要缓存的 RVT 页面。
3. 每个活跃叶节点分配一个固定尺寸的 `Texture2DArray` 物理切片。
4. 当节点细分或合并时，重新生成该节点覆盖范围内的地形混合结果，并写入物理切片。
5. 填充 index texture/page table，让 final terrain shader 从世界坐标或地形 UV 找到物理切片和 tile 内 UV。

性能动机是：复杂地形材质通常每个像素要采样并混合很多层 albedo/normal。RVT 先把这些层 bake 成缓存贴图，最终渲染时主要采样缓存结果，避免每帧重复完整混合。

## 文章机制要点

- `currentAllLeaves`：当前帧所有叶节点。
- `nextAllLeaves`：更新 LOD 后的下一帧叶节点队列。
- `physicEmptyIndexQueue`：可用物理纹理数组索引队列。
- `onLoadData`：节点需要生成/加载贴图内容时触发。
- 每帧限制 split/merge 数量，避免瞬时卡顿。
- 贴图生成使用 Blit，而不是用相机重新渲染整套地形流程。
- index texture 的 texel 存物理切片、节点位置和尺寸；final shader 先采 index，再采 RVT cache。
- 斜面噪点需要多 mip cache 和基于稳定地形 UV 的导数修正，第一版可以先不做。

## 当前项目适配

`feature-zhihu-style-spherical-rvt` 分支没有直接照搬平面 XZ 四叉树，而是先做一版球面 lonlat 页面系统：

- `SphericalRvtManager` 维护 lonlat 虚拟页、有限物理 tile cache、dirty/ready 状态和 LRU 回收。
- `SphericalRvtIndex.compute` 写 page table/index texture。
- `SphericalRvtBake.compute` 只 bake dirty pages，目前输出 albedo cache。
- `Custom_ComputeShader_Tri.shader` 命中有效 page 时采 RVT cache，未命中时才回退到 per-area terrain sampling。

这版的目标是先验证文章的核心缓存管线：

```text
virtual page -> physical tile -> index texture -> final shader cache sample
```

后续可把 lonlat 虚拟空间替换为更适合球面的二十面体 face atlas、pack triangle cluster 或 area cluster。
