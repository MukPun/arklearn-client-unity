using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class SliderValueText : MonoBehaviour {
    [SerializeField] private int multiplier = 10;

    private Slider slider;
    private Text handleText;

    private void Awake() {
        slider = GetComponent<Slider>();
        Transform handle = slider.handleRect;
        if (handle != null) {
            handleText = handle.GetComponentInChildren<Text>();
        }
        UpdateText(slider.value);
        slider.onValueChanged.AddListener(OnValueChanged);
    }

    private void OnValueChanged(float value) {
        UpdateText(value);
    }

    private void UpdateText(float value) {
        if (handleText != null) {
            handleText.text = ((int)(value * multiplier)).ToString();
        }
    }

    private void OnDestroy() {
        if (slider != null) {
            slider.onValueChanged.RemoveListener(OnValueChanged);
        }
    }
}
