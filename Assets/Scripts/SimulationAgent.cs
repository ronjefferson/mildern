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
    public float3 moveStartPosition;
    public float3 moveEndPosition;
    public float moveStartTime;
    public float arrivalTime;

    public float3 position;
    public float3 targetPosition;
    public float3 personalOffset;

    public HealthState healthState;
    public AgentScheduleState scheduleState;

    public float infectionTimer;
    public float recoveryTimer;
    public float speed;

    public int homeID;
    public int workID;
    public int commercialID;
    public int currentWaypointIndex;
    public int destinationWaypointIndex;
    
    public int prev1; 
    public int prev2;
    public int prev3;
    public int prev4;
    
    public float workStartHour;
    public float workEndHour;
    public float returnHomeHour;
    public float commercialArrivalHour;
    public float complianceLevel;
    public float commutingStartTime;

    public bool isWeekendWorker;
    public bool isActive;
    public bool isInsideBuilding;
    public bool hasMovementSegment;
    public bool hasDestinationWaypoint;
    public bool visitsCommercial;
    public bool isWeekendRoamer;
}