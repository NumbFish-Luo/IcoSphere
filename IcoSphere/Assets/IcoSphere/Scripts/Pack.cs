using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    // mesh所需数据打包
    public struct Pack {
        public List<Vector3> verts;
        public List<Tri> tris;
        public Dictionary<Edge, Abut> abuts;

        public Pack(PackArr packArr) {
            verts = new List<Vector3>(packArr.verts);
            tris = new List<Tri>(packArr.tris);
            abuts = packArr.GetAbuts();
        }

        public void CalcAbuts(List<Tri> tris = null) {
            tris ??= this.tris;
            abuts = new();
            int n = tris.Count;
            for (int j = 0; j < n; ++j) {
                Tri t = tris[j];
                int v1 = t.v1;
                int v2 = t.v2;
                int v3 = t.v3;
                Edge e1 = new(v1, v2);
                Edge e2 = new(v2, v3);
                Edge e3 = new(v3, v1);

                if (abuts.TryGetValue(e1, out Abut a1) == false) {
                    a1 = Abut.New;
                }
                a1.Push(j);
                abuts[e1] = a1;

                if (abuts.TryGetValue(e2, out Abut a2) == false) {
                    a2 = Abut.New;
                }
                a2.Push(j);
                abuts[e2] = a2;

                if (abuts.TryGetValue(e3, out Abut a3) == false) {
                    a3 = Abut.New;
                }
                a3.Push(j);
                abuts[e3] = a3;
            }
        }
    }
}
