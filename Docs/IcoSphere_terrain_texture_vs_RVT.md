# IcoSphere Terrain Materials vs. Spherical RVT

## Two Rendering Paths

The IcoSphere renderer already determines the area id (`vid`) for each fragment. A direct per-area terrain material path looks like this:

```text
pixel -> vid -> AreaTerrainData -> terrainId -> terrain Texture2DArray
```

That path is useful as a fallback and as a simple way to prove that every hex/pent area can own a terrain type. It is not RVT by itself: if the final shader directly blends terrain material inputs every frame, the cost still lives in the final pass.

The simplified RVT article uses this core path instead:

```text
world position / terrain uv
  -> index texture / page table
  -> physical Texture2DArray slice
  -> baked cached tile
```

Expensive terrain material blending is baked into cache tiles. The final shader samples the cache.

## Current Branch Approach

`feature-zhihu-style-spherical-rvt` keeps the direct path only as fallback and adds a first spherical adaptation of the article's cache idea:

- Virtual space: lonlat page grid.
- Physical cache: limited albedo tile slices.
- Page selection: pages near the camera direction.
- Update policy: only dirty pages are baked, with a per-frame update limit.
- Page table: an index texture stores physical slice and page rect data.
- Fallback: invalid/non-resident pages, or cached tiles whose baked terrain id does not match the current fragment's real `vid`, fall back to direct per-area terrain sampling.

When RVT hits and the terrain id matches the current hex/pent area, the final shader samples `_SphericalRvtAlbedoArray` directly. Mismatches prefer correct cell shape over using a stale or approximate cache sample.

## Terrain Texture Channel Contract

Textures under `Assets/IcoSphere/Textures/Terrain` follow this convention:

| Suffix | Meaning | Used as color? |
| --- | --- | --- |
| `*_d.png` | diffuse/albedo | yes |
| `*_h.png` | height | no |
| `*_m.png` | mask/material parameters/blend mask | no |

`Water` has no `Water_d.png`, so water albedo uses a blue fallback color. `Water_h.png` and `Water_m.png` must not be used as albedo.

## Why Not Planar XZ Quadtree

The article targets planar terrain and assumes XZ space can be divided by a quadtree. IcoSphere is a spherical icosahedron-derived topology, so direct XZ partitioning is unstable around the back side, poles, and seams.

The first implementation uses lonlat because it is simple and lets the full RVT pipeline run:

```text
sphere position -> lonlat uv -> page table -> physical tile cache
```

This is not the final ideal spherical page space. Better future options are:

- one page hierarchy per original icosahedron face,
- pack-triangle clusters,
- or area-cluster page tables.

## Current Limits

- The terrain id map is a nearest-area-center spherical Voronoi approximation, not exact rasterization from the same triangle fan tests used by the fragment shader.
- Only albedo cache tiles are generated.
- Normal/height/mask cache outputs are not implemented yet.
- The article's multi-mip cache and derivative correction are not implemented yet.
- Lonlat seam and pole stretching are accepted first-version limitations.
