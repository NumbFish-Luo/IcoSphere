# 《大地形的一种简化 RVT》阅读笔记

来源链接：

- 你给的知乎原链接：https://zhuanlan.zhihu.com/p/552748937
- 本次可访问的镜像页：https://blog.uwa4d.com/archives/USparkle_RVT.html
- 文章中提到的相关仓库：https://github.com/jackie2009/unityRVTTerrain

说明：原文/镜像页明确写了未经授权不要转载，所以这里保存的是本地阅读笔记和资源索引，不是原文全文转载，也没有把图片本体复制进仓库。

## 核心思路

文章介绍的是一种面向 Unity 大地形的简化 Runtime Virtual Texture 方案。它没有实现完整虚拟纹理里的 feedback 流程，而是采用更直观的“按相机距离决定 LOD”的方式：

1. 用四叉树在 XZ 平面划分地形。
2. 只把叶节点当作当前需要显示/缓存的 RVT 页面。
3. 每个活跃叶节点分配一个固定尺寸的 `Texture2DArray` 物理纹理槽位。
4. 当节点细分或合并时，把该节点对应的地形内容重新 Blit 到它分配到的纹理数组切片里。
5. 填充一张索引纹理，让最终地形 shader 能从世界坐标/地形 UV 找到正确的纹理数组切片和 tile 内 UV。

这个方案的性能动机是：复杂地形混合通常每个像素都要采样很多层 albedo/normal。RVT 先把这些层混合成缓存贴图，最终渲染时就采样缓存结果，不用每帧重复做完整混合。

## 主要机制

四叉树管理：

- `currentAllLeaves`：当前帧所有叶节点。
- `nextAllLeaves`：更新 LOD 状态时填充的下一帧叶节点队列。
- `physicEmptyIndexQueue`：可用的物理纹理数组索引队列。
- `onLoadData`：节点需要生成/加载贴图数据时触发的回调。
- 每帧细分次数限制：把节点更新分摊到多帧，避免瞬时卡顿。

节点贴图生成：

- 文章倾向用 Blit，而不是放相机拍，因为 Blit 不需要跑完整地形渲染流程。
- 地表层纹理先放进纹理数组。
- Blit shader 针对某个节点矩形区域合成 albedo/normal。
- 后续版本用 MRT 一次输出 albedo 和 normal。

索引纹理：

- 每个地形着色点用地形 UV 去采样索引纹理。
- 索引纹理的 texel 存储物理纹理数组索引，以及 tile 的位置/尺寸。
- shader 用这些数据算出 tile 内 UV，再去采样缓存的 RVT 纹理。

Mip 处理：

- CPU 侧四叉树 LOD 是按距离算的，但 GPU 正常会根据导数选择 mip。
- 斜面如果只提供单份缓存 mip，容易出现噪点。
- 文章的修正方式是提供多级 mip，并在 shader 中用稳定的地形 UV 导数计算修正后的 mip。

## 和当前项目的重叠

这个仓库里已经有一条实现线，看起来就是参考了这篇简化 RVT：

- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/Rvt.cs`
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/QuadTree.cs`
- `IcoSphere/Assets/IcoSphere/Scripts/Rvt/VirtualCapture.cs`
- `IcoSphere/Assets/IcoSphere/Shaders/Rvt.compute`
- `IcoSphere/Assets/IcoSphere/Shaders/Custom_Rvt_Blit.shader`

这些代码里的结构和文章术语非常接近：叶节点队列、物理纹理索引、`Texture2DArray` 的 albedo/normal 缓存、索引 RenderTexture，以及用 compute 填充索引纹理。

## 远程资源索引

图片和视频对理解文章很有用，但这里不下载本体。需要对照时可以打开下面的原始远程 URL。

| 资源 | 作用 / 图注 | URL |
| --- | --- | --- |
| 1.png | 地形优化前截图 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/1.png |
| 2.png | 地形优化后截图 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/2.png |
| 3.png | 性能/效果截图 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/3.png |
| 4.mp4 | 移动时性能演示 | https://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/4.mp4 |
| 5.png | 根据相机距离进行 XZ 四叉树划分 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/5.png |
| 6.png | 四叉树数据结构 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/6.png |
| 7.png | 创建根节点 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/7.png |
| 8.png | 每帧叶节点更新循环 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/8.png |
| 9.png | 节点 LOD 判断逻辑 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/9.png |
| 10.png | 节点合并逻辑 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/10.png |
| 11.png | 节点细分逻辑 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/11.png |
| 12.gif | 四叉树细分/合并动态效果 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/12.gif |
| 13.mp4 | 四叉树更新/相机移动演示 | https://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/13.mp4 |
| 14.png | 实时生成 tile 内容 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/14.png |
| 15.png | 根据节点位置和尺寸做 offset/scale | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/15.png |
| 16.png | 生成节点贴图的 shader | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/16.png |
| 17.png | 数据数组和索引纹理填充 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/17.png |
| 18.png | 斜面噪点问题 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/18.png |
| 19.gif | 噪点对比 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/19.gif |
| 20.png | 为不同角度需求准备多级 mip | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/20.png |
| 21.png | mip 修正 shader 采样 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/21.png |
| 22.gif | mip 修正后的效果 | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/22.gif |
| 23.png | 原先两次 DrawQuad | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/23.png |
| 24.png | MRT 一次 DrawQuad | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/24.png |
