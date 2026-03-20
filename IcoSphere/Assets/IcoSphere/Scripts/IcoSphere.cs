using System;
using System.Collections.Generic;
using Unity.VisualScripting.Dependencies.Sqlite;
using UnityEngine;

// 参考: http://blog.andreaskahler.com/2009/06/creating-icosphere-mesh-in-code.html
public class IcoSphere : MonoBehaviour {
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private float radius = 1f;
    [SerializeField, Range(0, 5)] private int recursion = 2; // 递归细分次数，越大面数越多

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
        Dictionary<Int64, int> cache = new();
        for (int i = 0; i < recursion; ++i) {
            List<Tri> faces2 = new();
            foreach (Tri tri in faces) {
                // replace triangle by 4 triangles
                int a = GetMidPoint(cache, verts, uvs, tri.v1, tri.v2);
                int b = GetMidPoint(cache, verts, uvs, tri.v2, tri.v3);
                int c = GetMidPoint(cache, verts, uvs, tri.v3, tri.v1);

                faces2.Add(new(tri.v1, a, c));
                faces2.Add(new(tri.v2, b, a));
                faces2.Add(new(tri.v3, c, b));
                faces2.Add(new(a, b, c));
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

    // return index of point in the middle of p1 and p2
    private int GetMidPoint(Dictionary<Int64, int> cache, List<Vector3> verts, List<Vector2> uvs, int p1, int p2) {
        // first check if we have it already
        bool firstIsSmaller = p1 < p2;
        Int64 smallerIdx = firstIsSmaller ? p1 : p2;
        Int64 greaterIdx = firstIsSmaller ? p2 : p1;
        Int64 key = (smallerIdx << 32) + greaterIdx;

        if (cache.TryGetValue(key, out int ret)) {
            return ret;
        }

        // not in cache, calculate it
        Vector3 point1 = verts[p1];
        Vector3 point2 = verts[p2];
        Vector3 mid = ((point1 + point2) * 0.5f).normalized;

        // add vertex makes sure point is on unit sphere
        verts.Add(mid);
        uvs.Add(SphereToUV(mid));
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
