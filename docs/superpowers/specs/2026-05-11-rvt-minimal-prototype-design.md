# Minimal Runtime Virtual Texture Prototype Design

## Sources Reviewed

- Local saved Zhihu article: `C:/Users/D/Desktop/(20 封私信) 大地形的一种简化RVT - 知乎.html`
- Original article: `https://zhuanlan.zhihu.com/p/552748937`
- Reference repository: `https://github.com/jackie2009/unityRVTTerrain`
- Sphere mapping paper: `https://www.jcgt.org/published/0007/02/01/paper-lowres.pdf`

The saved Zhihu article and repository clarify that this is not a standard full virtual texturing pipeline. The implementation deliberately removes the feedback pass and complex page-table management. It uses distance-driven quadtree leaves, a `Texture2DArray` physical pool, and an index texture where each covered virtual texel stores the physical layer and tile rect data.

## Background

This project currently renders a globe from precomputed binary IcoSphere model data. Unity reads `Resources/Bin/pack_arr_N.bytes` through `Pack.Read()`, builds per-triangle `InstanceData`, culls it in `IcoSphere.compute`, and renders it with indirect instancing in `Custom_ComputeShader_Tri.shader`.

The reference implementation targets Unity 5.6 built-in terrain. It is not directly portable to this Unity 2022 URP globe project because it depends on `TerrainData`, built-in terrain splat shaders, surface shaders, and planar `x/z` coordinates. The useful part is the simplified RVT architecture: quadtree LOD, generated physical tiles, an index texture, and explicit mip selection in the draw shader.

Unity Streaming Virtual Texturing must remain disabled. This design is for a project-owned runtime virtual texture system, not Unity SVT.

## Goals

- Keep the existing binary IcoSphere model pipeline.
- Implement the simplified RVT idea from the article and repository, not a full SVT-style virtual texture system.
- Replace the planar terrain source with a source that works from the project's binary sphere data and virtual sphere coordinates.
- Replace planar `x/z/size` quadtree coordinates with a longitude-latitude virtual address space for the first prototype.
- Use a `Texture2DArray` physical pool for generated tile content.
- Use an index texture that maps virtual UV cells to physical tile layer and tile rect data.
- Preserve a mapping boundary so longitude-latitude can later be replaced by a cube-sphere mapping.

## Non-Goals

- No Unity Streaming Virtual Texturing.
- No GPU feedback pass in the first prototype.
- No classic page table or fragment-time linear page lookup.
- No async disk streaming requirement in the first prototype.
- No final commercial residency policy, priority queue, compression, or tile streaming format.
- No direct dependency on Unity `Terrain`.
- No immediate cube-sphere rewrite.

## Key Design Decisions

1. RVT is a runtime system owned by project scripts and shaders.
2. Tile demand is driven by a quadtree and camera distance, matching the article's simplified design.
3. The first virtual address space is longitude-latitude UV:
   - longitude maps to `u` in `[0, 1]`
   - latitude maps to `v` in `[0, 1]`
4. Quadtree nodes are identified by `(level, x, y)` and a virtual tile rect.
5. Runtime code writes an index texture. Shader sampling reads the index texture, then samples the physical texture array.
6. Physical tile layers store generated tile content plus a small fixed number of mip levels. The original implementation uses four generated mips to reduce grazing-angle noise.
7. Tile content generation is isolated from tile selection. Debug colors, world-map tiles, and later material-baked tiles are separate tile sources.
8. The coordinate mapping is isolated so the prototype can later swap longitude-latitude for cube-sphere without replacing the cache, physical pool, index texture, or shader sampling model.

## Proposed Components

### `RvtTileId`

Small value type:

- `int level`
- `int x`
- `int y`

Responsibilities:

- Generate child ids.
- Generate parent id.
- Provide stable hashing and equality.
- Convert to normalized virtual tile rect.

### `IRvtAddressMapping`

Interface for converting globe positions and camera state into virtual texture addresses.

Responsibilities:

- `Vector2 WorldToVirtualUv(Vector3 worldPos)`
- `RvtTileRect GetTileRect(RvtTileId id)`
- `Vector2 CameraToVirtualUv(Camera camera, Transform globeTransform)`
- `float EstimateTileError(RvtTileId id, Vector2 focusUv, float cameraDistance)`

Initial implementation:

- `LonLatRvtAddressMapping`
- Uses the existing longitude-latitude math from `Misc.ToLonLatUv`.
- Handles longitude wrap at `u = 0/1`.
- Clamps latitude to `[0, 1]`.

Future implementation:

- `CubeSphereRvtAddressMapping`
- Uses six faces and face-local quadtree coordinates.
- Intended to reduce polar distortion and seam problems.

### `RvtQuadTree`

Prototype quadtree for tile LOD selection.

Responsibilities:

- Maintain current leaf tiles.
- Split tiles near the camera focus.
- Merge tiles far from the camera focus.
- Emit bake/upload requests for newly active tiles.
- Emit release requests for inactive tiles.

Relationship to the repository:

- The current project `QuadTree.cs` is already close to the repository's `VT_Terrain.Node`.
- Keep the frame-limited split/merge idea.
- Replace static global ownership with `RvtManager` instance-owned state.
- Replace planar `x/z/size` meaning with virtual tile rect coordinates.
- Add longitude wrapping behavior for `u = 0/1`.

LOD rule for prototype:

- Compute camera focus in virtual UV.
- Measure wrapped UV distance from tile rect to focus.
- Convert distance to desired level with a logarithmic rule similar to the original distance-to-size logic.
- Clamp level to serialized `minLevel` and `maxLevel`.
- Split at most `maxSplitsPerFrame` nodes to avoid frame spikes.

### `RvtPhysicalTexturePool`

Owns generated physical tile textures.

Prototype pool:

- `RenderTexture` or `Texture2DArray` with `TextureDimension.Tex2DArray`
- one array for albedo/debug color
- one optional array for normal data
- fixed `tileSize`, default 512 for article parity or 256 for faster globe prototyping
- fixed `physicalTileCount`
- `useMipMap = true`
- `autoGenerateMips = false`
- generated mip count default: 4

Responsibilities:

- Allocate and release physical layers.
- Copy generated tile mip levels into the assigned array layer.
- Bind `_RvtAlbedoTexArray` and optional `_RvtNormalTexArray`.

Important behavior from the repository:

- Each quadtree node receives one physical layer index.
- On tile load, generated albedo and normal render targets are copied into the assigned layer.
- The original code copies mip levels `0..3` for each tile layer.

### `RvtIndexTexture`

GPU-readable texture that maps virtual cells to physical tile data.

Responsibilities:

- Own an index `RenderTexture`.
- Fill the virtual rect covered by a tile whenever a tile is loaded.
- Store enough data for shader sampling.
- Use point filtering and no mips.

Prototype encoding:

- `r`: physical layer index
- `g`: virtual rect origin x, in root cells
- `b`: virtual rect origin y, in root cells
- `a`: virtual rect size, in root cells

Repository equivalent:

- `VT_index_generator.compute` writes `int4(physicTexIndex, x, z, size)` into the index texture.
- `VT_Terrain.shader` samples `_VT_IndexTex`, computes local UV from world position and tile rect, and samples the physical array.

Sphere adaptation:

- `x`, `y`, and `size` are virtual UV grid cells, not world meters.
- For longitude wrapping, a tile that crosses `u = 0/1` must either be split into two index writes or represented by a mapping that avoids crossing rects.

### `RvtTileSource`

Produces generated tile content.

First source:

- `DebugRvtTileSource`
- Generates deterministic tile colors and id/level markings.
- Used to validate quadtree selection, index texture writes, array upload, and shader sampling.

Second source:

- `TextureRvtTileSource`
- Cuts tiles from a readable world map texture in longitude-latitude UV space.
- Used to validate orientation against the existing `MappingTex` workflow.

Later source:

- `BakedTerrainRvtTileSource`
- Uses Blit/MRT to bake diffuse, normal, and optional mask data into tile render targets.
- The repository's `VirtualCapture` and `VT_Terrain_Blit.shader` are the closest reference.
- This is where the RVT performance benefit appears: many terrain-layer samples are collapsed into a small fixed number of runtime samples.

### `RvtManager`

Scene component that owns the runtime RVT system.

Serialized fields:

- `Camera targetCamera`
- `Material targetMaterial`
- `int minLevel`
- `int maxLevel`
- `int rootCellSize`
- `int tileSize`
- `int physicalTileCount`
- `int generatedMipCount`
- `int maxSplitsPerFrame`
- `Texture2D sourceTexture` for the texture-backed prototype
- `RvtDebugMode debugMode`

Prototype defaults:

- `minLevel = 0`
- `maxLevel = 6`
- `rootCellSize = 1 << maxLevel`
- `tileSize = 256` for quick globe iteration; `512` if matching the article more closely
- `physicalTileCount = 384` for article parity or `64` for a smaller first scene
- `generatedMipCount = 4`
- `maxSplitsPerFrame = 1`

Responsibilities:

- Initialize mapping, quadtree, physical pool, index texture, and tile source.
- Update active tiles each frame.
- Bake or generate new tile data.
- Copy generated tile mips into physical array layers.
- Fill the corresponding region in the index texture.
- Bind `_RvtIndexTex`, `_RvtAlbedoTexArray`, `_RvtNormalTexArray`, `_RvtRootCellSize`, `_RvtTileSize`, and related constants.

## Shader Integration

The existing globe shader should add an RVT sampling path.

Sampling flow:

1. Fragment shader receives world position.
2. Convert world position to virtual UV using the same mapping convention as C#.
3. Sample `_RvtIndexTex` with point filtering.
4. Decode physical layer, virtual rect origin, and virtual rect size.
5. Convert virtual UV to root cell position.
6. Compute tile-local UV from `(virtualCell - rectOrigin) / rectSize`.
7. Compute explicit mip from derivatives of the continuous virtual coordinate, not from physical array UV.
8. Subtract the tile's base LOD from the derivative mip estimate, because a larger rect rendered into the same tile size is already lower detail.
9. Clamp explicit mip to the generated mip range.
10. Sample the physical albedo and optional normal arrays with `SAMPLE_TEXTURE2D_ARRAY_LOD`.

Important rule from the article:

- Do not calculate `ddx`/`ddy` from physical array UV. Neighboring tiles can jump from local UV `1` to `0`, causing false large derivatives and seams.
- Use continuous terrain/virtual coordinates for derivative calculation.

Prototype mip formula:

```text
virtualCell = virtualUv * rootCellSize
baseLod = log2(index.rectSize)
dx = ddx(virtualCell * tileSize)
dy = ddy(virtualCell * tileSize)
screenMip = 0.5 * log2(max(dot(dx, dx), dot(dy, dy)))
sampleMip = clamp(screenMip - baseLod + mipBias, 0, generatedMipCount - 1)
```

## Data Flow

1. `RvtManager.Start`
   - creates physical albedo and optional normal arrays
   - creates the index texture
   - creates root quadtree tile
   - allocates root physical layer
   - generates root tile content
   - copies generated mip levels into physical arrays
   - fills root region in index texture
   - binds resources to the material

2. `RvtManager.Update`
   - gets camera focus on the globe by raycasting from viewport center to the sphere, falling back to normalized camera position when the center ray misses
   - maps focus to virtual UV
   - updates the quadtree
   - handles split/merge load requests
   - regenerates changed tile content
   - updates corresponding index texture regions

3. `Custom_ComputeShader_Tri.shader`
   - renders existing IcoSphere geometry
   - maps world position to virtual UV
   - samples the index texture
   - samples the physical texture array using explicit mip
   - blends or replaces the current color path according to debug mode

## Longitude-Latitude Quadtree Details

Root:

- `level = 0`
- `x = 0`
- `y = 0`
- covers `u = [0, 1]`, `v = [0, 1]`

Children:

- child 0: lower-left tile rect
- child 1: lower-right tile rect
- child 2: upper-left tile rect
- child 3: upper-right tile rect

Distance:

- `u` distance wraps across the date line.
- `v` distance clamps at poles.
- Distance from focus to tile uses closest point on tile rect.

Known defects:

- Longitude-latitude tiles are distorted near poles.
- Date-line tiles need explicit wrapping behavior.
- These defects are acceptable for the first prototype because the mapping is replaceable.

## Why Not Use the Current `QuadTree.cs` As-Is

The current `QuadTree.cs` is very close to the repository's `VT_Terrain.Node`, so it is a useful starting point. It still needs adaptation:

- It stores planar `x`, `z`, and `size`.
- It uses static queues and static callbacks.
- It assumes one global tree.
- It does not model longitude wrapping or pole behavior.
- It does not update an index texture.
- It does not expose tile source, physical pool, and shader binding boundaries.

## Risks And Mitigations

### Original Implementation Is Unity 5.6 Built-In Terrain

Risk:

- The repository shader code is not directly compatible with Unity 2022 URP.

Mitigation:

- Port the architecture, not the built-in surface shader.
- Implement URP HLSL sampling in the existing globe shader.
- Keep Blit/MRT baking as a separate source module.

### Polar Distortion

Risk:

- Longitude-latitude quadtree produces poor tile distribution near poles.

Mitigation:

- Keep `IRvtAddressMapping`.
- Limit first acceptance to proving RVT mechanics.
- Replace mapping with cube-sphere in a later phase.

### Grazing-Angle Noise

Risk:

- Distance-driven quadtree LOD can be too sharp on surfaces where screen derivatives require lower mip levels.

Mitigation:

- Generate multiple mip levels per physical tile layer.
- Compute explicit mip from continuous virtual coordinates in shader.
- Avoid derivative calculation on local array UV.

### Index Texture Precision

Risk:

- Storing layer, origin, and size in a normalized color format can introduce precision errors.

Mitigation:

- Prefer integer-capable or high precision formats where Unity and target platforms allow it.
- Use point filtering and no mipmaps.
- Add debug views for physical layer, rect origin, and rect size.

### Scope Creep Into Full VT

Risk:

- Feedback pass, async IO, page-table hierarchy, and compression can expand the prototype.

Mitigation:

- First prototype ends when debug/world-map tiles render through index texture and physical arrays on the sphere.
- Full VT features remain future work.

## Validation Plan

Manual validation in Unity:

1. Open the IcoSphere scene or a new RVT prototype scene.
2. Attach `RvtManager` to a scene object.
3. Bind the existing IcoSphere material.
4. Run with `DebugRvtTileSource`.
5. Move camera around the globe.
6. Confirm nearby leaves split and distant leaves merge.
7. Visualize `_RvtIndexTex` and confirm regions store physical layer, origin, and size.
8. Confirm date-line wrapping does not leave obvious unloaded strips.
9. Switch to `TextureRvtTileSource`.
10. Confirm the world map orientation matches the existing `MappingTex` workflow.
11. Enable explicit mip debug and confirm grazing angles do not produce strong noise.

Code validation:

- Edit mode tests for `RvtTileId` parent/child math.
- Edit mode tests for longitude-latitude mapping of known axis points.
- Edit mode tests for wrapped distance across `u = 0/1`.
- Edit mode tests for tile rect to index texture write rect conversion.
- Shader/debug validation for explicit mip calculation.
- Small play mode smoke test that creates a manager, uploads root tile, fills index texture, and binds shader resources.

## Milestones

### Milestone 1: Addressing And Quadtree

- Add `RvtTileId`.
- Add `LonLatRvtAddressMapping`.
- Adapt `RvtQuadTree` from the existing `QuadTree.cs` pattern.
- Add edit mode tests for tile math, mapping, and wrapped distance.

### Milestone 2: Physical Pool And Index Texture

- Add `RvtPhysicalTexturePool`.
- Add `RvtIndexTexture`.
- Add compute shader or equivalent GPU path to fill index texture rects.
- Add debug visualization for index texture channels.

### Milestone 3: Debug Tile Source And Shader Sampling

- Add `DebugRvtTileSource`.
- Generate and upload root and child debug tiles.
- Add RVT sampling path to the globe shader.
- Use explicit mip selection based on continuous virtual coordinates.

### Milestone 4: Texture-Backed Tiles

- Add `TextureRvtTileSource`.
- Cut tiles from a readable world map.
- Validate orientation against existing longitude-latitude mapping.

### Milestone 5: Material Baking Source

- Add prototype `BakedTerrainRvtTileSource`.
- Use Blit/MRT to bake diffuse and normal outputs into physical texture arrays.
- Adapt the repository's `VirtualCapture` concept without depending on Unity built-in `Terrain`.

## Completion Criteria

The minimal prototype is complete when:

- Unity project setting `virtualTexturingSupportEnabled` remains disabled.
- Existing binary IcoSphere data still loads through `Pack.Read()`.
- The globe renders with RVT debug tiles.
- Camera movement changes active quadtree leaves.
- Physical texture layers are allocated and reused.
- Index texture regions match resident tile regions.
- The shader samples physical arrays through index texture indirection.
- Explicit mip selection reduces grazing-angle noise.
- A world-map source can be displayed through RVT without using Unity SVT.
