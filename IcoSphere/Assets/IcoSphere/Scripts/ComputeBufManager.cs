using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class ComputeBufManager : MonoBehaviour {
        private static ComputeBufManager instance;
        public static ComputeBufManager Instance {
            get {
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
        }

        private readonly static HashSet<ComputeBuffer> trackeds = new();
        private readonly static Queue<ComputeBuffer> pendingReleases = new();
        private static bool quitting = false;

        private void Awake() {
            Application.quitting += OnQuitting;
        }

        private void Update() {
            ProcessPendingReleases();
        }

        private void OnDestroy() {
            ForceReleaseAll();
        }

        public ComputeBuffer NewBuf(int count, int stride, ComputeBufferType type = ComputeBufferType.Default) {
            if (quitting) {
                Debug.LogWarning("Application is quitting, cannot create ComputeBuffer");
                return null;
            }

            ComputeBuffer buf = new(count, stride, type);
            lock (trackeds) {
                trackeds.Add(buf);
            }

            return buf;
        }

        public void ScheduleRelease(ComputeBuffer buf) {
            if (buf == null) {
                return;
            }

            lock (pendingReleases) {
                pendingReleases.Enqueue(buf);
            }
        }

        public void ReleaseImmediate(ComputeBuffer buf) {
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

        private void ProcessPendingReleases() {
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

        private void OnQuitting() {
            quitting = true;
            ForceReleaseAll();
        }

        public void ForceReleaseAll() {
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
