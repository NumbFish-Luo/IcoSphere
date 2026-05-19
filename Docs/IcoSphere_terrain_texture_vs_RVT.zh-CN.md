# IcoSphere 地块纹理方案与简化 RVT 的区别

## 当前 IcoSphere 渲染方式

当前球体渲染器绘制的是大量三角形实例。在片元 shader 里，代码会判断当前像素属于哪个六边形/五边形地块，并得到一个 `vid`。这个 `vid` 就是地块 ID。

当前颜色查询可以理解成：

```text
pixel -> vid -> _AllInstancesData[vid].col.rgb
```

这也是鼠标刷色能工作的原因：射线拾取得到 `vid`，然后 compute shader 把颜色写入 `_AllInstancesData[vid].col`。

## 我之前建议的方案

针对“每个地块都有自己的地形和纹理”这个目标，更轻量的方案是：

```text
areaId -> AreaTerrainData -> terrainId -> Texture2DArray slice
```

也就是按地块存数据，而不是按三角形存数据：

```csharp
struct AreaTerrainData {
    public uint terrainId;
    public uint flags;
    public Vector2 uvOffset;
    public Vector2 uvScale;
}
```

shader 路径会变成：

```text
pixel -> vid -> areaTerrainBuffer[vid] -> sample terrain texture array
```

这种方式保留当前的 indirect rendering 架构，不创建每地块 GameObject，不创建每地块 Material，也不增加 draw call。

## 简化 RVT 文章的做法

文章里的方法是一个地形缓存系统：

```text
world position / terrain UV -> index texture -> physical texture array slice -> cached albedo/normal tile
```

它在 XZ 地形平面上维护一棵四叉树。靠近相机的区域细分为更小节点，远离相机的区域合并为更大节点。每个活跃叶节点分配一个固定尺寸 `Texture2DArray` 物理槽位。系统会把该节点覆盖范围内的地形层混合结果 Blit 到这个槽位，最终 shader 再采样缓存 tile。

这个方案主要适合大块连续地形，尤其是每个像素要反复混合很多层地表材质的情况。

## 主要区别

| 对比项 | 按地块 terrainId 方案 | 文章里的简化 RVT |
| --- | --- | --- |
| 粒度 | IcoSphere 地块 / 六边形 / 五边形 | XZ 平面四叉树叶节点 |
| 查询键 | 球面 shader 已经算出的 `vid` 地块 ID | 用地形 UV 采样索引纹理 |
| 存储数据 | 地形类型、颜色微调、UV 变换、可选高度参数 | 烘好的 albedo/normal tile 缓存 |
| 运行时更新 | 只有地块地形变化时更新 | 相机移动导致四叉树细分/合并时更新 |
| 纹理来源 | 静态地形 `Texture2DArray` | 运行时 Blit/MRT 合成的缓存 tile |
| 最适合 | 离散地块身份明确的地图 | 多层混合的大型连续地形 |
| 工程量 | 低到中 | 高，尤其移植到球面时更高 |
| CPU 参与 | 上传 buffer 后很少 | 需要维护四叉树 LOD 和 tile 更新调度 |
| GPU 成本 | 最终 shader 直接采样地形数组 | 最终 shader 采样索引纹理，再采样 RVT 缓存 |
| LOD/mip | 先依赖普通纹理 mipmap | 需要自定义缓存 LOD 和导数修正 |

## 对当前项目的适配性

对当前 IcoSphere 球面地图来说，按地块 terrainId 的方案更贴合你的需求：

- 世界本身已经是离散地块：每个可玩区域都有 area id。
- 拾取、颜色、邻接查询、国家映射都已经围绕 area id 工作。
- 你的目标更像“这个地块是平原/山地/水域等”，不一定是连续地形 splat 混合。
- 调试更直接：看 `areaId`，看 `terrainId`，看采样到的地形纹理。

文章里的 RVT 在这些目标下会更有吸引力：

- 需要非常高频的连续表面细节。
- 每个可见像素要混合很多层地形材质。
- 需要把道路/贴花烘进地形纹理。
- 需要随相机距离动态调整地形纹理分辨率。
- 或者你要做一条独立的平面大地形渲染线。

## 把 RVT 直接搬到球面上的成本

文章方案不能直接无痛搬到这个球面上：

1. 文章默认的是平面 XZ 地形和规则地形 UV。
2. 当前球体用的是二十面体细分拓扑，有六边形/五边形地块。
3. 四叉树需要改造成：
   - 每个二十面体面一套层级；
   - 或球面 UV tile 系统，并处理 seam；
   - 或自定义的地块聚类层级。
4. 索引纹理需要稳定的球面参数化，否则 seam 和导数 mip 选择会很麻烦。
5. 缓存 tile 生成也要知道如何把球面地块数据光栅化/Blit 到每个 tile。

所以 RVT 很强，但它解决的是比“每个离散地块有一个地形类型和纹理”更大、更复杂的问题。

## 推荐路径

建议先实现更简单的按地块地形方案：

1. 新增按 area id 索引的 `AreaTerrainData` buffer。
2. 新增 `TerrainType` 枚举，以及 `SetAreaTerrain(areaId, type)` 这样的编辑器/运行时 API。
3. 把现有地形纹理整理成一个或多个 `Texture2DArray` 资产。
4. 修改 `Custom_ComputeShader_Tri.shader`，让 `vid` 不只取颜色，而是选择地形纹理数据。
5. 保留当前网格线，作为地形纹理之上的 overlay。
6. diffuse 采样跑通后，再加 normal/height 支持。

这个方案跑通后，再评估是否需要 RVT 类缓存。如果后续地形 shader 因为每像素混合很多层而变贵，再借鉴文章的 cache 思路；但在球面上更建议做“地块聚类缓存”或“二十面体面缓存”，而不是直接照搬平面 XZ 四叉树。
