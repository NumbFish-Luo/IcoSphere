# IcoSphere 地块纹理方案与简化 RVT 的区别

## Current IcoSphere Rendering

The current sphere renderer draws many triangle instances. In the fragment shader, the code determines which hex/pent area a pixel belongs to and gets a `vid`. That `vid` is the area id.

Current color lookup is conceptually:

```text
pixel -> vid -> _AllInstancesData[vid].col.rgb
```

This is why mouse painting works: the ray pick computes a `vid`, then the compute shader writes color data into `_AllInstancesData[vid].col`.

## Earlier Proposed Plan

For the goal "each area has its own terrain and texture", the lighter plan is:

```text
areaId -> AreaTerrainData -> terrainId -> Texture2DArray slice
```

The data would be stored per area, not per triangle:

```csharp
struct AreaTerrainData {
    public uint terrainId;
    public uint flags;
    public Vector2 uvOffset;
    public Vector2 uvScale;
}
```

Then the shader path becomes:

```text
pixel -> vid -> areaTerrainBuffer[vid] -> sample terrain texture array
```

This keeps the current indirect rendering architecture. It does not create per-area GameObjects, per-area materials, or extra draw calls.

## What The Simplified RVT Article Does

The article's method is a terrain cache system:

```text
world position / terrain UV -> index texture -> physical texture array slice -> cached albedo/normal tile
```

It uses a quadtree over XZ terrain space. Near camera areas split into smaller nodes, far areas merge into larger nodes. Every active leaf gets a physical slot in a fixed-size `Texture2DArray`. The system blits the terrain layer blend into that slot, then the final shader samples the cached tile.

This is designed for large continuous terrain where the expensive part is repeatedly blending many terrain layers per pixel.

## Main Differences

| Topic | Per-area terrainId plan | Article simplified RVT |
| --- | --- | --- |
| Granularity | IcoSphere area / hex / pent cell | Quadtree leaf tile in XZ terrain space |
| Lookup key | `vid` area id already computed by sphere shader | terrain UV samples an index texture |
| Stored data | terrain type, tint, UV transform, optional height params | baked albedo/normal tile cache |
| Runtime updates | only when an area's terrain changes | when camera movement causes quadtree split/merge |
| Texture source | static terrain texture arrays | blitted/composited runtime tiles |
| Best at | discrete per-cell terrain identity | continuous large terrain with many blended layers |
| Engineering cost | low to medium | high, especially on a sphere |
| CPU involvement | minimal after buffer upload | quadtree LOD management and tile update scheduling |
| GPU cost | final shader samples terrain arrays directly | final shader samples index texture plus cached RVT texture |
| LOD/mips | normal texture mipmaps first | custom cache LOD and derivative correction |

## Suitability For This Project

For the current IcoSphere map, the per-area plan fits the stated requirement better:

- The world is already discrete: every playable region is an area id.
- Picking, coloring, neighbor lookup, and country mapping already use area ids.
- The desired data is "this area is plains/mountain/water/etc.", not necessarily a continuous terrain splat blend.
- It is much easier to author and debug: inspect `areaId`, inspect `terrainId`, inspect sampled terrain texture.

The article's RVT becomes attractive later if the goal changes toward:

- very high-frequency continuous surface detail,
- many blended terrain layers per visible pixel,
- roads/decals baked into terrain texture,
- camera-dependent terrain texture resolution,
- or a separate planar terrain scene.

## Sphere-Specific Cost Of Adapting RVT

Adapting the article directly to this sphere is not a drop-in job:

1. The article assumes planar XZ terrain and regular terrain UV.
2. The sphere uses icosphere topology with hex/pent regions.
3. A quadtree would need to become either:
   - a hierarchy per icosahedron face,
   - a spherical UV tile system with seam handling,
   - or a custom area-cluster hierarchy.
4. The index texture idea needs a stable spherical parameterization, otherwise seams and derivative-based mip selection will be painful.
5. Cached tile generation must know how to rasterize or blit spherical area data into each tile.

So RVT is powerful, but it solves a larger and different problem than "each discrete area has terrain type and texture."

## Recommended Path

Start with the simpler per-area terrain path:

1. Add `AreaTerrainData` buffer indexed by area id.
2. Add a `TerrainType` enum and editor/runtime APIs like `SetAreaTerrain(areaId, type)`.
3. Build one or more `Texture2DArray` assets from the terrain textures.
4. Modify `Custom_ComputeShader_Tri.shader` so `vid` selects terrain texture data instead of only color.
5. Keep current grid-line rendering as an overlay.
6. Add normal/height support after diffuse sampling is correct.

After that works, evaluate whether an RVT-like cache is needed. If the terrain shader becomes too expensive because each pixel blends many layers, then borrow the article's cache idea, but adapt it as an area-cluster or spherical-face cache rather than a planar XZ quadtree.
