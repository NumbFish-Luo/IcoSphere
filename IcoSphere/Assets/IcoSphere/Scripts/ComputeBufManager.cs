using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    // ComputeBuffer管理工具, 防止内存泄漏
    public class ComputeBufManager : MonoBehaviour {
        private static ComputeBufManager instance;
        private static bool quitting = false;
        private readonly static HashSet<ComputeBuffer> trackeds = new();
        private readonly static Queue<ComputeBuffer> pendingReleases = new();

        public static ComputeBufManager InitInstance() {
            if (quitting) {
                Debug.LogWarning("Application is quitting, cannot create ComputeBuffer");
                return null;
            }
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

        private void Update() {
            ProcessPendingReleases();
        }

        private void OnDestroy() {
            OnDestroyOrQuit();
        }

        private void OnApplicationQuit() {
            OnDestroyOrQuit();
        }

        private void OnDestroyOrQuit() {
            quitting = true;
            ForceReleaseAll();
            instance = null;
        }

        public static ComputeBuffer NewBuf(int count, int stride, ComputeBufferType type = ComputeBufferType.Default) {
            if (InitInstance() == null) {
                return null;
            }

            ComputeBuffer buf = new(count, stride, type);
            lock (trackeds) {
                trackeds.Add(buf);
            }

            return buf;
        }

        public static void ScheduleRelease(ComputeBuffer buf) {
            if (buf == null) {
                return;
            }

            lock (pendingReleases) {
                pendingReleases.Enqueue(buf);
            }
        }

        private static void ReleaseImmediate(ComputeBuffer buf) {
            if (buf == null) {
                return;
            }

            lock (trackeds) {
                if (trackeds.Contains(buf)) {
                    trackeds.Remove(buf);
                }
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

            lock (pendingReleases) {
                while (pendingReleases.Count > 0 && released < maxReleasePerFrame) {
                    ComputeBuffer buf = pendingReleases.Dequeue();
                    if (buf != null) {
                        ReleaseImmediate(buf);
                        ++released;
                    }
                }
            }
        }

        private static void ForceReleaseAll() {
            lock (trackeds) {
                foreach (ComputeBuffer b in trackeds) {
                    try {
                        b?.Release();
                    } catch {
                        // ...
                    }
                }
                trackeds.Clear();
            }

            lock (pendingReleases) {
                while (pendingReleases.Count > 0) {
                    ComputeBuffer b = pendingReleases.Dequeue();
                    try {
                        b?.Release();
                    } catch {
                        // ...
                    }
                }
            }
        }
    }
}
