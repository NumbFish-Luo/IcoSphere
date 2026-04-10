using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IcoSphere {
    // 简单的相机控制脚本
    public class CamCtrl : MonoBehaviour {
        [SerializeField] private Camera cam;
        [SerializeField] private Text txtInfo;
        [SerializeField] private IcoSphere icoSphere;
        [SerializeField] private float spdMove = 1.0f;
        [SerializeField] private float spdFly = 1.0f;

        private float rotX = 0.0f;
        private float rotY = 0.0f;
        private float height = 0.0f;

        private void Awake() {
            height = -icoSphere.SphereRadius * 2.0f;
            cam.transform.localPosition = new Vector3(0.0f, 0.0f, height);
        }

        private void Update() {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            float fly = Input.GetAxis("Jump"); // 默认是空格
            float fall = Input.GetAxis("Fire3"); // 默认是左shift

            rotX -= v * spdMove;
            rotY += h * spdMove;
            height -= fly * spdFly;
            height += fall * spdFly;

            transform.localRotation = Quaternion.Euler(rotX, rotY, 0);
            cam.transform.localPosition = new Vector3(0.0f, 0.0f, height);

            txtInfo.text = $"相机旋转: {rotX}, {rotY}\n相机高度: {-height}";
        }
    }
}
