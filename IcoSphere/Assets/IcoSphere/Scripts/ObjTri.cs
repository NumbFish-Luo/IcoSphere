using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class ObjTri {
        public readonly GameObject obj;
        public readonly MeshFilter meshFilter;

        public ObjTri(string name, Material mat, Transform parent) {
            obj = new GameObject(name);
            meshFilter = obj.AddComponent<MeshFilter>();
            obj.AddComponent<MeshRenderer>().material = mat;
            Transform tf = obj.transform;
            tf.SetParent(parent);
            tf.localPosition = Vector3.zero;
        }
    }
}
