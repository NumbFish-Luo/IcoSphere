# 大地形的一种简化 RVT - 阅读笔记

Source links:

- Original requested URL: https://zhuanlan.zhihu.com/p/552748937
- Accessible mirror read here: https://blog.uwa4d.com/archives/USparkle_RVT.html
- Related repository linked by the article: https://github.com/jackie2009/unityRVTTerrain

Note: the original page/mirror explicitly says it should not be reposted without authorization. This file is therefore a local study note and resource index, not a verbatim copy of the article or its images.

## Core Idea

The article describes a simplified Runtime Virtual Texture approach for large Unity terrain. It avoids a full virtual-texture feedback pipeline and uses a more direct camera-distance LOD model:

1. Divide the terrain in XZ space with a quadtree.
2. Keep only leaf nodes as render-relevant RVT pages.
3. Give every active leaf node one fixed-size physical texture slot in a `Texture2DArray`.
4. When a node splits or merges, render/blit that node's terrain content into its assigned physical texture slice.
5. Fill an index texture so the terrain shader can map world/terrain UV to the correct texture-array slice and local tile UV.

The important performance motivation is that complex terrain blending often requires many albedo/normal samples per pixel. The RVT pass pre-composes those layers into cached tile textures, so the final terrain shader samples the cached result instead of redoing the full blend every frame.

## Main Mechanics

Quadtree management:

- `currentAllLeaves` stores the active leaf nodes for this frame.
- `nextAllLeaves` is filled while updating LOD state.
- `physicEmptyIndexQueue` stores free physical texture-array indices.
- `onLoadData` is called when a node needs its texture data generated.
- A per-frame split limit throttles updates and avoids visible spikes.

Tile content generation:

- The article favors a blit path over camera capture because it avoids a full terrain render path.
- Terrain layer textures are stored in a texture array.
- The blit shader composes terrain albedo/normal for the requested node rectangle.
- Later versions use MRT to output albedo and normal together.

Index texture:

- Each terrain shading point samples an index texture using terrain UV.
- The index texel stores the physical texture-array index plus tile position/size.
- The shader uses that data to compute local tile UV and sample the cached RVT texture.

Mip handling:

- The CPU quadtree LOD is distance based, while the GPU normally chooses mips from derivatives.
- Sloped surfaces can show noise if the cache only provides one mip.
- The article's fix is to provide several mip levels and compute an adjusted mip in the shader using stable terrain UV derivatives.

## Current Project Adaptation

The `feature-zhihu-style-spherical-rvt` branch implements a first spherical adaptation of the article's simplified RVT idea:

- `SphericalRvtManager` maintains lonlat virtual pages and a smaller physical tile cache.
- `SphericalRvtIndex.compute` fills an index texture/page table for active pages.
- `SphericalRvtBake.compute` bakes dirty albedo tiles from terrain layer arrays.
- `Custom_ComputeShader_Tri.shader` samples the RVT cache on valid pages and falls back to direct per-area terrain sampling when a page is missing or the cached terrain id does not match the current `vid`.

This is intentionally not the article's planar XZ quadtree. The first spherical page space is lonlat so the page-table/cache/final-sampling pipeline can be validated before moving to an icosahedron-face atlas or area-cluster hierarchy.

## Remote Asset Index

Images and videos are useful for understanding the article, but they are not downloaded here. Use these original remote URLs if you need to view them beside this note.

| Resource | Caption / Role | URL |
| --- | --- | --- |
| 1.png | terrain before optimization | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/1.png |
| 2.png | terrain after optimization | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/2.png |
| 3.png | performance/effect screenshot | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/3.png |
| 4.mp4 | moving performance demo | https://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/4.mp4 |
| 5.png | quadtree XZ partitioning by camera distance | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/5.png |
| 6.png | quadtree data structure | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/6.png |
| 7.png | root node creation | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/7.png |
| 8.png | per-frame leaf update loop | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/8.png |
| 9.png | node LOD decision logic | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/9.png |
| 10.png | node merge logic | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/10.png |
| 11.png | node split logic | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/11.png |
| 12.gif | quadtree split/merge visual behavior | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/12.gif |
| 13.mp4 | quadtree update/movement demo | https://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/13.mp4 |
| 14.png | realtime tile content generation | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/14.png |
| 15.png | offset/scale by node position and size | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/15.png |
| 16.png | shader for node texture generation | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/16.png |
| 17.png | data array and index texture fill | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/17.png |
| 18.png | slope noise artifact | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/18.png |
| 19.gif | noise comparison | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/19.gif |
| 20.png | several mips for angular needs | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/20.png |
| 21.png | mip correction shader sampling | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/21.png |
| 22.gif | improved mip result | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/22.gif |
| 23.png | previous two draw-quad passes | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/23.png |
| 24.png | MRT single draw-quad pass | http://uwa-ducument-img.oss-cn-beijing.aliyuncs.com/Blog/USparkle_RVT/24.png |
