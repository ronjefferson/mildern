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
    public NativeArray<int> distanceChecksTracker;

    public float deltaTime, realSecondsPerInGameDay;
    public int absoluteTickCounter; 
    public float infectionRadius, transmissionRate, recoveryTime, mortalityRate;
    public float minIncubationDays, maxIncubationDays;
    
    public float currentVaccineEfficacy;
    public float naturalImmunityEfficacy;
    public float immunityDurationDays;
    public float evasionPenaltyPerLevel;

    public float historicalImmunityEfficacy;
    public float historicalRecoveryMultiplier;
    
    public float hospitalTransmissionMultiplier;

    public float cellSize; public float3 gridOrigin; public int gridWidth, gridHeight;

    public void Execute(int i)
    {
        SimulationAgent agent = agentsIn[i];
        int localChecks = 0;

        if (!agent.isActive || agent.healthState == HealthState.Dead) { 
            agentsOut[i] = agent; 
            distanceChecksTracker[i] = 0;
            return; 
        }

        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + absoluteTickCounter * 131 + 1));

        if (agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated)
        {
            agent.immunityTimer -= deltaTime;
            if (agent.immunityTimer <= 0) {
                agent.healthState = HealthState.Susceptible;
                agent.protectedStrainID = -1;
            }
        }

        if (agent.healthState == HealthState.Exposed)
        {
            agent.incubationTimer -= deltaTime;
            if (agent.incubationTimer <= 0) {
                agent.healthState = HealthState.Infected;
                agent.recoveryTimer = recoveryTime * realSecondsPerInGameDay;
            }
        }
        else if (agent.healthState == HealthState.Infected)
        {
            float currentRecoverySpeed = 1f;
            float currentMortalityChance = mortalityRate;
            
            if (agent.isAtHospital) { currentRecoverySpeed = 2f; currentMortalityChance = mortalityRate * 0.2f; }
            if (agent.protectedStrainID != -1) { currentRecoverySpeed *= 1.5f; currentMortalityChance *= 0.1f; }
            
            bool hasHistoricalMemory = (agent.historicalStrainMask & (1u << agent.activeStrainID)) != 0;
            if (hasHistoricalMemory) {
                currentRecoverySpeed *= historicalRecoveryMultiplier;
                currentMortalityChance *= 0.1f;
            }

            agent.recoveryTimer -= (deltaTime * currentRecoverySpeed);
            if (agent.recoveryTimer <= 0)
            {
                if (rng.NextFloat() < currentMortalityChance) {
                    agent.healthState = HealthState.Dead;
                    agent.isActive = false;
                }
                else
                {
                    agent.healthState = HealthState.Recovered;
                    agent.historicalStrainMask |= (1u << agent.activeStrainID);
                    agent.protectedStrainID = agent.activeStrainID;
                    agent.activeStrainID = -1;
                    agent.immunityTimer = immunityDurationDays * realSecondsPerInGameDay;
                }
            }
        }
        else if (agent.healthState == HealthState.Susceptible || agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated)
        {
            float combinedSurvivalChance = 1.0f;
            int potentialStrain = -1;

            if (!agent.isInsideBuilding) {
                int gridX = (int)math.floor((agent.position.x - gridOrigin.x) / cellSize);
                int gridY = (int)math.floor((agent.position.z - gridOrigin.z) / cellSize);
                for (int x = -1; x <= 1; x++) {
                    for (int y = -1; y <= 1; y++) {
                        int checkX = gridX + x; int checkY = gridY + y;
                        if (checkX >= 0 && checkX < gridWidth && checkY >= 0 && checkY < gridHeight) {
                            foreach (int neighborIndex in grid.GetValuesForKey(checkX + checkY * gridWidth)) {
                                if (neighborIndex == i) continue;
                                SimulationAgent neighbor = agentsIn[neighborIndex];
                                
                                if (neighbor.healthState == HealthState.Infected && !neighbor.isInsideBuilding) 
                                {
                                    localChecks++;
                                    if (math.distancesq(agent.position, neighbor.position) <= infectionRadius * infectionRadius)
                                    {
                                        float vulnerability = 1.0f;
                                        if (agent.healthState != HealthState.Susceptible && agent.protectedStrainID != -1) {
                                            int gap = math.abs(neighbor.activeStrainID - agent.protectedStrainID);
                                            float penalty = gap * evasionPenaltyPerLevel;
                                            float baseEff = (agent.healthState == HealthState.Recovered) ? naturalImmunityEfficacy : currentVaccineEfficacy;
                                            vulnerability = 1.0f - math.max(0f, baseEff - penalty);
                                        }
                                        else if ((agent.historicalStrainMask & (1u << neighbor.activeStrainID)) != 0) {
                                            vulnerability = 1.0f - historicalImmunityEfficacy;
                                        }

                                        if (vulnerability > 0f) {
                                            float risk = math.clamp(transmissionRate * vulnerability * deltaTime, 0f, 1f);
                                            combinedSurvivalChance *= (1.0f - risk);
                                            potentialStrain = math.max(potentialStrain, neighbor.activeStrainID);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else {
                int buildingKey = -1;
                if (agent.scheduleState == AgentScheduleState.Home) buildingKey = 1000000 + agent.homeID;
                else if (agent.scheduleState == AgentScheduleState.AtWork) buildingKey = 2000000 + agent.workID;
                else if (agent.scheduleState == AgentScheduleState.AtCommercial) buildingKey = 3000000 + agent.commercialID;
                else if (agent.scheduleState == AgentScheduleState.AtHospital) buildingKey = 4000000 + agent.healthcareID;

                if (buildingKey != -1) {
                    float indoorMultiplier = 1.5f;
                    if (agent.scheduleState == AgentScheduleState.AtHospital) {
                        indoorMultiplier = hospitalTransmissionMultiplier;
                    }

                    foreach (int neighborIndex in indoorMap.GetValuesForKey(buildingKey)) {
                        if (neighborIndex == i) continue;
                        SimulationAgent neighbor = agentsIn[neighborIndex];

                        if (neighbor.healthState == HealthState.Infected) {
                            float vulnerability = 1.0f;
                            if (agent.healthState != HealthState.Susceptible && agent.protectedStrainID != -1) {
                                int gap = math.abs(neighbor.activeStrainID - agent.protectedStrainID);
                                float penalty = gap * evasionPenaltyPerLevel;
                                float baseEff = (agent.healthState == HealthState.Recovered) ? naturalImmunityEfficacy : currentVaccineEfficacy;
                                vulnerability = 1.0f - math.max(0f, baseEff - penalty);
                            }
                            else if ((agent.historicalStrainMask & (1u << neighbor.activeStrainID)) != 0) {
                                vulnerability = 1.0f - historicalImmunityEfficacy;
                            }

                            if (vulnerability > 0f) {
                                float risk = math.clamp(transmissionRate * indoorMultiplier * vulnerability * deltaTime, 0f, 1f);
                                combinedSurvivalChance *= (1.0f - risk);
                                potentialStrain = math.max(potentialStrain, neighbor.activeStrainID);
                            }
                        }
                    }
                }
            }

            float totalInfectionChance = 1.0f - combinedSurvivalChance;
            if (totalInfectionChance > 0f && rng.NextFloat() < totalInfectionChance) {
                agent.healthState = HealthState.Exposed;
                agent.activeStrainID = potentialStrain;
                agent.incubationTimer = rng.NextFloat(minIncubationDays, maxIncubationDays) * realSecondsPerInGameDay;
            }
        }
        agentsOut[i] = agent;
        distanceChecksTracker[i] = localChecks;
    }
}