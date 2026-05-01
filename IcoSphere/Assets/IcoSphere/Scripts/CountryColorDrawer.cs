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
                int r = (int)(cs.col.r * 255);
                int g = (int)(cs.col.g * 255);
                int b = (int)(cs.col.b * 255);
                // a永远为255
                string colStr = $"{r},{g},{b}";
                writer.WriteLine($"{cs.name}\t{cs.id}\t{colStr}");
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

                string[] rgb = parts[2].Split(',');
                if (rgb.Length < 3) {
                    continue;
                }

                if (!int.TryParse(rgb[0], out int r) ||
                    !int.TryParse(rgb[1], out int g) ||
                    !int.TryParse(rgb[2], out int b)) {
                    continue;
                }

                Color col = new(r / 255f, g / 255f, b / 255f, 1f);
                countrySettings.Add(new CountrySetting {
                    name = name, id = id, col = col
                });
            }
            InitDict();
            Debug.Log("成功读取配置表: " + saveCfgPath);
        }
    }
}
