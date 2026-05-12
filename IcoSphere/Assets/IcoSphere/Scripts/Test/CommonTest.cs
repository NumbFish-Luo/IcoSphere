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
                Debug.Log("地块总数: " + icoSphere.GetAreaCount());
                Debug.Log("地块[0]中心点世界坐标: " + icoSphere.GetAreaCenter(0));
                Debug.Log("地块[0]相邻地块数量: " + icoSphere.GetNeighborCount(0));
                Debug.Log("地块[0]相邻地块[0]的id: " + icoSphere.GetNeighborId(0, 0));
            }
        }
    }
}
