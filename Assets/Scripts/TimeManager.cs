using UnityEngine;

public class TimeManager : MonoBehaviour
{
    [Header("Sun")]
    public Light sun;

    [Header("Time Settings")]
    [Range(0, 24)]
    public float currentHour = 8f;
    public float timeMultiplier = 1f;

    [Header("Sun Intensity")]
    public float maxSunIntensity = 1f;
    public float minSunIntensity = 0f;

    [Header("Ambient Light")]
    public Color dayAmbientLight = new Color(0.5f, 0.5f, 0.5f);
    public Color nightAmbientLight = new Color(0.05f, 0.05f, 0.1f);

    [Header("Day Tracking")]
    public int currentDay = 0;
    public string[] dayNames = { "Monday", "Tuesday", "Wednesday",
        "Thursday", "Friday", "Saturday", "Sunday" };

    [Header("Base Time Ratio")]
    [Tooltip("Sim minutes per real second at 1x speed. 10 = 10 sim min per real sec")]
    public float simMinutesPerRealSecond = 10f;

    public static TimeManager Instance;

    void Awake() { Instance = this; }

    void Update()
    {
        UpdateSun();
        UpdateAmbientLight();
    }
    public void AdvanceTime(float realDeltaSeconds)
    {
        if (timeMultiplier == 0f) return;
        
        currentHour += (realDeltaSeconds * timeMultiplier * simMinutesPerRealSecond) / 60f;

        if (currentHour >= 24f)
        {
            currentHour -= 24f;
            currentDay = (currentDay + 1) % 7;
        }
    }

    void UpdateSun()
    {
        if (sun == null) return;
        float sunAngle = (currentHour / 24f) * 360f - 90f;
        sun.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);

        if (currentHour >= 6f && currentHour <= 18f)
        {
            float t = Mathf.InverseLerp(6f, 18f, currentHour);
            sun.intensity = Mathf.Lerp(minSunIntensity, maxSunIntensity,
                Mathf.Sin(t * Mathf.PI));
        }
        else
        {
            sun.intensity = minSunIntensity;
        }
    }

    void UpdateAmbientLight()
    {
        if (currentHour >= 6f && currentHour <= 18f)
        {
            float t = Mathf.InverseLerp(6f, 18f, currentHour);
            RenderSettings.ambientLight = Color.Lerp(nightAmbientLight, dayAmbientLight,
                Mathf.Sin(t * Mathf.PI));
        }
        else
        {
            RenderSettings.ambientLight = nightAmbientLight;
        }
    }

    public bool IsWeekend() => currentDay == 5 || currentDay == 6;
    public string GetDayName() => dayNames[currentDay];

    public string GetFormattedTime()
    {
        int hours = Mathf.FloorToInt(currentHour);
        int minutes = Mathf.FloorToInt((currentHour - hours) * 60f);
        return $"{hours:00}:{minutes:00}";
    }

    public void SetTimeMultiplier(float multiplier) => timeMultiplier = multiplier;
    public void Pause() => timeMultiplier = 0f;
}