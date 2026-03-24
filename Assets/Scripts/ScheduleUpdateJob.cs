using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct ScheduleUpdateJob : IJobParallelFor
{
    public NativeArray<SimulationAgent> agents;
    
    [ReadOnly] public NativeArray<int> homeNearestWaypoint;
    [ReadOnly] public NativeArray<int> workNearestWaypoint;
    [ReadOnly] public NativeArray<int> commercialNearestWaypoint;
    [ReadOnly] public NativeArray<float3> homePositions;
    [ReadOnly] public NativeArray<float3> waypoints;

    public float currentHour;
    public float stuckTimeoutSimHours;
    public float waypointReachDistance;
    public float destinationOffsetRange;
    public float groundY;

    public void Execute(int i)
    {
        SimulationAgent agent = agents[i];
        if (!agent.isActive) return;

        // Sweep: If it's very late and they aren't heading home yet, force them to start walking
        bool isLateNight = currentHour >= agent.returnHomeHour || currentHour < 5f;
        if (isLateNight && agent.scheduleState != AgentScheduleState.Returning && agent.scheduleState != AgentScheduleState.Home)
        {
            SetReturningHome(ref agent, i);
            agents[i] = agent;
            return;
        }

        switch (agent.scheduleState)
        {
            case AgentScheduleState.Home:
                // Organic dispatch: They leave when their personal shift starts (between 7 AM and 9 AM).
                // The window is wide (until 13f) so if you start the sim at 8 AM, they spawn perfectly.
                if (!isLateNight && currentHour >= agent.workStartHour && currentHour < 13f)
                {
                    agent.scheduleState = AgentScheduleState.Commuting;
                    agent.isInsideBuilding = false;
                    agent.destinationWaypointIndex = workNearestWaypoint[agent.workID];
                    agent.hasDestinationWaypoint = true;
                    agent.hasMovementSegment = false;
                    agent.personalOffset = GetPersonalOffset(i);
                    agent.commutingStartTime = currentHour;
                }
                break;

            case AgentScheduleState.Commuting:
                if (ReachedOffsetDestination(agent.position, waypoints[workNearestWaypoint[agent.workID]], agent.personalOffset))
                {
                    agent.scheduleState = AgentScheduleState.AtWork;
                    agent.isInsideBuilding = true;
                    agent.hasMovementSegment = false;
                    agent.hasDestinationWaypoint = false;
                    agent.commutingStartTime = -9999f;
                }
                break;

            case AgentScheduleState.AtWork:
                if (currentHour >= agent.workEndHour)
                {
                    if (agent.visitsCommercial && currentHour < agent.returnHomeHour)
                    {
                        agent.scheduleState = AgentScheduleState.AtCommercial;
                        agent.isInsideBuilding = false;
                        agent.destinationWaypointIndex = commercialNearestWaypoint[agent.commercialID];
                        agent.hasDestinationWaypoint = true;
                        agent.commercialArrivalHour = currentHour;
                        agent.hasMovementSegment = false;
                        agent.personalOffset = GetPersonalOffset(i);
                        agent.commutingStartTime = currentHour;
                    }
                    else
                    {
                        SetReturningHome(ref agent, i);
                    }
                }
                break;

            case AgentScheduleState.AtCommercial:
                if (ReachedOffsetDestination(agent.position, waypoints[commercialNearestWaypoint[agent.commercialID]], agent.personalOffset))
                {
                    agent.isInsideBuilding = true;
                    agent.hasMovementSegment = false;
                    agent.hasDestinationWaypoint = false;
                    agent.commutingStartTime = -9999f;
                    
                    if (currentHour >= agent.commercialArrivalHour + 1.5f)
                    {
                        SetReturningHome(ref agent, i);
                    }
                }
                break;

            case AgentScheduleState.Returning:
                bool arrivedHome = ReachedOffsetDestination(agent.position, waypoints[homeNearestWaypoint[agent.homeID]], agent.personalOffset);
                
                if (arrivedHome)
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
        agent.destinationWaypointIndex = homeNearestWaypoint[agent.homeID];
        agent.hasDestinationWaypoint = true;
        agent.hasMovementSegment = false;
        agent.personalOffset = GetPersonalOffset(agentIndex);
        agent.commutingStartTime = currentHour;
    }

    private float3 GetPersonalOffset(int agentIndex)
    {
        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(agentIndex * 7919));
        float range = destinationOffsetRange;
        return new float3(rng.NextFloat(-range, range), 0f, rng.NextFloat(-range, range));
    }

    private bool ReachedOffsetDestination(float3 agentPos, float3 waypointPos, float3 offset)
    {
        float3 offsetDest = waypointPos + new float3(offset.x, 0f, offset.z);
        return math.distance(
            new float3(agentPos.x, 0f, agentPos.z),
            new float3(offsetDest.x, 0f, offsetDest.z)
        ) < waypointReachDistance * 3f;
    }
}