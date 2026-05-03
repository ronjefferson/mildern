using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct ScheduleUpdateJob : IJobParallelFor
{
    public NativeArray<SimulationAgent> agents;
    
    [ReadOnly] public NativeArray<int> homeNearestWaypoint, workNearestWaypoint, commercialNearestWaypoint, hospitalNearestWaypoint;
    [ReadOnly] public NativeArray<float3> homePositions, hospitalPositions, commercialPositions; 
    [ReadOnly] public NativeArray<float3> waypoints;

    public float currentHour, stuckTimeoutSimHours, waypointReachDistance, destinationOffsetRange, groundY;
    
    public uint randomSeed; 
    public int totalCommercialWaypoints;
    
    public bool isLockdown;
    public float lockdownAbidanceThreshold; 
    
    public float hospitalizationAbidance;
    public float selfQuarantineAbidance;
    
    public float evacuationStaggerMax;
    public float shiftGracePeriodHours;

    public bool isSocialDistancing;
    public float sdAbidance;
    public float sdOffsetExpansion;

    public void Execute(int i)
    {
        SimulationAgent agent = agents[i];
        if (!agent.isActive) return;

        bool forceHome = (isLockdown && agent.complianceLevel <= lockdownAbidanceThreshold);
        bool wantsHospital = false;
        
        if (agent.healthState == HealthState.Infected)
        {
            if (agent.complianceLevel > hospitalizationAbidance) { wantsHospital = true; forceHome = false; }
            else if (agent.complianceLevel > selfQuarantineAbidance) { forceHome = true; }
        }
        
        if (agent.isSeekingVaccine)
        {
            forceHome = false; wantsHospital = false; 
            
            bool isAtTargetState = (agent.isVaccineClinicCommercial && agent.scheduleState == AgentScheduleState.AtCommercial) || 
                                   (!agent.isVaccineClinicCommercial && agent.scheduleState == AgentScheduleState.AtHospital);

            if (!isAtTargetState)
            {
                agent.scheduleState = agent.isVaccineClinicCommercial ? AgentScheduleState.AtCommercial : AgentScheduleState.AtHospital;
                agent.isInsideBuilding = false;
                agent.isAtHospital = false; 
                
                agent.destinationWaypointIndex = agent.isVaccineClinicCommercial ? commercialNearestWaypoint[agent.vaccineClinicID] : hospitalNearestWaypoint[agent.vaccineClinicID];
                agent.hasDestinationWaypoint = true;
                agent.hasMovementSegment = false;
                agent.personalOffset = GetPersonalOffset(i, agent.complianceLevel);
                agent.commutingStartTime = currentHour;
                agents[i] = agent;
                return;
            }
        }

        if (wantsHospital && agent.scheduleState != AgentScheduleState.AtHospital)
        {
            agent.scheduleState = AgentScheduleState.AtHospital;
            agent.isInsideBuilding = false; agent.isAtHospital = false;
            agent.destinationWaypointIndex = hospitalNearestWaypoint[agent.healthcareID];
            agent.hasDestinationWaypoint = true; agent.hasMovementSegment = false;
            agent.personalOffset = GetPersonalOffset(i, agent.complianceLevel); 
            agent.commutingStartTime = currentHour;
            agents[i] = agent; return;
        }

        // ---> BUG FIX: The Lockdown Override Safety Net <---
        // Only force them to go home if they are NOT already at home and NOT currently walking home.
        if (forceHome && !wantsHospital && !agent.isSeekingVaccine && agent.scheduleState != AgentScheduleState.Returning && agent.scheduleState != AgentScheduleState.Home)
        {
            SetReturningHome(ref agent, i);
            agents[i] = agent; return;
        }

        // --- THE CASCADING SCHEDULE (NORMAL ROUTINE) ---
        if (!forceHome && !wantsHospital && !agent.isSeekingVaccine)
        {
            // 1. WAKE UP -> START SHIFT OR ERRAND
            if (IsTimeBetween(currentHour, agent.workStartHour, agent.workStartHour + shiftGracePeriodHours) && agent.scheduleState == AgentScheduleState.Home)
            {
                if (agent.isWorker)
                {
                    agent.scheduleState = AgentScheduleState.Commuting; 
                    agent.destinationWaypointIndex = workNearestWaypoint[agent.workID];
                }
                else
                {
                    agent.scheduleState = AgentScheduleState.Leisure; 
                    var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + (int)currentHour * 100));
                    agent.commercialID = rng.NextInt(0, commercialNearestWaypoint.Length);
                    agent.destinationWaypointIndex = commercialNearestWaypoint[agent.commercialID];
                }
                
                agent.isInsideBuilding = false;
                agent.hasDestinationWaypoint = true;
                agent.hasMovementSegment = false;
                agent.personalOffset = GetPersonalOffset(i, agent.complianceLevel); 
                agent.commutingStartTime = currentHour;
            }

            // 2. SHIFT ENDS -> LEISURE TIME (Wander Zones)
            if (IsTimeBetween(currentHour, agent.workEndHour, agent.workEndHour + shiftGracePeriodHours) 
                && agent.scheduleState != AgentScheduleState.Leisure 
                && agent.scheduleState != AgentScheduleState.Returning)
            {
                // The "Flying Dutchman" Exhaustion Check
                bool isExhaustedWorker = agent.isWorker && !agent.isInsideBuilding;
                bool isExhaustedNonWorker = !agent.isWorker && agent.hasDestinationWaypoint;

                if (isExhaustedWorker || isExhaustedNonWorker)
                {
                    // Exhausted: Spent shift walking. Zero out leisure, go straight home.
                    agent.leisureDuration = 0f;
                    SetReturningHome(ref agent, i);
                }
                else
                {
                    // Normal Behavior: Start Wander Zone Leisure
                    agent.scheduleState = AgentScheduleState.Leisure;
                    var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 8123 + (int)currentHour * 50));
                    agent.commercialID = rng.NextInt(0, commercialNearestWaypoint.Length);
                    agent.destinationWaypointIndex = commercialNearestWaypoint[agent.commercialID];
                    agent.hasDestinationWaypoint = true;
                    agent.hasMovementSegment = false;
                    agent.isInsideBuilding = false;
                    agent.personalOffset = GetPersonalOffset(i, agent.complianceLevel);
                    agent.commutingStartTime = currentHour;
                }
            }

            // 3. LEISURE ENDS -> GO HOME
            float dynamicHomeTime = agent.workEndHour + agent.leisureDuration;
            if (dynamicHomeTime >= 24f) dynamicHomeTime -= 24f; // Midnight wrap-around logic

            if (IsTimeBetween(currentHour, dynamicHomeTime, dynamicHomeTime + shiftGracePeriodHours) 
                && agent.scheduleState != AgentScheduleState.Returning)
            {
                SetReturningHome(ref agent, i);
            }
        }

        // --- ARRIVAL / STATE SNAPPING ---
        switch (agent.scheduleState)
        {
            case AgentScheduleState.Commuting: // For going inside an Office
                if (ReachedOffsetDestination(agent.position, waypoints[workNearestWaypoint[agent.workID]], agent.personalOffset))
                {
                    agent.scheduleState = AgentScheduleState.AtWork; 
                    agent.isInsideBuilding = true;
                    agent.hasMovementSegment = false; 
                    agent.hasDestinationWaypoint = false; 
                    agent.commutingStartTime = -9999f;
                }
                break;

            case AgentScheduleState.Leisure: // For Wandering Errand/Leisure Blocks
                if (agent.hasDestinationWaypoint && ReachedOffsetDestination(agent.position, waypoints[commercialNearestWaypoint[agent.commercialID]], agent.personalOffset))
                {
                    // Arrived at destination. Drop target so Wander Engine takes over!
                    agent.hasDestinationWaypoint = false; 
                    agent.isInsideBuilding = false; 
                    agent.hasMovementSegment = false; 
                    agent.commutingStartTime = -9999f; 
                }
                break;

            case AgentScheduleState.AtCommercial: // Strictly for Indoor Vaccines
                if (ReachedOffsetDestination(agent.position, waypoints[commercialNearestWaypoint[agent.vaccineClinicID]], agent.personalOffset))
                {
                    agent.isInsideBuilding = true; 
                    agent.hasMovementSegment = false; 
                    agent.hasDestinationWaypoint = false; 
                    agent.commutingStartTime = -9999f; 
                }
                break;

            case AgentScheduleState.AtHospital:
                if (ReachedOffsetDestination(agent.position, waypoints[hospitalNearestWaypoint[agent.healthcareID]], agent.personalOffset))
                {
                    agent.isInsideBuilding = true; 
                    agent.isAtHospital = true; 
                    agent.hasMovementSegment = false; 
                    agent.hasDestinationWaypoint = false; 
                    agent.commutingStartTime = -9999f;
                    agent.position = new float3(hospitalPositions[agent.healthcareID].x, groundY, hospitalPositions[agent.healthcareID].z) + agent.personalOffset;
                }
                if (agent.healthState == HealthState.Recovered && !agent.isSeekingVaccine) { 
                    agent.isAtHospital = false; 
                    SetReturningHome(ref agent, i); 
                }
                break;

            case AgentScheduleState.Returning:
                if (ReachedOffsetDestination(agent.position, waypoints[homeNearestWaypoint[agent.homeID]], agent.personalOffset))
                {
                    agent.scheduleState = AgentScheduleState.Home; 
                    agent.isInsideBuilding = true;
                    agent.hasMovementSegment = false; 
                    agent.hasDestinationWaypoint = false; 
                    agent.commutingStartTime = -9999f;
                    agent.position = new float3(homePositions[agent.homeID].x, groundY, homePositions[agent.homeID].z) + agent.personalOffset;
                }
                break;
        }

        agents[i] = agent;
    }

    private void SetReturningHome(ref SimulationAgent agent, int agentIndex)
    {
        agent.scheduleState = AgentScheduleState.Returning; 
        agent.isInsideBuilding = false; 
        agent.isAtHospital = false;
        agent.destinationWaypointIndex = homeNearestWaypoint[agent.homeID]; 
        agent.hasDestinationWaypoint = true;
        agent.hasMovementSegment = false; 
        agent.personalOffset = GetPersonalOffset(agentIndex, agent.complianceLevel); 
        
        // ---> FIX: Staggered Evacuation Timers <---
        // Gives everyone a slightly offset timer so they don't hit the failsafe on the exact same frame!
        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(agentIndex * 1234));
        agent.commutingStartTime = currentHour + rng.NextFloat(0f, evacuationStaggerMax); 
    }

    // Helper Math 
    private bool IsTimeBetween(float time, float start, float end)
    {
        if (start <= end) return time >= start && time <= end;
        return time >= start || time <= end; // Handles midnight wrap-around
    }

    private float3 GetPersonalOffset(int agentIndex, float complianceLevel) 
    { 
        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(agentIndex * 7919)); 
        float range = destinationOffsetRange; 
        
        if (isSocialDistancing && complianceLevel <= sdAbidance) {
            range *= sdOffsetExpansion;
        }

        return new float3(rng.NextFloat(-range, range), 0f, rng.NextFloat(-range, range)); 
    }

    private bool ReachedOffsetDestination(float3 agentPos, float3 waypointPos, float3 offset) 
    { 
        float3 offsetDest = waypointPos + new float3(offset.x, 0f, offset.z); 
        return math.distance(new float3(agentPos.x, 0f, agentPos.z), new float3(offsetDest.x, 0f, offsetDest.z)) < waypointReachDistance * 3f; 
    }
}