using System.Collections;
using System.Collections.Generic;
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
        [SerializeField] private string savePath = "Assets/IcoSphere/Resources/Bin/all_buf_data.bytes";
        [SerializeField] private string nowCountryName = null;
        [SerializeField] private List<CountrySetting> countrySettings = new();

        private readonly Dictionary<string, CountrySetting> countrySettingsDict = new();
        private string preCountryName = null;

        private void Awake() {
            foreach (CountrySetting cs in countrySettings) {
                countrySettingsDict.Add(cs.name, cs);
            }
            SetRayHexColor();
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

        [ContextMenu("保存国家刷色数据")]
        public void SaveAllBufData() {
            icoSphere.SaveAllBufData(savePath);
            Debug.Log("成功保存数据: " + savePath);
        }

        [ContextMenu("读取国家刷色数据")]
        public void LoadAllBufData() {
            icoSphere.LoadAllBufData(savePath);
            Debug.Log("成功读取数据: " + savePath);
        }
    }
}
