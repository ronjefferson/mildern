using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct IndoorMappingJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<SimulationAgent> agents;
    public NativeParallelMultiHashMap<int, int>.ParallelWriter indoorMapWriter;
    public void Execute(int i)
    {
        SimulationAgent agent = agents[i];
        if (!agent.isActive || !agent.isInsideBuilding) return;
        int buildingKey = -1;
        if (agent.scheduleState == AgentScheduleState.Home) buildingKey = 1000000 + agent.homeID;
        else if (agent.scheduleState == AgentScheduleState.AtWork) buildingKey = 2000000 + agent.workID;
        else if (agent.scheduleState == AgentScheduleState.AtCommercial) buildingKey = 3000000 + agent.commercialID;
        else if (agent.scheduleState == AgentScheduleState.AtHospital) buildingKey = 4000000 + agent.healthcareID;
        if (buildingKey != -1) indoorMapWriter.Add(buildingKey, i);
    }
}

[BurstCompile]
public struct EpidemicJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<SimulationAgent> agentsIn;
    public NativeArray<SimulationAgent> agentsOut;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> grid;      
    [ReadOnly] public NativeParallelMultiHashMap<int, int> indoorMap; 

    public float deltaTime, timeMultiplier, currentSimTime, realSecondsPerInGameDay;
    public float infectionRadius, transmissionRate, recoveryTime, mortalityRate;
    public float minIncubationDays, maxIncubationDays; 
    public float naturalImmunityGiven; // How much shield catching the virus gives you (e.g. 0.85f)
    public float dailyImmunityDecay; // How much shield drops per day (e.g. 0.01f)

    public float cellSize; public float3 gridOrigin; public int gridWidth, gridHeight;

    public void Execute(int i)
    {
        SimulationAgent agent = agentsIn[i];
        if (!agent.isActive || agent.healthState == HealthState.Dead) { agentsOut[i] = agent; return; }

        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + (int)(currentSimTime * 1000) + 1));

        // 1. CONSTANT WANING IMMUNITY (Applies to everyone)
        if (agent.immunityDefense > 0f)
        {
            float decayThisFrame = dailyImmunityDecay * (deltaTime / realSecondsPerInGameDay);
            agent.immunityDefense = math.max(0f, agent.immunityDefense - decayThisFrame);

            // If their shield drops to 0, they visually return to Susceptible
            if (agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated) {
                if (agent.immunityDefense <= 0.05f) agent.healthState = HealthState.Susceptible;
            }
        }

        // 2. INCUBATION PHASE
        if (agent.healthState == HealthState.Exposed)
        {
            agent.incubationTimer -= deltaTime;
            if (agent.incubationTimer <= 0) { agent.healthState = HealthState.Infected; agent.recoveryTimer = recoveryTime * realSecondsPerInGameDay; }
        }
        
        // 3. SICK PHASE
        else if (agent.healthState == HealthState.Infected)
        {
            float currentRecoverySpeed = 1f; float currentMortalityChance = mortalityRate;
            if (agent.isAtHospital) { currentRecoverySpeed = 2f; currentMortalityChance = mortalityRate * 0.2f; }
            
            // Even if sick, a residual shield helps them survive!
            if (agent.immunityDefense > 0.2f) { currentRecoverySpeed *= 1.5f; currentMortalityChance *= 0.1f; }

            agent.recoveryTimer -= (deltaTime * currentRecoverySpeed);
            if (agent.recoveryTimer <= 0)
            {
                if (rng.NextFloat() < currentMortalityChance) { agent.healthState = HealthState.Dead; agent.isActive = false; }
                else 
                { 
                    agent.healthState = HealthState.Recovered;
                    // NATURAL IMMUNITY TOP-OFF (Compounding Math!)
                    agent.immunityDefense = agent.immunityDefense + ((1.0f - agent.immunityDefense) * naturalImmunityGiven); 
                }
            }
        }
        
        // 4. INFECTION CHECK (Outdoor & Indoor combined logic)
        else if (agent.healthState == HealthState.Susceptible || agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated)
        {
            bool gotExposed = false;
            float vulnerability = math.max(0f, 1.0f - agent.immunityDefense); // The core math!

            // Outdoors
            if (!agent.isInsideBuilding) {
                int gridX = (int)math.floor((agent.position.x - gridOrigin.x) / cellSize); int gridY = (int)math.floor((agent.position.z - gridOrigin.z) / cellSize);
                for (int x = -1; x <= 1; x++) {
                    for (int y = -1; y <= 1; y++) {
                        int checkX = gridX + x; int checkY = gridY + y;
                        if (checkX >= 0 && checkX < gridWidth && checkY >= 0 && checkY < gridHeight) {
                            foreach (int neighborIndex in grid.GetValuesForKey(checkX + checkY * gridWidth)) {
                                if (neighborIndex == i) continue;
                                SimulationAgent neighbor = agentsIn[neighborIndex];
                                if (neighbor.healthState == HealthState.Infected && !neighbor.isInsideBuilding && math.distancesq(agent.position, neighbor.position) <= infectionRadius * infectionRadius) {
                                    if (rng.NextFloat() < (transmissionRate * vulnerability * deltaTime)) { gotExposed = true; break; }
                                }
                            }
                        }
                        if (gotExposed) break;
                    }
                    if (gotExposed) break;
                }
            }
            // Indoors
            else {
                int buildingKey = -1;
                if (agent.scheduleState == AgentScheduleState.Home) buildingKey = 1000000 + agent.homeID;
                else if (agent.scheduleState == AgentScheduleState.AtWork) buildingKey = 2000000 + agent.workID;
                else if (agent.scheduleState == AgentScheduleState.AtCommercial) buildingKey = 3000000 + agent.commercialID;
                else if (agent.scheduleState == AgentScheduleState.AtHospital) buildingKey = 4000000 + agent.healthcareID;

                if (buildingKey != -1) {
                    foreach (int neighborIndex in indoorMap.GetValuesForKey(buildingKey)) {
                        if (neighborIndex == i) continue;
                        if (agentsIn[neighborIndex].healthState == HealthState.Infected) {
                            if (rng.NextFloat() < (transmissionRate * 1.5f * vulnerability * deltaTime)) { gotExposed = true; break; }
                        }
                    }
                }
            }

            if (gotExposed) { agent.healthState = HealthState.Exposed; agent.incubationTimer = rng.NextFloat(minIncubationDays, maxIncubationDays) * realSecondsPerInGameDay; }
        }
        agentsOut[i] = agent;
    }
}