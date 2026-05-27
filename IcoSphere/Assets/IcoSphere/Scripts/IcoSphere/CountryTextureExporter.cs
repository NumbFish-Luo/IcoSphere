using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IcoSphere {
    public class CountryTextureExporter : MonoBehaviour {
        [SerializeField] private CountryColorDrawer countryColorDrawer = null;
        [SerializeField] private IcoSphere icoSphere = null;
        [SerializeField, Min(1)] private int exportTexWidth = 2048;
        [SerializeField, Min(1)] private int exportTexHeight = 1024;
        [SerializeField] private string exportTexPath = "Assets/IcoSphere/Resources/Cfg/export_country_map.png";
        [SerializeField, Min(0)] private int exportLonBuckets = 0;
        [SerializeField, Min(0)] private int exportLatBuckets = 0;
        [SerializeField, Min(0)] private int exportBucketSearchRadius = 2;

        private void Reset() {
            ResolveReferences(false);
        }

        private void Awake() {
            ResolveReferences(false);
        }

        public Texture2D ExportCountryTexture() {
            if (!ResolveReferences(true)) {
                return null;
            }

            if (!icoSphere.TryGetAreaCountryColors(out Vector4[] countryColors)) {
                Debug.LogError("CountryTextureExporter: 无法读取当前国家颜色数据");
                return null;
            }

            Vector3[] areaCenters = icoSphere.GetRawUnsortedAreas();
            if (areaCenters == null || areaCenters.Length <= 0) {
                Debug.LogError("CountryTextureExporter: 地块数据为空");
                return null;
            }

            if (countryColors.Length != areaCenters.Length) {
                Debug.LogError("CountryTextureExporter: 地块数量和国家颜色数量不一致");
                return null;
            }

            int lonBuckets = exportLonBuckets;
            int latBuckets = exportLatBuckets;
            ResolveBucketCounts(areaCenters.Length, ref lonBuckets, ref latBuckets);
            AreaBucketIndex bucketIndex = new(areaCenters, lonBuckets, latBuckets);

            int width = Mathf.Max(1, exportTexWidth);
            int height = Mathf.Max(1, exportTexHeight);
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false, true) {
                name = $"country_map_{width}x{height}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            float[] lonCos = new float[width];
            float[] lonSin = new float[width];
            for (int x = 0; x < width; ++x) {
                float u = (x + 0.5f) / width;
                float lon = (u * 2.0f - 1.0f) * Mathf.PI;
                lonCos[x] = Mathf.Cos(lon);
                lonSin[x] = Mathf.Sin(lon);
            }

            int searchRadius = Mathf.Max(0, exportBucketSearchRadius);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; ++y) {
                float v = (y + 0.5f) / height;
                float lat = (v - 0.5f) * Mathf.PI;
                float cosLat = Mathf.Cos(lat);
                float sinLat = Mathf.Sin(lat);

                for (int x = 0; x < width; ++x) {
                    Vector3 dir = new(
                        cosLat * lonCos[x],
                        sinLat,
                        cosLat * lonSin[x]
                    );
                    int areaId = bucketIndex.FindNearestAreaId(dir, searchRadius);
                    pixels[y * width + x] = ToColor32(countryColors[areaId]);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        [ContextMenu("保存国家数据为贴图 (.png)")]
        public void SaveCountryTexture() {
            if (string.IsNullOrWhiteSpace(exportTexPath)) {
                Debug.LogError("CountryTextureExporter: 导出路径为空");
                return;
            }

            Texture2D tex = ExportCountryTexture();
            if (tex == null) {
                return;
            }

            string directory = Path.GetDirectoryName(exportTexPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(exportTexPath, tex.EncodeToPNG());

#if UNITY_EDITOR
            string assetPath = exportTexPath.Replace("\\", "/");
            if (assetPath.StartsWith("Assets/")) {
                AssetDatabase.ImportAsset(assetPath);
            } else {
                AssetDatabase.Refresh();
            }
#endif
            Debug.Log($"成功保存国家贴图: {exportTexPath}");
        }

        private bool ResolveReferences(bool logError) {
            if (countryColorDrawer == null) {
                countryColorDrawer = GetComponent<CountryColorDrawer>();
            }

            if (icoSphere == null && countryColorDrawer != null) {
                icoSphere = countryColorDrawer.TargetIcoSphere;
            }

            if (icoSphere == null) {
                icoSphere = GetComponent<IcoSphere>();
            }

            if (icoSphere != null) {
                return true;
            }

            if (logError) {
                Debug.LogError("CountryTextureExporter: 未找到IcoSphere引用");
            }
            return false;
        }

        private static void ResolveBucketCounts(int areaCount, ref int lonBucketCount, ref int latBucketCount) {
            if (lonBucketCount <= 0 && latBucketCount <= 0) {
                lonBucketCount = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(areaCount * 2.0f)), 16, 2048);
                latBucketCount = Mathf.Clamp(Mathf.CeilToInt(lonBucketCount * 0.5f), 8, 1024);
                return;
            }

            if (lonBucketCount <= 0) {
                lonBucketCount = Mathf.Max(1, latBucketCount * 2);
            }

            if (latBucketCount <= 0) {
                latBucketCount = Mathf.Max(1, Mathf.CeilToInt(lonBucketCount * 0.5f));
            }
        }

        private static Color32 ToColor32(Vector4 col) {
            return new(
                ToColorByte(col.x),
                ToColorByte(col.y),
                ToColorByte(col.z),
                255
            );
        }

        private static byte ToColorByte(float v) {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255.0f), 0, 255);
        }

        private sealed class AreaBucketIndex {
            private readonly int lonBucketCount;
            private readonly int latBucketCount;
            private readonly Vector3[] areaDirs;
            private readonly List<int>[] buckets;

            public AreaBucketIndex(Vector3[] areaCenters, int lonBucketCount, int latBucketCount) {
                this.lonBucketCount = Mathf.Max(1, lonBucketCount);
                this.latBucketCount = Mathf.Max(1, latBucketCount);
                areaDirs = new Vector3[areaCenters.Length];
                buckets = new List<int>[this.lonBucketCount * this.latBucketCount];

                for (int i = 0; i < areaCenters.Length; ++i) {
                    Vector3 dir = areaCenters[i].normalized;
                    areaDirs[i] = dir;
                    Vector2 uv = Misc.ToLonLatUv(dir);
                    int bucketIndex = GetBucketIndex(GetLonBucket(uv.x), GetLatBucket(uv.y));
                    buckets[bucketIndex] ??= new List<int>(4);
                    buckets[bucketIndex].Add(i);
                }
            }

            public int FindNearestAreaId(Vector3 dir, int searchRadius) {
                Vector2 uv = Misc.ToLonLatUv(dir);
                int centerLon = GetLonBucket(uv.x);
                int centerLat = GetLatBucket(uv.y);
                int radius = Mathf.Max(0, searchRadius);
                int maxRadius = Mathf.Max(lonBucketCount, latBucketCount);

                while (radius <= maxRadius) {
                    int areaId = FindNearestAreaIdInBuckets(dir, centerLon, centerLat, radius);
                    if (areaId >= 0) {
                        return areaId;
                    }
                    radius = radius == 0 ? 1 : radius * 2;
                }

                return FindNearestAreaIdInAllAreas(dir);
            }

            private int FindNearestAreaIdInBuckets(Vector3 dir, int centerLon, int centerLat, int searchRadius) {
                int bestAreaId = -1;
                float bestDot = float.NegativeInfinity;
                int lonRadius = GetLonSearchRadius(dir, searchRadius);
                int minLat = Mathf.Max(0, centerLat - searchRadius);
                int maxLat = Mathf.Min(latBucketCount - 1, centerLat + searchRadius);
                bool searchAllLon = lonRadius >= lonBucketCount / 2;

                for (int lat = minLat; lat <= maxLat; ++lat) {
                    if (searchAllLon) {
                        for (int lon = 0; lon < lonBucketCount; ++lon) {
                            TryFindNearestInBucket(dir, lon, lat, ref bestAreaId, ref bestDot);
                        }
                    } else {
                        for (int dx = -lonRadius; dx <= lonRadius; ++dx) {
                            int lon = WrapLonBucket(centerLon + dx);
                            TryFindNearestInBucket(dir, lon, lat, ref bestAreaId, ref bestDot);
                        }
                    }
                }

                return bestAreaId;
            }

            private void TryFindNearestInBucket(Vector3 dir, int lon, int lat, ref int bestAreaId, ref float bestDot) {
                List<int> areaIds = buckets[GetBucketIndex(lon, lat)];
                if (areaIds == null) {
                    return;
                }

                foreach (int areaId in areaIds) {
                    float dot = Vector3.Dot(dir, areaDirs[areaId]);
                    if (dot > bestDot) {
                        bestDot = dot;
                        bestAreaId = areaId;
                    }
                }
            }

            private int FindNearestAreaIdInAllAreas(Vector3 dir) {
                int bestAreaId = 0;
                float bestDot = float.NegativeInfinity;
                for (int i = 0; i < areaDirs.Length; ++i) {
                    float dot = Vector3.Dot(dir, areaDirs[i]);
                    if (dot > bestDot) {
                        bestDot = dot;
                        bestAreaId = i;
                    }
                }
                return bestAreaId;
            }

            private int GetLonSearchRadius(Vector3 dir, int searchRadius) {
                float cosLat = Mathf.Sqrt(Mathf.Clamp01(1.0f - dir.y * dir.y));
                if (cosLat < 0.01f) {
                    return lonBucketCount / 2;
                }

                int lonRadius = Mathf.CeilToInt(searchRadius / cosLat);
                return Mathf.Clamp(lonRadius, searchRadius, lonBucketCount / 2);
            }

            private int GetLonBucket(float u) {
                return Mathf.Clamp(Mathf.FloorToInt(u * lonBucketCount), 0, lonBucketCount - 1);
            }

            private int GetLatBucket(float v) {
                return Mathf.Clamp(Mathf.FloorToInt(v * latBucketCount), 0, latBucketCount - 1);
            }

            private int WrapLonBucket(int lon) {
                lon %= lonBucketCount;
                if (lon < 0) {
                    lon += lonBucketCount;
                }
                return lon;
            }

            private int GetBucketIndex(int lon, int lat) {
                return lat * lonBucketCount + lon;
            }
        }
    }
}
