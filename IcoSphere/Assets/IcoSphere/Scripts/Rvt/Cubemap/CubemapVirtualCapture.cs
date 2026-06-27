using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class CubemapVirtualCapture : MonoBehaviour {
        public void Init(int vtTexSize) {

        }

        public void VirtualCaptureMrt(CubemapQuadTree node, out RenderTexture rtAlbedo, out RenderTexture rtNormal) {
            rtAlbedo = null;
            rtNormal = null;
        }
    }
}
