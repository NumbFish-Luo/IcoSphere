# 2026-05-12 RVT 代码修改记录

## 范围

本记录用于跟踪按 `docs/superpowers/specs/2026-05-11-rvt-minimal-prototype-design.zh-CN.md` 实现的最小 RVT 原型代码改动。

## 当前目标

- 先跑通 Debug tile 到索引贴图，再到球体 shader 采样的最小链路。
- 保持 Unity `virtualTexturingSupportEnabled` 关闭。
- 不实现 Unity SVT。
- 不实现完整 feedback pass。
- 不提交 git commit。

## 修改记录

### 设计与计划文档

- `docs/superpowers/specs/2026-05-11-rvt-minimal-prototype-design.md`
  - 按保存的知乎 HTML 和 `unityRVTTerrain` 仓库重新校正方案。
  - 明确这版采用“索引贴图 + Texture2DArray”的简化 RVT，不采用 lookup/page-table SVT 框架。
- `docs/superpowers/specs/2026-05-11-rvt-minimal-prototype-design.zh-CN.md`
  - 中文版设计说明。
  - 补充经纬度四叉树、二进制模型数据源接口、已知设计缺陷与商业级 RVT 缺口。
- `docs/superpowers/plans/2026-05-12-rvt-minimal-prototype.md`
  - 实施计划，按 addressing、physical pool/index/source、manager、shader、验证拆分。

### EditMode 测试

- `IcoSphere/Assets/IcoSphere/Tests/EditMode/IcoSphere.EditModeTests.asmdef`
  - 新增 EditMode 测试程序集，引用 `IcoSphere.Rvt`。
- `IcoSphere/Assets/IcoSphere/Tests/EditMode/RvtAddressingTests.cs`
  - 覆盖 `RvtTileId` 子节点/父节点、归一化 tile rect、日期变更线 wrap 距离。
  - 覆盖经纬度映射轴约定：`x` 正方向为 `u=0.5`，`z` 正方向为 `u=0.75`，`y` 为纬度。
- `IcoSphere/Assets/IcoSphere/Tests/EditMode/RvtRuntimeDataTests.cs`
  - 覆盖物理层分配/释放复用。
  - 覆盖索引负载从 tile 到最细 index grid 的映射。
  - 覆盖 debug tile source 的确定性输出。
  - 覆盖 `RvtManager.BuildWantedTiles()` 的 root-to-focus 链路、经度 wrap 和纬度 clamp。

### RVT 运行时代码

- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/IcoSphere.Rvt.asmdef`
  - 新增 RVT runtime 程序集，避免测试程序集依赖默认 `Assembly-CSharp`。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtTileId.cs`
  - 新增不可变 tile id：`level/x/y`，含边界校验、子节点、父节点、归一化 rect、相等比较。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtTileRect.cs`
  - 新增归一化虚拟贴图矩形。
  - 增加按 U 方向 wrap 的距离计算，用于经度日期变更线附近的 tile 选择。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/IRvtAddressMapping.cs`
  - 新增地址映射接口，为后续经纬度映射或 paper 中 cube/sphere 映射切换留入口。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/LonLatRvtAddressMapping.cs`
  - 新增经纬度映射实现：`atan2(z, x)`、`asin(y)`。
  - 提供 camera focus 到虚拟 UV 的 fallback 逻辑。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtPhysicalTexturePool.cs`
  - 新增物理 `Texture2DArray` layer 池。
  - 支持 deterministic layer 分配、释放复用、padding 后 tile 尺寸、可选创建 `Texture2DArray`。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtIndexTexture.cs`
  - 新增 `RvtIndexPayload`。
  - 将 resident tile 编码为 `(layer, originX, originY, size)`，用于 shader 从索引贴图恢复物理层和局部 UV。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/IRvtTileSource.cs`
  - 新增 tile source 接口。
  - 这是后续接入你的二进制模型数据读取器的替换点。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/DebugRvtTileSource.cs`
  - 新增 deterministic debug tile source。
  - 当前用 hash 颜色和棋盘格填充 tile，用于先跑通 RVT 链路。
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtManager.cs`
  - 新增最小 RVT manager。
  - 每帧根据视口中心射线或 camera 位置计算经纬度 focus UV。
  - 构建 root-to-focus tile 链，分配物理层，上传 debug tile 到 `Texture2DArray`。
  - 用 CPU 重写 RGBAFloat 索引 `Texture2D`，然后绑定到目标材质。
  - 暂未修改 Unity 项目设置，未开启 Unity Virtual Texturing。

### Shader 改动

- `IcoSphere/Assets/IcoSphere/Shaders/Custom_ComputeShader_Tri.shader`
  - 新增 `_UseRvt`、`_RvtIndexTex`、`_RvtAlbedoTexArray`、tile 尺寸、padding、mip bias 等材质参数。
  - 新增与 C# 一致的 `ToLonLatUv()`。
  - 新增 `SampleRvtAlbedo()`：读取索引贴图，计算 tile 内局部 UV，按 padding 映射到物理数组采样。
  - `_UseRvt <= 0.5` 时完全保留原有国家/六边形颜色路径。

## 验证记录

- 已运行 Unity 2022.3.55f1c1 EditMode 测试：
  - `rvt-editmode-task1g-results.xml`: 4/4 passed。
  - `rvt-editmode-task2-green-results.xml`: 7/7 passed。
  - `rvt-editmode-task3-green-results.xml`: 9/9 passed。
  - `rvt-editmode-task4-results.xml`: 9/9 passed。
  - `rvt-editmode-final-clean-results.xml`: 9/9 passed。
- `rvt-editmode-final-clean.log` 未匹配到 `Shader error` / `Shader warning` / `error CS`。

## 当前限制

- 当前仍是最小 RVT 原型，不是商业级完整 RVT。
- 未开启 Unity `virtualTexturingSupportEnabled`，也未使用 Unity SVT。
- 暂无 GPU feedback pass、请求队列优先级、异步 IO、tile baking cache、跨帧预算控制。
- 当前 index texture 由 CPU 重写；后续 tile 数量变大时应改成 compute shader 局部更新或分块更新。
- 当前 resident tile 选择是 root-to-focus 链路，不是完整屏幕可见区域 coverage。
- 当前 tile source 是 debug 颜色；你的二进制模型数据需要在 `IRvtTileSource` 后面接入实际采样/烘焙逻辑。
- 经纬度四叉树在两极有面积畸变。若后续视觉误差或 tile 密度不可接受，应切换到 paper 中的 cube/sphere 映射。
