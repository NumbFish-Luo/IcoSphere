using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    // mesh所需数据打包
    public class Pack {
        public List<Vector3> verts;
        public List<Vector2> uvs;
        public List<Color> cols;
        public List<Tri> tris;
    }
}
