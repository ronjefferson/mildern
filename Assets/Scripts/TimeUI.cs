using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TimeUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI timeText;
    public Slider speedSlider;
    public TextMeshProUGUI speedText;

    [Header("Speed Buttons")]
    public Button pauseButton;
    public Button speed1xButton;
    public Button speed2xButton;
    public Button speed10xButton;
    public Button speed100xButton;

    void Start()
    {
        speedSlider.minValue = 0f;
        speedSlider.maxValue = 100f;
        speedSlider.value = 1f;

        speedSlider.onValueChanged.AddListener(OnSliderChanged);

        pauseButton.onClick.AddListener(() => SetSpeed(0f));
        speed1xButton.onClick.AddListener(() => SetSpeed(1f));
        speed2xButton.onClick.AddListener(() => SetSpeed(2f));
        speed10xButton.onClick.AddListener(() => SetSpeed(10f));
        speed100xButton.onClick.AddListener(() => SetSpeed(100f));
    }

    void Update()
    {
        timeText.text = TimeManager.Instance.GetFormattedTime();
        speedText.text = $"Speed: {TimeManager.Instance.timeMultiplier}x";
    }

    void OnSliderChanged(float value)
    {
        SetSpeed(value);
    }

    void SetSpeed(float speed)
    {
        TimeManager.Instance.SetTimeMultiplier(speed);
        speedSlider.value = speed;
    }
}