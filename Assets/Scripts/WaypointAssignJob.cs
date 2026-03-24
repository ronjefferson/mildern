using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct WaypointAssignJob : IJobParallelFor
{
    public NativeArray<SimulationAgent> agents;
    [ReadOnly] public NativeArray<float3> waypoints;
    [ReadOnly] public NativeArray<int> neighborData;
    [ReadOnly] public NativeArray<int> neighborStart;
    [ReadOnly] public NativeArray<int> neighborCount;

    public float currentSimTime;
    public float realTime;
    public float waypointReachDistance;
    public float currentHour;

    public void Execute(int i)
    {
        SimulationAgent agent = agents[i];

        if (!agent.isActive || agent.isInsideBuilding) return;

        // ----- THE TARGETED TELEPORT (ONLY FOR LOOPING AGENTS) -----
        if (agent.scheduleState == AgentScheduleState.Returning && agent.hasDestinationWaypoint && agent.commutingStartTime > -100f)
        {
            float commuteDuration = currentHour - agent.commutingStartTime;
            if (commuteDuration < 0f) commuteDuration += 24f; // Handles midnight wrap-around
            
            // If this specific agent has been walking for over 3 in-game hours, they are stuck in a loop.
            if (commuteDuration > 3.0f)
            {
                agent.currentWaypointIndex = agent.destinationWaypointIndex;
                agent.position = waypoints[agent.destinationWaypointIndex] + agent.personalOffset;
                agent.targetPosition = agent.position;
                agent.moveEndPosition = agent.position;
                agent.hasMovementSegment = false;
                
                agents[i] = agent;
                return; // Teleport complete, exit early.
            }
        }
        // -----------------------------------------------------------

        int maxSteps = 100; 
        int steps = 0;

        while ((!agent.hasMovementSegment || realTime >= agent.arrivalTime) && steps < maxSteps)
        {
            steps++;

            float segmentStartTime = agent.hasMovementSegment ? agent.arrivalTime : realTime;

            if (agent.hasMovementSegment)
            {
                agent.position = agent.moveEndPosition;
            }

            float3 effectiveDest = agent.hasDestinationWaypoint
                ? waypoints[agent.destinationWaypointIndex] + agent.personalOffset
                : agent.targetPosition;

            float3 toTarget = effectiveDest - agent.position;
            toTarget.y = 0f;
            float distToTarget = math.length(toTarget);

            bool isAtDestinationNode = agent.hasDestinationWaypoint && (agent.currentWaypointIndex == agent.destinationWaypointIndex);

            if (distToTarget <= waypointReachDistance * 3f || isAtDestinationNode)
            {
                agent.hasMovementSegment = false;
                break;
            }

            int wIdx = agent.currentWaypointIndex;
            int start = neighborStart[wIdx];
            int count = neighborCount[wIdx];

            if (count > 0)
            {
                bool isDeadEnd = (count == 1);

                if (agent.hasDestinationWaypoint)
                {
                    float3 dest = effectiveDest;
                    int best1 = -1, best2 = -1, best3 = -1;
                    float dist1 = float.MaxValue, dist2 = float.MaxValue, dist3 = float.MaxValue;

                    for (int n = 0; n < count; n++)
                    {
                        int candidateIdx = neighborData[start + n];
                        
                        // Breadcrumbs: Heavy penalty to discourage turning around
                        float penalty = 0f;
                        if (candidateIdx == agent.prev1) penalty = 10000000f; 
                        else if (candidateIdx == agent.prev2) penalty = 5000000f;
                        else if (candidateIdx == agent.prev3) penalty = 2500000f;
                        else if (candidateIdx == agent.prev4) penalty = 1250000f;

                        float d = math.distancesq(
                            new float3(waypoints[candidateIdx].x, 0f, waypoints[candidateIdx].z),
                            new float3(dest.x, 0f, dest.z)
                        ) + penalty;
                        
                        if (d < dist1) { dist3 = dist2; best3 = best2; dist2 = dist1; best2 = best1; dist1 = d; best1 = candidateIdx; }
                        else if (d < dist2) { dist3 = dist2; best3 = best2; dist2 = d; best2 = candidateIdx; }
                        else if (d < dist3) { dist3 = d; best3 = candidateIdx; }
                    }

                    var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + wIdx * 104729 + (uint)(currentSimTime * 13) + steps * 17));
                    float roll = rng.NextFloat();
                    
                    int chosen;
                    if (roll < 0.6f || best2 == -1) chosen = best1;
                    else if (roll < 0.9f || best3 == -1) chosen = best2;
                    else chosen = best3;

                    if (chosen >= 0)
                    {
                        agent.prev4 = agent.prev3;
                        agent.prev3 = agent.prev2;
                        agent.prev2 = agent.prev1;
                        agent.prev1 = agent.currentWaypointIndex;
                        
                        agent.currentWaypointIndex = chosen;
                        agent.targetPosition = waypoints[chosen];
                        agent.targetPosition.y = agent.position.y;
                    }
                    else agent.prev1 = -1;
                }
                else
                {
                    // Wandering
                    var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + wIdx * 104729 + (uint)(currentSimTime * 37) + steps * 17));
                    int nextIdx = -1;
                    
                    if (isDeadEnd) nextIdx = neighborData[start];
                    else
                    {
                        int validCount = count - 1;
                        int pick = rng.NextInt(0, validCount);
                        int currentValid = 0;
                        for (int n = 0; n < count; n++)
                        {
                            int candidateIdx = neighborData[start + n];
                            if (candidateIdx == agent.prev1) continue;
                            if (currentValid == pick) { nextIdx = candidateIdx; break; }
                            currentValid++;
                        }
                    }

                    if (nextIdx >= 0)
                    {
                        agent.prev4 = agent.prev3;
                        agent.prev3 = agent.prev2;
                        agent.prev2 = agent.prev1;
                        agent.prev1 = agent.currentWaypointIndex;
                        agent.currentWaypointIndex = nextIdx;
                        agent.targetPosition = waypoints[nextIdx];
                        agent.targetPosition.y = agent.position.y;
                    }
                    else agent.prev1 = -1;
                }
            }

            AssignSegment(ref agent, segmentStartTime);
        }

        agents[i] = agent;
    }

    void AssignSegment(ref SimulationAgent agent, float startTime)
    {
        float3 direction = agent.targetPosition - agent.position;
        direction.y = 0f;
        float distance = math.length(direction);

        if (distance < 0.01f)
        {
            agent.hasMovementSegment = false;
            agent.arrivalTime = startTime + 0.1f; 
            return;
        }

        float realTravelTime = distance / agent.speed;
        agent.moveStartPosition = agent.position;
        agent.moveEndPosition = agent.targetPosition;
        agent.moveStartTime = startTime;
        agent.arrivalTime = startTime + realTravelTime;
        agent.hasMovementSegment = true;
    }
}