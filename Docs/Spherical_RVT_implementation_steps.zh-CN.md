# 球面知乎式简化 RVT 实现记录

目标：在 `feature-zhihu-style-spherical-rvt` 分支中，把之前“固定全局 RVT-like 原型”收敛为一版更接近《大地形的一种简化 RVT》的球面适配原型。它不是直接照搬平面 XZ 四叉树，而是在 IcoSphere 上先使用经纬度 lonlat 虚拟空间作为第一版页面空间。

参考文章：

- 知乎原文：https://zhuanlan.zhihu.com/p/552748937
- 可访问镜像：https://blog.uwa4d.com/archives/USparkle_RVT.html

## 已实现

1. 保留 `TerrainType` 和按 area id 存储的 `AreaTerrainData`。
2. 将地形贴图整理为三组明确的 `Texture2DArray`：
   - `*_d.png`：diffuse/albedo，作为颜色来源。
   - `*_h.png`：height，只用于 bake 阶段的高度明暗调制。
   - `*_m.png`：mask/材质参数/混合遮罩，只用于材质调制。
3. `Water` 没有 `Water_d.png`，因此 albedo 使用蓝色 fallback，不会把 `Water_h.png` 或 `Water_m.png` 当颜色贴图。
4. `SphericalRvtManager` 维护 lonlat 虚拟页表：
   - `pageId`
   - 虚拟 UV 矩形
   - 物理 `Texture2DArray` slice
   - dirty/ready/queued 状态
   - `lastUsedFrame`
5. RVT 物理缓存不再等于全局页面总数。当前实现使用有限 `physicalTileCount`，相机附近页面按需分配物理 slice；旧页面按 LRU 方式回收。
6. index texture/page table 中 inactive page 写入 `slice = -1`。final shader 看到无效 page 时回退到直接 per-area terrain 采样。
7. bake pass 在 albedo alpha 中编码 baked terrain id；final shader 会用真实 `vid -> AreaTerrainData.terrainId` 校验，避免 lonlat page cache 跨六边形/五边形地块污染。
8. `terrainIdMap` 不再由 2D flood fill 生成，改为先构建 lonlat texel 到最近球面 area center 的 `areaIdMap`，再从 area id 派生 terrain id。
9. bake pass 只处理 dirty pages，每帧由 `pagesToBakePerFrame` 限制更新数量。
10. final shader 命中 RVT page 且 terrain id 校验通过时采样 `_SphericalRvtAlbedoArray`；page 未命中或 terrain id 不一致时回退到按六边形的 per-area terrain sampling。
11. 主渲染 shader 不做 vertex 阶段地形几何高度，也不在 vertex 阶段采样地形贴图，避免重新触发 Unity shader compiler IPC 崩溃路径。
12. 鼠标高亮、area `vid` 判断、网格线 overlay 保持原路径。

## 当前数据流

```text
camera direction
  -> lonlat virtual uv
  -> choose wanted virtual pages
  -> allocate/reuse physical slices
  -> mark dirty pages
  -> SphericalRvtBake.compute writes albedo cache tile
  -> cache alpha stores baked terrain id
  -> SphericalRvtIndex.compute writes page table/index texture
  -> final shader samples page table
  -> valid slice and terrain id matches current vid: sample RVT albedo cache
  -> invalid/mismatched slice: fallback to vid -> AreaTerrainData -> terrain texture arrays
```

## 球面适配说明

第一版使用 lonlat UV：

```text
sphere normal/world position -> longitude/latitude -> uv(0..1)
```

这比平面 XZ quadtree 更符合当前球面数据，但仍不是最终最优方案。它的优点是实现简单、page table 可以复用 2D texture；缺点是极区拉伸和经度 seam 仍然存在。后续更合理的方向是：

- 二十面体原始面 atlas，每个 face 内做 page hierarchy。
- 按 pack triangle cluster 或 area cluster 做页面。
- 用更稳定的球面导数和多 mip cache 处理斜视角噪点。

## 未实现

1. 还没有按文章完整实现 quadtree split/merge 层级；当前是固定 lonlat grid + 相机附近工作集。
2. 还没有 normal/height/mask 多 render target cache；当前只输出 albedo cache。
3. 还没有 mip 链和 derivative 修正；斜角采样可能仍需要后续处理。
4. terrain id map 已改为最近 area center 的球面 Voronoi 近似，但仍不是和 fragment shader 完全同源的三角形扇区光栅化。
5. 未接入道路、贴花、编辑器刷地形等更完整的地形生产流程。

## 验证记录

- `dotnet build IcoSphere\Assembly-CSharp.csproj` 通过。
- 已检查 `C:\Users\w\AppData\Local\Unity\Editor\Editor.log`。日志中存在旧导入过程留下的 `Custom_ComputeShader_Tri.shader(227)` 语法错误和 shader compiler IPC 记录；后续同 shader 有成功导入/编译记录，当前代码没有重新加入 vertex texture/geometry height 路径。
