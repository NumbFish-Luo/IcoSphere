using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    /// <summary>
    /// IcoSphere数学库
    /// </summary>
    public static class Math {
        /// <summary>
        /// 黄金比例
        /// </summary>
        public readonly static float GOLDEN_RATIO = (1.0f + Mathf.Sqrt(5.0f)) * 0.5f;

        /// <summary>
        /// 输出射线触碰到的球面坐标
        /// </summary>
        /// <param name="sphereCenter">球心坐标</param>
        /// <param name="radius">球体半径</param>
        /// <param name="rayOrigin">射线起点</param>
        /// <param name="rayDir">射线方向</param>
        /// <param name="sphereSurfacePoint">输出射线触碰到的球面坐标</param>
        /// <returns>如果为false则没有触碰到球面</returns>
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

        /// <summary>
        /// 输出鼠标点击生成的射线, 以及射线触碰到的球面坐标
        /// </summary>
        /// <param name="sphereCenter">球心坐标</param>
        /// <param name="radius">球体半径</param>
        /// <param name="cam">相机</param>
        /// <param name="ray">输出鼠标点击生成的射线</param>
        /// <param name="sphereSurfacePoint">输出射线触碰到的球面坐标</param>
        /// <returns>如果为false则没有触碰到球面</returns>
        public static bool GetRayResult(Vector3 sphereCenter, float radius, Camera cam, out Ray ray, out Vector3 sphereSurfacePoint) {
            ray = cam.ScreenPointToRay(Input.mousePosition);
            return GetRayResult(sphereCenter, radius, ray.origin, ray.direction, out sphereSurfacePoint);
        }

        /// <summary>
        /// 输出鼠标点击生成的射线, 以及射线触碰到的球面坐标
        /// </summary>
        /// <param name="icoSphere">IcoSphere球体</param>
        /// <param name="cam">相机</param>
        /// <param name="ray">输出鼠标点击生成的射线</param>
        /// <param name="sphereSurfacePoint">输出射线触碰到的球面坐标</param>
        /// <returns>如果为false则没有触碰到球面</returns>
        public static bool GetRayResult(IcoSphere icoSphere, Camera cam, out Ray ray, out Vector3 sphereSurfacePoint) {
            return GetRayResult(Vector3.zero, icoSphere.SphereRadius, cam, out ray, out sphereSurfacePoint);
        }
    }
}
