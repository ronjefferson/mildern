using UnityEngine;

public class AgentProfile
{
    public float workStartHour;
    public float workEndHour;
    public float returnHomeHour;
    public bool isWeekendWorker;
    public bool isNightOwl;
    public bool isEarlyBird;
    public float speedMultiplier;
    public float commercialVisitChance;

    public static AgentProfile Generate(SimulationConfig config)
    {
        AgentProfile profile = new AgentProfile();

        profile.workStartHour = Random.Range(config.workStartMin, config.workStartMax);
        profile.workEndHour = Random.Range(config.workEndMin, config.workEndMax);
        profile.returnHomeHour = Random.Range(config.returnHomeHourMin, config.returnHomeHourMax);
        profile.isWeekendWorker = Random.value < config.weekendWorkerChance;
        profile.isNightOwl = Random.value < config.nightOwlChance;
        profile.isEarlyBird = Random.value < config.earlyBirdChance;
        profile.speedMultiplier = Random.Range(config.minSpeedMultiplier, config.maxSpeedMultiplier);

        if (profile.isNightOwl)
        {
            profile.workStartHour += 1f;
            profile.returnHomeHour += 1f;
        }
        if (profile.isEarlyBird)
        {
            profile.workStartHour -= 1f;
            profile.returnHomeHour -= 1f;
        }

        return profile;
    }
}