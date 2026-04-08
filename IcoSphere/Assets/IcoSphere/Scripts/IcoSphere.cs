using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class IcoSphere : MonoBehaviour {
        [SerializeField, Range(0, 5)] private int recursion = 3;

        private void Start() {
            Init();
        }

        private void Init() {
            Pack.Read(recursion);
        }
    }
}
