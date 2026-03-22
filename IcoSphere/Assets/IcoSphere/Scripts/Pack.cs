using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class Pack {
        public List<Vector3> verts;
        public List<Vector2> uvs;
        public List<Color> cols;
        public List<Tri> tris;
    }

    public class PackToArr {
        public readonly Vector3[] verts;
        public readonly Vector2[] uvs;
        public readonly Color[] cols;
        public readonly int[] tris;

        public PackToArr(Pack pack) {
            verts = pack.verts.ToArray();
            uvs = pack.uvs.ToArray();
            cols = pack.cols.ToArray();
            tris = pack.tris.ToIntArr();
        }

        public PackToArr(Pack pack, int triIdx) {
            Tri packTris = pack.tris[triIdx];
            int v1 = packTris.v1;
            int v2 = packTris.v2;
            int v3 = packTris.v3;
            verts = new Vector3[3] { pack.verts[v1], pack.verts[v2], pack.verts[v3] };
            uvs = new Vector2[3] { pack.uvs[v1], pack.uvs[v2], pack.uvs[v3] };
            cols = new Color[3] { pack.cols[v1], pack.cols[v2], pack.cols[v3] };
            tris = new int[3] { 0, 1, 2 };
        }
    }
}
