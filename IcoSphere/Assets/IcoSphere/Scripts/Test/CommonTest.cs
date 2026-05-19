using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class CommonTest : MonoBehaviour {
        [SerializeField] private IcoSphere icoSphere;
        [SerializeField] private bool doTest;
        [SerializeField] private GameObject testBall;
        [SerializeField] private Color highlight = Color.white;

        private void Update() {
            if (doTest) {
                doTest = false;
                // 在这里随意加点要反复测试的代码
                TestDict();
            }

            // 左键按下时的测试代码
            if (Input.GetMouseButtonDown(0)) {
                TestRay();
            }

            // 右键持续按下时的测试代码
            if (Input.GetMouseButton(1)) {
                TestHighlight();
            }
        }

        // 搜索测试
        private void TestFind() {
            Vector3 p = icoSphere.GetRawAreaCenter(12345);
            PosVert[] s = icoSphere.GetRawSortedAreas();
            int i = icoSphere.FindAreaByPos(p);
            Debug.Log(s[i].v); // 应该输出12345
        }

        // 字典测试
        private readonly Dictionary<Vector3, int> dict = new();
        private void TestDict() {
            dict.Clear();
            Vector3[] u = icoSphere.GetRawUnsortedAreas();
            int n = u.Length;
            for (int i = 0; i < n; ++i) {
                dict.Add(u[i], i);
            }
            Debug.Log("完成字典构建");
        }

        // 射线检测
        private void TestRay() {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (icoSphere.TryPickArea(ray, out int i)) {
                Debug.Log(i);
                testBall.transform.position = icoSphere.GetAreaCenter(i);
            } else {
                testBall.transform.position = Vector3.zero;
            }
        }

        // 修改高亮
        private void TestHighlight() {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (icoSphere.TryPickArea(ray, out int i)) {
                icoSphere.SetAreaColor(i, highlight);
            }
        }
    }
}
