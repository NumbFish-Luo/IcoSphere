using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class CommonTest : MonoBehaviour {
        private void Awake() {
            Queue<int> q = new();
            for (int i = 0; i < 10; ++i) {
                q.Enqueue(i);
            }

            Debug.Log(q.Dequeue());
        }

        private void Start() {
            
        }
    }
}
