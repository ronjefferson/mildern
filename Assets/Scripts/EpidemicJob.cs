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
    
    public float currentVaccineEfficacy, naturalImmunityEfficacy, immunityDurationDays;

    public bool isSocialDistancing;
    public float sdAbidance, sdRadiusMultiplier, sdTransmissionMultiplier, sdContactCapMultiplier;

    public float hospitalRecoveryMultiplier, hospitalMortalityModifier;
    
    public int hospitalBedsPerFacility;

    public float homeTransmissionMultiplier; public int homeContactCap;
    public float workplaceTransmissionMultiplier; public int workplaceContactCap;
    public float commercialTransmissionMultiplier; public int commercialContactCap;
    public float hospitalTransmissionMultiplier; public int hospitalContactCap;

    public float cellSize; public float3 gridOrigin; public int gridWidth, gridHeight;

    public void Execute(int i)
    {
        SimulationAgent agent = agentsIn[i];
        int localChecks = 0;

        if (!agent.isActive || agent.healthState == HealthState.Dead) { 
            agentsOut[i] = agent; distanceChecksTracker[i] = 0; return; 
        }

        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(i * 7919 + absoluteTickCounter * 131 + 1));
        bool isAgentCompliant = isSocialDistancing && (agent.complianceLevel <= sdAbidance);

        if (agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated) {
            agent.immunityTimer -= deltaTime;
            if (agent.immunityTimer <= 0) agent.healthState = HealthState.Susceptible;
        }

        if (agent.healthState == HealthState.Exposed) {
            agent.incubationTimer -= deltaTime;
            if (agent.incubationTimer <= 0) {
                agent.healthState = HealthState.Infected;
                agent.recoveryTimer = recoveryTime * realSecondsPerInGameDay;
            }
        }
        else if (agent.healthState == HealthState.Infected) {
            float currentRecoverySpeed = 1f;
            float currentMortalityChance = mortalityRate;
            
            if (agent.isAtHospital) { 
                int buildingKey = 4000000 + agent.healthcareID;
                int queuePosition = 0;

                foreach (int occupantIndex in indoorMap.GetValuesForKey(buildingKey)) {
                    if (occupantIndex == i) continue;
                    
                    SimulationAgent occupant = agentsIn[occupantIndex];
                    if (occupant.isAtHospital) {
                        if (occupant.recoveryTimer < agent.recoveryTimer || 
                           (occupant.recoveryTimer == agent.recoveryTimer && occupantIndex < i)) {
                            queuePosition++; 
                        }
                    }
                }

                if (queuePosition < hospitalBedsPerFacility) {
                    currentRecoverySpeed = hospitalRecoveryMultiplier; 
                    currentMortalityChance = mortalityRate * hospitalMortalityModifier; 
                }
            }

            agent.recoveryTimer -= (deltaTime * currentRecoverySpeed);
            if (agent.recoveryTimer <= 0) {
                if (rng.NextFloat() < currentMortalityChance) { agent.healthState = HealthState.Dead; agent.isActive = false; }
                else { agent.healthState = HealthState.Recovered; agent.immunityTimer = immunityDurationDays * realSecondsPerInGameDay; }
            }
        }
        else if (agent.healthState == HealthState.Susceptible || agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated)
        {
            float combinedSurvivalChance = 1.0f;

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
                                    bool isNeighborCompliant = isSocialDistancing && (neighbor.complianceLevel <= sdAbidance);
                                    
                                    float effectiveRadius = infectionRadius;
                                    float effectiveTransRate = transmissionRate;

                                    if (isAgentCompliant || isNeighborCompliant) {
                                        effectiveRadius *= sdRadiusMultiplier;
                                        effectiveTransRate *= sdTransmissionMultiplier;
                                    }

                                    if (math.distancesq(agent.position, neighbor.position) <= effectiveRadius * effectiveRadius)
                                    {
                                        float vulnerability = 1.0f;
                                        if (agent.healthState == HealthState.Recovered) vulnerability = 1.0f - naturalImmunityEfficacy;
                                        else if (agent.healthState == HealthState.Vaccinated) vulnerability = 1.0f - currentVaccineEfficacy;

                                        if (vulnerability > 0f) {
                                            float risk = math.clamp(effectiveTransRate * vulnerability * deltaTime, 0f, 1f);
                                            combinedSurvivalChance *= (1.0f - risk);
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
                float currentIndoorMult = 1.0f;
                int baseIndoorCap = -1;

                if (agent.scheduleState == AgentScheduleState.Home) { buildingKey = 1000000 + agent.homeID; currentIndoorMult = homeTransmissionMultiplier; baseIndoorCap = homeContactCap; }
                else if (agent.scheduleState == AgentScheduleState.AtWork) { buildingKey = 2000000 + agent.workID; currentIndoorMult = workplaceTransmissionMultiplier; baseIndoorCap = workplaceContactCap; }
                else if (agent.scheduleState == AgentScheduleState.AtCommercial) { buildingKey = 3000000 + agent.commercialID; currentIndoorMult = commercialTransmissionMultiplier; baseIndoorCap = commercialContactCap; }
                else if (agent.scheduleState == AgentScheduleState.AtHospital) { buildingKey = 4000000 + agent.healthcareID; currentIndoorMult = hospitalTransmissionMultiplier; baseIndoorCap = hospitalContactCap; }

                if (buildingKey != -1) {
                    int contactsProcessed = 0;
                    int effectiveIndoorCap = baseIndoorCap;
                    
                    if (isAgentCompliant && baseIndoorCap > 0) {
                        effectiveIndoorCap = (int)math.max(1, baseIndoorCap * sdContactCapMultiplier);
                    }

                    float effectiveTransRate = transmissionRate;
                    if (isAgentCompliant) effectiveTransRate *= sdTransmissionMultiplier;

                    foreach (int neighborIndex in indoorMap.GetValuesForKey(buildingKey)) {
                        if (neighborIndex == i) continue;
                        if (effectiveIndoorCap > 0 && contactsProcessed >= effectiveIndoorCap) break;

                        SimulationAgent neighbor = agentsIn[neighborIndex];
                        if (neighbor.healthState == HealthState.Infected) {
                            float vulnerability = 1.0f;
                            if (agent.healthState == HealthState.Recovered) vulnerability = 1.0f - naturalImmunityEfficacy;
                            else if (agent.healthState == HealthState.Vaccinated) vulnerability = 1.0f - currentVaccineEfficacy;

                            if (vulnerability > 0f) {
                                float risk = math.clamp(effectiveTransRate * currentIndoorMult * vulnerability * deltaTime, 0f, 1f);
                                combinedSurvivalChance *= (1.0f - risk);
                                contactsProcessed++;
                            }
                        }
                    }
                }
            }

            float totalInfectionChance = 1.0f - combinedSurvivalChance;
            if (totalInfectionChance > 0f && rng.NextFloat() < totalInfectionChance) {
                agent.healthState = HealthState.Exposed;
                agent.incubationTimer = rng.NextFloat(minIncubationDays, maxIncubationDays) * realSecondsPerInGameDay;
            }
        }
        agentsOut[i] = agent;
        distanceChecksTracker[i] = localChecks;
    }
}