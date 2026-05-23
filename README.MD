# IcoSphere

## 一、简介

使用大量三角形组成球体，参考：[catch 22 - Andreas Kahler's blog: Creating an icosphere mesh in code](http://blog.andreaskahler.com/2009/06/creating-icosphere-mesh-in-code.html)

## 二、配置

需要打开深度图渲染选项，默认的UPR工程里面有一个叫URP-HighFidelity的渲染管线配置文件，勾选其Rendering/Depth Texture选项即可

## 三、实现细节

分为C++部分和Unity部分，C++生成二进制模型文件给Unity应用

> 文档由AI生成，我已对部分不确定的内容已做了此标记：**（AI生成，待确认）**

### 3.1 C++部分：IcoSphereBytesFileCreator

#### 3.1.1 简介

使用C++20标准，用于生成几何信息，并以二进制形式文件保存（`pack_arr_{n}.bytes`，`n`为递归级别数字），功能包括：

- 生成单位球面上的二十面体基础网格，并通过递归细分得到任意精度的球面网格。
- 自动构建每个顶点周围的邻接三角形信息（六边形/五边形环状结构）。
- 计算每个三角形相邻三角形的中心点（投影到球面），可用于渲染或物理模拟。
- 支持将完整的网格数据（顶点、三角形、邻接关系、中心点等）序列化为二进制文件，并支持从文件恢复。
- 使用缓存机制避免重复计算细分过程中的中间顶点，提升性能。

| 模块名                | 文件            | 功能说明                                                     |
| :-------------------- | :-------------- | :----------------------------------------------------------- |
| `Math.Vec3`           | `Vec3.ixx`      | 3D 向量（float），支持基本运算、点积、叉积、长度、归一化等。 |
| `Mesh.Tri`            | `Tri.ixx`       | 三角形，由三个顶点索引构成。                                 |
| `Mesh.Abut`           | `Abut.ixx`      | 边邻接信息，存储共享一条边的两个三角形索引。                 |
| `IcoSphere.VertCache` | `VertCache.ixx` | 顶点缓存键，用于细分时避免重复计算中间点或中心点。           |
| `IcoSphere.HexAbuts`  | `HexAbuts.ixx`  | 环绕一个顶点的邻接关系（最多 6 条边/三角形，对应六边形或五边形）。 |
| `IcoSphere.Pack`      | `Pack.ixx`      | 球体网格数据包，包含顶点、三角形、邻接关系、中心点等，支持二进制 IO。 |
| `IcoSphere.IcoSphere` | `IcoSphere.ixx` | 核心生成器类，提供静态方法 `NewPackAndSave()` 生成指定细分级别的球体并保存。 |
| 主程序                | `Main.cpp`      | 示例：生成递归 5 次的球体网格。                              |

#### 3.1.2 使用方法

生成球体网格：

```cpp
import IcoSphere.IcoSphere;

int recursion = 3;  // 细分次数，0 表示原始二十面体（20 个三角形）
auto pack = IcoSphere::IcoSphere::NewPackAndSave(recursion);
```

- `NewPackAndSave(recursion)` 会：
  - 尝试从 `Output/pack_arr_{recursion}.bytes` 读取已存在的二进制数据。
  - 如果不存在，则从 0 级开始逐步细分并保存每一级的结果。
  - 最后返回当前递归次数的 `Pack` 对象。

使用 `Pack` 数据：

```cpp
// 顶点数组
const std::vector<Math::Vec3>& vertices = pack.verts;

// 三角形数组
const std::vector<Mesh::Tri>& triangles = pack.tris;

// 每个顶点的邻接关系
const std::vector<IcoSphere::HexAbuts>& abuts = pack.abuts;

// 每个三角形三个邻接三角形的中心点（用于插值等）
const std::vector<Math::Vec3>& centers = pack.ctrs; // 长度为 tris.size() * 3

// 相邻三角形的索引
const std::vector<Mesh::Tri>& adjTris = pack.adjTris; // 每个存储 (t01, t12, t20)
```

手动保存/加载：

```cpp
pack.Save("mydata.bytes");
Pack newPack;
newPack.Read("mydata.bytes");
```

#### 3.1.3 二进制文件格式

文件以小端字节序存储（适用于 x86/x64 平台）。

文件头（Header）:

6 个 `int32_t`

| 字段         | 含义                     |
| :----------- | :----------------------- |
| vertsSize    | `verts` 数组元素个数     |
| trisSize     | `tris` 数组元素个数      |
| abutsSize    | `abutsData` 数组元素个数 |
| ctrsSize     | `ctrs` 数组元素个数      |
| adjTrisSize  | `adjTris` 数组元素个数   |
| posVertsSize | `posVerts`数组元素个数   |

数据体（Body）:

1. **verts**：`vertsSize` 个 `Vec3`，每个 `Vec3` 为 3 个 `float`
2. **tris**：`trisSize` 个 `Tri`，每个 `Tri` 为 3 个 `int32_t`
3. **abutsData**：`abutsSize` 个 `HexAbuts::Kvs`，更多细节如下：
   - 每个 `Kvs` 是固定长度为 `KVS_MAX_SIZE`（=6）的 `Kv` 数组
   - 每个 `Kv` 是 `pair<int32_t, Abut>`
   - 其中 `Abut` 为 2 个 `int32_t`
   - 序列化时每个 `Kv` 连续写入 `3` 个 `int32_t`（第一个为顶点索引，后两个为三角形索引），因此每个 `Kvs` 占用 `6 * 3 * sizeof(int32_t)` 字节
   - 未使用的条目用 `(-1, -1, -1)` 填充
   - `abutsData`最终会按顶点下标序号排序，但是对于`-1`非法顶点序号，会排序到最后面
4. **ctrs**：`ctrsSize` 个 `Vec3`
5. **adjTris**：`adjTrisSize` 个 `Tri`
6. **posVerts**：（todo...）

`Pack.ixx`文件注释中有相关描述：

```cpp
// 二进制模型文件格式
// Header (6 * int32_t):
//   [vertsSize] [trisSize] [abutsSize] [ctrsSize] [adjTrisSize] [posVertsSize]
// Body:
//   verts   : vertsSize    * Vec3          (3 * float)
//   tris    : trisSize     * Tri           (3 * int32_t)
//   abuts   : abutsSize    * HexAbuts::Kvs (6 * 3 * int32_t)
//   ctrs    : ctrsSize     * Vec3          (3 * float)
//   adjTris : adjTrisSize  * Tri           (3 * int32_t)
//   posVerts: posVertsSize * PosVert       (3 * float + 1 * int32_t)
```

#### 3.1.4 算法细节

- **原始二十面体**：12 个顶点（基于黄金比例），20 个三角形。
- **细分策略**：每个三角形划分为 9 个小三角形（每条边三等分，连接对应分点，并取三角形中心点归一化到球面）。使用 `VertCache` 缓存中间点，避免重复计算。
- **邻接关系构建**：`CalcAbuts()` 为每个顶点记录相邻的边和三角形；`HexAbuts` 内部使用 `std::map<int32_t, Abut>` 存储，最多 6 项。
- **中心点计算**：`CalcCtrs()` 计算每个三角形三个邻接三角形的重心（投影到球面），结果可用于平滑着色等。

#### 3.1.5 编译与运行

使用Visual Studio 2022编译即可

1. 将所有 `.ixx` 文件添加到项目中（如果已添加则忽略这一步）。
2. 项目属性 → C/C++ → 语言 → **C++ 语言标准**：`/std:c++latest`。
3. 启用 **C++ 模块**（同一页面下：“启用 C++ 模块” 选 “是”）。
4. 主程序 `Main.cpp` 使用 `import IcoSphere.IcoSphere;`。
5. 编译运行，将在 `Output/` 目录下生成 `pack_arr_0.bytes`、`pack_arr_1.bytes` … `pack_arr_N.bytes`。

#### 3.1.6 注意事项

- 二进制文件读写使用了 `reinterpret_cast`，在跨平台（不同字节序或内存对齐）时可能存在问题。当前仅保证 x86/x64 平台正常工作
- `HexAbuts::KVS_MAX_SIZE` 固定为 6，适用于规则六边形结构；极点的顶点可能只有 5 个邻接边，未使用的条目以 `-1` 填充；`abutsData`最终会按顶点下标序号排序，但是对于`-1`非法顶点序号，会排序到最后面
- 细分次数过高会导致顶点数爆炸（约 `20 * 9^recursion`），请注意内存限制

#### 3.1.7 todo...

**`Pack::Read` 中使用临时对象 `HexAbuts::Kvs(n)` 赋值给已有元素，与 `resize(n)` 相比哪个更好？**

- **临时对象赋值**：会构造一个新 `vector` 然后移动赋值（在 C++11 后 `vector` 有移动赋值运算符）。通常开销是 O(n) 的元素构造 + 一次移动赋值。对于固定小尺寸（6）来说，性能差异可忽略。
- **`resize(n)`**：直接在原 `vector` 上调整大小，如果原大小已经为 `n` 则无操作；否则可能重新分配并默认构造新元素。它避免了临时对象的分配，但依然需要构造元素。在 `Kvs` 尺寸很小的情况下，两种方式几乎无差别。
- **建议**：如果追求极致性能且 `abutsData` 很大（例如数百万个顶点），`resize(n)` 可能略好一些，因为它不需要临时 `vector` 的分配和释放。但目前写法足够清晰，保持现状也无妨。

### 3.2 Unity部分：IcoSphere

#### 3.2.1 简介

加载由 C++ 后端生成的二十面球体二进制网格数据，并通过 **Compute Shader + GPU Instancing** 实现高性能的球体绘制（支持百万级三角形）。主要特性：

- 支持从 `Resources/Bin/pack_arr_{recursion}.bytes` 读取二进制网格（顶点、三角形、邻接中心点等）。
- 每个三角形作为一个实例，通过 GPU Instancing 渲染。
- Compute Shader 执行**视锥剔除**、**背面剔除**和**射线-三角形相交检测**。
- 支持**纹理映射**：根据世界地图图片，将每个顶点的颜色（即国家颜色）自动赋值。
- 提供编辑器工具 **CountryColorDrawer**：管理国家颜色表，支持刷色、保存配置、生成映射。
- 渲染效果包括六边形边缘线（通过投影邻接中心点实现）和鼠标高亮当前六边形区域。

#### 3.2.2 环境依赖

- Unity 2022.3.54（需支持 Compute Shader）
- 图形 API：DirectX 11/12、Vulkan、Metal（支持 Compute Shader 和 StructuredBuffer）
- .NET 4.x 或以上
- 目标平台：Windows / macOS / 支持 Compute Shader 的移动设备（需 Shader Model 5.0）

#### 3.2.3 模块说明

| 脚本                                 | 功能                                                         |
| :----------------------------------- | :----------------------------------------------------------- |
| `IcoSphere.cs`                       | 主控制器：加载 mesh 和 Pack 数据，管理 ComputeBuffer，每帧调用 Compute Shader 剔除和绘制。 |
| `Pack.cs`                            | 二进制数据解析器：从 Resources 读取 `.bytes` 文件并还原为顶点、三角形、邻接结构。 |
| `HexAbuts.cs` / `Abut.cs` / `Tri.cs` | 数据结构体，与 C++ 侧兼容，用于存储网格拓扑信息。            |
| `Misc.cs`                            | 工具类：随机颜色生成、经纬度转换、二进制读写扩展等。         |
| `Math.cs`                            | 射线与球面相交检测（CPU 端备用）。                           |
| `CamCtrl.cs`                         | 简易相机控制：旋转、缩放（推拉）。                           |
| `RayTest.cs`                         | 鼠标射线测试辅助（仅用于调试显示交点）。                     |
| `ComputeBufManager.cs`               | ComputeBuffer 生命周期管理（自动释放，防止泄漏）。           |
| `CountryColorDrawer.cs`              | 编辑器工具：管理国家颜色配置表（TSV），支持纹理映射、保存/加载配置、刷色到球体。 |

Shader 文件

- `IcoSphere.compute`：剔除和射线检测的核心 Compute Shader。
- `Custom_ComputeShader_Tri.shader`：实例化渲染表面和网格线的表面着色器。

#### 3.2.4 使用流程

##### 3.2.4.1 准备二进制数据

C++ 后端生成 `pack_arr_0.bytes` ~ `pack_arr_N.bytes` 文件（详见 C++ 端文档）。将其放入 Unity 项目的 `Resources/Bin` 目录中（如没有则创建）。

> **注意**：文件扩展名必须是 `.bytes`，否则 `Resources.Load` 无法加载。Unity 导入设置无需特殊调整。

##### 3.2.4.2 场景设置

见示例场景：`Assets/IcoSphere/Scenes/IcoSphere.unity`

##### 3.2.4.3 纹理映射（为国家/区域刷色）

1. 准备一张**世界地图风格的纹理**，必须遵循以下设置：
   - **Texture Type**：Default
   - **Format**：RGBA 32 bit
   - **Read/Write Enabled**：√
   - **Generate Mipmaps**：×
   - **Filter Mode**：Point (no filter)
   - **Non-Power of 2**：None（避免压缩导致颜色变化）
2. 将纹理拖入 `CountryColorDrawer` 组件的 `Mapping Tex` 字段。
3. 在 `CountryColorDrawer` 的 Inspector 中右键，点击 **生成地图映射配置，并执行地图贴图映射**。
   - 这会自动分析纹理中所有不重复的颜色（支持精度过滤），为每种颜色创建国家配置（临时 ID）。
   - 然后立即执行映射：根据每个顶点的经纬度采样纹理颜色，并将颜色/国家 ID 写入实例数据。
4. 可选：保存映射后的颜色数据到 `.bytes` 文件（`保存国家刷色数据 (.bytes)`），以便下次直接加载。

##### 3.2.4.4 国家颜色配置管理

- 在 `CountryColorDrawer` 中手动添加/修改国家条目（名称、ID、颜色）。
- 使用 `保存国家颜色配置表 (.tsv)` 导出为 TSV 文件（可编辑）。
- 使用 `读取国家颜色配置表 (.tsv)` 导入。
- 运行时：鼠标指向国家区域，按鼠标左键即可将该国家的颜色刷到球体上（实际是修改顶点颜色的关联国家 ID，下次映射时会生效）。

#### 3.2.5 关键代码说明

##### 3.2.5.1 射线高亮逻辑

- Compute Shader 每帧对所有三角形执行射线检测，找到最近的交点（最近的正面三角形）。
- 写入 `_RayResult` buffer，其中 `vid` 字段记录了击中的六边形区域（顶点）。
- 渲染时，Shader 根据像素所属的顶点 ID 与 `vid` 比较，若一致则叠加高亮颜色。

##### 3.2.5.2 纹理映射原理

1. 将顶点（`Vec3`）通过经纬度转换得到 UV 坐标。
2. 采样纹理获取 RGB 颜色，结合用户配置的 `hexRgbIdDict`（RGB -> 国家 ID）。
3. 将颜色和国家 ID 写入**顶点颜色数组**（复用 `InstanceData.col` 字段）。
4. **容易混淆的设计：`InstanceData.col` 实际上是按顶点索引存储的，但由于每个三角形实例引用三个顶点颜色，且顶点数小于三角形数，直接通过顶点索引写入不会越界。代码中 `data[(uint)v.w].col` 即是这种复用方式。**

##### 3.2.5.3 内存管理

- 使用 `ComputeBufManager` 追踪所有 ComputeBuffer，在场景销毁时自动释放。
- `MappingTex` 中重建 `allBuf` 前已调用 `FreeBuf` 释放旧 Buffer，防止内存泄漏。

#### 3.2.6 方便外部应用的API

##### 3.2.6.1 `IcoSphere`

| 函数名                | 参数列表                                          | 返回类型    | 作用                                   | 细节                                                         |
| --------------------- | ------------------------------------------------- | ----------- | -------------------------------------- | ------------------------------------------------------------ |
| `GetAreaCount`        |                                                   | `int`       | 返回地块总数（六边形/五边形数量）      | 地块总数相当于顶点数，因为每个六边形/五边形的中心点就是顶点  |
| `IsValidAreaId`       | `int areaId`                                      | `bool`      | 判断`areaId`是否有效                   | 用于避免点击、寻路、读档时传入非法id                         |
| `GetAreaCenter`       | `int areaId`                                      | `Vector3`   | 返回某个地块中心点的世界坐标           |                                                              |
| `GetRawAreaCenter`    | `int areaId`                                      | `Vector3`   | 返回某个地块中心点的原始坐标           | 注意！这里返回的是原始坐标数据，如果需要使用，要自己乘球体半径`sphereRadius`，或用`GetAreaCenter`函数 |
| `GetAreaNormal`       | `int areaId`                                      | `Vector3`   | 返回某个地块中心点的球面外法线         | 用途：让单位、图标、模型能正确贴在球面上                     |
| `GetNeighborCount`    | `int areaId`                                      | `int`       | 返回某个地块的相邻地块数量             | 六边形是6，五边形是5（少数）                                 |
| `GetNeighborId`       | `int areaId, int neighborIndex`                   | `int`       | 返回某个相邻地块id                     | `neighborIndex`的范围是`0`到`GetNeighborCount(areaId) - 1`   |
| `GetRawUnsortedAreas` |                                                   | `Vector3[]` | 获取未按坐标排序好的地块列表           | 注意！这里返回的原始坐标数据，如果需要使用，要自己乘球体半径`sphereRadius` |
| `GetRawSortedAreas`   |                                                   | `Vector3[]` | 获取按坐标排序好的地块列表             | 注意！这里返回的原始坐标数据，如果需要使用，要自己乘球体半径`sphereRadius` |
| `FindAreaByPos`       | `Vector3 p`                                       | `int`       | 尝试通过坐标寻找地块id                 | 如果没寻找到，则返回-1                                       |
| `TryPickArea`         | `Ray ray, out int areaId`                         | `bool`      | 用射线拾取地块                         | 如果命中地块，返回true，并输出areaId；如果没有命中地块，返回false |
| `SetAreaColor`        | `int areaId, Color color`                         |             | 设置单个地块颜色                       | 注意：只是做选中高亮效果，如果color的透明度为0，则相当于清除高亮 |
| `ChangeAreaCountry`   | `int areaId, Color countryCol, uint countryId`    |             | 设置单个地块的国家颜色                 | 注意：需要同时传入正确的国家颜色和国家id才行，可以看`CountryColorDrawer.countrySettings`列表或`.countrySettingsDict`字典获取正确的国家颜色和国家id |
| `SetAreaColors`       | `int[] areaIds, Color color`                      |             | 批量设置多个地块为同一种颜色           | 用途：批量范围内选中高亮效果，如果`color`的透明度为0，则相当于清除高亮；注意：为了性能考虑, 传入的数值大小不能超过`MAX_DRAW_HEX_COUNT` = 1000，如果需要超过这个值，则需要下一帧再传入剩余的数据 |
| `ChangeAreaCountries` | `int[] areaIds, Color countryCol, uint countryId` |             | 批量设置多个地块的国家颜色             | 注意：为了性能考虑, 传入的数值大小不能超过`MAX_DRAW_HEX_COUNT` = 1000，如果需要超过这个值，则需要下一帧再传入剩余的数据 |
| `ClearAreaColor`      | `int areaId`                                      |             | 清除单个地块的特殊颜色，并恢复默认颜色 |                                                              |

##### 3.2.6.2 `CountryColorDrawer`

| 函数名                   | 参数列表 | 返回类型                             | 作用                     | 细节          |
| ------------------------ | -------- | ------------------------------------ | ------------------------ | ------------- |
| `GetCountrySettings`     |          | `List<CountrySetting>`               | 获取国家颜色配置数据表   |               |
| `GetCountrySettingsDict` |          | `Dictionary<string, CountrySetting>` | 获取国家颜色配置数据字典 | key为国家名称 |

#### 3.2.7 性能数据参考（recursion=5）

| 项目             | 数值                                                   |
| :--------------- | :----------------------------------------------------- |
| 顶点数           | 590,492                                                |
| 三角形数         | 1,180,980                                              |
| 实例数           | 1,180,980                                              |
| GPU 剔除后       | **（AI生成，待确认）**平均 300k~600k （取决于视角）    |
| 显存占用         | **（AI生成，待确认）**约 200 MB（实例数据 + 可见列表） |
| 帧率（RTX 3060） | **（AI生成，待确认）**60~100 FPS                       |

#### 3.2.8 FAQ

**Q：运行后球体不可见，控制台报错 “Resources 中未找到文件”？**
A：请确保二进制文件放置在 `Resources/Bin/` 中，文件名如 `pack_arr_3.bytes`，且 `Recursion` 参数与序号一致。

**Q：纹理映射后颜色错乱或出现锯齿？**
A：请严格按照要求设置纹理（RGBA32、Point过滤、无Mipmap、可读写）。若仍有色差，尝试在 `CountUniqueColors` 方法中增大 `precisionLv`（精度等级）合并相近颜色。

**Q：点击刷色后颜色变化不立即生效？**
A：刷色操作修改的是 GPU 端的 `drawHexBuf`，仅高亮当前六边形区域，不会永久改变球体颜色。如需永久改变颜色，应重新执行纹理映射或保存为新的 `vertBuf` 数据。

**Q：编辑器下按 Ctrl+R 刷新脚本后球体消失？**
A：这是已知问题（热重载后 Material 绑定丢失），将在后续实现自动重新绑定。临时解决方法：重新运行场景或 Disable/Enable `IcoSphere` 组件。

#### 3.2.9 todo...

- **实现热重载后材质的 Buffer 重新绑定**
  完善 `IcoSphere.OnAfterAssemblyReload()`，在编辑器重新编译后重新调用 `mat.SetBuffer` 等，避免渲染丢失。
- **完成光照渲染**
  在 Shader 中加入基于法线的光照模型（如 Lambert/Blinn-Phong），并根据项目需求添加阴影支持。

## 四、Todo

按优先级从高到低排列：

- 分析Honey Framework的WorldOven代码
  - 需要使用虚拟纹理技术才能解决，不能直接使用Honey Framework的代码
- 曲面细分，制作地形高度差