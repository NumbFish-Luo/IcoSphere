using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public static class Math {
        public readonly static float GOLDEN_RATIO = (1.0f + Mathf.Sqrt(5.0f)) * 0.5f;

        // 输入球心坐标, 球体半径, 射线起点, 射线方向
        // 输出射线触碰到的球面坐标
        // 返回值如果为false则没有触碰到球面
        public static bool GetRayResult(Vector3 sphereCenter, float radius, Vector3 rayOrigin, Vector3 rayDir, out Vector3 sphereSurfacePoint) {
            sphereSurfacePoint = Vector3.zero;

            Vector3 o = sphereCenter;
            Vector3 p = rayOrigin - o;
            Vector3 v = rayDir;
            float r = radius;
            float a = v.sqrMagnitude;
            float b = 2.0f * Vector3.Dot(p, v);
            float c = p.sqrMagnitude - r * r;
            float d = b * b - 4.0f * a * c;
            if (d < 0.0f) {
                return false;
            }

            float t1 = (-b - Mathf.Sqrt(d)) / (2.0f * a);
            float t2 = (-b + Mathf.Sqrt(d)) / (2.0f * a);
            float t;
            if (t1 >= 0.0f) {
                t = t1;
            } else if (t2 >= 0) {
                t = t2;
            } else {
                return false;
            }

            sphereSurfacePoint = p + t * v + o;
            return true;
        }

        // 输入相机, 球体半径, 射线起点
        // 输出鼠标点击生成的射线, 以及射线触碰到的球面坐标
        // 返回值如果为false则没有触碰到球面
        public static bool GetRayResult(Vector3 sphereCenter, float radius, Camera cam, out Ray ray, out Vector3 sphereSurfacePoint) {
            ray = cam.ScreenPointToRay(Input.mousePosition);
            return GetRayResult(sphereCenter, radius, ray.origin, ray.direction, out sphereSurfacePoint);
        }

        // 输入IcoSphere球体, 相机
        // 输出鼠标点击生成的射线, 以及射线触碰到的球面坐标
        // 返回值如果为false则没有触碰到球面
        public static bool GetRayResult(IcoSphere icoSphere, Camera cam, out Ray ray, out Vector3 sphereSurfacePoint) {
            return GetRayResult(Vector3.zero, icoSphere.SphereRadius, cam, out ray, out sphereSurfacePoint);
        }
    }
}
