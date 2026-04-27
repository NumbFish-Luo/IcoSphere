using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    // 鼠标射线检测
    public class RayTest : MonoBehaviour {
        [SerializeField] private Camera cam;
        [SerializeField] private IcoSphere icoSphere;
        [SerializeField] private GameObject testSphereSurfacePoint;

        private void Update() {
            if (Math.GetRayResult(icoSphere, cam, out Ray ray, out Vector3 sphereSurfacePoint)) {
                testSphereSurfacePoint.transform.position = sphereSurfacePoint;
                Debug.DrawRay(ray.origin, ray.direction * 100.0f, Color.green, 1.0f);
            } else {
                testSphereSurfacePoint.transform.position = Vector3.zero;
                Debug.DrawRay(ray.origin, ray.direction * 100.0f, Color.red, 1.0f);
            }
        }
    }
}
