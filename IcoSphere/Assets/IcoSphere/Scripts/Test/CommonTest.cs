using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class CommonTest : MonoBehaviour {
        [SerializeField] private IcoSphere icoSphere;
        [SerializeField] private bool doTest;

        private void Update() {
            if (doTest) {
                doTest = false;
                // 在这里随意加点要反复测试的代码

                // 搜索测试
                Vector3 p = icoSphere.GetRawAreaCenter(12345);
                PosVert[] s = icoSphere.GetRawSortedAreas();
                int i = PosVert.BinarySearch(icoSphere.GetRawSortedAreas(), p);
                Debug.Log(s[i].v); // 应该输出12345
            }
        }
    }
}
