using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct MovementJob : IJobParallelFor
{
    public NativeArray<SimulationAgent> agents;
    [ReadOnly] public NativeArray<float3> waypoints;
    [ReadOnly] public NativeArray<float3> buildingCenters;
    [ReadOnly] public NativeArray<float3> buildingSizes;

    public float stepSize;
    public float waypointReachDistance;

    public void Execute(int i)
    {
        SimulationAgent agent = agents[i];

        if (!agent.isActive || agent.isInsideBuilding)
        {
            agent.previousPosition = agent.position;
            agents[i] = agent;
            return;
        }

        agent.previousPosition = agent.position;

        float3 direction = agent.targetPosition - agent.position;
        direction.y = 0f;
        float distance = math.length(direction);

        if (distance > waypointReachDistance)
        {
            float3 normalizedDir = math.normalize(direction);
            float move = math.min(agent.speed * stepSize, distance);
            float3 nextPos = agent.position + normalizedDir * move;
            nextPos.y = 10f;

            // Only check buildings if step is large enough to clip through one
            if (move > 1f && IsInsideAnyBuilding(nextPos))
            {
                // Pick a new waypoint instead of clipping through
                var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + agent.currentWaypointIndex * 104729));
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    int candidate = rng.NextInt(0, waypoints.Length);
                    float3 candidatePos = waypoints[candidate];
                    if (!IsInsideAnyBuilding(candidatePos))
                    {
                        agent.currentWaypointIndex = candidate;
                        agent.targetPosition = candidatePos;
                        break;
                    }
                }
                // Don't move this frame, just redirect
            }
            else
            {
                agent.position = nextPos;
                agent.velocity = normalizedDir * agent.speed;
            }
        }
        else
        {
            var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + agent.currentWaypointIndex * 104729));
            int nextWaypoint = agent.currentWaypointIndex;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int candidate = rng.NextInt(0, waypoints.Length);
                if (!IsInsideAnyBuilding(waypoints[candidate]))
                {
                    nextWaypoint = candidate;
                    break;
                }
            }
            agent.currentWaypointIndex = nextWaypoint;
            agent.targetPosition = waypoints[nextWaypoint];
            agent.velocity = float3.zero;
        }

        agents[i] = agent;
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