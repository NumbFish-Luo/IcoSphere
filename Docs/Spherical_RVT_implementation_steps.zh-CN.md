# 球面简化 RVT 实现步骤

目标：在当前 IcoSphere 球面地图上实现一套借鉴《大地形的一种简化 RVT》的球面版地形纹理缓存系统。实现不提交到 `main`，开发分支为 `feature-spherical-rvt-terrain`。

## 1. 分支与文档

1. 确认当前工作区状态。
2. 从 `main` 新建 `feature-spherical-rvt-terrain` 分支。
3. 把实现计划写入 `Docs/Spherical_RVT_implementation_steps.zh-CN.md`。

## 2. 地形纹理输入

1. 使用 `Assets/IcoSphere/Textures/Terrain` 下已有资源。
2. 建立地形类型枚举，例如 `Water`、`Sand`、`Plains`、`Mountain`、`Marsh`、`Hill`、`Dirt`、`River`。
3. 第一版先接入 albedo/diffuse 纹理，优先保证球面能显示不同地形。
4. 对缺失 diffuse 的地形提供 fallback，例如 `Water` 可先使用 mask 或颜色生成。
5. 对尺寸不一致的纹理采用运行时缩放/拷贝到统一尺寸的 `Texture2DArray`。

## 3. 每地块地形数据

1. 新增按 area id 索引的 `AreaTerrainData`。
2. 每个地块至少存：
   - `terrainId`
   - `tint`
   - `uvOffset`
   - `uvScale`
3. 增加公开 API：
   - `SetAreaTerrain(int areaId, TerrainType terrainType)`
   - `SetAreaTerrains(IReadOnlyList<int> areaIds, TerrainType terrainType)`
   - `GetAreaTerrain(int areaId)`
4. 默认地形可按纬度/高度简单初始化，后续再接地图生成器或编辑器刷地形。

## 4. 球面 RVT 虚拟空间

1. 第一版使用经纬度 UV 作为虚拟纹理空间：
   ```text
   sphere normal -> lon/lat -> uv(0..1)
   ```
2. 在该 UV 空间上做二维页面管理。
3. 横向 `u=0/1` seam 使用 wrap 处理。
4. 极区拉伸第一版接受，后续可升级为二十面体原始面 atlas 或地块聚类缓存。

## 5. RVT 页面管理

1. 新增 `SphericalRvtManager`。
2. 维护：
   - 活跃页面列表
   - 物理 tile 空闲索引
   - index texture
   - albedo `RenderTexture` array
3. 根据相机位置和页面中心估算屏幕重要性，选择需要更新的页面。
4. 第一版先用固定网格页面代替完整动态四叉树，确保管线跑通；之后再扩展 split/merge。
5. 每帧限制更新页面数量，避免卡顿。

## 6. 索引纹理

1. 新增 `SphericalRvtIndex.compute`。
2. 每个页面写入索引纹理覆盖区域。
3. 索引纹理 texel 存储：
   ```text
   physicalSlice, tileU, tileV, tileSize
   ```
4. shader 通过经纬度 UV 采样索引纹理，再计算物理 tile 内 UV。

## 7. RVT tile 生成

1. 新增 `SphericalRvtBake.compute`。
2. 对每个需要更新的页面：
   - 遍历 tile 像素。
   - 计算虚拟 UV。
   - 从地块地形查找图读取 terrain id。
   - 从地形源 `Texture2DArray` 采样。
   - 写入 RVT albedo array。
3. 第一版只写 albedo，后续再加入 height/mask/normal。

## 8. UV 到地块/地形查询

1. 为 bake 阶段准备一张 `terrainIdMap`。
2. `terrainIdMap` 的 UV 空间与 RVT 虚拟空间一致。
3. 初始化时把每个地块中心投影到经纬度 UV，并写入 terrain id。
4. 如果出现空洞，使用邻域填充或默认地形。
5. 后续如需精确地块边界，可改为 GPU 三角形/扇区光栅化生成 map。

## 9. 球体 shader 接入

1. 当前 shader 已经能算出当前像素所属 `vid`。
2. 新增 RVT 采样路径：
   ```text
   world pos -> lonlat uv -> _SphericalRvtIndexTex -> _SphericalRvtAlbedoArray
   ```
3. 保留 fallback：
   ```text
   vid -> areaTerrainBuffer[vid] -> terrain source texture array
   ```
4. 保留当前网格线和鼠标高亮叠加。

## 10. 场景绑定与验证

1. 在 `IcoSphere` 场景中绑定 `SphericalRvtManager`。
2. 运行时自动创建必要 RenderTexture/ComputeBuffer。
3. 验证：
   - 球体可见。
   - 网格线可见。
   - 不同地块能显示不同地形纹理。
   - RVT index/albedo 资源能被 shader 正确采样。
   - 鼠标高亮不被破坏。

## 11. 提交与推送

1. 检查 `git status`。
2. 运行能在当前环境执行的验证命令。
3. 提交到 `feature-spherical-rvt-terrain`。
4. 推送到远端同名分支。
