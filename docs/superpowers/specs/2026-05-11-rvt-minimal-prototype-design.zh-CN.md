# 最小运行时虚拟纹理原型设计

## 已阅读来源

- 本地保存的知乎文章：`C:/Users/D/Desktop/(20 封私信) 大地形的一种简化RVT - 知乎.html`
- 原文章：`https://zhuanlan.zhihu.com/p/552748937`
- 参考仓库：`https://github.com/jackie2009/unityRVTTerrain`
- 球面映射论文：`https://www.jcgt.org/published/0007/02/01/paper-lowres.pdf`

本地知乎文章和仓库说明了一个关键事实：这不是标准完整虚拟纹理管线。作者有意去掉了 feedback pass 和复杂 page table 管理，改成距离驱动的四叉树叶节点、`Texture2DArray` 物理池，以及一张索引贴图。索引贴图的每个虚拟纹素存储物理 layer 和 tile rect 数据。

## 背景

当前项目通过预计算的二进制 IcoSphere 模型数据渲染球体。Unity 通过 `Pack.Read()` 读取 `Resources/Bin/pack_arr_N.bytes`，构建每个三角形的 `InstanceData`，在 `IcoSphere.compute` 中做剔除，并通过 `Custom_ComputeShader_Tri.shader` 使用 indirect instancing 渲染。

参考仓库面向 Unity 5.6 built-in terrain，不能直接移植到当前 Unity 2022 URP 球体项目。它依赖 `TerrainData`、内置 terrain splat shader、surface shader 和平面 `x/z` 坐标。真正有价值的是简化 RVT 架构：四叉树 LOD、生成物理 tile、索引贴图，以及 draw shader 中的显式 mip 选择。

Unity Streaming Virtual Texturing 必须保持关闭。本设计是项目自有运行时虚拟纹理，不是 Unity SVT。

## 目标

- 保留现有二进制 IcoSphere 模型管线。
- 实现文章和仓库中的简化 RVT 思路，而不是完整 SVT 风格虚拟纹理系统。
- 将平面 terrain 数据源替换为能基于本项目二进制球体数据和球面虚拟坐标工作的数据源。
- 第一版把平面 `x/z/size` 四叉树坐标替换为经纬度虚拟地址空间。
- 使用 `Texture2DArray` 作为生成 tile 内容的物理池。
- 使用索引贴图把虚拟 UV cell 映射到物理 tile layer 和 tile rect 数据。
- 保留映射边界，方便后续把经纬度替换为 cube-sphere。

## 非目标

- 不使用 Unity Streaming Virtual Texturing。
- 第一版原型不做 GPU feedback pass。
- 不做传统 page table，也不在 fragment 阶段线性查询 page。
- 第一版原型不要求异步磁盘流式加载。
- 第一版不做最终商业级驻留策略、优先级队列、压缩或 tile 流式文件格式。
- 不直接依赖 Unity `Terrain`。
- 不立刻重写为 cube-sphere。

## 关键设计决策

1. RVT 是由项目脚本和 shader 自己管理的运行时系统。
2. Tile 需求由四叉树和相机距离驱动，匹配原文的简化设计。
3. 第一版虚拟地址空间使用经纬度 UV：
   - 经度映射到 `[0, 1]` 范围内的 `u`
   - 纬度映射到 `[0, 1]` 范围内的 `v`
4. 四叉树节点由 `(level, x, y)` 和虚拟 tile rect 标识。
5. 运行时代码写索引贴图。shader 先采样索引贴图，再采样物理纹理数组。
6. 物理 tile layer 存储生成的 tile 内容和少量固定 mip。原实现使用 4 级生成 mip 来减少斜面噪点。
7. Tile 内容生成与 tile 选择隔离。Debug 色块、世界地图 tile、后续材质烘焙 tile 是不同 tile source。
8. 坐标映射隔离在接口后，后续可以从经纬度切换到 cube-sphere，而不替换 cache、物理池、索引贴图或 shader 采样模型。

## 组件设计

### `RvtTileId`

小型值类型：

- `int level`
- `int x`
- `int y`

职责：

- 生成子节点 id。
- 生成父节点 id。
- 提供稳定哈希和相等判断。
- 转换为归一化虚拟 tile 矩形。

### `IRvtAddressMapping`

用于把球体位置和相机状态转换为虚拟纹理地址的接口。

职责：

- `Vector2 WorldToVirtualUv(Vector3 worldPos)`
- `RvtTileRect GetTileRect(RvtTileId id)`
- `Vector2 CameraToVirtualUv(Camera camera, Transform globeTransform)`
- `float EstimateTileError(RvtTileId id, Vector2 focusUv, float cameraDistance)`

初始实现：

- `LonLatRvtAddressMapping`
- 使用现有 `Misc.ToLonLatUv` 中的经纬度数学。
- 处理 `u = 0/1` 处的经度环绕。
- 将纬度夹到 `[0, 1]`。

未来实现：

- `CubeSphereRvtAddressMapping`
- 使用六个面和每个面内部的四叉树坐标。
- 目标是降低极区形变和接缝问题。

### `RvtQuadTree`

用于 tile LOD 选择的原型四叉树。

职责：

- 维护当前叶子 tile。
- 拆分靠近相机焦点的 tile。
- 合并远离相机焦点的 tile。
- 为新激活的 tile 发出 bake/upload 请求。
- 为不再激活的 tile 发出释放请求。

与参考仓库的关系：

- 当前项目的 `QuadTree.cs` 已经非常接近仓库里的 `VT_Terrain.Node`。
- 保留按帧限制 split/merge 的思路。
- 将静态全局状态改为 `RvtManager` 实例持有。
- 将平面 `x/z/size` 语义改为虚拟 tile rect 坐标。
- 增加 `u = 0/1` 处的经度环绕行为。

原型 LOD 规则：

- 计算相机焦点的虚拟 UV。
- 测量 tile 矩形到焦点的环绕 UV 距离。
- 用类似原文“距离转 size”的对数规则转换为期望 level。
- 将 level 限制在 `minLevel` 和 `maxLevel` 之间。
- 每帧最多拆分 `maxSplitsPerFrame` 个节点，避免帧尖峰。

### `RvtPhysicalTexturePool`

持有生成后的物理 tile 纹理。

原型物理池：

- `RenderTexture` 或 `Texture2DArray`，维度为 `TextureDimension.Tex2DArray`
- 一个数组存 albedo/debug color
- 一个可选数组存 normal
- 固定 `tileSize`，为了贴近原文可用 512，为了快速球体原型可用 256
- 固定 `physicalTileCount`
- `useMipMap = true`
- `autoGenerateMips = false`
- 生成 mip 数默认 4

职责：

- 分配和释放物理 layer。
- 将生成出的 tile mip levels 拷贝到指定数组 layer。
- 绑定 `_RvtAlbedoTexArray` 和可选 `_RvtNormalTexArray`。

仓库中的关键行为：

- 每个四叉树节点获得一个物理 layer index。
- tile load 时，生成的 albedo 和 normal RT 会被拷贝到分配的 layer。
- 原代码为每个 tile layer 拷贝 mip `0..3`。

### `RvtIndexTexture`

GPU 可读的索引贴图，用于把虚拟 cell 映射到物理 tile 数据。

职责：

- 持有一张 index `RenderTexture`。
- 每次 tile 加载时，填充该 tile 覆盖的虚拟 rect。
- 存储 shader 采样需要的数据。
- 使用 Point 过滤，不使用 mip。

原型编码：

- `r`：物理 layer index
- `g`：虚拟 rect origin x，单位是 root cell
- `b`：虚拟 rect origin y，单位是 root cell
- `a`：虚拟 rect size，单位是 root cell

仓库对应实现：

- `VT_index_generator.compute` 写入 `int4(physicTexIndex, x, z, size)`。
- `VT_Terrain.shader` 采样 `_VT_IndexTex`，根据世界位置和 tile rect 算 local UV，再采样物理数组。

球面适配：

- `x`、`y` 和 `size` 是虚拟 UV 网格 cell，不是世界米制坐标。
- 对于跨 `u = 0/1` 的经度环绕 tile，需要拆成两段写 index，或用后续映射避免跨界 rect。

### `RvtTileSource`

产生生成后的 tile 内容。

第一版数据源：

- `DebugRvtTileSource`
- 生成确定性的 tile 颜色和 id/level 标记。
- 用于验证四叉树选择、索引贴图写入、数组上传和 shader 采样。

第二版数据源：

- `TextureRvtTileSource`
- 从一张可读世界地图纹理中按经纬度 UV 切 tile。
- 用于对照现有 `MappingTex` 工作流验证方向。

后续数据源：

- `BakedTerrainRvtTileSource`
- 使用 Blit/MRT 将 diffuse、normal 和可选 mask 数据烘焙到 tile render targets。
- 仓库中的 `VirtualCapture` 和 `VT_Terrain_Blit.shader` 是最近的参考。
- 真正的 RVT 性能收益在这里体现：大量地形图层采样会被折叠为少量固定运行时采样。

### `RvtManager`

拥有运行时 RVT 系统的场景组件。

序列化字段：

- `Camera targetCamera`
- `Material targetMaterial`
- `int minLevel`
- `int maxLevel`
- `int rootCellSize`
- `int tileSize`
- `int physicalTileCount`
- `int generatedMipCount`
- `int maxSplitsPerFrame`
- `Texture2D sourceTexture`，用于贴图数据源原型
- `RvtDebugMode debugMode`

原型默认值：

- `minLevel = 0`
- `maxLevel = 6`
- `rootCellSize = 1 << maxLevel`
- `tileSize = 256` 用于快速球体迭代；如果更贴近原文则用 `512`
- `physicalTileCount = 384` 用于贴近原文；第一场景可先用 `64`
- `generatedMipCount = 4`
- `maxSplitsPerFrame = 1`

职责：

- 初始化 mapping、quadtree、物理池、索引贴图和 tile source。
- 每帧更新 active tile。
- bake 或生成新的 tile 数据。
- 将生成出的 tile mip 拷贝到物理数组 layer。
- 填充索引贴图对应区域。
- 绑定 `_RvtIndexTex`、`_RvtAlbedoTexArray`、`_RvtNormalTexArray`、`_RvtRootCellSize`、`_RvtTileSize` 和相关常量。

## Shader 接入

现有球体 shader 应增加一条 RVT 采样路径。

采样流程：

1. Fragment shader 接收世界坐标。
2. 使用与 C# 相同的映射约定将世界坐标转换为虚拟 UV。
3. 以 Point 过滤采样 `_RvtIndexTex`。
4. 解码物理 layer、虚拟 rect origin 和虚拟 rect size。
5. 将虚拟 UV 转换为 root cell 坐标。
6. 使用 `(virtualCell - rectOrigin) / rectSize` 计算 tile-local UV。
7. 从连续虚拟坐标的导数计算显式 mip，不从物理 array UV 计算。
8. 从导数 mip 估计中减去 tile 的 base LOD，因为更大 rect 渲染到同样 tile size 已经天然代表更低细节。
9. 将显式 mip 限制到生成 mip 范围内。
10. 用 `SAMPLE_TEXTURE2D_ARRAY_LOD` 采样物理 albedo 和可选 normal 数组。

原文中的重要规则：

- 不要从物理 array UV 计算 `ddx`/`ddy`。相邻 tile 可能从 local UV `1` 跳到 `0`，会造成错误的大导数和接缝。
- 要使用连续 terrain/virtual 坐标计算导数。

原型 mip 公式：

```text
virtualCell = virtualUv * rootCellSize
baseLod = log2(index.rectSize)
dx = ddx(virtualCell * tileSize)
dy = ddy(virtualCell * tileSize)
screenMip = 0.5 * log2(max(dot(dx, dx), dot(dy, dy)))
sampleMip = clamp(screenMip - baseLod + mipBias, 0, generatedMipCount - 1)
```

## 数据流

1. `RvtManager.Start`
   - 创建物理 albedo 和可选 normal 数组
   - 创建索引贴图
   - 创建根四叉树 tile
   - 为 root 分配物理 layer
   - 生成 root tile 内容
   - 将生成的 mip levels 拷贝到物理数组
   - 填充索引贴图 root 区域
   - 将资源绑定到材质

2. `RvtManager.Update`
   - 从 viewport 中心向球体做 raycast 获取相机焦点；如果中心射线没有击中球体，则 fallback 到归一化相机位置
   - 将焦点映射到虚拟 UV
   - 更新四叉树
   - 处理 split/merge load requests
   - 重新生成变化的 tile 内容
   - 更新索引贴图对应区域

3. `Custom_ComputeShader_Tri.shader`
   - 渲染现有 IcoSphere 几何
   - 将世界位置映射到虚拟 UV
   - 采样索引贴图
   - 使用显式 mip 采样物理纹理数组
   - 根据 debug mode 混合或替换当前颜色路径

## 经纬度四叉树细节

根节点：

- `level = 0`
- `x = 0`
- `y = 0`
- 覆盖 `u = [0, 1]`，`v = [0, 1]`

子节点：

- child 0：左下 tile 矩形
- child 1：右下 tile 矩形
- child 2：左上 tile 矩形
- child 3：右上 tile 矩形

距离：

- `u` 距离跨日期变更线环绕。
- `v` 距离在极区夹紧。
- 焦点到 tile 的距离使用焦点到 tile 矩形的最近点。

已知缺陷：

- 经纬度 tile 在极区附近存在明显形变。
- 日期变更线处的 tile 需要显式环绕处理。
- 第一版可以接受这些缺陷，因为映射是可替换的。

## 为什么不直接使用当前 `QuadTree.cs`

当前 `QuadTree.cs` 非常接近仓库里的 `VT_Terrain.Node`，因此是有用起点。但它仍需适配：

- 它存储的是平面 `x`、`z` 和 `size`。
- 它使用静态队列和静态回调。
- 它假设只有一棵全局树。
- 它不处理经度环绕或极区行为。
- 它不会更新索引贴图。
- 它没有暴露 tile source、物理池和 shader 绑定边界。

## 风险与缓解

### 原实现是 Unity 5.6 Built-In Terrain

风险：

- 仓库 shader 代码无法直接兼容 Unity 2022 URP。

缓解：

- 移植架构，不直接搬 built-in surface shader。
- 在现有球体 shader 中实现 URP HLSL 采样。
- Blit/MRT 烘焙保持为单独 source 模块。

### 极区形变

风险：

- 经纬度四叉树在极区附近 tile 分布很差。

缓解：

- 保留 `IRvtAddressMapping`。
- 第一版验收只证明 RVT 机制。
- 后续阶段替换为 cube-sphere 映射。

### 斜面噪点

风险：

- 距离驱动的四叉树 LOD 在屏幕导数需要更低 mip 的斜面上可能过锐。

缓解：

- 每个物理 tile layer 生成多级 mip。
- shader 中从连续虚拟坐标计算显式 mip。
- 避免从 local array UV 计算导数。

### 索引贴图精度

风险：

- 如果用普通归一化颜色格式存 layer、origin、size，可能出现精度误差。

缓解：

- 在 Unity 和目标平台允许时优先使用整数或高精度格式。
- 使用 Point 过滤，不使用 mip。
- 添加物理 layer、rect origin 和 rect size 的 debug view。

### 范围膨胀到完整 VT

风险：

- Feedback pass、异步 IO、层级 page table 和压缩会让原型失控。

缓解：

- 第一版原型以 debug/world-map tile 能在球体上通过索引贴图和物理数组正确渲染为结束点。
- 完整 VT 能力保留为未来工作。

## 验证计划

Unity 手动验证：

1. 打开 IcoSphere 场景或新的 RVT 原型场景。
2. 将 `RvtManager` 挂到场景对象上。
3. 绑定现有 IcoSphere 材质。
4. 使用 `DebugRvtTileSource` 运行。
5. 移动相机环绕球体。
6. 确认近处叶节点拆分、远处叶节点合并。
7. 可视化 `_RvtIndexTex`，确认区域中存储了 physical layer、origin 和 size。
8. 确认日期变更线环绕不会留下明显未加载条带。
9. 切换到 `TextureRvtTileSource`。
10. 确认世界地图方向与当前 `MappingTex` 工作流一致。
11. 开启显式 mip debug，确认斜视角不会产生强噪点。

代码验证：

- `RvtTileId` 父子关系数学的 edit mode 测试。
- 已知轴向点经纬度映射的 edit mode 测试。
- 跨 `u = 0/1` 的环绕距离 edit mode 测试。
- 虚拟 tile rect 到 index texture 写入 rect 的 edit mode 测试。
- shader/debug 验证显式 mip 计算。
- 小型 play mode smoke test：创建 manager、上传 root tile、填充索引贴图、绑定 shader 资源。

## 里程碑

### 里程碑 1：寻址与四叉树

- 添加 `RvtTileId`。
- 添加 `LonLatRvtAddressMapping`。
- 按当前 `QuadTree.cs` 模式适配 `RvtQuadTree`。
- 添加 tile 数学、mapping、wrapped distance 的 edit mode 测试。

### 里程碑 2：物理池与索引贴图

- 添加 `RvtPhysicalTexturePool`。
- 添加 `RvtIndexTexture`。
- 添加 compute shader 或等价 GPU 路径填充索引贴图 rect。
- 添加 index texture channels 的 debug 可视化。

### 里程碑 3：Debug Tile Source 与 Shader 采样

- 添加 `DebugRvtTileSource`。
- 生成并上传 root 和 child debug tiles。
- 在球体 shader 中添加 RVT 采样路径。
- 基于连续虚拟坐标使用显式 mip 选择。

### 里程碑 4：贴图驱动 Tile

- 添加 `TextureRvtTileSource`。
- 从可读世界地图中切 tile。
- 对照现有经纬度映射验证方向。

### 里程碑 5：材质烘焙数据源

- 添加原型 `BakedTerrainRvtTileSource`。
- 使用 Blit/MRT 将 diffuse 和 normal 输出烘焙到物理纹理数组。
- 适配仓库中的 `VirtualCapture` 概念，但不依赖 Unity built-in `Terrain`。

## 完成标准

最小原型完成时应满足：

- Unity 项目设置 `virtualTexturingSupportEnabled` 保持关闭。
- 现有二进制 IcoSphere 数据仍通过 `Pack.Read()` 加载。
- 球体能用 RVT debug tile 渲染。
- 相机移动会改变 active quadtree leaves。
- 物理纹理 layer 能被分配和复用。
- 索引贴图区域与驻留 tile 区域对应。
- shader 通过索引贴图间接寻址采样物理数组。
- 显式 mip 选择能降低斜面噪点。
- 世界地图数据源可以通过 RVT 显示，且不使用 Unity SVT。
