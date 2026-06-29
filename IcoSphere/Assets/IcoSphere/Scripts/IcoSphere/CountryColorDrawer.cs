using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace IcoSphere {
    /// <summary>
    /// 国家颜色刷色器
    /// </summary>
    public class CountryColorDrawer : MonoBehaviour {
        // ---- 内部类 ----
        /// <summary>
        /// 国家配置, 包含名称, id, 颜色
        /// </summary>
        [System.Serializable]
        public struct CountrySetting {
            public string name;
            public uint id;
            public Color col;

            /// <summary>
            /// 数据有效性检测, 仅检测名称是否为空
            /// </summary>
            /// <returns>是否有效</returns>
            public readonly bool IsValid() {
                return string.IsNullOrEmpty(name) == false;
            }
        }

        // ---- 可调参数 ----
        [SerializeField] private IcoSphere icoSphere = null;
        [SerializeField] private string saveBytesPath = "Assets/IcoSphere/Resources/Bin/";
        [SerializeField] private string saveCfgPath = "Assets/IcoSphere/Resources/Cfg/";
        [SerializeField] private Texture2D mappingTex = null;
        [SerializeField] private string nowCountryName = null;
        [SerializeField] private List<CountrySetting> countrySettings = new();

        // ---- 私有成员变量 ----
        private readonly Dictionary<string, CountrySetting> countrySettingsDict = new(); // 缓存用
        private string preCountryName = null;

        // ---- Unity生命周期函数 ----
        private void Awake() {
            InitDict();
            SetRayHexColor();
            RegisterCallback();
        }

        private void Update() {
            SetRayHexColor();
            if (Input.GetMouseButton(0)) {
                CountrySetting cs = GetCountrySetting(nowCountryName);
                if (cs.IsValid()) {
                    icoSphere.SetRayHexCountry(cs.col, cs.id);
                }
            }
        }

        // ---- 公有成员函数 ----

        /// <summary>
        /// 获取目标IcoSphere球体
        /// </summary>
        public IcoSphere TargetIcoSphere => icoSphere;

        /// <summary>
        /// 获取国家配置列表
        /// </summary>
        /// <returns>国家配置列表</returns>
        public List<CountrySetting> GetCountrySettings() => countrySettings;

        /// <summary>
        /// 获取国家配置字典
        /// </summary>
        /// <returns>国家配置字典</returns>
        public Dictionary<string, CountrySetting> GetCountrySettingsDict() => countrySettingsDict;

        /// <summary>
        /// 获取二进制刷色数据文件路径 (.bytes)
        /// </summary>
        /// <returns>二进制刷色数据文件路径 (.bytes)</returns>
        public string GetSaveBytesPath() {
            if (icoSphere == null) {
                Debug.LogError("icoSphere为空");
                return saveBytesPath + "vert_buf_data.bytes";
            }
            return saveBytesPath + "vert_buf_data_" + icoSphere.Recursion + ".bytes";
        }

        /// <summary>
        /// 获取国家配置数据表路径 (.tsv)
        /// </summary>
        /// <returns>国家配置数据表路径 (.tsv)</returns>
        public string GetSaveCfgPath() {
            if (icoSphere == null) {
                Debug.LogError("icoSphere为空");
                return saveBytesPath + "country_settings.tsv";
            }
            return saveCfgPath + "country_settings_" + icoSphere.Recursion + ".tsv";
        }

        /// <summary>
        /// 根据名称搜索国家配置
        /// </summary>
        /// <param name="name">国家名称</param>
        /// <param name="onlyFindDict">是否只从字典中寻找? 默认false. 此为性能选项, 如果已经确保数据是最新的, 那么可以传入true来只读取缓存的字典数据; 否则传入false, 会顺带刷新缓存数据</param>
        /// <returns>国家配置</returns>
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

        /// <summary>
        /// 根据ID搜索国家配置
        /// </summary>
        /// <param name="id">国家ID</param>
        /// <returns>国家配置</returns>
        public CountrySetting GetCountrySettingById(uint id) {
            foreach (CountrySetting cs in countrySettings) {
                if (cs.id == id) {
                    return cs;
                }
            }
            return new();
        }

        /// <summary>
        /// 添加国家配置
        /// </summary>
        /// <param name="cs">新增国家配置</param>
        /// <returns>是否添加成功, 失败原因只有一个, 就是国家名称重复</returns>
        public bool AddCountrySetting(CountrySetting cs) {
            if (countrySettingsDict.ContainsKey(cs.name)) {
                return false;
            }
            countrySettings.Add(cs);
            countrySettingsDict.Add(cs.name, cs);
            return true;
        }

        /// <summary>
        /// 清空国家配置
        /// </summary>
        public void ClearCountrySetting() {
            countrySettings.Clear();
            countrySettingsDict.Clear();
        }

        /// <summary>
        /// <para>统计贴图中的颜色种类数量</para>
        /// <para>参数precisionLv为精度等级, 消除颜色过于接近的问题</para>
        /// <para>precisionLv = 1, 对应0~255(不是256)</para>
        /// <para>precisionLv = 2, 对应0~128</para>
        /// <para>precisionLv = 3, 对应0~64</para>
        /// <para>之后会将数值再次返回0~255再得出结果</para>
        /// </summary>
        /// <param name="tex">要统计颜色种类数量的贴图</param>
        /// <param name="precisionLv">精度等级</param>
        /// <returns>返回十六进制颜色表</returns>
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

        /// <summary>
        /// 保存国家刷色数据 (.bytes)
        /// </summary>
        [ContextMenu("保存国家刷色数据 (.bytes)")]
        public void SaveVertBufData() {
            string path = GetSaveBytesPath();
            icoSphere.SaveVertBufData(path);
            Debug.Log("成功保存数据: " + path);
            Debug.Log("可以按Ctrl+R刷新Assets目录");
        }

        /// <summary>
        /// 读取国家刷色数据 (.bytes)
        /// </summary>
        [ContextMenu("读取国家刷色数据 (.bytes)")]
        public void LoadVertBufData() {
            string path = GetSaveBytesPath();
            icoSphere.LoadVertBufData(path);
            Debug.Log("成功读取数据: " + path);
        }

        /// <summary>
        /// 保存国家颜色配置表 (.tsv)
        /// </summary>
        [ContextMenu("保存国家颜色配置表 (.tsv)")]
        public void SaveCountrySettings() {
            string path = GetSaveCfgPath();
            using StreamWriter writer = new(path, false, Encoding.UTF8);
            writer.WriteLine("name\tid\tcol");
            foreach (CountrySetting cs in countrySettings) {
                // a永远为255
                uint rgb = Misc.ColorToHexRgb(cs.col);
                writer.WriteLine($"{cs.name}\t{cs.id}\t#{rgb:X6}");
            }
            Debug.Log("成功保存配置表: " + path);
            Debug.Log("可以按Ctrl+R刷新Assets目录");
        }

        /// <summary>
        /// 读取国家颜色配置表 (.tsv)
        /// </summary>
        [ContextMenu("读取国家颜色配置表 (.tsv)")]
        public void LoadCountrySettings() {
            string path = GetSaveCfgPath();
            if (!File.Exists(path)) {
                Debug.LogWarning($"配置文件不存在: {path}");
                return;
            }

            countrySettings.Clear();

            using StreamReader reader = new(path, Encoding.UTF8);
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
            Debug.Log("成功读取配置表: " + path);
        }

        /// <summary>
        /// 生成地图映射配置 (不保存文件)
        /// </summary>
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

        /// <summary>
        /// 生成地图映射配置, 并执行地图贴图映射 (不保存文件)
        /// </summary>
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

        // ---- 私有成员函数 ----
        private void InitDict() {
            countrySettingsDict.Clear();
            foreach (CountrySetting cs in countrySettings) {
                countrySettingsDict.Add(cs.name, cs);
            }
        }

        private void SetRayHexColor() {
            if (preCountryName != nowCountryName) {
                CountrySetting cs = GetCountrySetting(nowCountryName);
                if (cs.IsValid()) {
                    icoSphere.SetRayHexCol(cs.col);
                } else {
                    icoSphere.SetRayHexCol(Color.white);
                }
                preCountryName = nowCountryName;
            }
        }

        private void RegisterCallback() {
            if (icoSphere == null) {
                return;
            }

            if (icoSphere.IsInitialized) {
                LoadCountrySettings();
                LoadVertBufData();
                return;
            }

            icoSphere.OnInitOver += LoadCountrySettings;
            icoSphere.OnInitOver += LoadVertBufData;
        }
    }
}
