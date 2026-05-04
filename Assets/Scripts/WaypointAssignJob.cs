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
    public float stuckTimeoutSimHours;

    public void Execute(int i)
    {
        SimulationAgent agent = agents[i];

        if (!agent.isActive || agent.isInsideBuilding) return;
        
        if (agent.hasDestinationWaypoint && agent.commutingStartTime > -100f)
        {
            float commuteDuration = currentHour - agent.commutingStartTime;
            if (commuteDuration < 0f) commuteDuration += 24f; 
            
            if (commuteDuration > stuckTimeoutSimHours)
            {
                agent.currentWaypointIndex = agent.destinationWaypointIndex;
                agent.position = waypoints[agent.destinationWaypointIndex] + agent.personalOffset;
                agent.targetPosition = agent.position;
                agent.moveEndPosition = agent.position;
                agent.hasMovementSegment = false;
                
                agent.isEscaping = false;
                agent.frustrationCounter = 0;
                agent.highWatermarkDistance = 0f;

                if (agent.scheduleState == AgentScheduleState.Leisure) 
                {
                    agent.hasDestinationWaypoint = false;
                    agent.isInsideBuilding = false;
                } 
                else 
                {
                    agent.isInsideBuilding = true;
                    if (agent.scheduleState == AgentScheduleState.Returning)
                    {
                        agent.scheduleState = AgentScheduleState.Home;
                    }
                }
                
                agent.commutingStartTime = -9999f;
                agents[i] = agent;
                return; 
            }
        }

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
                agent.isEscaping = false;
                agent.frustrationCounter = 0;
                agent.highWatermarkDistance = 0f;
                
                if (agent.scheduleState == AgentScheduleState.Leisure) 
                {
                    agent.hasDestinationWaypoint = false;
                    agent.isInsideBuilding = false;
                    agent.commutingStartTime = -9999f;
                }
                else
                {
                    agent.isInsideBuilding = true;
                    agent.commutingStartTime = -9999f;

                    if (agent.scheduleState == AgentScheduleState.Returning)
                    {
                        agent.scheduleState = AgentScheduleState.Home;
                    }
                }
                
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
                    if (agent.isEscaping)
                    {
                        agent.escapeStepCount--;
                        if (agent.escapeStepCount <= 0)
                        {
                            agent.isEscaping = false;
                            agent.highWatermarkDistance = 0f; 
                            agent.frustrationCounter = 0;
                        }
                    }
                    else
                    {
                        float currentDistSq = math.distancesq(
                            new float3(agent.position.x, 0f, agent.position.z),
                            new float3(effectiveDest.x, 0f, effectiveDest.z)
                        );

                        if (agent.highWatermarkDistance == 0f || currentDistSq < agent.highWatermarkDistance - 1.0f)
                        {
                            agent.highWatermarkDistance = currentDistSq;
                            agent.frustrationCounter = 0;
                        }
                        else
                        {
                            agent.frustrationCounter++;
                        }

                        if (agent.frustrationCounter >= 15)
                        {
                            agent.isEscaping = true;
                            agent.escapeStepCount = 8; 
                            agent.frustrationCounter = 0;
                        }
                    }
                }

                if (agent.hasDestinationWaypoint)
                {
                    float3 dest = effectiveDest;
                    int best1 = -1, best2 = -1, best3 = -1;
                    float score1 = float.MaxValue, score2 = float.MaxValue, score3 = float.MaxValue;

                    for (int n = 0; n < count; n++)
                    {
                        int candidateIdx = neighborData[start + n];
                        
                        float penalty = 0f;
                        if      (candidateIdx == agent.prev1) penalty = 80000000f; 
                        else if (candidateIdx == agent.prev2) penalty = 70000000f;
                        else if (candidateIdx == agent.prev3) penalty = 60000000f;
                        else if (candidateIdx == agent.prev4) penalty = 50000000f;
                        else if (candidateIdx == agent.prev5) penalty = 40000000f;
                        else if (candidateIdx == agent.prev6) penalty = 30000000f;
                        else if (candidateIdx == agent.prev7) penalty = 20000000f;
                        else if (candidateIdx == agent.prev8) penalty = 10000000f;

                        float distSq = math.distancesq(
                            new float3(waypoints[candidateIdx].x, 0f, waypoints[candidateIdx].z),
                            new float3(dest.x, 0f, dest.z)
                        );
                        
                        float nodeScore = agent.isEscaping ? (-distSq + penalty) : (distSq + penalty);
                        
                        if (nodeScore < score1) { score3 = score2; best3 = best2; score2 = score1; best2 = best1; score1 = nodeScore; best1 = candidateIdx; }
                        else if (nodeScore < score2) { score3 = score2; best3 = best2; score2 = nodeScore; best2 = candidateIdx; }
                        else if (nodeScore < score3) { score3 = nodeScore; best3 = candidateIdx; }
                    }

                    var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + wIdx * 104729 + (uint)(currentSimTime * 13) + steps * 17));
                    float roll = rng.NextFloat();
                    
                    int chosen;
                    float p1 = agent.isEscaping ? 0.85f : 0.6f;
                    float p2 = agent.isEscaping ? 0.98f : 0.9f;

                    if (roll < p1 || best2 == -1) chosen = best1;
                    else if (roll < p2 || best3 == -1) chosen = best2;
                    else chosen = best3;

                    if (chosen >= 0)
                    {
                        agent.prev8 = agent.prev7;
                        agent.prev7 = agent.prev6;
                        agent.prev6 = agent.prev5;
                        agent.prev5 = agent.prev4;
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
                        agent.prev8 = agent.prev7;
                        agent.prev7 = agent.prev6;
                        agent.prev6 = agent.prev5;
                        agent.prev5 = agent.prev4;
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