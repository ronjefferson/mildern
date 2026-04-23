//TO BE DELETED

using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TimeUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI dayCounterText;
    public TextMeshProUGUI speedText;

    [Header("Speed Buttons")]
    public Button pauseButton;
    public Button speed1xButton;
    public Button speed2xButton;
    public Button speed10xButton;
    public Button speed100xButton;

    private int totalDays = 0;
    private int lastDay = 0;

    void Start()
    {
        pauseButton?.onClick.AddListener(() => SetSpeed(0f));
        speed1xButton?.onClick.AddListener(() => SetSpeed(1f));
        speed2xButton?.onClick.AddListener(() => SetSpeed(2f));
        speed10xButton?.onClick.AddListener(() => SetSpeed(10f));
        speed100xButton?.onClick.AddListener(() => SetSpeed(100f));

        if (TimeManager.Instance != null)
            lastDay = TimeManager.Instance.currentDay;
    }

    void Update()
    {
        if (TimeManager.Instance == null) return;

        // Track total days elapsed
        int currentDay = TimeManager.Instance.currentDay;
        if (currentDay != lastDay)
        {
            totalDays++;
            lastDay = currentDay;
        }

        if (timeText != null)
            timeText.text = TimeManager.Instance.GetFormattedTime();

        if (dayText != null)
            dayText.text = $"{TimeManager.Instance.GetDayName()}";

        if (dayCounterText != null)
            dayCounterText.text = $"Day {totalDays + 1}";

        if (speedText != null)
        {
            float multiplier = TimeManager.Instance.timeMultiplier;
            float simMinPerRealSec = TimeManager.Instance.simMinutesPerRealSecond * multiplier;

            if (multiplier == 0f)
                speedText.text = "Paused";
            else if (simMinPerRealSec < 60f)
                speedText.text = $"{simMinPerRealSec:0} sim min/sec";
            else
                speedText.text = $"{simMinPerRealSec / 60f:0.#} sim hr/sec";
        }
    }

    void SetSpeed(float multiplier)
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.SetTimeMultiplier(multiplier);
    }
}