using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    // 生成完整多个三角形mesh组成球体
    // 参考: http://blog.andreaskahler.com/2009/06/creating-icosphere-mesh-in-code.html
    public class IcoSphere : MonoBehaviour {
        [SerializeField] private Material mat;
        [SerializeField] private float radius = 1f;
        [SerializeField, Range(0, 3)] private int recursion = 2; // 递归细分次数, 越大面数越多

        [Header("测试")]
        [SerializeField] private bool testAnim = false;

        private readonly List<ObjTri> objTris = new();

        private void Start() {
            NewIcoSphere();
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Space)) {
                testAnim = !testAnim;
            }
            if (testAnim == false) {
                return;
            }

            int n = objTris.Count;
            float t = radius * Mathf.Lerp(0, 0.02f, (Mathf.Sin(Time.time) + 1.0f) * 0.5f);
            transform.rotation = Quaternion.Euler(0, Time.time, 0);
            for (int i = 0; i < n; ++i) {
                objTris[i].obj.transform.localPosition = new Vector3(
                    Mathf.Cos(i + Time.time) * t,
                    Mathf.Sin(i + Time.time) * t,
                    0);
            }
        }

        private void NewIcoSphere() {
            Pack pack = NewData(radius, recursion);
            NewObjTris(pack);
        }

        public static Pack NewData(float radius, int recursion) {
            Pack pack = new();

            // create 12 vertices of a icosahedron
            float t = Misc.GOLDEN_RATIO;
            pack.verts = new() {
                new(-1,  t,  0), new(1, t, 0), new(-1, -t,  0), new( 1, -t,  0),
                new( 0, -1,  t), new(0, 1, t), new( 0, -1, -t), new( 0,  1, -t),
                new( t,  0, -1), new(t, 0, 1), new(-t,  0, -1), new(-t,  0,  1)
            };
            pack.uvs = new();
            pack.cols = new();
            for (int i = 0; i < pack.verts.Count; i++) {
                Vector3 norm = pack.verts[i].normalized;
                pack.verts[i] = norm;
                pack.uvs.Add(SphereToUV(norm));
                pack.cols.Add(Color.white);
            }

            // create 20 triangles of the icosahedron
            pack.tris = new() {
                new(0, 11, 5), new(0, 5, 1),  new(0, 1, 7),   new(0, 7, 10), new(0, 10, 11), // 5 faces around point 0
                new(1, 5, 9),  new(5, 11, 4), new(11, 10, 2), new(10, 7, 6), new(7, 1, 8),   // 5 adjacent faces
                new(3, 9, 4),  new(3, 4, 2),  new(3, 2, 6),   new(3, 6, 8),  new(3, 8, 9),   // 5 faces around point 3
                new(4, 9, 5),  new(2, 4, 11), new(6, 2, 10),  new(8, 6, 7),  new(9, 8, 1)    // 5 adjacent faces
            };

            // refine triangles
            Dictionary<VertCache, int> cache = new();
            for (int i = 0; i < recursion; ++i) {
                List<Tri> tris2 = new();
                foreach (Tri tri in pack.tris) {
                    int v1 = tri.v1;
                    int v2 = tri.v2;
                    int v3 = tri.v3;

                    // 生成9个小三角形
                    //        v1
                    //       / \
                    //     c2---a1
                    //     / \ / \
                    //   c1---o---a2
                    //   / \ / \ / \
                    // v3--b2---b1--v2
                    int a1 = GetSplitPoint(cache, pack, v1, v2, 1, 3, false);
                    int a2 = GetSplitPoint(cache, pack, v1, v2, 2, 3, false);
                    int b1 = GetSplitPoint(cache, pack, v2, v3, 1, 3, false);
                    int b2 = GetSplitPoint(cache, pack, v2, v3, 2, 3, false);
                    int c1 = GetSplitPoint(cache, pack, v3, v1, 1, 3, false);
                    int c2 = GetSplitPoint(cache, pack, v3, v1, 2, 3, false);
                    int o = GetTriMidPoint(cache, pack, v1, v2, v3, true);

                    tris2.Add(new(v1, a1, c2));
                    tris2.Add(new(c2, a1, o));
                    tris2.Add(new(a1, a2, o));
                    tris2.Add(new(c2, o, c1));
                    tris2.Add(new(o, b1, b2));
                    tris2.Add(new(o, a2, b1));
                    tris2.Add(new(c1, o, b2));
                    tris2.Add(new(a2, v2, b1));
                    tris2.Add(new(c1, b2, v3));
                }
                pack.tris = tris2;
            }

            // 缩放顶点到目标半径
            for (int i = 0; i < pack.verts.Count; ++i) {
                pack.verts[i] *= radius;
            }

            return pack;
        }

        public void TestNewIcoSphereWhole(Pack pack) {
            string name = "TestWhole";
            GameObject obj = new(name);
            Transform tfWhole = obj.transform;
            tfWhole.SetParent(transform);
            tfWhole.localPosition = Vector3.zero;
            MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
            obj.AddComponent<MeshRenderer>().material = mat;
            PackToArr packToArr = new(pack);
            Mesh mesh = new() {
                name = name,
                vertices = packToArr.verts,
                uv = packToArr.uvs,
                colors = packToArr.cols,
                triangles = packToArr.tris
            };
            mesh.RecalculateNormals(); // 自动计算法线，实现光照效果
            mesh.RecalculateBounds();
            meshFilter.mesh = mesh;
        }

        // 分割点为t1/t2
        private static int GetSplitPoint(Dictionary<VertCache, int> cache, Pack pack, int p1, int p2, int t1, int t2, bool randomRgb) {
            VertCache key = new(p1, p2, t1, t2);
            if (cache.TryGetValue(key, out int ret)) {
                return ret;
            }

            // not in cache, calculate it
            Vector3 point1 = pack.verts[p1];
            Vector3 point2 = pack.verts[p2];
            Vector3 pointSplit = Vector3.Lerp(point1, point2, (t1 * 1.0f) / t2).normalized;

            // add vertex makes sure point is on unit sphere
            pack.verts.Add(pointSplit);
            pack.uvs.Add(SphereToUV(pointSplit));
            int i = pack.verts.Count - 1;
            pack.cols.Add(randomRgb ? Misc.RandomRgb(i) : Color.clear);
            cache.Add(key, i);
            return i;
        }

        private static int GetTriMidPoint(Dictionary<VertCache, int> cache, Pack pack, int p1, int p2, int p3, bool randomRgb) {
            VertCache key = new(p1, p2, p3);
            if (cache.TryGetValue(key, out int ret)) {
                return ret;
            }

            // not in cache, calculate it
            Vector3 point1 = pack.verts[p1];
            Vector3 point2 = pack.verts[p2];
            Vector3 point3 = pack.verts[p3];
            Vector3 pointSplit = ((point1 + point2 + point3) / 3.0f).normalized;

            // add vertex makes sure point is on unit sphere
            pack.verts.Add(pointSplit);
            pack.uvs.Add(SphereToUV(pointSplit));
            int i = pack.verts.Count - 1;
            pack.cols.Add(randomRgb ? Misc.RandomRgb(i) : Color.clear);
            cache.Add(key, i);
            return i;
        }

        // 将单位球面上的点转换为UV坐标, 经纬度映射, 默认p为单位向量
        public static Vector2 SphereToUV(Vector3 p) {
            float u = Mathf.Atan2(p.z, p.x) / (2.0f * Mathf.PI) + 0.5f;
            float v = 0.5f + Mathf.Asin(p.y) / Mathf.PI;
            return new Vector2(u, v);
        }

        public static void DestroyAllObjTris(List<ObjTri> objTris) {
            foreach (ObjTri o in objTris) {
                Destroy(o.obj);
            }
            objTris.Clear();
        }

        public void DestroyAllObjTris() {
            DestroyAllObjTris(objTris);
        }

        public void NewObjTris(Pack pack) {
            DestroyAllObjTris();

            int n = pack.tris.Count;
            for (int i = 0; i < n; ++i) {
                string name = "Tri_" + i;
                ObjTri objTri = new(name, mat, transform);
                objTris.Add(objTri);
                PackToArr packToArr = new(pack, i);
                Mesh mesh = new() {
                    name = name,
                    vertices = packToArr.verts,
                    uv = packToArr.uvs,
                    colors = packToArr.cols,
                    triangles = packToArr.tris,
                };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                objTri.meshFilter.mesh = mesh;
            }
        }
    }
}
