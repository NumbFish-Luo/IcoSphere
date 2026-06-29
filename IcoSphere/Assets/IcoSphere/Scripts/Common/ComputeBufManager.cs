using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    /// <summary>
    /// ComputeBuffer管理工具, 防止内存泄漏
    /// </summary>
    public class ComputeBufManager : MonoBehaviour {
        // ---- 私有静态变量 ----
        private static ComputeBufManager instance;
        private static bool quitting = false;
        private readonly static HashSet<ComputeBuffer> trackeds = new();
        private readonly static Queue<ComputeBuffer> pendingReleases = new();

        // ---- Unity生命周期函数 ----
        private void Update() {
            ProcessPendingReleases();
        }

        private void OnDestroy() {
            OnDestroyOrQuit();
        }

        private void OnApplicationQuit() {
            OnDestroyOrQuit();
        }

        // ---- 公有静态函数 ----
        /// <summary>
        /// 创建ComputeBuffer
        /// </summary>
        /// <param name="count">容量</param>
        /// <param name="stride">步长, 一般是sizeof(T), 或者Marshal.SizeOf(typeof(T))</param>
        /// <param name="type">ComputeBuffer类型</param>
        /// <returns>ComputeBuffer实例</returns>
        public static ComputeBuffer NewBuf(int count, int stride, ComputeBufferType type = ComputeBufferType.Default) {
            if (InitInstance() == null) {
                return null;
            }

            ComputeBuffer buf = new(count, stride, type);
            trackeds.Add(buf);

            return buf;
        }

        /// <summary>
        /// 将需要回收的ComputeBuffer放入回收队列中, 这个队列将会在Update中每帧自动回收
        /// </summary>
        /// <param name="buf">需要回收的ComputeBuffer</param>
        public static void ScheduleRelease(ComputeBuffer buf) {
            if (buf == null) {
                return;
            }
            pendingReleases.Enqueue(buf);
        }

        // ---- 私有静态函数 ----
        // 单例模式
        private static ComputeBufManager InitInstance() {
            if (quitting) {
                Debug.LogWarning("退出应用程序中, 无法创建ComputeBuffer");
                return null;
            }

            // 单例模式
            if (instance == null) {
                instance = FindAnyObjectByType(typeof(ComputeBufManager)) as ComputeBufManager;
                if (instance != null) {
                    DontDestroyOnLoad(instance.gameObject);
                }
            }
            if (instance == null) {
                GameObject obj = new("ComputeBufManager");
                instance = obj.AddComponent<ComputeBufManager>();
                DontDestroyOnLoad(obj);
            }
            return instance;
        }

        private static void ReleaseImmediate(ComputeBuffer buf) {
            if (buf == null) {
                return;
            }

            if (trackeds.Contains(buf)) {
                trackeds.Remove(buf);
            }

            try {
                buf.Release();
            } catch (System.Exception e) {
                Debug.LogError($"Failed to release buffer: {e.Message}");
            }
        }

        private static void ProcessPendingReleases() {
            if (quitting) {
                return;
            }

            int maxReleasePerFrame = 10;
            int released = 0;

            while (pendingReleases.Count > 0 && released < maxReleasePerFrame) {
                ComputeBuffer buf = pendingReleases.Dequeue();
                if (buf != null) {
                    ReleaseImmediate(buf);
                    ++released;
                }
            }
        }

        private static void ForceReleaseAll() {
            foreach (ComputeBuffer b in trackeds) {
                try {
                    b?.Release();
                } catch {
                    // ...
                }
            }
            trackeds.Clear();

            while (pendingReleases.Count > 0) {
                ComputeBuffer b = pendingReleases.Dequeue();
                try {
                    b?.Release();
                } catch {
                    // ...
                }
            }
        }

        // ---- 私有成员函数 ----
        private void OnDestroyOrQuit() {
            quitting = true;
            ForceReleaseAll();
            instance = null;
        }
    }
}
