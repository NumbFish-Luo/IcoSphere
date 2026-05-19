using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace IcoSphere {
    public class CountryRightClickInspector : MonoBehaviour {
        [SerializeField] private CountryColorDrawer countryColorDrawer = null;
        [SerializeField] private Text outputText = null;
        [SerializeField] private bool loadCountrySettingsOnStart = true;
        [SerializeField] private bool loadBrushDataOnStart = true;

        private void Awake() {
            if (countryColorDrawer == null) {
                countryColorDrawer = GetComponent<CountryColorDrawer>();
            }
        }

        private IEnumerator Start() {
            yield return null;

            if (countryColorDrawer == null) {
                Debug.LogWarning("CountryRightClickInspector: 未找到CountryColorDrawer组件");
                yield break;
            }

            if (loadCountrySettingsOnStart) {
                countryColorDrawer.LoadCountrySettings();
            }

            if (loadBrushDataOnStart) {
                countryColorDrawer.LoadVertBufData();
            }
        }

        private void Update() {
            if (!Input.GetMouseButtonDown(1)) {
                return;
            }

            ShowRayHitCountry();
        }

        private void ShowRayHitCountry() {
            if (countryColorDrawer == null || countryColorDrawer.TargetIcoSphere == null) {
                Show("未绑定国家上色组件或IcoSphere");
                return;
            }

            if (!countryColorDrawer.TargetIcoSphere.TryGetRayHexCountryId(
                out uint countryId,
                out uint hexId,
                out IcoSphere.RayData rayData)) {
                Show("未选中地块");
                return;
            }

            CountryColorDrawer.CountrySetting cs = countryColorDrawer.GetCountrySettingById(countryId);
            string countryName = cs.IsValid() ? cs.name : $"未知国家({countryId})";
            Show($"国家: {countryName}\n国家ID: {countryId}\n地块ID: {hexId}\n三角形ID: {rayData.tid}");
        }

        private void Show(string text) {
            if (outputText != null) {
                outputText.text = text;
            }
            Debug.Log(text);
        }
    }
}
