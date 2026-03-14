using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct WaypointAssignJob : IJobParallelFor
{
    public NativeArray<SimulationAgent> agents;
    [ReadOnly] public NativeArray<float3> waypoints;
    [ReadOnly] public NativeArray<float3> buildingCenters;
    [ReadOnly] public NativeArray<float3> buildingSizes;

    public float currentSimTime;
    public float waypointReachDistance;

    public void Execute(int i)
    {
        SimulationAgent agent = agents[i];

        if (!agent.isActive || agent.isInsideBuilding)
        {
            agents[i] = agent;
            return;
        }

        // Check if agent has arrived at their current movement segment end
        if (!agent.hasMovementSegment || currentSimTime >= agent.arrivalTime)
        {
            // Update logical position to where they arrived
            if (agent.hasMovementSegment)
                agent.position = agent.moveEndPosition;

            // Check if close enough to target waypoint to pick a new one
            float3 toTarget = agent.targetPosition - agent.position;
            toTarget.y = 0f;
            float distToTarget = math.length(toTarget);

            if (distToTarget <= waypointReachDistance)
            {
                // Pick next random waypoint
                var rng = Unity.Mathematics.Random.CreateFromIndex(
                    (uint)(i * 7919 + agent.currentWaypointIndex * 104729 + (uint)(currentSimTime * 37))
                );
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    int candidate = rng.NextInt(0, waypoints.Length);
                    float3 candidatePos = waypoints[candidate];
                    candidatePos.y = agent.position.y;
                    if (!IsInsideAnyBuilding(candidatePos))
                    {
                        agent.currentWaypointIndex = candidate;
                        agent.targetPosition = candidatePos;
                        break;
                    }
                }
            }

            // Assign new movement segment toward current target
            AssignSegment(ref agent);
        }

        agents[i] = agent;
    }

    void AssignSegment(ref SimulationAgent agent)
    {
        float3 direction = agent.targetPosition - agent.position;
        direction.y = 0f;
        float distance = math.length(direction);

        if (distance < 0.01f)
        {
            agent.hasMovementSegment = false;
            return;
        }

        // Move one step toward target — segment length based on speed
        // We move in chunks so agents follow waypoint paths naturally
        float segmentLength = math.min(distance, agent.speed * 10f);
        float3 segmentEnd = agent.position + math.normalize(direction) * segmentLength;
        segmentEnd.y = agent.position.y;

        float travelTime = segmentLength / agent.speed;

        agent.moveStartPosition = agent.position;
        agent.moveEndPosition = segmentEnd;
        agent.moveStartTime = currentSimTime;
        agent.arrivalTime = currentSimTime + travelTime;
        agent.hasMovementSegment = true;
    }

    bool IsInsideAnyBuilding(float3 point)
    {
        for (int b = 0; b < buildingCenters.Length; b++)
        {
            float3 toPoint = point - buildingCenters[b];
            toPoint.y = 0f;
            float3 halfSize = buildingSizes[b] * 0.5f + new float3(1f, 0f, 1f);
            if (math.abs(toPoint.x) < halfSize.x && math.abs(toPoint.z) < halfSize.z)
                return true;
        }
        return false;
    }
}