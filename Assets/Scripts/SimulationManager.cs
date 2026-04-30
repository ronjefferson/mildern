using UnityEngine;
using UnityEngine.UIElements;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using SFB;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

public struct HistoricalAgentState
{
    public float3 position;
    public float3 targetPosition;
    public AgentScheduleState scheduleState;
    public HealthState healthState;
    public int activeStrainID;
    public int protectedStrainID;
    public float immunityTimer;
    public uint historicalStrainMask;
}

public class SimulationManager : MonoBehaviour
{
    public static SimulationManager Instance;

    private struct TooltipData
    {
        public string purpose;
        public string input;
        public string range;

        public TooltipData(string p, string i, string r)
        {
            purpose = p;
            input = i;
            range = r;
        }
    }

    private Dictionary<string, TooltipData> tooltips;
    private VisualElement tooltipWindow;
    private Label ttTitle, ttPurpose, ttInput, ttRange;

    [Header("UI Icons (Texture2D)")]
    public Texture2D playIcon;
    public Texture2D pauseIcon;
    public Texture2D fastForwardIcon;

    [Header("UI Document")]
    public UIDocument dashboardDocument;
    private VisualElement setupContainer, runtimeContainer;
    private TextField popInput, infectedInput, radiusInput, transRateInput, recoveryInput, mortalityInput;

    private FloatField immDurField, histRecField, hospTransField;
    private IntegerField bedsField;
    private Toggle commToggle;
    private Slider natImmSlider, histImmSlider, lockAbidSlider;

    private Button startSimButton, playButton, fastButton, resetButton, exportBtn, loadSetupBtn;

    private Button tabSetupBtn, tabRuntimeBtn;
    private Toggle colorToggle, socialDistanceToggle, lockdownToggle;

    private EpidemicLineGraph lineGraph;
    private EpidemicBarGraph barGraph;
    private ActiveCasesGraph activeCasesGraph;
    private SimulationStatsDashboard simulationStats;
    private VirusVaccineControls virusVaccineControls;

    private Label headerDayLabel, headerTimeLabel, headerStatusLabel, headerSpeedLabel;
    private Label runtimeControlsTitle;
    private VisualElement branchControlsRow;
    private Button commitAttemptBtn, discardAttemptBtn;

    private LoadingOverlay loadingOverlay;
    private SimSaveData pendingImportData = null;

    [Header("Building UI Elements")]
    private VisualElement buildingPopup;
    private Label popupTitle;
    private Button resBtn, comBtn, workBtn, healthBtn;
    private Building currentPopupBuilding;

    private Slider resSlider, comSlider, workSlider, healthSlider;
    private FloatField resField, comField, workField, healthField;
    private bool isBalancingRatios = false;

    private bool isPaused = true;
    private bool isProcessingData = false;
    private CancellationTokenSource asyncTaskToken;

    private float[] fastSpeeds = { 1f, 10f, 100f };
    private int currentFastIndex = 0;

    private Dictionary<int, SimulationAgent[]> timelineHistory = new Dictionary<int, SimulationAgent[]>();
    private Dictionary<int, SimulationAgent[]> baselineHistory = new Dictionary<int, SimulationAgent[]>();

    private int furthestRecordedDay = 0;
    private int currentViewDay = 0;
    private bool isExploringPast = false;
    private bool hasGhostBaseline = false;

    [Header("Parameters")]
    public int populationSize = 10000;
    public int initialInfected = 10;
    public float infectionRadius = 3f;
    public float transmissionRate = 0.3f;
    public float recoveryTime = 14f;
    public float mortalityRate = 0.02f;
    public float minIncubationDays = 3f;
    public float maxIncubationDays = 6f;

    [Header("Immunity Rules")]
    public float naturalImmunityEfficacy = 1.0f;
    public float immunityDurationDays = 90f;
    public float historicalImmunityEfficacy = 0.60f;
    public float historicalRecoveryMultiplier = 2.0f;

    [Header("Building Modifiers")]
    public float hospitalTransmissionMultiplier = 0.1f;

    [Header("Global State Trackers")]
    public int baseStartingStrainID = 1;
    public float currentVaccineEfficacy = 0.0f;
    public float currentVaccineAbidance = 0.0f;
    public int currentDeployedVaccineStrain = -1;
    public int totalVaccinesAvailable = 0;
    public bool distributeToCommercial = true;
    public int hospitalBedsPerFacility = 50;
    public int absoluteTickCounter = 0;

    private int[] hospitalInventory;
    private int[] commercialInventory;

    private float baseTransmissionRate;
    private float baseInfectionRadius;

    public bool isLockdown = false;
    public float lockdownAbidance = 0.85f;

    public float realSecondsPerInGameDay = 60f;
    public float agentSpeed = 5f;
    public float waypointReachDistance = 5f;
    public float destinationOffsetRange = 8f;
    public float stuckTimeoutSimHours = 8f;
    public float agentGroundOffset = 0f;

    [Header("Editor Debug Settings")]
    public bool showWaypointsGizmo = false;
    public bool showSpatialGridGizmo = false;

    [Header("Diagnostics")]
    public NativeArray<int> distanceChecksTracker;
    public long TotalDistanceChecksThisTick = 0;

    [Header("References")]
    public WaypointData waypointData;
    public float cellSize = 10f;
    private NativeArray<SimulationAgent> agents, agentsBuffer;
    private NativeArray<float3> waypoints;
    private NativeArray<int> neighborData, neighborStart, neighborCount;
    private NativeArray<int> nativeHomeNearest, nativeWorkNearest, nativeCommercialNearest, nativeHospitalNearest;
    private NativeArray<float3> nativeHomePositions, nativeHospitalPositions, nativeCommercialPositions;
    private SpatialGrid spatialGrid;
    private NativeParallelMultiHashMap<int, int> indoorMap;

    private bool initialized = false;
    private float3[] homePositions, workPositions, commercialPositions, hospitalPositions;
    private int[] homeNearestWaypoint, workNearestWaypoint, commercialNearestWaypoint, hospitalNearestWaypoint;

    private const float groundY = 0f;
    private float realTime = 0f;
    private float epicAccumulator = 0f;
    private const float epicTickInterval = 1f;
    private float fixedTickAccumulator = 0f;
    private float fixedTickInterval = 0.05f;

    void Awake()
    {
        Instance = this;
        InitializeTooltips();
        asyncTaskToken = new CancellationTokenSource();
        Application.wantsToQuit += OnWantsToQuit;
    }

    private bool OnWantsToQuit()
    {
        return !isProcessingData;
    }

    void Start()
    {
        SetupUI();
    }

    void Update()
    {
        if (!initialized) return;

        float multiplier = isPaused ? 0f : fastSpeeds[currentFastIndex];

        if (multiplier > 0f)
        {
            float dynamicTick = math.max(fixedTickInterval * multiplier, 0.01f);
            fixedTickAccumulator += Time.deltaTime * multiplier;

            while (fixedTickAccumulator >= dynamicTick)
            {
                fixedTickAccumulator -= dynamicTick;
                realTime += dynamicTick;

                if (TimeManager.Instance != null)
                    TimeManager.Instance.AdvanceTime(dynamicTick / multiplier);

                int currentDay = Mathf.FloorToInt(realTime / realSecondsPerInGameDay) + 1;

                if (currentDay > furthestRecordedDay && !isExploringPast)
                {
                    SaveHistoricalSnapshot(currentDay);
                    furthestRecordedDay = currentDay;
                    if (lineGraph != null)
                        lineGraph.maxScrubableDay = furthestRecordedDay;
                }

                for (int i = 0; i < agents.Length; i++)
                {
                    var agent = agents[i];
                    if (!agent.isSeekingVaccine)
                    {
                        agents[i] = agent;
                        continue;
                    }

                    bool arrivedAtHospital = !agent.isVaccineClinicCommercial
                        && agent.scheduleState == AgentScheduleState.AtHospital
                        && agent.isAtHospital;

                    bool arrivedAtCommercial = agent.isVaccineClinicCommercial
                        && agent.scheduleState == AgentScheduleState.AtCommercial
                        && agent.isInsideBuilding
                        && agent.commutingStartTime == -9999f;

                    if (arrivedAtHospital || arrivedAtCommercial)
                    {
                        agent.vaccineWaitTimer -= dynamicTick;

                        if (agent.vaccineWaitTimer <= 0f)
                        {
                            bool isEligible = agent.healthState == HealthState.Susceptible
                                || agent.healthState == HealthState.Recovered
                                || agent.healthState == HealthState.Vaccinated;

                            if (isEligible)
                            {
                                bool gotShot = false;

                                if (!agent.isVaccineClinicCommercial && hospitalInventory[agent.vaccineClinicID] > 0)
                                {
                                    hospitalInventory[agent.vaccineClinicID]--;
                                    gotShot = true;
                                }
                                else if (agent.isVaccineClinicCommercial && commercialInventory[agent.vaccineClinicID] > 0)
                                {
                                    commercialInventory[agent.vaccineClinicID]--;
                                    gotShot = true;
                                }

                                if (gotShot && Random.value <= currentVaccineEfficacy)
                                {
                                    agent.healthState = HealthState.Vaccinated;
                                    agent.protectedStrainID = currentDeployedVaccineStrain;
                                    agent.immunityTimer = Mathf.Max(1f, immunityDurationDays) * realSecondsPerInGameDay;
                                }
                            }

                            agent.isSeekingVaccine = false;
                            agent.isAtHospital = false;
                        }
                    }

                    agents[i] = agent;
                }

                ScheduleUpdateJob scheduleJob = new ScheduleUpdateJob
                {
                    agents = agents,
                    currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f,
                    isLockdown = this.isLockdown,
                    lockdownAbidanceThreshold = this.lockdownAbidance,
                    homeNearestWaypoint = nativeHomeNearest,
                    workNearestWaypoint = nativeWorkNearest,
                    commercialNearestWaypoint = nativeCommercialNearest,
                    hospitalNearestWaypoint = nativeHospitalNearest,
                    homePositions = nativeHomePositions,
                    hospitalPositions = nativeHospitalPositions,
                    commercialPositions = nativeCommercialPositions,
                    waypoints = waypoints,
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
                    currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f
                };
                JobHandle waypointHandle = waypointJob.Schedule(agents.Length, 64, scheduleHandle);
                waypointHandle.Complete();

                epicAccumulator += dynamicTick;

                if (epicAccumulator >= epicTickInterval)
                {
                    absoluteTickCounter++;
                    float epidemicDelta = epicAccumulator;
                    epicAccumulator = 0f;

                    spatialGrid.grid.Clear();
                    indoorMap.Clear();

                    if (currentDeployedVaccineStrain != -1)
                    {
                        for (int i = 0; i < agents.Length; i++)
                        {
                            var agent = agents[i];
                            if (agent.isSeekingVaccine || agent.healthState == HealthState.Dead)
                                continue;
                            if (agent.protectedStrainID == currentDeployedVaccineStrain)
                                continue;

                            bool isEligible = agent.healthState == HealthState.Susceptible
                                || agent.healthState == HealthState.Recovered
                                || agent.healthState == HealthState.Vaccinated;

                            if (!isEligible || agent.complianceLevel > currentVaccineAbidance)
                                continue;

                            bool wantsCommercial = distributeToCommercial && Random.value > 0.5f;
                            int primaryClinic = wantsCommercial ? agent.commercialID : agent.healthcareID;
                            int primaryStock = wantsCommercial ? commercialInventory[primaryClinic] : hospitalInventory[primaryClinic];

                            if (primaryStock > 0)
                            {
                                agent.isSeekingVaccine = true;
                                agent.isVaccineClinicCommercial = wantsCommercial;
                                agent.vaccineClinicID = primaryClinic;
                                agent.vaccineWaitTimer = Random.Range(1f, 3f) * (realSecondsPerInGameDay / 24f);
                            }
                            else
                            {
                                bool fallbackCommercial = !wantsCommercial;
                                if (!fallbackCommercial || distributeToCommercial)
                                {
                                    int fallbackClinic = fallbackCommercial ? agent.commercialID : agent.healthcareID;
                                    int fallbackStock = fallbackCommercial ? commercialInventory[fallbackClinic] : hospitalInventory[fallbackClinic];

                                    if (fallbackStock > 0)
                                    {
                                        agent.isSeekingVaccine = true;
                                        agent.isVaccineClinicCommercial = fallbackCommercial;
                                        agent.vaccineClinicID = fallbackClinic;
                                        agent.vaccineWaitTimer = Random.Range(1f, 3f) * (realSecondsPerInGameDay / 24f);
                                    }
                                }
                            }

                            agents[i] = agent;
                        }
                    }

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

                    IndoorMappingJob indoorJob = new IndoorMappingJob
                    {
                        agents = agents,
                        indoorMapWriter = indoorMap.AsParallelWriter()
                    };
                    JobHandle indoorHandle = indoorJob.Schedule(agents.Length, 64, gridHandle);

                    EpidemicJob epidemicJob = new EpidemicJob
                    {
                        agentsIn = agents,
                        agentsOut = agentsBuffer,
                        grid = spatialGrid.grid,
                        indoorMap = indoorMap,
                        distanceChecksTracker = this.distanceChecksTracker,
                        deltaTime = epidemicDelta,
                        absoluteTickCounter = this.absoluteTickCounter,
                        realSecondsPerInGameDay = realSecondsPerInGameDay,
                        infectionRadius = infectionRadius,
                        transmissionRate = transmissionRate,
                        recoveryTime = recoveryTime,
                        mortalityRate = mortalityRate,
                        minIncubationDays = this.minIncubationDays,
                        maxIncubationDays = this.maxIncubationDays,
                        currentVaccineEfficacy = this.currentVaccineEfficacy,
                        naturalImmunityEfficacy = this.naturalImmunityEfficacy,
                        immunityDurationDays = this.immunityDurationDays,
                        evasionPenaltyPerLevel = 0.20f,
                        historicalImmunityEfficacy = this.historicalImmunityEfficacy,
                        historicalRecoveryMultiplier = this.historicalRecoveryMultiplier,
                        hospitalTransmissionMultiplier = this.hospitalTransmissionMultiplier,
                        cellSize = spatialGrid.cellSize,
                        gridOrigin = spatialGrid.gridOrigin,
                        gridWidth = spatialGrid.gridWidth,
                        gridHeight = spatialGrid.gridHeight
                    };
                    JobHandle epidemicHandle = epidemicJob.Schedule(agents.Length, 64, indoorHandle);
                    epidemicHandle.Complete();

                    agentsBuffer.CopyTo(agents);

                    long currentTickChecks = 0;
                    for (int k = 0; k < distanceChecksTracker.Length; k++)
                        currentTickChecks += distanceChecksTracker[k];
                    TotalDistanceChecksThisTick = currentTickChecks;

                    int alive = 0, dead = 0, infected = 0, recovered = 0,
                        susceptible = 0, exposed = 0, vaccinated = 0, hospitalized = 0;

                    var activeStrainCounts = new Dictionary<int, int>();
                    var activeVaccineCounts = new Dictionary<int, int>();

                    for (int i = 0; i < agents.Length; i++)
                    {
                        var ag = agents[i];

                        if (ag.healthState == HealthState.Dead)
                        {
                            dead++;
                            continue;
                        }

                        alive++;

                        switch (ag.healthState)
                        {
                            case HealthState.Infected:
                                infected++;
                                if (ag.activeStrainID != -1)
                                {
                                    if (!activeStrainCounts.ContainsKey(ag.activeStrainID))
                                        activeStrainCounts[ag.activeStrainID] = 0;
                                    activeStrainCounts[ag.activeStrainID]++;
                                }
                                break;

                            case HealthState.Exposed:
                                exposed++;
                                break;

                            case HealthState.Recovered:
                                recovered++;
                                if (ag.protectedStrainID != -1)
                                {
                                    if (!activeVaccineCounts.ContainsKey(ag.protectedStrainID))
                                        activeVaccineCounts[ag.protectedStrainID] = 0;
                                    activeVaccineCounts[ag.protectedStrainID]++;
                                }
                                break;

                            case HealthState.Vaccinated:
                                vaccinated++;
                                if (ag.protectedStrainID != -1)
                                {
                                    if (!activeVaccineCounts.ContainsKey(ag.protectedStrainID))
                                        activeVaccineCounts[ag.protectedStrainID] = 0;
                                    activeVaccineCounts[ag.protectedStrainID]++;
                                }
                                break;

                            case HealthState.Susceptible:
                                susceptible++;
                                break;
                        }

                        if (ag.scheduleState == AgentScheduleState.AtHospital)
                            hospitalized++;
                    }

                    int remainingVaccines = 0;
                    if (hospitalInventory != null)
                    {
                        foreach (int v in hospitalInventory) remainingVaccines += v;
                        if (distributeToCommercial)
                            foreach (int v in commercialInventory) remainingVaccines += v;
                    }

                    float exactDay = realTime / realSecondsPerInGameDay;

                    if (lineGraph != null)        lineGraph.AddData(susceptible, exposed, infected, recovered, vaccinated, dead, exactDay);
                    if (barGraph != null)          barGraph.UpdateData(susceptible, exposed, infected, recovered, vaccinated, dead);
                    if (activeCasesGraph != null)  activeCasesGraph.AddData(infected, exactDay);
                    if (virusVaccineControls != null)
                    {
                        virusVaccineControls.UpdateActiveStrains(activeStrainCounts);
                        virusVaccineControls.UpdateActiveVaccines(activeVaccineCounts);
                    }

                    if (headerDayLabel != null)
                        headerDayLabel.text = $"DAY: {currentDay}";

                    if (headerTimeLabel != null)
                    {
                        float currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 0f;
                        int hh = Mathf.FloorToInt(currentHour);
                        int mm = Mathf.FloorToInt((currentHour - hh) * 60f);
                        headerTimeLabel.text = $"TIME: {hh:00}:{mm:00}";
                    }

                    if (simulationStats != null)
                    {
                        int totalBeds = hospitalPositions != null ? hospitalPositions.Length * hospitalBedsPerFacility : 0;
                        simulationStats.UpdateData(alive, dead, susceptible, exposed, infected, recovered, vaccinated, hospitalized, totalBeds, remainingVaccines, totalVaccinesAvailable);
                    }
                }
            }
        }

        if (AgentRenderer.Instance != null)
            AgentRenderer.Instance.UpdateRender(agents, realTime, agentGroundOffset);
    }

    void OnDestroy()
    {
        Application.wantsToQuit -= OnWantsToQuit;
        if (asyncTaskToken != null)
        {
            asyncTaskToken.Cancel();
            asyncTaskToken.Dispose();
        }
        DisposeArrays();
    }

    private void InitializeTooltips()
    {
        tooltips = new Dictionary<string, TooltipData>
        {
            { "Population Size",         new TooltipData("The total number of agents spawned in the city.", "Integer (e.g., 10000)", "100 - 30,000 (Warning: High numbers require strong CPU)") },
            { "Initial Infected",        new TooltipData("The number of 'Patient Zeros' at Day 0.", "Integer (e.g., 10)", "1 to Population Size") },
            { "Infection Radius",        new TooltipData("The physical distance a virus can jump between agents.", "Float (1.0 = 1 meter)", "0.1 (Contact) to 5.0 (Airborne)") },
            { "Transmission Rate",       new TooltipData("The probability of infection per tick when inside the radius.", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Recovery Time",           new TooltipData("How many in-game days an agent remains infectious before recovering or dying.", "Float (Days)", "1.0 to 30.0+") },
            { "Mortality Rate",          new TooltipData("The percentage of infected agents that will not survive the illness.", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Immunity Duration (Days)",new TooltipData("How many days an agent is protected after vaccination.", "Float (Days)", "1.0 to 365.0+") },
            { "Natural Immunity Efficacy",   new TooltipData("The base protection an agent gets after recovering from a virus naturally.", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Reinfection Resistance",  new TooltipData("The baseline protection against mutated strains if the agent has a historical immunity record.", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Lockdown Abidance",       new TooltipData("Percentage of population obeying lockdown rules.", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Reinfection Recovery Boost", new TooltipData("Speed multiplier for recovering from reinfections.", "Float (Multiplier)", "1.0 (Normal) to 5.0 (Fast)") },
            { "Hospital Trans. Multiplier", new TooltipData("Adjusts transmission risk inside hospital buildings.", "Float (Multiplier)", "0.0 (Safe) to 1.0 (Normal)") },
            { "Beds Per Hospital",       new TooltipData("Maximum capacity of patients per healthcare facility.", "Integer", "10 to 500+") },
            { "Vaccines at Commercial",  new TooltipData("Allows pharmacies/commercial buildings to distribute vaccines.", "Toggle (True/False)", "N/A") },

            { "Reset",                   new TooltipData("Deletes the current simulation data, clears the RAM, and returns to the Setup tab.", "Action Button", "N/A") },
            { "Play / Pause",            new TooltipData("Freezes or resumes the simulation mathematical calculations. You can still move the camera while paused.", "Action Button", "N/A") },
            { "Fast Forward",            new TooltipData("Cycles through the simulation calculation speeds.", "Action Button", "1x, 10x, 100x") },
            { "Export",                  new TooltipData("Saves the current timeline and simulation parameters to your hard drive so you can load it later.", "Action Button", "N/A") },
            { "Show Building Colors",    new TooltipData("Visually color-codes the 3D buildings based on their type (Residential, Commercial, Healthcare).", "Checkbox (True/False)", "N/A") },
            { "Toggle Social Distancing",new TooltipData("Instantly forces all agents to attempt to stay further apart, effectively reducing the Infection Radius and Transmission Rate by a set multiplier.", "Checkbox (True/False)", "N/A") },
            { "Toggle Lockdown",         new TooltipData("Triggers a city-wide mandate. A percentage of agents (based on Lockdown Abidance) will immediately return home and stay there.", "Checkbox (True/False)", "N/A") },

            { "New Strain Level",        new TooltipData("The classification ID of the newly mutated virus.", "Integer (e.g., 2)", "Must be higher than current strain") },
            { "Immunity Evasion",        new TooltipData("Chance for the mutated virus to bypass existing immunity (recovered or vaccinated).", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Infection Rate",          new TooltipData("The base transmission probability for this specific mutated strain.", "Float (Percentage)", "0.01 (1%) to 1.0 (100%)") },
            { "Incubation (Days)",       new TooltipData("Days an agent remains asymptomatic but infectious before showing symptoms/recovering.", "Float (Days)", "1.0 to 14.0+") },
            { "Trigger Mutation",        new TooltipData("Immediately unleashes the mutated strain into the existing infected population.", "Action Button", "N/A") },

            { "Vaccine Strain",          new TooltipData("The specific virus strain ID this new vaccine wave protects against.", "Integer", "Matches active strain ID") },
            { "Supply Doses",            new TooltipData("Total number of vaccines available for distribution across all clinics.", "Integer", "100 to Population Size") },
            { "Base Efficacy",           new TooltipData("The percentage chance this vaccine successfully grants immunity.", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Public Abidance",         new TooltipData("The percentage of the susceptible population willing to go get vaccinated.", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Deploy Wave",             new TooltipData("Immediately distributes the vaccine supply to hospitals and commercial pharmacies.", "Action Button", "N/A") },

            { "Residential Ratio",       new TooltipData("Percentage of buildings assigned as homes.", "Float (0.0 to 1.0)", "Total of all ratios cannot exceed 1.0") },
            { "Commercial Ratio",        new TooltipData("Percentage of buildings assigned as shops/pharmacies.", "Float (0.0 to 1.0)", "Total of all ratios cannot exceed 1.0") },
            { "Workplace Ratio",         new TooltipData("Percentage of buildings assigned as offices/factories.", "Float (0.0 to 1.0)", "Total of all ratios cannot exceed 1.0") },
            { "Healthcare Ratio",        new TooltipData("Percentage of buildings assigned as hospitals/clinics.", "Float (0.0 to 1.0)", "Total of all ratios cannot exceed 1.0") }
        };
    }

    public void Initialize()
    {
        if (BuildingManager.Instance == null || waypointData == null || waypointData.waypoints.Length == 0)
            return;

        List<Building> homes       = BuildingManager.Instance.GetResidential();
        List<Building> works       = BuildingManager.Instance.GetWorkplace();
        List<Building> commercials = BuildingManager.Instance.GetCommercial();
        List<Building> hospitals   = BuildingManager.Instance.GetHealthcare();

        if (hospitals.Count == 0) hospitals = commercials;
        if (homes.Count == 0) return;

        homePositions       = new float3[homes.Count];
        workPositions       = new float3[Mathf.Max(works.Count, 1)];
        commercialPositions = new float3[Mathf.Max(commercials.Count, 1)];
        hospitalPositions   = new float3[Mathf.Max(hospitals.Count, 1)];

        for (int i = 0; i < homes.Count; i++)       homePositions[i]       = new float3(homes[i].GetPosition().x,       groundY, homes[i].GetPosition().z);
        for (int i = 0; i < works.Count; i++)        workPositions[i]       = new float3(works[i].GetPosition().x,       groundY, works[i].GetPosition().z);
        for (int i = 0; i < commercials.Count; i++)  commercialPositions[i] = new float3(commercials[i].GetPosition().x, groundY, commercials[i].GetPosition().z);
        for (int i = 0; i < hospitals.Count; i++)    hospitalPositions[i]   = new float3(hospitals[i].GetPosition().x,   groundY, hospitals[i].GetPosition().z);

        hospitalInventory   = new int[hospitalPositions.Length];
        commercialInventory = new int[commercialPositions.Length];

        waypoints     = new NativeArray<float3>(waypointData.waypoints.Length, Allocator.Persistent);
        neighborData  = new NativeArray<int>(waypointData.neighborData,  Allocator.Persistent);
        neighborStart = new NativeArray<int>(waypointData.neighborStart, Allocator.Persistent);
        neighborCount = new NativeArray<int>(waypointData.neighborCount, Allocator.Persistent);

        for (int i = 0; i < waypointData.waypoints.Length; i++)
            waypoints[i] = new float3(waypointData.waypoints[i].x, groundY, waypointData.waypoints[i].z);

        homeNearestWaypoint       = new int[homePositions.Length];
        workNearestWaypoint       = new int[workPositions.Length];
        commercialNearestWaypoint = new int[commercialPositions.Length];
        hospitalNearestWaypoint   = new int[hospitalPositions.Length];

        for (int i = 0; i < homePositions.Length; i++)       homeNearestWaypoint[i]       = FindNearestWaypointIndex(homePositions[i]);
        for (int i = 0; i < workPositions.Length; i++)        workNearestWaypoint[i]       = FindNearestWaypointIndex(workPositions[i]);
        for (int i = 0; i < commercialPositions.Length; i++)  commercialNearestWaypoint[i] = FindNearestWaypointIndex(commercialPositions[i]);
        for (int i = 0; i < hospitalPositions.Length; i++)    hospitalNearestWaypoint[i]   = FindNearestWaypointIndex(hospitalPositions[i]);

        nativeHomeNearest       = new NativeArray<int>(homeNearestWaypoint,       Allocator.Persistent);
        nativeWorkNearest       = new NativeArray<int>(workNearestWaypoint,       Allocator.Persistent);
        nativeCommercialNearest = new NativeArray<int>(commercialNearestWaypoint, Allocator.Persistent);
        nativeHospitalNearest   = new NativeArray<int>(hospitalNearestWaypoint,   Allocator.Persistent);

        nativeHomePositions       = new NativeArray<float3>(homePositions,       Allocator.Persistent);
        nativeHospitalPositions   = new NativeArray<float3>(hospitalPositions,   Allocator.Persistent);
        nativeCommercialPositions = new NativeArray<float3>(commercialPositions, Allocator.Persistent);

        float3 min = homePositions[0], max = homePositions[0];
        foreach (float3 p in homePositions)  { min = math.min(min, p); max = math.max(max, p); }
        foreach (float3 p in workPositions)  { min = math.min(min, p); max = math.max(max, p); }

        spatialGrid = new SpatialGrid(
            cellSize,
            min - new float3(100f, 0f, 100f),
            (int)((max.x - min.x + 200f) / cellSize) + 1,
            (int)((max.z - min.z + 200f) / cellSize) + 1,
            populationSize
        );

        indoorMap      = new NativeParallelMultiHashMap<int, int>(populationSize, Allocator.Persistent);
        agents         = new NativeArray<SimulationAgent>(populationSize, Allocator.Persistent);
        agentsBuffer   = new NativeArray<SimulationAgent>(populationSize, Allocator.Persistent);
        distanceChecksTracker = new NativeArray<int>(populationSize, Allocator.Persistent);

        if (pendingImportData != null && timelineHistory != null && timelineHistory.ContainsKey(pendingImportData.savedDay))
        {
            agents.CopyFrom(timelineHistory[pendingImportData.savedDay]);
            realTime = pendingImportData.savedDay * realSecondsPerInGameDay;
            absoluteTickCounter = Mathf.FloorToInt(realTime / epicTickInterval);

            if (lineGraph != null)
            {
                lineGraph.ClearData();
                for (int d = 0; d <= pendingImportData.savedDay; d++)
                {
                    if (!timelineHistory.ContainsKey(d)) continue;
                    int hS = 0, hE = 0, hI = 0, hR = 0, hV = 0, hD = 0;
                    foreach (var ag in timelineHistory[d])
                    {
                        if      (ag.healthState == HealthState.Susceptible) hS++;
                        else if (ag.healthState == HealthState.Exposed)     hE++;
                        else if (ag.healthState == HealthState.Infected)    hI++;
                        else if (ag.healthState == HealthState.Recovered)   hR++;
                        else if (ag.healthState == HealthState.Vaccinated)  hV++;
                        else if (ag.healthState == HealthState.Dead)        hD++;
                    }
                    lineGraph.AddData(hS, hE, hI, hR, hV, hD, d);
                    if (activeCasesGraph != null) activeCasesGraph.AddData(hI, d);
                }
            }
        }
        else
        {
            for (int i = 0; i < populationSize; i++)
            {
                int hIdx = i % homePositions.Length;
                HealthState assignedState = i < initialInfected ? HealthState.Infected : HealthState.Susceptible;

                agents[i] = new SimulationAgent
                {
                    position                 = waypoints[homeNearestWaypoint[hIdx]],
                    moveStartPosition        = waypoints[homeNearestWaypoint[hIdx]],
                    moveEndPosition          = waypoints[homeNearestWaypoint[hIdx]],
                    targetPosition           = waypoints[homeNearestWaypoint[hIdx]],
                    personalOffset           = GetPersonalOffset(i),
                    healthState              = assignedState,
                    scheduleState            = AgentScheduleState.Home,
                    activeStrainID           = assignedState == HealthState.Infected   ? baseStartingStrainID : -1,
                    protectedStrainID        = assignedState == HealthState.Vaccinated ? baseStartingStrainID : -1,
                    immunityTimer            = 0f,
                    historicalStrainMask     = assignedState == HealthState.Infected ? (1u << baseStartingStrainID) : 0u,
                    isSeekingVaccine         = false,
                    vaccineClinicID          = -1,
                    isVaccineClinicCommercial = false,
                    vaccineWaitTimer         = 0f,
                    healthcareID             = i % hospitalPositions.Length,
                    speed                    = Random.Range(agentSpeed * 0.75f, agentSpeed * 1.25f),
                    homeID                   = hIdx,
                    workID                   = i % workPositions.Length,
                    commercialID             = i % commercialPositions.Length,
                    currentWaypointIndex     = homeNearestWaypoint[hIdx],
                    destinationWaypointIndex = homeNearestWaypoint[hIdx],
                    workStartHour            = Random.Range(7f, 9f),
                    workEndHour              = Random.Range(16f, 18f),
                    returnHomeHour           = Mathf.Max(Random.Range(18f, 22f), Random.Range(16f, 18f) + 1f),
                    commercialArrivalHour    = Random.Range(16f, 18f) + Random.Range(0.25f, 1.0f),
                    complianceLevel          = Random.Range(0f, 1f),
                    commutingStartTime       = -9999f,
                    isActive                 = true,
                    isInsideBuilding         = true,
                    visitsCommercial         = Random.value < 0.4f
                };
            }

            SaveHistoricalSnapshot(0);
        }

        pendingImportData = null;
        initialized = true;

        int startS = 0, startE = 0, startI = 0, startR = 0, startV = 0, startD = 0;
        for (int i = 0; i < agents.Length; i++)
        {
            if      (agents[i].healthState == HealthState.Susceptible) startS++;
            else if (agents[i].healthState == HealthState.Exposed)     startE++;
            else if (agents[i].healthState == HealthState.Infected)    startI++;
            else if (agents[i].healthState == HealthState.Recovered)   startR++;
            else if (agents[i].healthState == HealthState.Vaccinated)  startV++;
            else if (agents[i].healthState == HealthState.Dead)        startD++;
        }

        if (barGraph != null)
            barGraph.UpdateData(startS, startE, startI, startR, startV, startD);

        if (simulationStats != null)
        {
            int tBeds = hospitalPositions.Length * hospitalBedsPerFacility;
            simulationStats.UpdateData(populationSize - startD, startD, startS, startE, startI, startR, startV, 0, tBeds, 0, 0);
        }

        TogglePlayPause();
    }

    void SetupUI()
    {
        if (dashboardDocument == null || dashboardDocument.rootVisualElement == null) return;

        var root = dashboardDocument.rootVisualElement;

        CreateTooltipUI(root);

        setupContainer  = root.Q<VisualElement>("SetupContainer");
        runtimeContainer = root.Q<VisualElement>("RuntimeContainer");

        if (setupContainer != null)
        {
            var setupScroll = new ScrollView(ScrollViewMode.Vertical);
            setupScroll.style.flexGrow = 1;
            setupScroll.contentContainer.style.paddingRight = 10;
            StyleCustomScrollbar(setupScroll);
            var children = new List<VisualElement>(setupContainer.Children());
            foreach (var c in children) setupScroll.Add(c);
            setupContainer.Add(setupScroll);
        }

        tabSetupBtn   = root.Q<Button>("TabSetupButton");
        tabRuntimeBtn = root.Q<Button>("TabRuntimeButton");

        if (tabSetupBtn != null)
        {
            tabSetupBtn.clicked += () => SwitchTab(true);
            if (tabSetupBtn.parent != null) tabSetupBtn.parent.style.flexShrink = 0;
        }
        if (tabRuntimeBtn != null)
            tabRuntimeBtn.clicked += () => SwitchTab(false);

        popInput       = root.Q<TextField>("PopInput");
        infectedInput  = root.Q<TextField>("InfectedInput");
        radiusInput    = root.Q<TextField>("RadiusInput");
        transRateInput = root.Q<TextField>("TransRateInput");
        recoveryInput  = root.Q<TextField>("RecoveryInput");
        mortalityInput = root.Q<TextField>("MortalityInput");
        startSimButton = root.Q<Button>("StartSimButton");

        if (setupContainer != null)
        {
            var allTextFields = setupContainer.Query<TextField>().ToList();
            foreach (var field in allTextFields) field.AddToClassList("inspector-field");
        }

        if (popInput != null)       popInput.value       = populationSize.ToString();
        if (infectedInput != null)  infectedInput.value  = initialInfected.ToString();
        if (radiusInput != null)    radiusInput.value    = infectionRadius.ToString();
        if (transRateInput != null) transRateInput.value = transmissionRate.ToString();
        if (recoveryInput != null)  recoveryInput.value  = recoveryTime.ToString();
        if (mortalityInput != null) mortalityInput.value = mortalityRate.ToString();

        InjectInfoButtonToUXML(popInput,       "Population Size");
        InjectInfoButtonToUXML(infectedInput,  "Initial Infected");
        InjectInfoButtonToUXML(radiusInput,    "Infection Radius");
        InjectInfoButtonToUXML(transRateInput, "Transmission Rate");
        InjectInfoButtonToUXML(recoveryInput,  "Recovery Time");
        InjectInfoButtonToUXML(mortalityInput, "Mortality Rate");

        if (setupContainer != null)
        {
            var advancedBox = new VisualElement
            {
                style =
                {
                    marginTop = 15, paddingBottom = 10, marginBottom = 10,
                    borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f)
                }
            };

            immDurField = new FloatField("Immunity Duration (Days):") { value = immunityDurationDays };
            immDurField.AddToClassList("inspector-field");
            immDurField.RegisterValueChangedCallback(evt => immunityDurationDays = evt.newValue);
            advancedBox.Add(WrapWithInfoButton(immDurField, "Immunity Duration (Days)"));

            advancedBox.Add(CreateSliderRow("Natural Immunity Efficacy:", 0f, 1f, naturalImmunityEfficacy, out natImmSlider, val => naturalImmunityEfficacy = val));
            advancedBox.Add(CreateSliderRow("Reinfection Resistance:",    0f, 1f, historicalImmunityEfficacy, out histImmSlider, val => historicalImmunityEfficacy = val));
            advancedBox.Add(CreateSliderRow("Lockdown Abidance:",         0f, 1f, lockdownAbidance, out lockAbidSlider, val => lockdownAbidance = val));

            histRecField = new FloatField("Reinfection Recovery Boost:") { value = historicalRecoveryMultiplier };
            histRecField.AddToClassList("inspector-field");
            histRecField.RegisterValueChangedCallback(evt => historicalRecoveryMultiplier = evt.newValue);
            advancedBox.Add(WrapWithInfoButton(histRecField, "Reinfection Recovery Boost"));

            hospTransField = new FloatField("Hospital Trans. Multiplier:") { value = hospitalTransmissionMultiplier };
            hospTransField.AddToClassList("inspector-field");
            hospTransField.RegisterValueChangedCallback(evt => hospitalTransmissionMultiplier = evt.newValue);
            advancedBox.Add(WrapWithInfoButton(hospTransField, "Hospital Trans. Multiplier"));

            bedsField = new IntegerField("Beds Per Hospital:") { value = hospitalBedsPerFacility };
            bedsField.AddToClassList("inspector-field");
            bedsField.RegisterValueChangedCallback(evt => hospitalBedsPerFacility = evt.newValue);
            advancedBox.Add(WrapWithInfoButton(bedsField, "Beds Per Hospital"));

            commToggle = new Toggle("Vaccines at Commercial:") { value = distributeToCommercial };
            commToggle.AddToClassList("custom-unity-toggle");
            commToggle.RegisterValueChangedCallback(evt => distributeToCommercial = evt.newValue);
            advancedBox.Add(WrapWithInfoButton(commToggle, "Vaccines at Commercial"));

            if (startSimButton != null && startSimButton.parent != null)
                startSimButton.parent.Insert(startSimButton.parent.IndexOf(startSimButton), advancedBox);
            else
                setupContainer.Add(advancedBox);

            var buildingBox = new VisualElement
            {
                style =
                {
                    marginTop = 15, paddingBottom = 10, marginBottom = 10,
                    borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f)
                }
            };

            var bldgTitle = new Label("BUILDING GENERATION RATIOS")
            {
                style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 }
            };
            buildingBox.Add(bldgTitle);

            if (BuildingManager.Instance != null)
            {
                buildingBox.Add(CreateRatioRow("Residential Ratio:", BuildingManager.Instance.residentialRatio, out resSlider, out resField, "Residential Ratio"));
                buildingBox.Add(CreateRatioRow("Commercial Ratio:",  BuildingManager.Instance.commercialRatio,  out comSlider, out comField, "Commercial Ratio"));
                buildingBox.Add(CreateRatioRow("Workplace Ratio:",   BuildingManager.Instance.workplaceRatio,   out workSlider, out workField, "Workplace Ratio"));
                buildingBox.Add(CreateRatioRow("Healthcare Ratio:",  BuildingManager.Instance.healthcareRatio,  out healthSlider, out healthField, "Healthcare Ratio"));

                resSlider.RegisterValueChangedCallback(evt    => OnRatioSliderChanged(resSlider, evt.newValue));
                resField.RegisterValueChangedCallback(evt     => { resSlider.value = evt.newValue; });
                comSlider.RegisterValueChangedCallback(evt    => OnRatioSliderChanged(comSlider, evt.newValue));
                comField.RegisterValueChangedCallback(evt     => { comSlider.value = evt.newValue; });
                workSlider.RegisterValueChangedCallback(evt   => OnRatioSliderChanged(workSlider, evt.newValue));
                workField.RegisterValueChangedCallback(evt    => { workSlider.value = evt.newValue; });
                healthSlider.RegisterValueChangedCallback(evt => OnRatioSliderChanged(healthSlider, evt.newValue));
                healthField.RegisterValueChangedCallback(evt  => { healthSlider.value = evt.newValue; });

                var autoAssignToggle = new Toggle("Auto-Assign Buildings") { value = BuildingManager.Instance.autoAssignTypes };
                autoAssignToggle.AddToClassList("custom-unity-toggle");
                autoAssignToggle.RegisterValueChangedCallback(evt =>
                {
                    if (BuildingManager.Instance != null)
                    {
                        BuildingManager.Instance.autoAssignTypes = evt.newValue;
                        if (evt.newValue) BuildingManager.Instance.AutoAssignTypes();
                        else              BuildingManager.Instance.UnassignAll();
                    }
                });
                buildingBox.Add(autoAssignToggle);
            }

            if (startSimButton != null && startSimButton.parent != null)
                startSimButton.parent.Insert(startSimButton.parent.IndexOf(startSimButton), buildingBox);
            else
                setupContainer.Add(buildingBox);

            var importBox = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, marginTop = 20,
                    paddingTop = 15, borderTopWidth = 1, borderTopColor = new Color(0.3f, 0.3f, 0.3f)
                }
            };

            loadSetupBtn = new Button { text = "Load Save File" };
            loadSetupBtn.AddToClassList("action-button");
            loadSetupBtn.style.flexGrow = 1;
            loadSetupBtn.clicked += PromptLoadFile;
            importBox.Add(loadSetupBtn);

            if (startSimButton != null && startSimButton.parent != null)
                startSimButton.parent.Add(importBox);
            else
                setupContainer.Add(importBox);
        }

        var runtimeHeader = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column, flexShrink = 0,
                backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f),
                paddingTop = 10, paddingBottom = 10, paddingLeft = 15, paddingRight = 15,
                borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f),
                marginBottom = 10
            }
        };

        var overviewTitle = new Label("RUNTIME OVERVIEW")
        {
            style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8, unityTextAlign = TextAnchor.MiddleLeft }
        };
        runtimeHeader.Add(overviewTitle);

        headerDayLabel    = new Label("DAY: 0")    { style = { color = Color.white, fontSize = 11, marginBottom = 4 } };
        headerTimeLabel   = new Label("TIME: 00:00") { style = { color = Color.white, fontSize = 11, marginBottom = 4 } };
        headerStatusLabel = new Label("STATUS: PAUSED") { style = { color = new Color(1f, 0.6f, 0f, 1f), fontSize = 11, marginBottom = 4 } };
        headerSpeedLabel  = new Label("SPEED: 1x")  { style = { color = new Color(0.8f, 0.8f, 0.8f, 1f), fontSize = 11 } };

        runtimeHeader.Add(headerDayLabel);
        runtimeHeader.Add(headerTimeLabel);
        runtimeHeader.Add(headerStatusLabel);
        runtimeHeader.Add(headerSpeedLabel);

        if (runtimeContainer != null)
        {
            runtimeContainer.style.justifyContent = Justify.FlexStart;
            runtimeContainer.style.flexShrink = 1;
            runtimeContainer.style.minHeight = 0;
            runtimeContainer.Insert(0, runtimeHeader);
        }

        playButton  = root.Q<Button>("PlayButton");
        fastButton  = root.Q<Button>("FastButton");
        resetButton = root.Q<Button>("ResetButton");
        exportBtn   = root.Q<Button>("ExportButton");

        float standardWidth   = 75f;
        float standardHeight  = 36f;
        float standardPadding = 8f;

        if (playButton != null && playButton.parent != null)
        {
            playButton.parent.style.flexShrink = 0;
            playButton.parent.style.justifyContent = Justify.Center;

            runtimeControlsTitle = new Label("RUNTIME CONTROLS")
            {
                style =
                {
                    color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 8, marginTop = 8, paddingLeft = 15, unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            playButton.parent.parent.Insert(playButton.parent.parent.IndexOf(playButton.parent), runtimeControlsTitle);

            branchControlsRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, justifyContent = Justify.Center, marginTop = 8, display = DisplayStyle.None }
            };

            discardAttemptBtn = new Button { text = "DISCARD TIMELINE" };
            discardAttemptBtn.AddToClassList("action-button");
            discardAttemptBtn.style.paddingLeft  = 12;
            discardAttemptBtn.style.paddingRight = 12;
            discardAttemptBtn.style.height       = standardHeight;
            discardAttemptBtn.style.marginRight  = 10;
            discardAttemptBtn.clicked += CancelAttempt;

            commitAttemptBtn = new Button { text = "COMMIT TIMELINE" };
            commitAttemptBtn.AddToClassList("action-button");
            commitAttemptBtn.style.paddingLeft  = 12;
            commitAttemptBtn.style.paddingRight = 12;
            commitAttemptBtn.style.height       = standardHeight;
            commitAttemptBtn.clicked += CommitAttempt;

            branchControlsRow.Add(discardAttemptBtn);
            branchControlsRow.Add(commitAttemptBtn);
            playButton.parent.parent.Insert(playButton.parent.parent.IndexOf(playButton.parent) + 1, branchControlsRow);
        }

        if (resetButton != null)
        {
            resetButton.style.backgroundImage = null;
            resetButton.AddToClassList("action-button");
            resetButton.style.width  = standardWidth;
            resetButton.style.height = standardHeight;
            resetButton.clicked += ResetSimulation;
        }
        if (exportBtn != null)
        {
            exportBtn.style.backgroundImage = null;
            exportBtn.AddToClassList("action-button");
            exportBtn.style.width  = standardWidth;
            exportBtn.style.height = standardHeight;
            exportBtn.clicked += PromptSaveFile;
        }
        if (playButton != null)
        {
            StyleIconButton(playButton, playIcon, standardWidth, standardHeight, standardPadding);
            playButton.clicked += TogglePlayPause;
        }
        if (fastButton != null)
        {
            StyleIconButton(fastButton, fastForwardIcon, standardWidth, standardHeight, standardPadding);
            fastButton.clicked += CycleSpeed;
        }

        UpdateButtonVisibility();

        colorToggle          = root.Q<Toggle>("ColorToggle");
        socialDistanceToggle = root.Q<Toggle>("SocialDistanceToggle");
        lockdownToggle       = root.Q<Toggle>("LockdownToggle");

        if (lockdownToggle != null)
            lockdownToggle.RegisterValueChangedCallback(evt => { isLockdown = evt.newValue; });

        if (colorToggle != null)          colorToggle.AddToClassList("custom-unity-toggle");
        if (socialDistanceToggle != null) socialDistanceToggle.AddToClassList("custom-unity-toggle");
        if (lockdownToggle != null)       lockdownToggle.AddToClassList("custom-unity-toggle");

        InjectInfoButtonToUXML(colorToggle,          "Show Building Colors");
        InjectInfoButtonToUXML(socialDistanceToggle, "Toggle Social Distancing");
        InjectInfoButtonToUXML(lockdownToggle,        "Toggle Lockdown");

        AttachHoverTooltip(resetButton, "Reset");
        AttachHoverTooltip(playButton,  "Play / Pause");
        AttachHoverTooltip(fastButton,  "Fast Forward");
        AttachHoverTooltip(exportBtn,   "Export");

        lineGraph        = root.Q<EpidemicLineGraph>();
        barGraph         = root.Q<EpidemicBarGraph>();
        activeCasesGraph = root.Q<ActiveCasesGraph>();
        simulationStats  = root.Q<SimulationStatsDashboard>();
        virusVaccineControls = root.Q<VirusVaccineControls>();

        if (lineGraph != null)
        {
            lineGraph.OnDayClicked    += ScrubToHistoricalDay;
            lineGraph.OnCancelClicked += CancelScrubPreview;
        }
        if (virusVaccineControls != null)
        {
            virusVaccineControls.OnMutateVirus  += TriggerMutationEvent;
            virusVaccineControls.OnDeployVaccine += TriggerVaccineWave;
        }

        if (startSimButton != null)
            startSimButton.clicked += () => StartSimulationFromUI();

        if (colorToggle != null)
            colorToggle.RegisterValueChangedCallback(evt =>
            {
                if (BuildingManager.Instance != null) BuildingManager.Instance.ToggleColors(evt.newValue);
            });

        if (socialDistanceToggle != null)
            socialDistanceToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    transmissionRate = baseTransmissionRate * 0.4f;
                    infectionRadius  = baseInfectionRadius  * 0.6f;
                }
                else
                {
                    transmissionRate = baseTransmissionRate;
                    infectionRadius  = baseInfectionRadius;
                }
            });

        loadingOverlay = new LoadingOverlay();
        root.Add(loadingOverlay);

        SwitchTab(true);
        CreateBuildingPopup(root);
    }

    void StartSimulationFromUI()
    {
        if (popInput != null       && int.TryParse(popInput.value, out int p))       populationSize   = p;
        if (infectedInput != null  && int.TryParse(infectedInput.value, out int i))  initialInfected  = i;
        if (radiusInput != null    && float.TryParse(radiusInput.value, out float r)) infectionRadius  = r;
        if (transRateInput != null && float.TryParse(transRateInput.value, out float tr)) transmissionRate = tr;
        if (recoveryInput != null  && float.TryParse(recoveryInput.value, out float rec)) recoveryTime  = rec;
        if (mortalityInput != null && float.TryParse(mortalityInput.value, out float m)) mortalityRate   = m;

        baseTransmissionRate = transmissionRate;
        baseInfectionRadius  = infectionRadius;

        if (lineGraph != null)        lineGraph.maxPopulation        = populationSize;
        if (barGraph != null)          barGraph.maxPopulation         = populationSize;
        if (activeCasesGraph != null)  activeCasesGraph.maxPopulation = populationSize;

        if (setupContainer != null) setupContainer.SetEnabled(false);
        SwitchTab(false);
        Initialize();
    }

    public void ResetSimulation()
    {
        if (!initialized) return;
        initialized = false;
        pendingImportData = null;
        ForcePauseUI();
        DisposeArrays();

        realTime              = 0f;
        epicAccumulator       = 0f;
        fixedTickAccumulator  = 0f;
        furthestRecordedDay   = 0;
        isExploringPast       = false;
        currentFastIndex      = 0;
        absoluteTickCounter   = 0;
        hasGhostBaseline      = false;
        currentDeployedVaccineStrain = -1;
        currentVaccineEfficacy       = 0f;
        currentVaccineAbidance       = 0f;
        totalVaccinesAvailable       = 0;

        timelineHistory.Clear();
        baselineHistory.Clear();

        if (lineGraph != null)        { lineGraph.maxScrubableDay = 0; lineGraph.ClearData(); }
        if (activeCasesGraph != null)  activeCasesGraph.ClearData();
        if (barGraph != null)          barGraph.ClearData();
        if (simulationStats != null)   simulationStats.ClearData();

        if (virusVaccineControls != null)
        {
            virusVaccineControls.UpdateActiveStrains(new Dictionary<int, int>());
            virusVaccineControls.UpdateActiveVaccines(new Dictionary<int, int>());
        }

        if (headerDayLabel != null)  headerDayLabel.text  = "DAY: 0";
        if (headerTimeLabel != null) headerTimeLabel.text = "TIME: 00:00";

        UpdateHUDDisplay();
        UpdateButtonVisibility();

        if (setupContainer != null) setupContainer.SetEnabled(true);
        SwitchTab(true);
    }

    private void TogglePlayPause()
    {
        if (isExploringPast)
        {
            BranchTimeline(currentViewDay);
            isExploringPast = false;
        }

        isPaused = !isPaused;

        var iconEl = playButton != null ? playButton.Q<VisualElement>("btn-icon") : null;

        if (isPaused)
        {
            if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = 0f;
            if (iconEl != null && playIcon != null) iconEl.style.backgroundImage = new StyleBackground(playIcon);
        }
        else
        {
            if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = fastSpeeds[currentFastIndex];
            if (iconEl != null && pauseIcon != null) iconEl.style.backgroundImage = new StyleBackground(pauseIcon);
        }

        UpdateHUDDisplay();
    }

    private void CycleSpeed()
    {
        currentFastIndex = (currentFastIndex + 1) % fastSpeeds.Length;
        if (!isPaused && TimeManager.Instance != null)
            TimeManager.Instance.timeMultiplier = fastSpeeds[currentFastIndex];
        UpdateHUDDisplay();
    }

    private void ForcePauseUI()
    {
        isPaused = true;
        if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = 0f;
        var iconEl = playButton != null ? playButton.Q<VisualElement>("btn-icon") : null;
        if (iconEl != null && playIcon != null) iconEl.style.backgroundImage = new StyleBackground(playIcon);
        UpdateHUDDisplay();
    }

    private void UpdateHUDDisplay()
    {
        if (headerStatusLabel != null)
        {
            headerStatusLabel.text = isPaused ? "STATUS: PAUSED" : "STATUS: PLAYING";
            headerStatusLabel.style.color = isPaused ? new Color(1f, 0.6f, 0f, 1f) : new Color(0.2f, 0.8f, 0.2f, 1f);
        }
        if (headerSpeedLabel != null)
            headerSpeedLabel.text = $"SPEED: {fastSpeeds[currentFastIndex]}x";
    }

    private void SaveHistoricalSnapshot(int day)
    {
        SimulationAgent[] snapshot = new SimulationAgent[agents.Length];
        agents.CopyTo(snapshot);
        timelineHistory[day] = snapshot;
    }

    private void ScrubToHistoricalDay(int targetDay)
    {
        if (!timelineHistory.ContainsKey(targetDay) && !baselineHistory.ContainsKey(targetDay)) return;

        ForcePauseUI();
        if (TimeManager.Instance != null) TimeManager.Instance.currentHour = 0f;
        epicAccumulator      = 0f;
        fixedTickAccumulator = 0f;
        isExploringPast      = targetDay < furthestRecordedDay;
        currentViewDay       = targetDay;

        SimulationAgent[] snapshot = timelineHistory.ContainsKey(targetDay)
            ? timelineHistory[targetDay]
            : baselineHistory[targetDay];

        agents.CopyFrom(snapshot);
        realTime            = (targetDay - 1) * realSecondsPerInGameDay;
        absoluteTickCounter = Mathf.FloorToInt(realTime / epicTickInterval);

        if (AgentRenderer.Instance != null)
            AgentRenderer.Instance.UpdateRender(agents, realTime, agentGroundOffset);
    }

    private void CancelScrubPreview()
    {
        if (!isExploringPast) return;
        isExploringPast = false;
        currentViewDay  = furthestRecordedDay;

        SimulationAgent[] snapshot = timelineHistory[furthestRecordedDay];
        agents.CopyFrom(snapshot);
        realTime = (furthestRecordedDay - 1) * realSecondsPerInGameDay;

        if (AgentRenderer.Instance != null)
            AgentRenderer.Instance.UpdateRender(agents, realTime, agentGroundOffset);
    }

    private void BranchTimeline(int branchDay)
    {
        if (!hasGhostBaseline) LockNewBaseline();

        List<int> daysToDelete = timelineHistory.Keys.Where(day => day > branchDay).ToList();
        foreach (var day in daysToDelete) timelineHistory.Remove(day);

        furthestRecordedDay = branchDay;
        if (lineGraph != null) lineGraph.maxScrubableDay = furthestRecordedDay;
        if (lineGraph != null) lineGraph.TruncateData(branchDay);
        if (activeCasesGraph != null) activeCasesGraph.TruncateData(branchDay);

        UpdateButtonVisibility();
    }

    private void LockNewBaseline()
    {
        baselineHistory  = new Dictionary<int, SimulationAgent[]>(timelineHistory);
        hasGhostBaseline = true;
        if (lineGraph != null) lineGraph.SetGhostBaseline();
        UpdateButtonVisibility();
    }

    private void CancelAttempt()
    {
        if (!hasGhostBaseline) return;

        timelineHistory     = new Dictionary<int, SimulationAgent[]>(baselineHistory);
        furthestRecordedDay = timelineHistory.Keys.Max();
        isExploringPast     = false;
        currentViewDay      = furthestRecordedDay;

        if (lineGraph != null) lineGraph.RestoreFromGhost();

        ScrubToHistoricalDay(furthestRecordedDay);
        isExploringPast  = false;
        hasGhostBaseline = false;

        UpdateButtonVisibility();
    }

    private void CommitAttempt()
    {
        if (!hasGhostBaseline) return;
        baselineHistory.Clear();
        hasGhostBaseline = false;
        if (lineGraph != null) lineGraph.ClearGhostBaseline();
        UpdateButtonVisibility();
    }

    private void UpdateButtonVisibility()
    {
        if (branchControlsRow != null)
            branchControlsRow.style.display = hasGhostBaseline ? DisplayStyle.Flex : DisplayStyle.None;

        if (runtimeControlsTitle != null)
        {
            if (hasGhostBaseline)
            {
                runtimeControlsTitle.text = "RUNTIME CONTROLS (Alternate Timeline Active)";
                runtimeControlsTitle.style.color = new Color(1f, 0.7f, 0.1f, 1f);
            }
            else
            {
                runtimeControlsTitle.text = "RUNTIME CONTROLS";
                runtimeControlsTitle.style.color = Color.white;
            }
        }
    }

    private void TriggerMutationEvent(int newStrainID, float evasion, float trans, float incubation, float fatality)
    {
        baseTransmissionRate = trans;
        transmissionRate = (socialDistanceToggle != null && socialDistanceToggle.value)
            ? baseTransmissionRate * 0.4f
            : baseTransmissionRate;

        minIncubationDays = Mathf.Max(1f, incubation - 1f);
        maxIncubationDays = incubation + 1f;
        mortalityRate     = fatality;

        for (int i = 0; i < agents.Length; i++)
        {
            var agent = agents[i];

            if (agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated)
            {
                if (Random.value <= evasion)
                {
                    agent.healthState     = HealthState.Susceptible;
                    agent.protectedStrainID = -1;
                }
            }
            if (agent.healthState == HealthState.Infected || agent.healthState == HealthState.Exposed)
            {
                agent.activeStrainID = newStrainID;
            }

            agents[i] = agent;
        }
    }

    private void TriggerVaccineWave(int strainID, int doses, float efficacy, float abidance)
    {
        currentDeployedVaccineStrain = strainID;
        currentVaccineEfficacy       = efficacy;
        currentVaccineAbidance       = abidance;
        totalVaccinesAvailable       = doses;

        int activeClinics       = hospitalPositions.Length + (distributeToCommercial ? commercialPositions.Length : 0);
        int vaccinesPerClinic   = doses / Mathf.Max(1, activeClinics);

        for (int i = 0; i < hospitalInventory.Length; i++)   hospitalInventory[i]   = vaccinesPerClinic;
        if (distributeToCommercial)
            for (int i = 0; i < commercialInventory.Length; i++) commercialInventory[i] = vaccinesPerClinic;
    }

    private async void PromptSaveFile()
    {
        if (!initialized) return;
        ForcePauseUI();

        var extensions = new[] { new ExtensionFilter("Simulation Save", "sim") };
        string filePath = StandaloneFileBrowser.SaveFilePanel("Save Simulation State", "", "Simulation_Day_" + furthestRecordedDay, extensions);
        if (string.IsNullOrEmpty(filePath)) return;

        isProcessingData = true;
        loadingOverlay.Show("SAVING FILE... PLEASE WAIT");

        var progressHandler = new Progress<float>(percent =>
        {
            if (!Application.isPlaying || this == null) return;
            loadingOverlay.SetProgress(percent, $"SAVING FILE... {(int)(percent * 100)}% PLEASE WAIT");
        });

        int d = 0, s = 0, e = 0, inf = 0, r = 0, v = 0;
        for (int i = 0; i < agents.Length; i++)
        {
            if      (agents[i].healthState == HealthState.Dead)        d++;
            else if (agents[i].healthState == HealthState.Susceptible) s++;
            else if (agents[i].healthState == HealthState.Exposed)     e++;
            else if (agents[i].healthState == HealthState.Infected)    inf++;
            else if (agents[i].healthState == HealthState.Recovered)   r++;
            else if (agents[i].healthState == HealthState.Vaccinated)  v++;
        }

        SimSaveData saveData = new SimSaveData
        {
            popSize       = populationSize,
            radius        = infectionRadius,
            transRate     = transmissionRate,
            recTime       = recoveryTime,
            mortRate      = mortalityRate,
            natImmunity   = naturalImmunityEfficacy,
            immDuration   = immunityDurationDays,
            histImmunity  = historicalImmunityEfficacy,
            histRecMult   = historicalRecoveryMultiplier,
            lockAbidance  = lockdownAbidance,
            hospTransMult = hospitalTransmissionMultiplier,
            beds          = hospitalBedsPerFacility,
            distComm      = distributeToCommercial,
            s = s, e = e, i = inf, r = r, v = v, d = d,
            savedDay = Mathf.FloorToInt(realTime / realSecondsPerInGameDay) + 1
        };

        await SimulationSerializer.SaveSimulationToFileAsync(filePath, saveData, timelineHistory, asyncTaskToken.Token, progressHandler);

        if (Application.isPlaying && this != null)
        {
            loadingOverlay.Hide();
            isProcessingData = false;
        }
    }

    private async void PromptLoadFile()
    {
        ForcePauseUI();

        var extensions = new[] { new ExtensionFilter("Simulation Save", "sim") };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Load Simulation State", "", extensions, false);
        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

        string filePath = paths[0];
        isProcessingData = true;
        loadingOverlay.Show("LOADING FILE... PLEASE WAIT");

        var progressHandler = new Progress<float>(percent =>
        {
            if (!Application.isPlaying || this == null) return;
            loadingOverlay.SetProgress(percent, $"LOADING FILE... {(int)(percent * 100)}% PLEASE WAIT");
        });

        var result = await SimulationSerializer.LoadSimulationFromFileAsync(filePath, asyncTaskToken.Token, progressHandler);

        if (!Application.isPlaying || this == null) return;

        loadingOverlay.Hide();
        isProcessingData = false;

        if (result.paramsData == null || result.history == null) return;

        ResetSimulation();

        var pData = result.paramsData;

        if (popInput != null)       popInput.value       = pData.popSize.ToString();
        if (radiusInput != null)    radiusInput.value    = pData.radius.ToString();
        if (transRateInput != null) transRateInput.value = pData.transRate.ToString();
        if (recoveryInput != null)  recoveryInput.value  = pData.recTime.ToString();
        if (mortalityInput != null) mortalityInput.value = pData.mortRate.ToString();
        if (immDurField != null)    immDurField.value    = pData.immDuration;
        if (histRecField != null)   histRecField.value   = pData.histRecMult;
        if (hospTransField != null) hospTransField.value = pData.hospTransMult;
        if (bedsField != null)      bedsField.value      = pData.beds;
        if (commToggle != null)     commToggle.value     = pData.distComm;
        if (natImmSlider != null)   natImmSlider.value   = pData.natImmunity;
        if (histImmSlider != null)  histImmSlider.value  = pData.histImmunity;
        if (lockAbidSlider != null) lockAbidSlider.value = pData.lockAbidance;

        pendingImportData   = pData;
        timelineHistory     = result.history;
        furthestRecordedDay = pData.savedDay;

        StartSimulationFromUI();
    }

    private void CreateBuildingPopup(VisualElement root)
    {
        buildingPopup = new VisualElement();
        buildingPopup.style.position        = Position.Absolute;
        buildingPopup.style.display         = DisplayStyle.None;
        buildingPopup.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        buildingPopup.style.borderTopLeftRadius     = 8;
        buildingPopup.style.borderTopRightRadius    = 8;
        buildingPopup.style.borderBottomLeftRadius  = 8;
        buildingPopup.style.borderBottomRightRadius = 8;
        buildingPopup.style.paddingTop    = 12;
        buildingPopup.style.paddingBottom = 12;
        buildingPopup.style.paddingLeft   = 12;
        buildingPopup.style.paddingRight  = 12;
        buildingPopup.style.width         = 220;
        buildingPopup.style.bottom        = 50;
        buildingPopup.style.left          = Length.Percent(50);
        buildingPopup.style.marginLeft    = -110;

        popupTitle = new Label("SELECTED BUILDING TYPE");
        popupTitle.style.color                    = Color.white;
        popupTitle.style.unityFontStyleAndWeight  = FontStyle.Bold;
        popupTitle.style.fontSize                 = 12;
        popupTitle.style.marginBottom             = 10;
        popupTitle.style.unityTextAlign           = TextAnchor.MiddleCenter;
        buildingPopup.Add(popupTitle);

        resBtn    = CreatePopupButton("Residential", BuildingType.Residential, Building.ResidentialColor);
        comBtn    = CreatePopupButton("Commercial",  BuildingType.Commercial,  Building.CommercialColor);
        workBtn   = CreatePopupButton("Workplace",   BuildingType.Workplace,   Building.WorkplaceColor);
        healthBtn = CreatePopupButton("Healthcare",  BuildingType.Healthcare,  Building.HealthcareColor);

        buildingPopup.Add(resBtn);
        buildingPopup.Add(comBtn);
        buildingPopup.Add(workBtn);
        buildingPopup.Add(healthBtn);

        var gameView = root.Q<VisualElement>("GameViewArea");
        if (gameView != null) gameView.Add(buildingPopup);
        else root.Add(buildingPopup);
    }

    private Button CreatePopupButton(string text, BuildingType type, Color col)
    {
        var btn = new Button();
        btn.AddToClassList("action-button");
        btn.style.flexDirection   = FlexDirection.Row;
        btn.style.justifyContent  = Justify.SpaceBetween;
        btn.style.alignItems      = Align.Center;
        btn.style.marginBottom    = 6;
        btn.style.paddingLeft     = 12;
        btn.style.paddingRight    = 12;
        btn.style.height          = 34;
        btn.style.borderTopLeftRadius     = 6;
        btn.style.borderTopRightRadius    = 6;
        btn.style.borderBottomLeftRadius  = 6;
        btn.style.borderBottomRightRadius = 6;

        var label = new Label(text);
        label.style.color    = Color.white;
        label.style.fontSize = 12;
        btn.Add(label);

        var colorBox = new VisualElement();
        colorBox.style.width                    = 14;
        colorBox.style.height                   = 14;
        colorBox.style.backgroundColor          = col;
        colorBox.style.borderTopLeftRadius     = 3;
        colorBox.style.borderTopRightRadius    = 3;
        colorBox.style.borderBottomLeftRadius  = 3;
        colorBox.style.borderBottomRightRadius = 3;
        btn.Add(colorBox);

        btn.clicked += () =>
        {
            if (currentPopupBuilding != null)
            {
                currentPopupBuilding.SetType(type);
                if (BuildingManager.Instance != null) BuildingManager.Instance.RefreshLists();
                RefreshPopupHighlights();
            }
        };

        return btn;
    }

    public void ShowBuildingPopup(Building b)
    {
        currentPopupBuilding = b;
        buildingPopup.style.display = DisplayStyle.Flex;
        RefreshPopupHighlights();
    }

    public void HideBuildingPopup()
    {
        buildingPopup.style.display = DisplayStyle.None;
        currentPopupBuilding = null;
    }

    private void RefreshPopupHighlights()
    {
        if (currentPopupBuilding == null) return;
        BuildingType t = currentPopupBuilding.buildingType;

        SetButtonHighlight(resBtn,    t == BuildingType.Residential);
        SetButtonHighlight(comBtn,    t == BuildingType.Commercial);
        SetButtonHighlight(workBtn,   t == BuildingType.Workplace);
        SetButtonHighlight(healthBtn, t == BuildingType.Healthcare);
    }

    private void SetButtonHighlight(Button btn, bool active)
    {
        float w = active ? 2 : 0;
        btn.style.borderTopWidth    = w;
        btn.style.borderBottomWidth = w;
        btn.style.borderLeftWidth   = w;
        btn.style.borderRightWidth  = w;
        btn.style.borderTopColor    = Color.white;
        btn.style.borderBottomColor = Color.white;
        btn.style.borderLeftColor   = Color.white;
        btn.style.borderRightColor  = Color.white;
    }

    public bool IsPointerOverPopup()
    {
        if (buildingPopup == null || buildingPopup.style.display == DisplayStyle.None) return false;
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        mousePos.y = Screen.height - mousePos.y;
        return buildingPopup.worldBound.Contains(mousePos);
    }

    private void CreateTooltipUI(VisualElement root)
    {
        tooltipWindow = new VisualElement
        {
            style =
            {
                position = Position.Absolute, display = DisplayStyle.None,
                backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.98f),
                borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor    = new Color(0.4f, 0.4f, 0.4f),
                borderBottomColor = new Color(0.4f, 0.4f, 0.4f),
                borderLeftColor   = new Color(0.4f, 0.4f, 0.4f),
                borderRightColor  = new Color(0.4f, 0.4f, 0.4f),
                borderTopLeftRadius = 6, borderTopRightRadius = 6,
                borderBottomLeftRadius = 6, borderBottomRightRadius = 6,
                paddingTop = 10, paddingBottom = 10, paddingLeft = 12, paddingRight = 12,
                width = 280
            }
        };

        ttTitle   = new Label { style = { color = new Color(0.3f, 0.8f, 0.8f), fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 6, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f), paddingBottom = 4 } };
        ttPurpose = new Label { enableRichText = true, style = { color = Color.white, fontSize = 11, whiteSpace = WhiteSpace.Normal, marginBottom = 5 } };
        ttInput   = new Label { enableRichText = true, style = { color = new Color(0.8f, 0.8f, 0.8f), fontSize = 11, whiteSpace = WhiteSpace.Normal, marginBottom = 3 } };
        ttRange   = new Label { enableRichText = true, style = { color = new Color(1f, 0.8f, 0.4f), fontSize = 11, whiteSpace = WhiteSpace.Normal } };

        tooltipWindow.Add(ttTitle);
        tooltipWindow.Add(ttPurpose);
        tooltipWindow.Add(ttInput);
        tooltipWindow.Add(ttRange);
        root.Add(tooltipWindow);
    }

    public bool HasTooltip(string key)
    {
        return tooltips != null && tooltips.ContainsKey(key);
    }

    public void ShowTooltip(string key, VisualElement targetElement)
    {
        if (!tooltips.ContainsKey(key)) return;
        var data = tooltips[key];

        ttTitle.text   = key.ToUpper();
        ttPurpose.text = "<b>Purpose:</b> " + data.purpose;
        ttInput.text   = "<b>Input:</b> "   + data.input;
        ttRange.text   = "<b>Range:</b> "   + data.range;

        tooltipWindow.style.display = DisplayStyle.Flex;

        var bounds      = targetElement.worldBound;
        float windowWidth = 280f;
        float screenWidth = Screen.width;

        float xPos = bounds.xMax + 10;
        if (xPos + windowWidth > screenWidth && bounds.xMin - windowWidth > 0)
            xPos = bounds.xMin - windowWidth - 10;

        tooltipWindow.style.left = xPos;
        tooltipWindow.style.top  = bounds.yMin;
    }

    public void HideTooltip()
    {
        if (tooltipWindow != null) tooltipWindow.style.display = DisplayStyle.None;
    }

    private void AttachHoverTooltip(VisualElement element, string key)
    {
        if (element == null || !tooltips.ContainsKey(key)) return;
        element.RegisterCallback<PointerEnterEvent>(evt => ShowTooltip(key, element));
        element.RegisterCallback<PointerLeaveEvent>(evt => HideTooltip());
    }

    private Button CreateInfoButton(string key)
    {
        var btn = new Button { text = "?" };
        btn.style.width                   = 16;
        btn.style.height                  = 16;
        btn.style.borderTopLeftRadius     = 8;
        btn.style.borderTopRightRadius    = 8;
        btn.style.borderBottomLeftRadius  = 8;
        btn.style.borderBottomRightRadius = 8;
        btn.style.backgroundColor         = new Color(0.34f, 0.34f, 0.34f, 1f);
        btn.style.borderTopWidth          = 1;
        btn.style.borderBottomWidth       = 1;
        btn.style.borderLeftWidth         = 1;
        btn.style.borderRightWidth        = 1;
        btn.style.borderTopColor          = new Color(0.14f, 0.14f, 0.14f, 1f);
        btn.style.borderBottomColor       = new Color(0.14f, 0.14f, 0.14f, 1f);
        btn.style.borderLeftColor         = new Color(0.14f, 0.14f, 0.14f, 1f);
        btn.style.borderRightColor        = new Color(0.14f, 0.14f, 0.14f, 1f);
        btn.style.color                   = new Color(0.9f, 0.9f, 0.9f, 1f);
        btn.style.fontSize                = 10;
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        btn.style.paddingTop              = 0;
        btn.style.paddingBottom           = 0;
        btn.style.paddingLeft             = 0;
        btn.style.paddingRight            = 0;
        btn.style.marginLeft              = 5;

        btn.RegisterCallback<PointerEnterEvent>(evt =>
        {
            btn.style.backgroundColor = new Color(0.42f, 0.42f, 0.42f, 1f);
            if (SimulationManager.Instance != null && SimulationManager.Instance.HasTooltip(key))
                SimulationManager.Instance.ShowTooltip(key, btn);
        });
        btn.RegisterCallback<PointerLeaveEvent>(evt =>
        {
            btn.style.backgroundColor = new Color(0.34f, 0.34f, 0.34f, 1f);
            if (SimulationManager.Instance != null)
                SimulationManager.Instance.HideTooltip();
        });

        return btn;
    }

    private void InjectInfoButtonToUXML(VisualElement field, string key)
    {
        if (field == null || field.parent == null) return;
        var parent = field.parent;
        var index  = parent.IndexOf(field);

        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, width = Length.Percent(100) } };
        field.style.flexGrow = 1;

        parent.RemoveAt(index);
        row.Add(field);
        if (tooltips.ContainsKey(key)) row.Add(CreateInfoButton(key));
        parent.Insert(index, row);
    }

    private VisualElement WrapWithInfoButton(VisualElement field, string key)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, width = Length.Percent(100) } };
        field.style.flexGrow = 1;
        row.Add(field);
        if (tooltips.ContainsKey(key)) row.Add(CreateInfoButton(key));
        return row;
    }

    private VisualElement CreateRatioRow(string labelText, float defaultVal, out Slider sliderReference, out FloatField fieldReference, string tooltipKey)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };

        var slider = new Slider(labelText, 0f, 1f) { value = defaultVal, style = { flexGrow = 1 } };
        slider.AddToClassList("inspector-field");
        sliderReference = slider;

        var floatField = new FloatField { value = defaultVal, style = { width = 45, marginLeft = 5 } };
        floatField.AddToClassList("inspector-field");
        fieldReference = floatField;

        slider.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            var tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
            if (tracker != null && tracker.Q("limit-fill") == null)
            {
                var fill = new VisualElement { name = "limit-fill" };
                fill.style.position                 = Position.Absolute;
                fill.style.left                     = 0;
                fill.style.top                      = 0;
                fill.style.bottom                   = 0;
                fill.style.backgroundColor          = new Color(1f, 1f, 1f, 0.25f);
                fill.style.borderTopLeftRadius     = 3;
                fill.style.borderBottomLeftRadius  = 3;
                tracker.Add(fill);
                UpdateRatioLimits();
            }
        });

        row.Add(slider);
        row.Add(floatField);
        if (tooltips.ContainsKey(tooltipKey)) row.Add(CreateInfoButton(tooltipKey));
        return row;
    }

    private void OnRatioSliderChanged(Slider activeSlider, float newValue)
    {
        if (isBalancingRatios) return;
        isBalancingRatios = true;

        var allSliders   = new[] { resSlider, comSlider, workSlider, healthSlider };
        var otherSliders = allSliders.Where(s => s != activeSlider).ToList();
        float otherSum   = otherSliders.Sum(s => s.value);
        
        float maxAllowed = Mathf.Clamp01(1.0f - otherSum);
        if (newValue > maxAllowed)
        {
            newValue = maxAllowed;
            activeSlider.SetValueWithoutNotify(newValue);
        }

        if (resField != null)    resField.SetValueWithoutNotify((float)Math.Round(resSlider.value, 3));
        if (comField != null)    comField.SetValueWithoutNotify((float)Math.Round(comSlider.value, 3));
        if (workField != null)   workField.SetValueWithoutNotify((float)Math.Round(workSlider.value, 3));
        if (healthField != null) healthField.SetValueWithoutNotify((float)Math.Round(healthSlider.value, 3));

        if (BuildingManager.Instance != null)
        {
            BuildingManager.Instance.residentialRatio = resSlider.value;
            BuildingManager.Instance.commercialRatio  = comSlider.value;
            BuildingManager.Instance.workplaceRatio   = workSlider.value;
            BuildingManager.Instance.healthcareRatio  = healthSlider.value;
        }

        UpdateRatioLimits();
        isBalancingRatios = false;
    }

    private void UpdateRatioLimits()
    {
        if (resSlider == null || comSlider == null || workSlider == null || healthSlider == null) return;
        float res = resSlider.value, com = comSlider.value, wrk = workSlider.value, hth = healthSlider.value;
        UpdateLimitFill(resSlider,    com + wrk + hth);
        UpdateLimitFill(comSlider,    res + wrk + hth);
        UpdateLimitFill(workSlider,   res + com + hth);
        UpdateLimitFill(healthSlider, res + com + wrk);
    }

    private void UpdateLimitFill(Slider slider, float sumOfOthers)
    {
        var tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
        if (tracker == null) return;
        var fill = tracker.Q<VisualElement>("limit-fill");
        if (fill == null) return;
        float limit = Mathf.Clamp01(1.0f - sumOfOthers);
        fill.style.width = Length.Percent(limit * 100f);
    }

    private VisualElement CreateSliderRow(string labelText, float min, float max, float defaultVal, out Slider sliderReference, System.Action<float> onValueChanged)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };

        var slider = new Slider(labelText, min, max) { value = defaultVal, style = { flexGrow = 1 } };
        slider.AddToClassList("inspector-field");
        sliderReference = slider;

        var floatField = new FloatField { value = defaultVal, style = { width = 45, marginLeft = 5 } };
        floatField.AddToClassList("inspector-field");

        slider.RegisterValueChangedCallback(evt =>
        {
            floatField.SetValueWithoutNotify((float)System.Math.Round(evt.newValue, 3));
            onValueChanged(evt.newValue);
        });
        floatField.RegisterValueChangedCallback(evt =>
        {
            float val = Mathf.Clamp(evt.newValue, min, max);
            if (evt.newValue != val) floatField.SetValueWithoutNotify(val);
            slider.SetValueWithoutNotify(val);
            onValueChanged(val);
        });

        row.Add(slider);
        row.Add(floatField);

        string key = labelText.Replace(":", "").Trim();
        if (tooltips.ContainsKey(key)) row.Add(CreateInfoButton(key));

        return row;
    }

    private void StyleIconButton(Button btn, Texture2D icon, float btnWidth, float btnHeight, float iconPadding)
    {
        btn.text = "";
        btn.Clear();
        btn.style.backgroundImage = null;
        btn.AddToClassList("action-button");
        btn.style.width         = btnWidth;
        btn.style.height        = btnHeight;
        btn.style.paddingTop    = iconPadding;
        btn.style.paddingBottom = iconPadding;
        btn.style.paddingLeft   = iconPadding;
        btn.style.paddingRight  = iconPadding;

        var iconVisual = new VisualElement { name = "btn-icon" };
        if (icon != null) iconVisual.style.backgroundImage = new StyleBackground(icon);
        iconVisual.style.unityBackgroundImageTintColor = Color.white;
        iconVisual.style.unityBackgroundScaleMode      = ScaleMode.ScaleToFit;
        iconVisual.style.width     = Length.Percent(100);
        iconVisual.style.height    = Length.Percent(100);
        iconVisual.style.alignSelf = Align.Center;
        btn.Add(iconVisual);
    }

    private void SwitchTab(bool showSetup)
    {
        if (setupContainer != null)   setupContainer.style.display   = showSetup ? DisplayStyle.Flex : DisplayStyle.None;
        if (runtimeContainer != null) runtimeContainer.style.display = !showSetup ? DisplayStyle.Flex : DisplayStyle.None;

        if (tabSetupBtn != null)
        {
            if (showSetup) tabSetupBtn.AddToClassList("tab-button--active");
            else           tabSetupBtn.RemoveFromClassList("tab-button--active");
        }
        if (tabRuntimeBtn != null)
        {
            if (!showSetup) tabRuntimeBtn.AddToClassList("tab-button--active");
            else            tabRuntimeBtn.RemoveFromClassList("tab-button--active");
        }
    }

    private void StyleCustomScrollbar(ScrollView sv)
    {
        sv.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            var scroller = sv.verticalScroller;
            if (scroller == null) return;

            scroller.style.width    = 6;
            scroller.style.minWidth = 6;
            scroller.style.maxWidth = 6;
            scroller.style.borderLeftWidth  = 0;
            scroller.style.borderRightWidth = 0;

            var upBtn   = scroller.Q(className: "unity-scroller__high-button");
            var downBtn = scroller.Q(className: "unity-scroller__low-button");
            if (upBtn   != null) upBtn.style.display   = DisplayStyle.None;
            if (downBtn != null) downBtn.style.display = DisplayStyle.None;

            var tracker = scroller.Q(className: "unity-base-slider__tracker");
            if (tracker != null)
            {
                tracker.style.backgroundColor = Color.clear;
                tracker.style.borderLeftWidth  = 0;
                tracker.style.borderRightWidth = 0;
            }

            var dragger = scroller.Q(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor          = new Color(0.4f, 0.4f, 0.4f, 1f);
                dragger.style.borderTopLeftRadius     = 3;
                dragger.style.borderTopRightRadius    = 3;
                dragger.style.borderBottomLeftRadius  = 3;
                dragger.style.borderBottomRightRadius = 3;
                dragger.style.width      = 6;
                dragger.style.left       = 0;
                dragger.style.marginLeft = 0;
            }
        });
    }

    float3 GetPersonalOffset(int agentIndex)
    {
        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(agentIndex * 7919));
        return new float3(
            rng.NextFloat(-destinationOffsetRange, destinationOffsetRange),
            0f,
            rng.NextFloat(-destinationOffsetRange, destinationOffsetRange)
        );
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

    public string GetPopulationStateHash()
    {
        if (!agents.IsCreated) return "NO_DATA";
        int s = 0, e = 0, i = 0, r = 0, v = 0, d = 0;
        for (int k = 0; k < agents.Length; k++)
        {
            if      (agents[k].healthState == HealthState.Susceptible) s++;
            else if (agents[k].healthState == HealthState.Exposed)     e++;
            else if (agents[k].healthState == HealthState.Infected)    i++;
            else if (agents[k].healthState == HealthState.Recovered)   r++;
            else if (agents[k].healthState == HealthState.Vaccinated)  v++;
            else if (agents[k].healthState == HealthState.Dead)        d++;
        }
        return $"S:{s} | E:{e} | I:{i} | R:{r} | V:{v} | D:{d}";
    }

    private void DisposeArrays()
    {
        if (agents.IsCreated)                 agents.Dispose();
        if (agentsBuffer.IsCreated)           agentsBuffer.Dispose();
        if (waypoints.IsCreated)              waypoints.Dispose();
        if (neighborData.IsCreated)           neighborData.Dispose();
        if (neighborStart.IsCreated)          neighborStart.Dispose();
        if (neighborCount.IsCreated)          neighborCount.Dispose();
        if (nativeHomeNearest.IsCreated)      nativeHomeNearest.Dispose();
        if (nativeWorkNearest.IsCreated)      nativeWorkNearest.Dispose();
        if (nativeCommercialNearest.IsCreated)nativeCommercialNearest.Dispose();
        if (nativeHospitalNearest.IsCreated)  nativeHospitalNearest.Dispose();
        if (nativeHomePositions.IsCreated)    nativeHomePositions.Dispose();
        if (nativeHospitalPositions.IsCreated)nativeHospitalPositions.Dispose();
        if (nativeCommercialPositions.IsCreated) nativeCommercialPositions.Dispose();
        if (indoorMap.IsCreated)              indoorMap.Dispose();
        if (distanceChecksTracker.IsCreated)  distanceChecksTracker.Dispose();
        spatialGrid?.Dispose();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (UnityEditor.SceneView.lastActiveSceneView != null)
            UnityEditor.SceneView.lastActiveSceneView.Repaint();
    }

    void OnDrawGizmos()
    {
        if (showWaypointsGizmo && waypointData != null && waypointData.waypoints != null)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypointData.waypoints.Length; i++)
                Gizmos.DrawSphere(waypointData.waypoints[i], 1f);

            if (waypointData.neighborData != null)
            {
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.3f);
                for (int i = 0; i < waypointData.waypoints.Length; i++)
                {
                    int start = waypointData.neighborStart[i];
                    int count = waypointData.neighborCount[i];
                    for (int n = 0; n < count; n++)
                    {
                        int neighborIdx = waypointData.neighborData[start + n];
                        Gizmos.DrawLine(waypointData.waypoints[i], waypointData.waypoints[neighborIdx]);
                    }
                }
            }
        }

        if (showSpatialGridGizmo && Application.isPlaying && initialized)
        {
            Gizmos.color = new Color(1f, 0f, 1f, 0.4f);

            float width  = spatialGrid.gridWidth  * spatialGrid.cellSize;
            float depth  = spatialGrid.gridHeight * spatialGrid.cellSize;
            Vector3 origin = new Vector3(spatialGrid.gridOrigin.x, 0.5f, spatialGrid.gridOrigin.z);

            for (int x = 0; x <= spatialGrid.gridWidth; x++)
            {
                Vector3 start = origin + new Vector3(x * spatialGrid.cellSize, 0, 0);
                Vector3 end   = start  + new Vector3(0, 0, depth);
                Gizmos.DrawLine(start, end);
            }
            for (int z = 0; z <= spatialGrid.gridHeight; z++)
            {
                Vector3 start = origin + new Vector3(0, 0, z * spatialGrid.cellSize);
                Vector3 end   = start  + new Vector3(width, 0, 0);
                Gizmos.DrawLine(start, end);
            }
        }
    }
#endif
}