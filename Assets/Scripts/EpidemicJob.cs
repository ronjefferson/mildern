using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct EpidemicJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<SimulationAgent> agentsIn;
    [WriteOnly] public NativeArray<SimulationAgent> agentsOut;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> grid;

    public float deltaTime;
    public float timeMultiplier;
    
    // Time injection to make the RNG dynamic over time
    public float currentSimTime; 
    
    public float infectionRadius;
    public float transmissionRate;
    public float recoveryTime;
    public float mortalityRate;
    public float cellSize;
    public float3 gridOrigin;
    public int gridWidth;
    public int gridHeight;

    public void Execute(int i)
    {
        SimulationAgent agent = agentsIn[i];

        if (!agent.isActive)
        {
            agentsOut[i] = agent;
            return;
        }

        if (agent.healthState == HealthState.Infected)
        {
            agent.recoveryTimer += deltaTime * timeMultiplier;

            if (agent.recoveryTimer >= recoveryTime)
            {
                // RNG Seed includes simulation time to prevent deterministic loops
                var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + (uint)(currentSimTime * 1000f)));
                
                if (rng.NextFloat() < mortalityRate)
                    agent.healthState = HealthState.Dead;
                else
                    agent.healthState = HealthState.Recovered;

                agent.recoveryTimer = 0f;
                agent.isActive = agent.healthState != HealthState.Dead;
            }
        }

        if (agent.healthState == HealthState.Susceptible && !agent.isInsideBuilding)
        {
            int x = (int)((agent.position.x - gridOrigin.x) / cellSize);
            int z = (int)((agent.position.z - gridOrigin.z) / cellSize);
            x = math.clamp(x, 0, gridWidth - 1);
            z = math.clamp(z, 0, gridHeight - 1);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx < 0 || nx >= gridWidth || nz < 0 || nz >= gridHeight) continue;

                    int key = nx + nz * gridWidth;

                    if (grid.TryGetFirstValue(key, out int neighborIdx, out var iterator))
                    {
                        do
                        {
                            if (neighborIdx == i) continue;

                            SimulationAgent neighbor = agentsIn[neighborIdx];
                            if (neighbor.healthState != HealthState.Infected) continue;

                            float dist = math.distance(agent.position, neighbor.position);
                            if (dist < infectionRadius)
                            {
                                float chance = transmissionRate * (1f - agent.complianceLevel) * deltaTime * timeMultiplier;
                                
                                // RNG Seed includes simulation time for daily exposure checks
                                var rng = Unity.Mathematics.Random.CreateFromIndex(
                                    (uint)(i * 73856 + neighborIdx * 19274 + (uint)(currentSimTime * 1000f))
                                );
                                
                                if (rng.NextFloat() < chance)
                                {
                                    agent.healthState = HealthState.Infected;
                                    agent.infectionTimer = 0f;
                                }
                            }
                        }
                        while (grid.TryGetNextValue(out neighborIdx, ref iterator));
                    }
                }
            }
        }

        agentsOut[i] = agent;
    }
}