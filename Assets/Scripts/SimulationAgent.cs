using Unity.Mathematics;

public enum HealthState
{
    Susceptible,
    Infected,
    Recovered,
    Vaccinated,
    Dead
}

public enum AgentScheduleState
{
    Home,
    Commuting,
    AtWork,
    AtCommercial,
    Leisure,
    Returning
}

public struct SimulationAgent
{
    public float3 position;
    public float3 previousPosition;
    public float3 velocity;
    public float3 targetPosition;

    public HealthState healthState;
    public AgentScheduleState scheduleState;

    public float infectionTimer;
    public float recoveryTimer;
    public float speed;

    public int homeID;
    public int workID;
    public int commercialID;
    public int currentWaypointIndex;

    public float workStartHour;
    public float workEndHour;
    public float returnHomeHour;
    public float complianceLevel;

    public bool isWeekendWorker;
    public bool isActive;
    public bool isInsideBuilding;
}