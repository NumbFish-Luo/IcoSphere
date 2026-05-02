using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace IcoSphere {
    public class CountryColorDrawer : MonoBehaviour {
        [System.Serializable]
        public struct CountrySetting {
            public string name;
            public uint id;
            public Color col;

            // 有效性检测
            public readonly bool IsValid() {
                return string.IsNullOrEmpty(name) == false;
            }
        }

        [SerializeField] private IcoSphere icoSphere = null;
        [SerializeField] private string saveBytesPath = "Assets/IcoSphere/Resources/Bin/all_buf_data.bytes";
        [SerializeField] private string saveCfgPath = "Assets/IcoSphere/Resources/Cfg/country_settings.tsv";
        [SerializeField] private Texture2D mappingTex = null;
        [SerializeField] private string nowCountryName = null;
        [SerializeField] private List<CountrySetting> countrySettings = new();

        private readonly Dictionary<string, CountrySetting> countrySettingsDict = new();
        private string preCountryName = null;

        private void Awake() {
            InitDict();
            SetRayHexColor();
        }

        private void InitDict() {
            countrySettingsDict.Clear();
            foreach (CountrySetting cs in countrySettings) {
                countrySettingsDict.Add(cs.name, cs);
            }
        }

        private void Update() {
            SetRayHexColor();
            if (Input.GetMouseButton(0)) {
                CountrySetting cs = GetCountrySetting(nowCountryName);
                if (cs.IsValid()) {
                    icoSphere.DrawHexColorToComputeShader(cs.col, cs.id);
                }
            }
        }

        private void SetRayHexColor() {
            if (preCountryName != nowCountryName) {
                CountrySetting cs = GetCountrySetting(nowCountryName);
                if (cs.IsValid()) {
                    icoSphere.SetRayHexColorToShader(cs.col);
                } else {
                    icoSphere.SetRayHexColorToShader(Color.white);
                }
                preCountryName = nowCountryName;
            }
        }

        public CountrySetting GetCountrySetting(string name, bool onlyFindDict = false) {
            if (countrySettingsDict.TryGetValue(name, out CountrySetting outCs) == false) {
                if (onlyFindDict) {
                    return new();
                }
                foreach (CountrySetting cs in countrySettings) {
                    if (cs.name == name) {
                        outCs = cs;
                        countrySettingsDict.Add(cs.name, cs);
                    }
                }
            }
            return outCs;
        }

        public bool AddCountrySetting(CountrySetting cs) {
            if (countrySettingsDict.ContainsKey(cs.name)) {
                return false;
            }
            countrySettings.Add(cs);
            countrySettingsDict.Add(cs.name, cs);
            return true;
        }

        public void ClearCountrySetting() {
            countrySettings.Clear();
            countrySettingsDict.Clear();
        }

        // 参数precisionLv为精度等级, 消除颜色过于接近的问题
        // precisionLv = 1, 对应0~255(不是256)
        // precisionLv = 2, 对应0~128
        // precisionLv = 3, 对应0~64
        // 之后会将数值再次返回0~255再得出结果
        public static HashSet<uint> CountUniqueColors(Texture2D tex, int precisionLv = 0) {
            if (tex.format != TextureFormat.RGBA32) {
                Debug.LogWarning("纹理非RGBA32格式, 建议先转换后再调用");
                Debug.LogWarning("请先阅读README文件修改图片设置");
            }

            // 获取原始字节数组, 每像素4字节
            byte[] rawData = tex.GetRawTextureData();
            int n = rawData.Length / 4;
            HashSet<uint> result = new();
            int p = precisionLv;

            for (int i = 0; i < n; ++i) {
                int offset = i * 4;
                // 忽略A通道
                byte r = rawData[offset];
                byte g = rawData[offset + 1];
                byte b = rawData[offset + 2];
                if (precisionLv > 0) {
                    // 先右移抹除部分精度
                    r >>= p;
                    g >>= p;
                    b >>= p;
                    // 然后左移恢复原本大小
                    r <<= p;
                    g <<= p;
                    b <<= p;
                }
                uint packed = ((uint)r << 16) | ((uint)g << 8) | b;
                result.Add(packed);
            }
            return result;
        }

        [ContextMenu("保存国家刷色数据 (.bytes)")]
        public void SaveAllBufData() {
            icoSphere.SaveAllBufData(saveBytesPath);
            Debug.Log("成功保存数据: " + saveBytesPath);
            Debug.Log("可以按Ctrl+R刷新Assets目录");
        }

        [ContextMenu("读取国家刷色数据 (.bytes)")]
        public void LoadAllBufData() {
            icoSphere.LoadAllBufData(saveBytesPath);
            Debug.Log("成功读取数据: " + saveBytesPath);
        }

        [ContextMenu("保存国家颜色配置表 (.tsv)")]
        public void SaveCountrySettings() {
            using StreamWriter writer = new(saveCfgPath, false, Encoding.UTF8);
            writer.WriteLine("name\tid\tcol");
            foreach (CountrySetting cs in countrySettings) {
                // a永远为255
                uint rgb = Misc.ColorToHexRgb(cs.col);
                writer.WriteLine($"{cs.name}\t{cs.id}\t#{rgb:X6}");
            }
            Debug.Log("成功保存配置表: " + saveCfgPath);
            Debug.Log("可以按Ctrl+R刷新Assets目录");
        }

        [ContextMenu("读取国家颜色配置表 (.tsv)")]
        public void LoadCountrySettings() {
            if (!File.Exists(saveCfgPath)) {
                Debug.LogWarning($"配置文件不存在: {saveCfgPath}");
                return;
            }

            countrySettings.Clear();

            using StreamReader reader = new(saveCfgPath, Encoding.UTF8);
            string line = reader.ReadLine(); // 读取表头, 用于忽略这一行
            while ((line = reader.ReadLine()) != null) {
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }

                string[] parts = line.Split('\t');
                if (parts.Length < 3) {
                    continue;
                }

                string name = parts[0];
                if (!uint.TryParse(parts[1], out uint id)) {
                    continue;
                }

                // [1..]是Substring, 用于消除#
                if (!uint.TryParse(parts[2][1..], System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) {
                    continue;
                }
                Color col = Misc.HexRgbToColor(rgb);
                countrySettings.Add(new CountrySetting {
                    name = name, id = id, col = col
                });
            }
            InitDict();
            Debug.Log("成功读取配置表: " + saveCfgPath);
        }

        [ContextMenu("生成地图映射配置 (不保存文件)")]
        public void GenMappingTexSettings() {
            HashSet<uint> hexRgbs = CountUniqueColors(mappingTex);
            Debug.Log("读取到的颜色种类量: " + hexRgbs.Count);
            countrySettings.Clear();
            uint i = 0;
            foreach (uint hex in hexRgbs) {
                countrySettings.Add(new CountrySetting() {
                    name = "未定义" + i,
                    id = i,
                    col = Misc.HexRgbToColor(hex)
                });
                ++i;
            }
            InitDict();
        }

        [ContextMenu("生成地图映射配置, 并执行地图贴图映射 (不保存文件)")]
        public void DoMapping() {
            GenMappingTexSettings();

            Dictionary<uint, uint> hexRgbIdDict = new();
            foreach (CountrySetting cs in countrySettings) {
                hexRgbIdDict.Add(Misc.ColorToHexRgb(cs.col), cs.id);
            }
            icoSphere.MappingTex(mappingTex, hexRgbIdDict);
            Debug.Log("完成地图贴图映射");
        }
    }
}
