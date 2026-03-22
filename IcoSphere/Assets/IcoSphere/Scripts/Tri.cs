using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public struct Tri {
        public int v1;
        public int v2;
        public int v3;

        public Tri(int v1, int v2, int v3) {
            this.v1 = v1;
            this.v2 = v2;
            this.v3 = v3;
        }
    }

    public static class TriEx {
        public static int[] ToIntArr(this List<Tri> tris) {
            int[] iTris = new int[tris.Count * 3];
            for (int i = 0; i < tris.Count; ++i) {
                iTris[3 * i + 0] = tris[i].v1;
                iTris[3 * i + 1] = tris[i].v2;
                iTris[3 * i + 2] = tris[i].v3;
            }
            return iTris;
        }
    }
}
