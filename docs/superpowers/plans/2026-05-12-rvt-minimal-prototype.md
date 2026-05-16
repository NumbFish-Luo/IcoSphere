# Minimal RVT Prototype Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first runnable slice of the simplified RVT design: longitude-latitude tile addressing, debug tile generation, physical texture array upload, index texture updates, and shader-side sampling hooks.

**Architecture:** Runtime code owns a quadtree-driven RVT manager. Each resident tile has a physical texture-array layer and writes `(layer, originX, originY, size)` into an index texture. The globe shader maps world position to virtual UV, reads the index texture, computes local tile UV, and samples the physical array with explicit mip selection.

**Tech Stack:** Unity 2022.3, URP 14, C#, ComputeShader, HLSL, NUnit EditMode tests.

---

### Task 1: Addressing Types And Tests

**Files:**
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtTileId.cs`
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtTileRect.cs`
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/IRvtAddressMapping.cs`
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/LonLatRvtAddressMapping.cs`
- Create: `IcoSphere/Assets/IcoSphere/Tests/EditMode/IcoSphere.EditModeTests.asmdef`
- Create: `IcoSphere/Assets/IcoSphere/Tests/EditMode/RvtAddressingTests.cs`

- [ ] **Step 1: Write failing EditMode tests**

Tests cover child/parent ids, tile rect conversion, wrapped longitude distance, and known axis mapping.

- [ ] **Step 2: Run tests and verify red**

Run Unity EditMode tests for `RvtAddressingTests`. Expected: compile/test failure because RVT types do not exist yet.

- [ ] **Step 3: Implement minimal addressing code**

Implement small immutable structs and `LonLatRvtAddressMapping` using `Misc.ToLonLatUv`.

- [ ] **Step 4: Run tests and verify green**

Run the same EditMode tests. Expected: all addressing tests pass.

### Task 2: Physical Pool, Index Texture, And Debug Tile Source

**Files:**
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtPhysicalTexturePool.cs`
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtIndexTexture.cs`
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/IRvtTileSource.cs`
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/DebugRvtTileSource.cs`
- Create: `IcoSphere/Assets/IcoSphere/Shaders/RvtIndexWriter.compute`
- Create: `IcoSphere/Assets/IcoSphere/Tests/EditMode/RvtRuntimeDataTests.cs`

- [ ] **Step 1: Write failing tests for CPU-visible behavior**

Tests cover physical slot allocation/release, index payload creation, and debug tile deterministic colors.

- [ ] **Step 2: Run tests and verify red**

Run Unity EditMode tests. Expected: failure because runtime data types do not exist yet.

- [ ] **Step 3: Implement minimal physical pool and index helpers**

Implement deterministic allocation, generated mip count configuration, and index payload calculation.

- [ ] **Step 4: Run tests and verify green**

Run EditMode tests. Expected: addressing and runtime data tests pass.

### Task 3: RVT Manager Runtime Slice

**Files:**
- Create: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/RvtManager.cs`
- Modify: `IcoSphere/Assets/IcoSphere/Scripts/Rvt/QuadTree.cs` only if needed for shared helpers; prefer leaving it intact and adding new RVT-specific classes.

- [ ] **Step 1: Add manager using tested components**

Manager initializes mapping, physical pool, index texture, root tile, debug tile source, and shader bindings.

- [ ] **Step 2: Add per-frame focus update and tile refresh**

Use viewport-center ray-to-sphere focus and fallback to normalized camera position.

- [ ] **Step 3: Add editor-safe cleanup**

Release render textures and compute buffers in `OnDestroy`.

### Task 4: Shader Sampling Hook

**Files:**
- Modify: `IcoSphere/Assets/IcoSphere/Shaders/Custom_ComputeShader_Tri.shader`

- [ ] **Step 1: Add RVT material properties and shader uniforms**

Add `_UseRvt`, `_RvtIndexTex`, `_RvtAlbedoTexArray`, `_RvtRootCellSize`, `_RvtTileSize`, `_RvtGeneratedMipCount`, and `_RvtMipBias`.

- [ ] **Step 2: Add world-to-lonlat helper matching C#**

Use the same axis convention as `Misc.ToLonLatUv`: `atan2(z, x)` and `asin(y)`.

- [ ] **Step 3: Add RVT sampling function**

Read index texture, compute local UV, calculate explicit mip from continuous virtual coordinates, and sample the physical array.

- [ ] **Step 4: Blend RVT debug color into the existing color path**

When `_UseRvt > 0.5`, replace the existing region color with RVT sample color.

### Task 5: Modification Log

**Files:**
- Create: `docs/implementation-notes/2026-05-12-rvt-code-changes.zh-CN.md`

- [ ] **Step 1: Record every code file changed**

List purpose, key behavior, and verification status.

- [ ] **Step 2: Record known limitations**

Call out no Unity SVT, no feedback pass, longitude-latitude distortion, and incomplete material-baking source.

### Task 6: Verification

**Files:**
- No new files.

- [ ] **Step 1: Run available automated tests**

Prefer Unity EditMode tests. If Unity CLI is unavailable, record that limitation.

- [ ] **Step 2: Run lightweight text checks**

Search for stale `_RvtLookupTex`/lookup-page-table terms in code and docs.

- [ ] **Step 3: Check git status**

Confirm no commit was created and report changed files.

