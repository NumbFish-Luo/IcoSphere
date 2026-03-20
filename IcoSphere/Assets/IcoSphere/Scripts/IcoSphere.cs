using System;
using System.Collections.Generic;
using UnityEngine;

// 参考: http://blog.andreaskahler.com/2009/06/creating-icosphere-mesh-in-code.html
public class IcoSphere : MonoBehaviour {
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private float radius = 1f;

    // 递归细分次数, 越大面数越多
    // 注意: 迭代4的时候可能有些数据超过int值范围了, 所以面会崩
    [SerializeField, Range(0, 3)] private int recursion = 2;

    readonly static float GOLDEN_RATIO = (1.0f + Mathf.Sqrt(5.0f)) * 0.5f;

    private struct Tri {
        public int v1;
        public int v2;
        public int v3;

        public Tri(int v1, int v2, int v3) {
            this.v1 = v1;
            this.v2 = v2;
            this.v3 = v3;
        }
    }

    private struct VertCache {
        public int p1;
        public int p2;
        public int p3;
        public int t1;
        public int t2;

        public VertCache(int p1, int p2, int t1, int t2) {
            this.p1 = Mathf.Min(p1, p2);
            this.p2 = Mathf.Max(p1, p2);
            p3 = -1;
            this.t1 = (p1 < p2) ? (t1) : (t2 - t1);
            this.t2 = t2;
        }

        public VertCache(int p1, int p2, int p3) {
            this.p1 = Mathf.Min(p1, p2, p3);
            this.p3 = Mathf.Max(p1, p2, p3);
            this.p2 = p1 ^ p2 ^ p3 ^ this.p1 ^ this.p3;
            t1 = -1;
            t2 = -1;
        }
    }

    private void Awake() {
        meshFilter = GetComponent<MeshFilter>();
    }

    private void Start() {
        GenerateIcoSphere();
    }

    private void GenerateIcoSphere() {
        // create 12 vertices of a icosahedron
        float t = GOLDEN_RATIO;
        List<Vector3> verts = new() {
            new(-1,  t,  0), new(1, t, 0), new(-1, -t,  0), new( 1, -t,  0),
            new( 0, -1,  t), new(0, 1, t), new( 0, -1, -t), new( 0,  1, -t),
            new( t,  0, -1), new(t, 0, 1), new(-t,  0, -1), new(-t,  0,  1)
        };
        List<Vector2> uvs = new();
        for (int i = 0; i < verts.Count; i++) {
            Vector3 norm = verts[i].normalized;
            verts[i] = norm;
            uvs.Add(SphereToUV(norm));
        }

        // create 20 triangles of the icosahedron
        List<Tri> faces = new() {
            new(0, 11, 5), new(0, 5, 1),  new(0, 1, 7),   new(0, 7, 10), new(0, 10, 11), // 5 faces around point 0
            new(1, 5, 9),  new(5, 11, 4), new(11, 10, 2), new(10, 7, 6), new(7, 1, 8),   // 5 adjacent faces
            new(3, 9, 4),  new(3, 4, 2),  new(3, 2, 6),   new(3, 6, 8),  new(3, 8, 9),   // 5 faces around point 3
            new(4, 9, 5),  new(2, 4, 11), new(6, 2, 10),  new(8, 6, 7),  new(9, 8, 1)    // 5 adjacent faces
        };

        // refine triangles
        Dictionary<VertCache, int> cache = new();
        for (int i = 0; i < recursion; ++i) {
            List<Tri> faces2 = new();
            foreach (Tri tri in faces) {
                int v1 = tri.v1;
                int v2 = tri.v2;
                int v3 = tri.v3;

                // 生成 9 个小三角形
                //        v1
                //       / \
                //     c2---a1
                //     / \ / \
                //   c1---o---a2
                //   / \ / \ / \
                // v3--b2---b1--v2
                int a1 = GetSplitPoint(cache, verts, uvs, v1, v2, 1, 3);
                int a2 = GetSplitPoint(cache, verts, uvs, v1, v2, 2, 3);
                int b1 = GetSplitPoint(cache, verts, uvs, v2, v3, 1, 3);
                int b2 = GetSplitPoint(cache, verts, uvs, v2, v3, 2, 3);
                int c1 = GetSplitPoint(cache, verts, uvs, v3, v1, 1, 3);
                int c2 = GetSplitPoint(cache, verts, uvs, v3, v1, 2, 3);
                int o = GetTriMidPoint(cache, verts, uvs, v1, v2, v3);

                faces2.Add(new(v1, a1, c2));
                faces2.Add(new(c2, a1, o));
                faces2.Add(new(a1, a2, o));
                faces2.Add(new(c2, o, c1));
                faces2.Add(new(o, b1, b2));
                faces2.Add(new(o, a2, b1));
                faces2.Add(new(c1, o, b2));
                faces2.Add(new(a2, v2, b1));
                faces2.Add(new(c1, b2, v3));
            }
            faces = faces2;
        }

        // 缩放顶点到目标半径
        for (int i = 0; i < verts.Count; ++i) {
            verts[i] *= radius;
        }

        int[] tris = new int[faces.Count * 3];
        for (int i = 0; i < faces.Count; ++i) {
            tris[3 * i + 0] = faces[i].v1;
            tris[3 * i + 1] = faces[i].v2;
            tris[3 * i + 2] = faces[i].v3;
        }

        // 构建Mesh
        Mesh mesh = new() {
            vertices = verts.ToArray(),
            uv = uvs.ToArray(),
            triangles = tris
        };

        mesh.RecalculateNormals(); // 自动计算法线，实现光照效果
        mesh.RecalculateBounds();

        meshFilter.mesh = mesh;
    }

    // 分割点为t1/t2
    private int GetSplitPoint(Dictionary<VertCache, int> cache, List<Vector3> verts, List<Vector2> uvs, int p1, int p2, int t1, int t2) {
        VertCache key = new VertCache(p1, p2, t1, t2);
        if (cache.TryGetValue(key, out int ret)) {
            return ret;
        }

        // not in cache, calculate it
        Vector3 point1 = verts[p1];
        Vector3 point2 = verts[p2];
        Vector3 pointSplit = Vector3.Lerp(point1, point2, (t1 * 1.0f) / t2).normalized;

        // add vertex makes sure point is on unit sphere
        verts.Add(pointSplit);
        uvs.Add(SphereToUV(pointSplit));
        int i = verts.Count - 1;

        cache.Add(key, i);
        return i;
    }

    private int GetTriMidPoint(Dictionary<VertCache, int> cache, List<Vector3> verts, List<Vector2> uvs, int p1, int p2, int p3) {
        VertCache key = new VertCache(p1, p2, p3);
        if (cache.TryGetValue(key, out int ret)) {
            return ret;
        }

        // not in cache, calculate it
        Vector3 point1 = verts[p1];
        Vector3 point2 = verts[p2];
        Vector3 point3 = verts[p3];
        Vector3 pointSplit = ((point1 + point2 + point3) / 3.0f).normalized;

        // add vertex makes sure point is on unit sphere
        verts.Add(pointSplit);
        uvs.Add(SphereToUV(pointSplit));
        int i = verts.Count - 1;

        cache.Add(key, i);
        return i;
    }

    // 将单位球面上的点转换为UV坐标 (经纬度映射)
    private Vector2 SphereToUV(Vector3 p) {
        float u = Mathf.Atan2(p.z, p.x) / (2.0f * Mathf.PI) + 0.5f;
        float v = 0.5f + Mathf.Asin(p.y) / Mathf.PI;
        return new Vector2(u, v);
    }

#if UNITY_EDITOR
    private void OnValidate() {
        if (Application.isPlaying) {
            return;
        }
        if (meshFilter == null) {
            meshFilter = GetComponent<MeshFilter>();
        }
        if (meshFilter.sharedMesh != null) {
            GenerateIcoSphere();
        }
    }
#endif
}
