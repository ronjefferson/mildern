using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public class SimulationManager : MonoBehaviour
{
    public static SimulationManager Instance;

    [Header("Population")]
    public int populationSize = 10000;
    public int initialInfected = 10;

    [Header("Epidemic Parameters")]
    public float infectionRadius = 3f;
    public float transmissionRate = 0.3f;
    public float recoveryTime = 14f;
    public float mortalityRate = 0.02f;

    [Header("Movement")]
    public float agentSpeed = 5f;
    public float waypointReachDistance = 5f;
    public float destinationOffsetRange = 8f;

    [Header("Schedule")]
    public float stuckTimeoutSimHours = 8f;

    [Header("Rendering")]
    public float agentGroundOffset = 0f;

    [Header("Waypoints")]
    public WaypointData waypointData;

    [Header("Buildings")]
    public BuildingBoundsData buildingBoundsData;

    [Header("Grid")]
    public float cellSize = 10f;

    [Header("Epidemic Ticks")]
    public int maxEpicTicksPerFrame = 5;

    private NativeArray<SimulationAgent> agents;
    private NativeArray<SimulationAgent> agentsBuffer;
    private NativeArray<float3> waypoints;
    private NativeArray<int> neighborData;
    private NativeArray<int> neighborStart;
    private NativeArray<int> neighborCount;
    
    private NativeArray<int> nativeHomeNearest;
    private NativeArray<int> nativeWorkNearest;
    private NativeArray<int> nativeCommercialNearest;
    private NativeArray<float3> nativeHomePositions;

    private SpatialGrid spatialGrid;

    private bool initialized = false;

    private float3[] homePositions;
    private float3[] workPositions;
    private float3[] commercialPositions;

    private int[] homeNearestWaypoint;
    private int[] workNearestWaypoint;
    private int[] commercialNearestWaypoint;

    private const float groundY = 0f;

    private float realTime = 0f;
    private float epicAccumulator = 0f;
    private const float epicTickInterval = 1f;

    private float fixedTickAccumulator = 0f;
    private float fixedTickInterval = 0.05f; 

    void Awake() { Instance = this; }
    void Start() { Invoke("Initialize", 1f); }

    void Initialize()
    {
        if (BuildingManager.Instance == null) return;
        if (waypointData == null || waypointData.waypoints == null || waypointData.waypoints.Length == 0) return; 
        if (waypointData.neighborData == null || waypointData.neighborData.Length == 0) return;

        List<Building> homes = BuildingManager.Instance.GetResidential();
        List<Building> works = BuildingManager.Instance.GetWorkplace();
        List<Building> commercials = BuildingManager.Instance.GetCommercial();

        if (homes.Count == 0) return;

        homePositions = new float3[homes.Count];
        for (int i = 0; i < homes.Count; i++) homePositions[i] = new float3(homes[i].GetPosition().x, groundY, homes[i].GetPosition().z);

        workPositions = new float3[Mathf.Max(works.Count, 1)];
        for (int i = 0; i < works.Count; i++) workPositions[i] = new float3(works[i].GetPosition().x, groundY, works[i].GetPosition().z);

        commercialPositions = new float3[Mathf.Max(commercials.Count, 1)];
        for (int i = 0; i < commercials.Count; i++) commercialPositions[i] = new float3(commercials[i].GetPosition().x, groundY, commercials[i].GetPosition().z);

        int numWaypoints = waypointData.waypoints.Length;
        waypoints = new NativeArray<float3>(numWaypoints, Allocator.Persistent);
        for (int i = 0; i < numWaypoints; i++) waypoints[i] = new float3(waypointData.waypoints[i].x, groundY, waypointData.waypoints[i].z);

        neighborData = new NativeArray<int>(waypointData.neighborData, Allocator.Persistent);
        neighborStart = new NativeArray<int>(waypointData.neighborStart, Allocator.Persistent);
        neighborCount = new NativeArray<int>(waypointData.neighborCount, Allocator.Persistent);

        homeNearestWaypoint = new int[homePositions.Length];
        for (int i = 0; i < homePositions.Length; i++) homeNearestWaypoint[i] = FindNearestWaypointIndex(homePositions[i]);

        workNearestWaypoint = new int[workPositions.Length];
        for (int i = 0; i < workPositions.Length; i++) workNearestWaypoint[i] = FindNearestWaypointIndex(workPositions[i]);

        commercialNearestWaypoint = new int[commercials.Count];
        for (int i = 0; i < commercials.Count; i++) commercialNearestWaypoint[i] = FindNearestWaypointIndex(commercialPositions[i]);

        nativeHomeNearest = new NativeArray<int>(homeNearestWaypoint, Allocator.Persistent);
        nativeWorkNearest = new NativeArray<int>(workNearestWaypoint, Allocator.Persistent);
        nativeCommercialNearest = new NativeArray<int>(commercialNearestWaypoint, Allocator.Persistent);
        nativeHomePositions = new NativeArray<float3>(homePositions, Allocator.Persistent);

        float3 min = homePositions[0], max = homePositions[0];
        foreach (float3 p in homePositions) { min = math.min(min, p); max = math.max(max, p); }
        foreach (float3 p in workPositions) { min = math.min(min, p); max = math.max(max, p); }
        foreach (float3 p in commercialPositions) { min = math.min(min, p); max = math.max(max, p); }

        float3 origin = min - new float3(100f, 0f, 100f);
        int gridW = (int)((max.x - min.x + 200f) / cellSize) + 1;
        int gridH = (int)((max.z - min.z + 200f) / cellSize) + 1;
        spatialGrid = new SpatialGrid(cellSize, origin, gridW, gridH);

        agents = new NativeArray<SimulationAgent>(populationSize, Allocator.Persistent);
        agentsBuffer = new NativeArray<SimulationAgent>(populationSize, Allocator.Persistent);

        realTime = 0f;

        for (int i = 0; i < populationSize; i++)
        {
            int homeIdx = i % homePositions.Length;
            int workIdx = i % workPositions.Length;
            int commercialIdx = i % commercialPositions.Length;

            int startWaypoint = homeNearestWaypoint[homeIdx];
            float3 spawnPos = waypoints[startWaypoint];
            spawnPos.y = groundY;

            agents[i] = new SimulationAgent
            {
                position = spawnPos,
                moveStartPosition = spawnPos,
                moveEndPosition = spawnPos,
                moveStartTime = 0f,
                arrivalTime = 1f,
                targetPosition = spawnPos,
                personalOffset = GetPersonalOffset(i),
                
                healthState = i < initialInfected ? HealthState.Infected : HealthState.Susceptible,
                scheduleState = AgentScheduleState.Home,
                
                infectionTimer = 0f,
                recoveryTimer = 0f,
                speed = Random.Range(agentSpeed * 0.75f, agentSpeed * 1.25f),
                
                homeID = homeIdx,
                workID = workIdx,
                commercialID = commercialIdx,
                
                currentWaypointIndex = startWaypoint,
                destinationWaypointIndex = homeNearestWaypoint[homeIdx],
                
                prev1 = startWaypoint, 
                prev2 = -1, 
                prev3 = -1, 
                prev4 = -1,
                
                workStartHour = Random.Range(7f, 9f),
                workEndHour = Random.Range(16f, 18f),
                returnHomeHour = Mathf.Max(Random.Range(18f, 22f), Random.Range(16f, 18f) + 1f),
                commercialArrivalHour = Random.Range(16f, 18f) + Random.Range(0.25f, 1.0f),
                
                complianceLevel = Random.Range(0f, 1f),
                commutingStartTime = -9999f,
                
                isWeekendWorker = Random.value < 0.2f,
                isActive = true,
                isInsideBuilding = true,
                hasMovementSegment = false,
                hasDestinationWaypoint = false,
                visitsCommercial = Random.value < 0.4f,
                isWeekendRoamer = Random.value < 0.6f
            };
        }

        initialized = true;
    }

    void Update()
    {
        if (!initialized) return;

        float multiplier = TimeManager.Instance != null ? TimeManager.Instance.timeMultiplier : 1f;

        if (multiplier > 0f)
        {
            float dynamicTickInterval = fixedTickInterval * multiplier;
            if (dynamicTickInterval < 0.01f) dynamicTickInterval = 0.01f; 

            float unscaledTick = dynamicTickInterval / multiplier;

            fixedTickAccumulator += Time.deltaTime * multiplier;

            while (fixedTickAccumulator >= dynamicTickInterval)
            {
                fixedTickAccumulator -= dynamicTickInterval;
                realTime += dynamicTickInterval;

                if (TimeManager.Instance != null)
                    TimeManager.Instance.AdvanceTime(unscaledTick);

                float currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f;

                ScheduleUpdateJob scheduleJob = new ScheduleUpdateJob
                {
                    agents = agents,
                    homeNearestWaypoint = nativeHomeNearest,
                    workNearestWaypoint = nativeWorkNearest,
                    commercialNearestWaypoint = nativeCommercialNearest,
                    homePositions = nativeHomePositions,
                    waypoints = waypoints,
                    currentHour = currentHour,
                    stuckTimeoutSimHours = stuckTimeoutSimHours,
                    waypointReachDistance = waypointReachDistance,
                    destinationOffsetRange = destinationOffsetRange,
                    groundY = groundY
                };
                JobHandle scheduleHandle = scheduleJob.Schedule(agents.Length, 64);

                WaypointAssignJob waypointJob = new WaypointAssignJob
                {
                    agents = agents,
                    waypoints = waypoints,
                    neighborData = neighborData,
                    neighborStart = neighborStart,
                    neighborCount = neighborCount,
                    currentSimTime = realTime,
                    realTime = realTime,
                    waypointReachDistance = waypointReachDistance,
                    currentHour = currentHour
                };
                JobHandle waypointHandle = waypointJob.Schedule(agents.Length, 64, scheduleHandle);
                
                waypointHandle.Complete();

                epicAccumulator += dynamicTickInterval;
                if (epicAccumulator >= epicTickInterval)
                {
                    // THE 100x SPEED TIME-DROP FIX
                    // Capture the exact amount of accumulated time before resetting
                    float epidemicDelta = epicAccumulator;
                    epicAccumulator = 0f;
                    
                    spatialGrid.grid.Clear();
                    
                    UpdateGridJob gridJob = new UpdateGridJob
                    {
                        agents = agents,
                        gridWriter = spatialGrid.grid.AsParallelWriter(),
                        cellSize = spatialGrid.cellSize,
                        gridOrigin = spatialGrid.gridOrigin,
                        gridWidth = spatialGrid.gridWidth,
                        gridHeight = spatialGrid.gridHeight
                    };
                    JobHandle gridHandle = gridJob.Schedule(agents.Length, 64);

                    EpidemicJob epidemicJob = new EpidemicJob
                    {
                        agentsIn = agents,
                        agentsOut = agentsBuffer,
                        grid = spatialGrid.grid,
                        
                        // Pass the exact delta time block so math scales perfectly
                        deltaTime = epidemicDelta, 
                        
                        timeMultiplier = 1f,
                        currentSimTime = realTime, 
                        
                        infectionRadius = infectionRadius,
                        transmissionRate = transmissionRate,
                        recoveryTime = recoveryTime,
                        mortalityRate = mortalityRate,
                        cellSize = spatialGrid.cellSize,
                        gridOrigin = spatialGrid.gridOrigin,
                        gridWidth = spatialGrid.gridWidth,
                        gridHeight = spatialGrid.gridHeight
                    };
                    JobHandle epidemicHandle = epidemicJob.Schedule(agents.Length, 64, gridHandle);
                    epidemicHandle.Complete();

                    agentsBuffer.CopyTo(agents);
                }
            }
        }

        if (AgentRenderer.Instance != null)
            AgentRenderer.Instance.UpdateRender(agents, realTime, agentGroundOffset);
    }

    float3 GetPersonalOffset(int agentIndex)
    {
        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(agentIndex * 7919));
        float range = destinationOffsetRange;
        return new float3(rng.NextFloat(-range, range), 0f, rng.NextFloat(-range, range));
    }

    int FindNearestWaypointIndex(float3 target)
    {
        float bestDist = float.MaxValue;
        int bestIdx = 0;
        for (int i = 0; i < waypoints.Length; i++)
        {
            float dist = math.distancesq(new float3(target.x, 0f, target.z), new float3(waypoints[i].x, 0f, waypoints[i].z));
            if (dist < bestDist) { bestDist = dist; bestIdx = i; }
        }
        return bestIdx;
    }

    void OnDestroy()
    {
        if (agents.IsCreated) agents.Dispose();
        if (agentsBuffer.IsCreated) agentsBuffer.Dispose();
        if (waypoints.IsCreated) waypoints.Dispose();
        if (neighborData.IsCreated) neighborData.Dispose();
        if (neighborStart.IsCreated) neighborStart.Dispose();
        if (neighborCount.IsCreated) neighborCount.Dispose();
        
        if (nativeHomeNearest.IsCreated) nativeHomeNearest.Dispose();
        if (nativeWorkNearest.IsCreated) nativeWorkNearest.Dispose();
        if (nativeCommercialNearest.IsCreated) nativeCommercialNearest.Dispose();
        if (nativeHomePositions.IsCreated) nativeHomePositions.Dispose();
        
        spatialGrid?.Dispose();
    }

    public NativeArray<SimulationAgent> GetAgents() => agents;
    public bool IsInitialized() => initialized;
    public float GetSimTime() => realTime;
}