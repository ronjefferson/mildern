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
    
    public bool isLockdown;
    public float lockdownAbidanceThreshold; 

    public void Execute(int i)
    {
        SimulationAgent agent = agents[i];
        if (!agent.isActive) return;

        bool forceHome = (isLockdown && agent.complianceLevel <= lockdownAbidanceThreshold) || currentHour >= agent.returnHomeHour || currentHour < 5f;
        bool wantsHospital = false;

        if (agent.healthState == HealthState.Infected)
        {
            if (agent.complianceLevel > 0.5f) { wantsHospital = true; forceHome = false; }
            else if (agent.complianceLevel > 0.2f) { forceHome = true; }
        }

        // ==========================================
        // VACCINE DETOUR (STRICT ARRIVAL LOGIC)
        // ==========================================
        if (agent.isSeekingVaccine)
        {
            forceHome = false; wantsHospital = false; 
            
            bool isAtTargetState = (agent.isVaccineClinicCommercial && agent.scheduleState == AgentScheduleState.AtCommercial) || 
                                   (!agent.isVaccineClinicCommercial && agent.scheduleState == AgentScheduleState.AtHospital);

            if (!isAtTargetState)
            {
                agent.scheduleState = agent.isVaccineClinicCommercial ? AgentScheduleState.AtCommercial : AgentScheduleState.AtHospital;
                agent.isInsideBuilding = false;
                agent.isAtHospital = false; // FORCE FALSE: They must walk there!
                
                agent.destinationWaypointIndex = agent.isVaccineClinicCommercial ? commercialNearestWaypoint[agent.vaccineClinicID] : hospitalNearestWaypoint[agent.vaccineClinicID];
                agent.hasDestinationWaypoint = true;
                agent.hasMovementSegment = false;
                agent.personalOffset = GetPersonalOffset(i);
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
            agent.personalOffset = GetPersonalOffset(i); agent.commutingStartTime = currentHour;
            agents[i] = agent; return;
        }

        if (forceHome && !wantsHospital && !agent.isSeekingVaccine && agent.scheduleState != AgentScheduleState.Returning && agent.scheduleState != AgentScheduleState.Home)
        {
            SetReturningHome(ref agent, i);
            agents[i] = agent; return;
        }

        switch (agent.scheduleState)
        {
            case AgentScheduleState.Home:
                if (!forceHome && !wantsHospital && !agent.isSeekingVaccine && currentHour >= agent.workStartHour && currentHour < 13f)
                {
                    agent.scheduleState = AgentScheduleState.Commuting; agent.isInsideBuilding = false;
                    agent.destinationWaypointIndex = workNearestWaypoint[agent.workID];
                    agent.hasDestinationWaypoint = true; agent.hasMovementSegment = false;
                    agent.personalOffset = GetPersonalOffset(i); agent.commutingStartTime = currentHour;
                }
                break;

            case AgentScheduleState.Commuting:
                if (ReachedOffsetDestination(agent.position, waypoints[workNearestWaypoint[agent.workID]], agent.personalOffset))
                {
                    agent.scheduleState = AgentScheduleState.AtWork; agent.isInsideBuilding = true;
                    agent.hasMovementSegment = false; agent.hasDestinationWaypoint = false; agent.commutingStartTime = -9999f;
                }
                break;

            case AgentScheduleState.AtWork:
                if (currentHour >= agent.workEndHour)
                {
                    if (agent.visitsCommercial && currentHour < agent.returnHomeHour)
                    {
                        agent.scheduleState = AgentScheduleState.AtCommercial; agent.isInsideBuilding = false;
                        agent.destinationWaypointIndex = commercialNearestWaypoint[agent.commercialID];
                        agent.hasDestinationWaypoint = true; agent.commercialArrivalHour = currentHour;
                        agent.hasMovementSegment = false; agent.personalOffset = GetPersonalOffset(i); agent.commutingStartTime = currentHour;
                    }
                    else SetReturningHome(ref agent, i);
                }
                break;

            case AgentScheduleState.AtCommercial:
                if (ReachedOffsetDestination(agent.position, waypoints[commercialNearestWaypoint[agent.commercialID]], agent.personalOffset))
                {
                    agent.isInsideBuilding = true; agent.hasMovementSegment = false; agent.hasDestinationWaypoint = false; 
                    agent.commutingStartTime = -9999f; // SET ON EXACT ARRIVAL!
                    if (!agent.isSeekingVaccine && currentHour >= agent.commercialArrivalHour + 1.5f) SetReturningHome(ref agent, i);
                }
                break;

            case AgentScheduleState.AtHospital:
                if (ReachedOffsetDestination(agent.position, waypoints[hospitalNearestWaypoint[agent.healthcareID]], agent.personalOffset))
                {
                    agent.isInsideBuilding = true; 
                    agent.isAtHospital = true; // SET ON EXACT ARRIVAL!
                    agent.hasMovementSegment = false; agent.hasDestinationWaypoint = false; agent.commutingStartTime = -9999f;
                    agent.position = new float3(hospitalPositions[agent.healthcareID].x, groundY, hospitalPositions[agent.healthcareID].z) + agent.personalOffset;
                }
                if (agent.healthState == HealthState.Recovered && !agent.isSeekingVaccine) { agent.isAtHospital = false; SetReturningHome(ref agent, i); }
                break;

            case AgentScheduleState.Returning:
                if (ReachedOffsetDestination(agent.position, waypoints[homeNearestWaypoint[agent.homeID]], agent.personalOffset))
                {
                    agent.scheduleState = AgentScheduleState.Home; agent.isInsideBuilding = true;
                    agent.hasMovementSegment = false; agent.hasDestinationWaypoint = false; agent.commutingStartTime = -9999f;
                    agent.position = new float3(homePositions[agent.homeID].x, groundY, homePositions[agent.homeID].z) + agent.personalOffset;
                }
                break;
        }
        agents[i] = agent;
    }

    private void SetReturningHome(ref SimulationAgent agent, int agentIndex)
    {
        agent.scheduleState = AgentScheduleState.Returning; agent.isInsideBuilding = false; agent.isAtHospital = false;
        agent.destinationWaypointIndex = homeNearestWaypoint[agent.homeID]; agent.hasDestinationWaypoint = true;
        agent.hasMovementSegment = false; agent.personalOffset = GetPersonalOffset(agentIndex); agent.commutingStartTime = currentHour;
    }

    private float3 GetPersonalOffset(int agentIndex) { var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(agentIndex * 7919)); float range = destinationOffsetRange; return new float3(rng.NextFloat(-range, range), 0f, rng.NextFloat(-range, range)); }
    private bool ReachedOffsetDestination(float3 agentPos, float3 waypointPos, float3 offset) { float3 offsetDest = waypointPos + new float3(offset.x, 0f, offset.z); return math.distance(new float3(agentPos.x, 0f, agentPos.z), new float3(offsetDest.x, 0f, offsetDest.z)) < waypointReachDistance * 3f; }
}