using UnityEngine;

[CreateAssetMenu(fileName = "SimulationConfig", menuName = "Simulation/Config")]
public class SimulationConfig : ScriptableObject
{
    [Header("Time Settings")]
    public float dayStartHour = 6f;
    public float dayEndHour = 22f;

    [Header("Work Schedule")]
    public float workStartMin = 7f;
    public float workStartMax = 9f;
    public float workEndMin = 16f;
    public float workEndMax = 19f;
    public float workBuildingStayMin = 30f;
    public float workBuildingStayMax = 120f;

    [Header("Commercial Schedule")]
    public float commercialVisitChanceWeekday = 0.3f;
    public float commercialVisitChanceWeekend = 0.7f;
    public float commercialStayMin = 10f;
    public float commercialStayMax = 45f;

    [Header("Home Schedule")]
    public float leaveHomeChancePerMinute = 0.1f;
    public float returnHomeHourMin = 18f;
    public float returnHomeHourMax = 22f;

    [Header("Population Behavior")]
    [Range(0f, 1f)] public float weekendWorkerChance = 0.2f;
    [Range(0f, 1f)] public float nightOwlChance = 0.2f;
    [Range(0f, 1f)] public float earlyBirdChance = 0.2f;
    [Range(0f, 1f)] public float wanderChance = 0.3f;

    [Header("Movement")]
    public float wanderRadius = 50f;
    public float wanderDuration = 5f;
    public float minSpeedMultiplier = 0.8f;
    public float maxSpeedMultiplier = 1.2f;
}