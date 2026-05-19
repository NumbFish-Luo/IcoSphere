# IcoSphere 地块材质与球面 RVT 的边界

## 两条路径

当前 IcoSphere 渲染会在 fragment shader 中判断像素属于哪个六边形/五边形地块，并得到 `vid`。`vid` 就是 area id。

直接地块材质路径是：

```text
pixel -> vid -> AreaTerrainData -> terrainId -> terrain Texture2DArray
```

这条路径适合做 fallback，也适合验证“每个地块有自己的 terrain type”。但它不是 RVT：如果每个像素都直接混合多张地形材质，最终 shader 仍然每帧承担复杂材质成本。

知乎简化 RVT 的核心路径是：

```text
world position / terrain uv
  -> index texture/page table
  -> physical Texture2DArray slice
  -> cached baked tile
```

它把复杂地形材质混合提前 bake 到 tile cache，final shader 只采缓存。

## 本分支采用的折中

`feature-zhihu-style-spherical-rvt` 不是继续旧的“全局固定 RVT-like 覆盖”原型，而是做第一版球面页面系统：

- 虚拟空间：lonlat page grid。
- 物理缓存：有限数量的 albedo tile slices。
- 页面选择：按相机方向选择附近 pages。
- 更新策略：dirty page 才 bake，每帧限制 bake 数量。
- page table：index texture 写入 physical slice 和 page rect。
- fallback：page 未 ready 或未驻留时，才走直接 per-area terrain sampling。

这样 final shader 在 RVT 命中时不会重复采样 albedo/height/mask 组合，只采 `_SphericalRvtAlbedoArray`。

## 贴图通道约定

`Assets/IcoSphere/Textures/Terrain` 中贴图含义固定如下：

| 后缀 | 含义 | 是否作为颜色贴图 |
| --- | --- | --- |
| `*_d.png` | diffuse/albedo | 是 |
| `*_h.png` | height | 否 |
| `*_m.png` | mask/材质参数/混合遮罩 | 否 |

特别注意：`Water` 没有 `Water_d.png`。水面颜色使用 fallback blue albedo，`Water_h.png` 和 `Water_m.png` 只进入 height/mask 调制，不能直接贴到球面当颜色。

## 为什么不是平面 XZ 四叉树

原文章以平面大地形为目标，默认世界空间可以按 XZ 做 quadtree。IcoSphere 是球面二十面体细分数据，直接使用 XZ 会在背面、极区和 seam 上产生不稳定页面关系。

第一版用 lonlat 是工程折中：

- 方便把 sphere position 映射到 2D page table。
- 能快速验证 dirty pages、physical slices、index texture、bake cache、final sampling 这一整条 RVT 管线。
- 明确保留后续替换空间：二十面体 face atlas、pack triangle cluster、area cluster page table。

## 当前限制

- terrain id map 不是精确地块边界 rasterize，而是 area center 投影后 flood fill 的近似。
- albedo cache 已实现，normal/height/mask cache 未实现。
- 没有按文章做多 mip cache 和导数修正。
- lonlat seam 和极区拉伸只是第一版可接受问题，不是最终方案。
