using Unity.Mathematics;

public enum HealthState
{
    Susceptible, Exposed, Infected, Recovered, Vaccinated, Dead
}

public enum AgentScheduleState
{
    Home, Commuting, AtWork, AtCommercial, Leisure, Returning, AtHospital
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
    public float incubationTimer;
    public float immunityTimer; 
    
    public bool isSeekingVaccine;
    public int vaccineClinicID;
    public bool isVaccineClinicCommercial;
    public float vaccineWaitTimer; 

    public int healthcareID;      
    public bool isAtHospital;     
    public float speed;

    public int homeID;
    public int workID;
    public int commercialID;
    
    public int currentWaypointIndex;
    public int destinationWaypointIndex;
   
    public int prev1, prev2, prev3, prev4, prev5, prev6, prev7, prev8;
    
    public bool isEscaping;
    public int escapeStepCount;
    public int frustrationCounter;
    public float highWatermarkDistance;
    
    public bool isWorker;
    public float workStartHour;
    public float workEndHour;
    public float leisureDuration;
    
    public float complianceLevel;
    public float commutingStartTime;

    public bool isWeekendWorker;
    public bool isActive;
    public bool isInsideBuilding;
    public bool hasMovementSegment;
    public bool hasDestinationWaypoint;
    public bool isWeekendRoamer;
}