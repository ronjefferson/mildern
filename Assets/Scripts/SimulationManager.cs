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

    [Header("Rendering")]
    public float agentGroundOffset = 0f;

    [Header("Waypoints")]
    public WaypointData waypointData;

    [Header("Buildings")]
    public BuildingBoundsData buildingBoundsData;

    [Header("Grid")]
    public float cellSize = 10f;

    private NativeArray<SimulationAgent> agents;
    private NativeArray<SimulationAgent> agentsBuffer;
    private NativeArray<float3> waypoints;
    private NativeArray<float3> buildingCenters;
    private NativeArray<float3> buildingSizes;
    private SpatialGrid spatialGrid;

    private bool initialized = false;

    private float3[] homePositions;
    private float3[] workPositions;
    private float3[] commercialPositions;

    private int[] homeNearestWaypoint;
    private int[] workNearestWaypoint;
    private int[] commercialNearestWaypoint;

    private const float groundY = 0f;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        Invoke("Initialize", 1f);
    }

    void Initialize()
    {
        if (BuildingManager.Instance == null) { Debug.LogError("No BuildingManager!"); return; }
        if (waypointData == null || waypointData.waypoints == null || waypointData.waypoints.Length == 0) { Debug.LogError("No WaypointData!"); return; }
        if (buildingBoundsData == null || buildingBoundsData.centers == null || buildingBoundsData.centers.Length == 0) { Debug.LogError("No BuildingBoundsData!"); return; }

        List<Building> homes = BuildingManager.Instance.GetResidential();
        List<Building> works = BuildingManager.Instance.GetWorkplace();
        List<Building> commercials = BuildingManager.Instance.GetCommercial();

        if (homes.Count == 0) { Debug.LogError("No residential buildings!"); return; }

        homePositions = new float3[homes.Count];
        for (int i = 0; i < homes.Count; i++)
            homePositions[i] = new float3(homes[i].GetPosition().x, groundY, homes[i].GetPosition().z);

        workPositions = new float3[Mathf.Max(works.Count, 1)];
        for (int i = 0; i < works.Count; i++)
            workPositions[i] = new float3(works[i].GetPosition().x, groundY, works[i].GetPosition().z);

        commercialPositions = new float3[Mathf.Max(commercials.Count, 1)];
        for (int i = 0; i < commercials.Count; i++)
            commercialPositions[i] = new float3(commercials[i].GetPosition().x, groundY, commercials[i].GetPosition().z);

        waypoints = new NativeArray<float3>(waypointData.waypoints.Length, Allocator.Persistent);
        for (int i = 0; i < waypointData.waypoints.Length; i++)
            waypoints[i] = new float3(waypointData.waypoints[i].x, groundY, waypointData.waypoints[i].z);

        buildingCenters = new NativeArray<float3>(buildingBoundsData.centers.Length, Allocator.Persistent);
        buildingSizes = new NativeArray<float3>(buildingBoundsData.sizes.Length, Allocator.Persistent);
        for (int i = 0; i < buildingBoundsData.centers.Length; i++)
        {
            buildingCenters[i] = new float3(buildingBoundsData.centers[i].x, 0f, buildingBoundsData.centers[i].z);
            buildingSizes[i] = new float3(buildingBoundsData.sizes[i].x, 0f, buildingBoundsData.sizes[i].z);
        }

        homeNearestWaypoint = new int[homePositions.Length];
        for (int i = 0; i < homePositions.Length; i++)
            homeNearestWaypoint[i] = FindNearestWaypointIndex(homePositions[i]);

        workNearestWaypoint = new int[workPositions.Length];
        for (int i = 0; i < workPositions.Length; i++)
            workNearestWaypoint[i] = FindNearestWaypointIndex(workPositions[i]);

        commercialNearestWaypoint = new int[commercialPositions.Length];
        for (int i = 0; i < commercialPositions.Length; i++)
            commercialNearestWaypoint[i] = FindNearestWaypointIndex(commercialPositions[i]);

        float3 min = homePositions[0];
        float3 max = homePositions[0];
        foreach (float3 pos in homePositions) { min = math.min(min, pos); max = math.max(max, pos); }
        foreach (float3 pos in workPositions) { min = math.min(min, pos); max = math.max(max, pos); }
        foreach (float3 pos in commercialPositions) { min = math.min(min, pos); max = math.max(max, pos); }

        float3 origin = min - new float3(100f, 0f, 100f);
        int gridW = (int)((max.x - min.x + 200f) / cellSize) + 1;
        int gridH = (int)((max.z - min.z + 200f) / cellSize) + 1;
        spatialGrid = new SpatialGrid(cellSize, origin, gridW, gridH);

        agents = new NativeArray<SimulationAgent>(populationSize, Allocator.Persistent);
        agentsBuffer = new NativeArray<SimulationAgent>(populationSize, Allocator.Persistent);

        for (int i = 0; i < populationSize; i++)
        {
            int homeIdx = i % homePositions.Length;
            int workIdx = i % workPositions.Length;
            int commercialIdx = i % commercialPositions.Length;
            int startWaypoint = Random.Range(0, waypoints.Length);
            float3 homePos = homePositions[homeIdx];
            float3 spawnPos = new float3(homePos.x + Random.Range(-10f, 10f), groundY, homePos.z + Random.Range(-10f, 10f));

            agents[i] = new SimulationAgent
            {
                position = spawnPos,
                previousPosition = spawnPos,
                velocity = float3.zero,
                targetPosition = waypoints[startWaypoint],
                healthState = i < initialInfected ? HealthState.Infected : HealthState.Susceptible,
                scheduleState = AgentScheduleState.Home,
                infectionTimer = 0f,
                recoveryTimer = 0f,
                speed = Random.Range(3f, 6f),
                homeID = homeIdx,
                workID = workIdx,
                commercialID = commercialIdx,
                currentWaypointIndex = startWaypoint,
                workStartHour = Random.Range(7f, 9f),
                workEndHour = Random.Range(16f, 19f),
                returnHomeHour = Random.Range(18f, 22f),
                complianceLevel = Random.Range(0f, 1f),
                isWeekendWorker = Random.value < 0.2f,
                isActive = true,
                isInsideBuilding = true
            };
        }

        initialized = true;
        Debug.Log($"Initialized {populationSize} agents, {waypoints.Length} waypoints, {buildingCenters.Length} buildings");
    }

    void Update()
    {
        if (!initialized) return;

        float multiplier = TimeManager.Instance != null ? TimeManager.Instance.timeMultiplier : 1f;
        if (multiplier == 0) return;

        float simDelta = Time.deltaTime * multiplier;

        float currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f;
        bool isWeekend = TimeManager.Instance != null && TimeManager.Instance.IsWeekend();

        if (TimeManager.Instance != null)
            TimeManager.Instance.AdvanceTime(simDelta);

        currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f;
        isWeekend = TimeManager.Instance != null && TimeManager.Instance.IsWeekend();

        UpdateSchedules(currentHour, isWeekend);

        MovementJob movementJob = new MovementJob
        {
            agents = agents,
            waypoints = waypoints,
            buildingCenters = buildingCenters,
            buildingSizes = buildingSizes,
            stepSize = simDelta,
            waypointReachDistance = waypointReachDistance
        };
        JobHandle movementHandle = movementJob.Schedule(agents.Length, 64);

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
        JobHandle gridHandle = gridJob.Schedule(agents.Length, 64, movementHandle);

        EpidemicJob epidemicJob = new EpidemicJob
        {
            agentsIn = agents,
            agentsOut = agentsBuffer,
            grid = spatialGrid.grid,
            deltaTime = simDelta,
            timeMultiplier = 1f,
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

        if (AgentRenderer.Instance != null)
            AgentRenderer.Instance.UpdateRender(agents, 1f, agentGroundOffset);
    }

    void UpdateSchedules(float currentHour, bool isWeekend)
    {
        for (int i = 0; i < agents.Length; i++)
        {
            SimulationAgent agent = agents[i];
            if (!agent.isActive) continue;

            switch (agent.scheduleState)
            {
                case AgentScheduleState.Home:
                    bool shouldWork = !isWeekend || agent.isWeekendWorker;
                    if (shouldWork && currentHour >= agent.workStartHour)
                    {
                        agent.scheduleState = AgentScheduleState.Commuting;
                        agent.targetPosition = waypoints[workNearestWaypoint[agent.workID]];
                        agent.isInsideBuilding = false;
                    }
                    break;

                case AgentScheduleState.Commuting:
                    float distToWork = math.distance(
                        new float3(agent.position.x, 0f, agent.position.z),
                        new float3(workPositions[agent.workID].x, 0f, workPositions[agent.workID].z)
                    );
                    if (distToWork < 10f)
                    {
                        agent.scheduleState = AgentScheduleState.AtWork;
                        agent.isInsideBuilding = true;
                    }
                    else
                    {
                        float distToWaypoint = math.distance(
                            new float3(agent.position.x, 0f, agent.position.z),
                            new float3(agent.targetPosition.x, 0f, agent.targetPosition.z)
                        );
                        if (distToWaypoint < waypointReachDistance)
                            agent.targetPosition = waypoints[workNearestWaypoint[agent.workID]];
                    }
                    break;

                case AgentScheduleState.AtWork:
                    if (currentHour >= agent.workEndHour)
                    {
                        agent.scheduleState = AgentScheduleState.Returning;
                        agent.targetPosition = waypoints[homeNearestWaypoint[agent.homeID]];
                        agent.isInsideBuilding = false;
                    }
                    break;

                case AgentScheduleState.Returning:
                    float distToHome = math.distance(
                        new float3(agent.position.x, 0f, agent.position.z),
                        new float3(homePositions[agent.homeID].x, 0f, homePositions[agent.homeID].z)
                    );
                    if (distToHome < 10f)
                    {
                        agent.scheduleState = AgentScheduleState.Home;
                        agent.isInsideBuilding = true;
                        agent.position = new float3(homePositions[agent.homeID].x, groundY, homePositions[agent.homeID].z);
                        agent.previousPosition = agent.position;
                    }
                    else
                    {
                        float distToWaypoint = math.distance(
                            new float3(agent.position.x, 0f, agent.position.z),
                            new float3(agent.targetPosition.x, 0f, agent.targetPosition.z)
                        );
                        if (distToWaypoint < waypointReachDistance)
                            agent.targetPosition = waypoints[homeNearestWaypoint[agent.homeID]];
                    }
                    break;

                case AgentScheduleState.Leisure:
                    if (currentHour >= agent.returnHomeHour)
                    {
                        agent.scheduleState = AgentScheduleState.Returning;
                        agent.targetPosition = waypoints[homeNearestWaypoint[agent.homeID]];
                        agent.isInsideBuilding = false;
                    }
                    break;
            }

            agents[i] = agent;
        }
    }

    int FindNearestWaypointIndex(float3 target)
    {
        float bestDist = float.MaxValue;
        int bestIdx = 0;
        for (int i = 0; i < waypoints.Length; i++)
        {
            float dist = math.distancesq(
                new float3(target.x, 0f, target.z),
                new float3(waypoints[i].x, 0f, waypoints[i].z)
            );
            if (dist < bestDist) { bestDist = dist; bestIdx = i; }
        }
        return bestIdx;
    }

    void OnDestroy()
    {
        if (agents.IsCreated) agents.Dispose();
        if (agentsBuffer.IsCreated) agentsBuffer.Dispose();
        if (waypoints.IsCreated) waypoints.Dispose();
        if (buildingCenters.IsCreated) buildingCenters.Dispose();
        if (buildingSizes.IsCreated) buildingSizes.Dispose();
        spatialGrid?.Dispose();
    }

    public NativeArray<SimulationAgent> GetAgents() => agents;
    public bool IsInitialized() => initialized;
}