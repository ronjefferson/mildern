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
    public float immunityTimer;
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

    private FloatField immDurField;
    private FloatField homeMultField, workMultField, commMultField, hospMultField;
    private IntegerField bedsField, homeCapField, workCapField, commCapField, hospCapField;
    
    private Slider sdAbidanceSlider, commPropensitySlider, vacPrefSlider;
    private Slider nonWorkerSlider, leisureSlider;
    private Slider sickHospSlider, sickStaySlider;
    private Slider speedVarSlider, gracePeriodSlider, evacStaggerSlider;
    
    private FloatField sdCapMultField, sdOffsetExpField;
    private FloatField hospRecovField, hospMortModField;
    private FloatField sdRadiusMultField, sdTransMultField;

    private Toggle commToggle;
    private Slider natImmSlider, lockAbidSlider;

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
    
    [Header("Documentation UI")]
    private VisualElement docOverlay;
    private ScrollView docScrollView;

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

    [Header("Social Distancing Parameters")]
    public bool isSocialDistancingToggle = false;
    public float sdAbidance = 0.8f;
    public float sdRadiusMultiplier = 0.6f;
    public float sdTransmissionMultiplier = 0.4f;
    public float sdContactCapMultiplier = 0.5f;
    public float sdOffsetExpansion = 2.0f;

    [Header("Building Modifiers")]
    public float homeTransmissionMultiplier = 1.5f;
    public int homeContactCap = 4;
    public float workplaceTransmissionMultiplier = 1.2f;
    public int workplaceContactCap = 10;
    public float commercialTransmissionMultiplier = 0.8f;
    public int commercialContactCap = 20;
    public float hospitalTransmissionMultiplier = 0.1f;
    public int hospitalContactCap = 50;

    [Header("Hospital Effectiveness")]
    public float hospitalRecoveryMultiplier = 2.0f;
    public float hospitalMortalityModifier = 0.2f;

    [Header("Societal Behavior")]
    public float commercialPropensity = 0.4f;
    public float workShiftStartMin = 7f;
    public float workShiftStartMax = 9f;
    public float workShiftEndMin = 16f;
    public float workShiftEndMax = 18f;
    
    [Range(0f, 1f)] public float nonWorkerRatio = 0.25f; 
    [Range(0f, 8f)] public float maxLeisureHours = 4.0f; 
    public float errandDurationMin = 1.0f;
    public float errandDurationMax = 3.0f;
    [Range(0f, 1f)] public float speedVariance = 0.25f;
    [Range(0f, 4f)] public float shiftGracePeriodHours = 1.0f;
    [Range(0f, 4f)] public float evacuationStaggerMax = 0.75f; 

    [Header("Vaccine Logistics")]
    public float vaccineCommercialPreference = 0.5f;
    public float vaccineWaitTimeMin = 1f;
    public float vaccineWaitTimeMax = 3f;

    [Header("Global State Trackers")]
    public float currentVaccineEfficacy = 0.0f;
    public float currentVaccineAbidance = 0.0f;
    public bool isVaccineCampaignActive = false;
    public int totalVaccinesAvailable = 0;
    public bool distributeToCommercial = true;
    public int hospitalBedsPerFacility = 50;
    public int absoluteTickCounter = 0;

    private int[] hospitalInventory;
    private int[] commercialInventory;

    private float baseTransmissionRate;
    private float baseInfectionRadius;

    [Header("Lockdown & Compliance Overrides")]
    public bool isLockdown = false;
    public float lockdownAbidance = 0.85f;
    [Range(0f, 1f)] public float sickHospitalThreshold = 0.5f;
    [Range(0f, 1f)] public float sickStayHomeThreshold = 0.2f;

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
        Application.runInBackground = true;
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
                        && agent.isAtHospital; // Replace isAtHospital if omitted in struct

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
                                || agent.healthState == HealthState.Recovered;

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
                                    agent.immunityTimer = Mathf.Max(1f, immunityDurationDays) * realSecondsPerInGameDay;
                                }
                            }

                            agent.isSeekingVaccine = false;
                        }
                    }

                    agents[i] = agent;
                }

                ScheduleUpdateJob scheduleJob = new ScheduleUpdateJob
                {
                    agents = agents,
                    currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f,
                    randomSeed = (uint)Time.frameCount, 
                    totalCommercialWaypoints = nativeCommercialNearest.Length,
                    isLockdown = this.isLockdown,
                    lockdownAbidanceThreshold = this.lockdownAbidance,
                    evacuationStaggerMax = this.evacuationStaggerMax,
                    shiftGracePeriodHours = this.shiftGracePeriodHours,
                    isSocialDistancing = this.isSocialDistancingToggle,
                    sdAbidance = this.sdAbidance,
                    sdOffsetExpansion = this.sdOffsetExpansion,
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
                    currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f,
                    stuckTimeoutSimHours = stuckTimeoutSimHours
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

                    if (isVaccineCampaignActive)
                    {
                        for (int i = 0; i < agents.Length; i++)
                        {
                            var agent = agents[i];
                            if (agent.isSeekingVaccine || agent.healthState == HealthState.Dead)
                                continue;
                            if (agent.healthState == HealthState.Vaccinated)
                                continue;

                            bool isEligible = agent.healthState == HealthState.Susceptible
                                || agent.healthState == HealthState.Recovered;

                            if (!isEligible || agent.complianceLevel > currentVaccineAbidance)
                                continue;

                            bool wantsCommercial = distributeToCommercial && Random.value < vaccineCommercialPreference;
                            int primaryClinic = wantsCommercial ? agent.commercialID : agent.healthcareID;
                            int primaryStock = wantsCommercial ? commercialInventory[primaryClinic] : hospitalInventory[primaryClinic];

                            if (primaryStock > 0)
                            {
                                agent.isSeekingVaccine = true;
                                agent.isVaccineClinicCommercial = wantsCommercial;
                                agent.vaccineClinicID = primaryClinic;
                                agent.vaccineWaitTimer = Random.Range(vaccineWaitTimeMin, vaccineWaitTimeMax) * (realSecondsPerInGameDay / 24f);
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
                                        agent.vaccineWaitTimer = Random.Range(vaccineWaitTimeMin, vaccineWaitTimeMax) * (realSecondsPerInGameDay / 24f);
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
                        
                        isSocialDistancing = this.isSocialDistancingToggle,
                        sdAbidance = this.sdAbidance,
                        sdRadiusMultiplier = this.sdRadiusMultiplier,
                        sdTransmissionMultiplier = this.sdTransmissionMultiplier,
                        sdContactCapMultiplier = this.sdContactCapMultiplier,
                        
                        hospitalRecoveryMultiplier = this.hospitalRecoveryMultiplier,
                        hospitalMortalityModifier = this.hospitalMortalityModifier,

                        homeTransmissionMultiplier = this.homeTransmissionMultiplier,
                        homeContactCap = this.homeContactCap,
                        workplaceTransmissionMultiplier = this.workplaceTransmissionMultiplier,
                        workplaceContactCap = this.workplaceContactCap,
                        commercialTransmissionMultiplier = this.commercialTransmissionMultiplier,
                        commercialContactCap = this.commercialContactCap,
                        hospitalTransmissionMultiplier = this.hospitalTransmissionMultiplier,
                        hospitalContactCap = this.hospitalContactCap,
                        
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
                                break;
                            case HealthState.Exposed:
                                exposed++;
                                break;
                            case HealthState.Recovered:
                                recovered++;
                                break;
                            case HealthState.Vaccinated:
                                vaccinated++;
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
            { "Lockdown Abidance",       new TooltipData("Percentage of population obeying lockdown rules.", "Float (Percentage)", "0.0 (0%) to 1.0 (100%)") },
            { "Beds Per Hospital",       new TooltipData("Maximum capacity of patients per healthcare facility.", "Integer", "10 to 500+") },
            { "Vaccines at Commercial",  new TooltipData("Allows commercial buildings to distribute vaccines.", "Toggle (True/False)", "N/A") },
            { "SD Abidance",             new TooltipData("Percentage of the population obeying the Social Distancing mandate.", "Float (Percentage)", "0.0 to 1.0") },
            { "SD Cap Multiplier",       new TooltipData("Reduces the number of people an agent interacts with indoors when SD is active.", "Float (Multiplier)", "0.1 to 1.0") },
            { "SD Offset Expansion",     new TooltipData("Forces compliant agents to stand further apart physically at destinations.", "Float (Multiplier)", "1.0 to 5.0") },
            { "SD Radius Multiplier",    new TooltipData("Reduces the outdoor infection radius when Social Distancing is toggled.", "Float (Multiplier)", "0.1 to 1.0") },
            { "SD Trans. Multiplier",    new TooltipData("Reduces the outdoor transmission probability when Social Distancing is toggled.", "Float (Multiplier)", "0.1 to 1.0") },
            { "Home Multiplier",         new TooltipData("Multiplies infection risk inside residential buildings.", "Float (Multiplier)", "0.1 to 10.0") },
            { "Home Contact Cap",        new TooltipData("Maximum number of family members interacted with per tick.", "Integer", "1 to 50") },
            { "Workplace Multiplier",    new TooltipData("Multiplies infection risk inside office buildings.", "Float (Multiplier)", "0.1 to 10.0") },
            { "Workplace Contact Cap",   new TooltipData("Maximum number of coworkers interacted with per tick.", "Integer", "1 to 100") },
            { "Commercial Multiplier",   new TooltipData("Multiplies infection risk inside shops and malls.", "Float (Multiplier)", "0.1 to 10.0") },
            { "Commercial Contact Cap",  new TooltipData("Maximum number of strangers interacted with per tick.", "Integer", "1 to 100") },
            { "Hospital Multiplier",     new TooltipData("Multiplies infection risk inside healthcare facilities.", "Float (Multiplier)", "0.0 to 10.0") },
            { "Hospital Contact Cap",    new TooltipData("Maximum number of patients/staff interacted with per tick.", "Integer", "1 to 100") },
            { "Hosp. Recovery Mult",     new TooltipData("How much faster an agent recovers if they reach a hospital bed.", "Float (Multiplier)", "0.5 to 5.0") },
            { "Hosp. Mortality Mod",     new TooltipData("How much the mortality rate is reduced if in a hospital bed.", "Float (Multiplier)", "0.0 to 1.0") },
            { "Commercial Propensity",   new TooltipData("The chance an agent decides to visit a commercial building after work.", "Float (%)", "0.0 to 1.0") },
            { "Work Shift Starts",       new TooltipData("The time window (in hours) when agents leave home for work.", "Float (24hr)", "0.0 to 24.0") },
            { "Work Shift Ends",         new TooltipData("The time window (in hours) when agents leave work for the day.", "Float (24hr)", "0.0 to 24.0") },
            { "Vac. Comm Preference",    new TooltipData("The probability an agent chooses a commercial place over a hospital for a vaccine.", "Float (%)", "0.0 to 1.0") },
            { "Vaccine Wait Time",       new TooltipData("The minimum and maximum sim-hours it takes to process an agent at a clinic.", "Float (Hours)", "0.1 to 10.0") },
            { "Non-Worker Ratio",        new TooltipData("Percentage of the population that does not have a 9-5 job, instead running daytime errands.", "Float (%)", "0.0 to 1.0") },
            { "Max Leisure Hours",       new TooltipData("Maximum hours an agent will wander the city for leisure before heading home.", "Float (Hours)", "0.0 to 8.0") },
            { "Errand Duration",         new TooltipData("The min and max hours a non-worker will spend running daytime errands.", "Float (Hours)", "0.0 to 24.0") },
            { "Speed Variance",          new TooltipData("How much individual agent walking speeds differ from the global average.", "Float (%)", "0.0 to 1.0") },
            { "Shift Grace Period",      new TooltipData("How many hours an agent has to trigger their commute before they skip it.", "Float (Hours)", "0.0 to 4.0") },
            { "Evacuation Panic",        new TooltipData("The maximum randomized delay (in hours) before an agent reacts to a sudden lockdown.", "Float (Hours)", "0.0 to 4.0") },
            { "Hospitalization Abidance", new TooltipData("Compliance required for a sick agent to independently seek a hospital bed, ignoring all other schedules.", "Float (%)", "0.0 to 1.0") },
            { "Self-Quarantine Abidance", new TooltipData("Compliance required for a sick agent to independently self-quarantine at home, ignoring work and leisure.", "Float (%)", "0.0 to 1.0") },
            { "Reset",                   new TooltipData("Deletes the current simulation data, clears the RAM, and returns to the Setup tab.", "Action Button", "N/A") },
            { "Play / Pause",            new TooltipData("Freezes or resumes the simulation mathematical calculations.", "Action Button", "N/A") },
            { "Fast Forward",            new TooltipData("Cycles through the simulation calculation speeds.", "Action Button", "1x, 10x, 100x") },
            { "Export",                  new TooltipData("Saves the current timeline and simulation parameters to disk.", "Action Button", "N/A") },
            { "Show Building Colors",    new TooltipData("Visually color-codes the 3D buildings based on their type.", "Checkbox", "N/A") },
            { "Toggle Social Distancing",new TooltipData("Instantly forces agents to stay further apart, based on Abidance.", "Checkbox", "N/A") },
            { "Toggle Lockdown",         new TooltipData("Triggers a city-wide mandate based on Lockdown Abidance.", "Checkbox", "N/A") },
            { "Supply Doses",            new TooltipData("Total number of vaccines available for distribution.", "Integer", "100 to Pop Size") },
            { "Base Efficacy",           new TooltipData("The percentage chance this vaccine successfully grants immunity.", "Float (%)", "0.0 to 1.0") },
            { "Public Abidance",         new TooltipData("The percentage of the susceptible population willing to go get vaccinated.", "Float (%)", "0.0 to 1.0") },
            { "Deploy Wave",             new TooltipData("Immediately distributes the vaccine supply to clinics.", "Action Button", "N/A") },
            { "Residential Ratio",       new TooltipData("Percentage of buildings assigned as homes.", "Float (0.0 to 1.0)", "Total <= 1.0") },
            { "Commercial Ratio",        new TooltipData("Percentage of buildings assigned as commercial places.", "Float (0.0 to 1.0)", "Total <= 1.0") },
            { "Workplace Ratio",         new TooltipData("Percentage of buildings assigned as offices.", "Float (0.0 to 1.0)", "Total <= 1.0") },
            { "Healthcare Ratio",        new TooltipData("Percentage of buildings assigned as hospitals.", "Float (0.0 to 1.0)", "Total <= 1.0") }
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
                float compLvl = Random.Range(0f, 1f);

                bool agentIsWorker = Random.value > nonWorkerRatio;
                float rolledLeisure = Random.Range(0.5f, maxLeisureHours);
                
                float assignedStart, assignedEnd;

                if (agentIsWorker)
                {
                    assignedStart = Random.Range(workShiftStartMin, workShiftStartMax);
                    assignedEnd = Random.Range(workShiftEndMin, workShiftEndMax);
                }
                else
                {
                    assignedStart = Random.Range(workShiftStartMin, workShiftStartMax);
                    assignedEnd = assignedStart + Random.Range(errandDurationMin, errandDurationMax); 
                    if (assignedEnd >= 24f) assignedEnd -= 24f; 
                }

                agents[i] = new SimulationAgent
                {
                    position                 = waypoints[homeNearestWaypoint[hIdx]],
                    moveStartPosition        = waypoints[homeNearestWaypoint[hIdx]],
                    moveEndPosition          = waypoints[homeNearestWaypoint[hIdx]],
                    targetPosition           = waypoints[homeNearestWaypoint[hIdx]],
                    personalOffset           = GetPersonalOffset(i, compLvl),
                    healthState              = assignedState,
                    scheduleState            = AgentScheduleState.Home,
                    immunityTimer            = 0f,
                    isSeekingVaccine         = false,
                    vaccineClinicID          = -1,
                    isVaccineClinicCommercial = false,
                    vaccineWaitTimer         = 0f,
                    healthcareID             = i % hospitalPositions.Length,
                    speed                    = Random.Range(agentSpeed * (1f - speedVariance), agentSpeed * (1f + speedVariance)),
                    homeID                   = hIdx,
                    
                    isWorker                 = agentIsWorker,
                    leisureDuration          = rolledLeisure,
                    workID                   = agentIsWorker ? (i % workPositions.Length) : -1,
                    workStartHour            = assignedStart,
                    workEndHour              = assignedEnd,
                    
                    commercialID             = i % commercialPositions.Length,
                    currentWaypointIndex     = homeNearestWaypoint[hIdx],
                    destinationWaypointIndex = homeNearestWaypoint[hIdx],
                    
                    complianceLevel          = compLvl,
                    commutingStartTime       = -9999f,
                    isActive                 = true,
                    isInsideBuilding         = true,
                    isEscaping               = false,
                    frustrationCounter       = 0,
                    highWatermarkDistance    = 0f
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
            var buildingParamsBox = new VisualElement { style = { marginTop = 15, paddingBottom = 10, marginBottom = 10, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f) } };
            buildingParamsBox.Add(new Label("BUILDING TRANSMISSION CONTROLS") { style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
            
            homeMultField = new FloatField("Home Multiplier:") { value = homeTransmissionMultiplier }; homeMultField.AddToClassList("inspector-field"); homeMultField.RegisterValueChangedCallback(evt => homeTransmissionMultiplier = evt.newValue);
            homeCapField = new IntegerField("Home Contact Cap:") { value = homeContactCap }; homeCapField.AddToClassList("inspector-field"); homeCapField.RegisterValueChangedCallback(evt => homeContactCap = evt.newValue);
            buildingParamsBox.Add(WrapWithInfoButton(homeMultField, "Home Multiplier")); buildingParamsBox.Add(WrapWithInfoButton(homeCapField, "Home Contact Cap"));

            workMultField = new FloatField("Workplace Multiplier:") { value = workplaceTransmissionMultiplier }; workMultField.AddToClassList("inspector-field"); workMultField.RegisterValueChangedCallback(evt => workplaceTransmissionMultiplier = evt.newValue);
            workCapField = new IntegerField("Workplace Contact Cap:") { value = workplaceContactCap }; workCapField.AddToClassList("inspector-field"); workCapField.RegisterValueChangedCallback(evt => workplaceContactCap = evt.newValue);
            buildingParamsBox.Add(WrapWithInfoButton(workMultField, "Workplace Multiplier")); buildingParamsBox.Add(WrapWithInfoButton(workCapField, "Workplace Contact Cap"));

            commMultField = new FloatField("Commercial Multiplier:") { value = commercialTransmissionMultiplier }; commMultField.AddToClassList("inspector-field"); commMultField.RegisterValueChangedCallback(evt => commercialTransmissionMultiplier = evt.newValue);
            commCapField = new IntegerField("Commercial Contact Cap:") { value = commercialContactCap }; commCapField.AddToClassList("inspector-field"); commCapField.RegisterValueChangedCallback(evt => commercialContactCap = evt.newValue);
            buildingParamsBox.Add(WrapWithInfoButton(commMultField, "Commercial Multiplier")); buildingParamsBox.Add(WrapWithInfoButton(commCapField, "Commercial Contact Cap"));

            hospMultField = new FloatField("Hospital Multiplier:") { value = hospitalTransmissionMultiplier }; hospMultField.AddToClassList("inspector-field"); hospMultField.RegisterValueChangedCallback(evt => hospitalTransmissionMultiplier = evt.newValue);
            hospCapField = new IntegerField("Hospital Contact Cap:") { value = hospitalContactCap }; hospCapField.AddToClassList("inspector-field"); hospCapField.RegisterValueChangedCallback(evt => hospitalContactCap = evt.newValue);
            buildingParamsBox.Add(WrapWithInfoButton(hospMultField, "Hospital Multiplier")); buildingParamsBox.Add(WrapWithInfoButton(hospCapField, "Hospital Contact Cap"));
            
            if (startSimButton != null && startSimButton.parent != null)
                startSimButton.parent.Insert(startSimButton.parent.IndexOf(startSimButton), buildingParamsBox);
            else setupContainer.Add(buildingParamsBox);

            var sdBox = new VisualElement { style = { marginTop = 15, paddingBottom = 10, marginBottom = 10, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f) } };
            sdBox.Add(new Label("SOCIAL DISTANCING DYNAMICS") { style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
            
            sdBox.Add(CreateSliderRow("SD Abidance:", 0f, 1f, sdAbidance, out sdAbidanceSlider, val => sdAbidance = val));
            
            sdCapMultField = new FloatField("SD Cap Multiplier:") { value = sdContactCapMultiplier }; sdCapMultField.AddToClassList("inspector-field"); sdCapMultField.RegisterValueChangedCallback(evt => sdContactCapMultiplier = evt.newValue);
            sdBox.Add(WrapWithInfoButton(sdCapMultField, "SD Cap Multiplier"));

            sdOffsetExpField = new FloatField("SD Offset Expansion:") { value = sdOffsetExpansion }; sdOffsetExpField.AddToClassList("inspector-field"); sdOffsetExpField.RegisterValueChangedCallback(evt => sdOffsetExpansion = evt.newValue);
            sdBox.Add(WrapWithInfoButton(sdOffsetExpField, "SD Offset Expansion"));
            
            sdRadiusMultField = new FloatField("SD Radius Multiplier:") { value = sdRadiusMultiplier }; sdRadiusMultField.AddToClassList("inspector-field"); sdRadiusMultField.RegisterValueChangedCallback(evt => sdRadiusMultiplier = evt.newValue);
            sdBox.Add(WrapWithInfoButton(sdRadiusMultField, "SD Radius Multiplier"));

            sdTransMultField = new FloatField("SD Trans. Multiplier:") { value = sdTransmissionMultiplier }; sdTransMultField.AddToClassList("inspector-field"); sdTransMultField.RegisterValueChangedCallback(evt => sdTransmissionMultiplier = evt.newValue);
            sdBox.Add(WrapWithInfoButton(sdTransMultField, "SD Trans. Multiplier"));

            if (startSimButton != null && startSimButton.parent != null)
                startSimButton.parent.Insert(startSimButton.parent.IndexOf(startSimButton), sdBox);
            else setupContainer.Add(sdBox);

            var behaveBox = new VisualElement { style = { marginTop = 15, paddingBottom = 10, marginBottom = 10, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f) } };
            behaveBox.Add(new Label("SOCIETAL BEHAVIOR & LOGISTICS") { style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
            
            behaveBox.Add(CreateSliderRow("Non-Worker Ratio:", 0f, 1f, nonWorkerRatio, out nonWorkerSlider, val => nonWorkerRatio = val));
            behaveBox.Add(CreateSliderRow("Max Leisure Hours:", 0f, 8f, maxLeisureHours, out leisureSlider, val => maxLeisureHours = val));
            
            behaveBox.Add(CreateMinMaxRow("Errand Duration:", errandDurationMin, errandDurationMax, "Errand Duration", 
                val => errandDurationMin = val, val => errandDurationMax = val));

            behaveBox.Add(CreateSliderRow("Commercial Propensity:", 0f, 1f, commercialPropensity, out commPropensitySlider, val => commercialPropensity = val));
            
            behaveBox.Add(CreateMinMaxRow("Work Shift Starts:", workShiftStartMin, workShiftStartMax, "Work Shift Starts", 
                val => workShiftStartMin = val, val => workShiftStartMax = val));

            behaveBox.Add(CreateMinMaxRow("Work Shift Ends:", workShiftEndMin, workShiftEndMax, "Work Shift Ends", 
                val => workShiftEndMin = val, val => workShiftEndMax = val));

            behaveBox.Add(CreateSliderRow("Vac. Comm Preference:", 0f, 1f, vaccineCommercialPreference, out vacPrefSlider, val => vaccineCommercialPreference = val));
            
            behaveBox.Add(CreateMinMaxRow("Vaccine Wait Time:", vaccineWaitTimeMin, vaccineWaitTimeMax, "Vaccine Wait Time", 
                val => vaccineWaitTimeMin = val, val => vaccineWaitTimeMax = val));

            if (startSimButton != null && startSimButton.parent != null)
                startSimButton.parent.Insert(startSimButton.parent.IndexOf(startSimButton), behaveBox);
            else setupContainer.Add(behaveBox);

            var advancedBox = new VisualElement { style = { marginTop = 15, paddingBottom = 10, marginBottom = 10, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f) } };
            advancedBox.Add(new Label("ADVANCED RULES") { style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });

            advancedBox.Add(CreateSliderRow("Speed Variance:", 0f, 1f, speedVariance, out speedVarSlider, val => speedVariance = val));
            advancedBox.Add(CreateSliderRow("Shift Grace Period:", 0f, 4f, shiftGracePeriodHours, out gracePeriodSlider, val => shiftGracePeriodHours = val));
            advancedBox.Add(CreateSliderRow("Evacuation Panic:", 0f, 4f, evacuationStaggerMax, out evacStaggerSlider, val => evacuationStaggerMax = val));

            hospRecovField = new FloatField("Hosp. Recovery Mult:") { value = hospitalRecoveryMultiplier }; hospRecovField.AddToClassList("inspector-field"); hospRecovField.RegisterValueChangedCallback(evt => hospitalRecoveryMultiplier = evt.newValue);
            advancedBox.Add(WrapWithInfoButton(hospRecovField, "Hosp. Recovery Mult"));

            hospMortModField = new FloatField("Hosp. Mortality Mod:") { value = hospitalMortalityModifier }; hospMortModField.AddToClassList("inspector-field"); hospMortModField.RegisterValueChangedCallback(evt => hospitalMortalityModifier = evt.newValue);
            advancedBox.Add(WrapWithInfoButton(hospMortModField, "Hosp. Mortality Mod"));

            immDurField = new FloatField("Immunity Duration (Days):") { value = immunityDurationDays };
            immDurField.AddToClassList("inspector-field");
            immDurField.RegisterValueChangedCallback(evt => immunityDurationDays = evt.newValue);
            advancedBox.Add(WrapWithInfoButton(immDurField, "Immunity Duration (Days)"));

            advancedBox.Add(CreateSliderRow("Natural Immunity Efficacy:", 0f, 1f, naturalImmunityEfficacy, out natImmSlider, val => naturalImmunityEfficacy = val));
            advancedBox.Add(CreateSliderRow("Lockdown Abidance:",         0f, 1f, lockdownAbidance, out lockAbidSlider, val => lockdownAbidance = val));
            
            advancedBox.Add(CreateSliderRow("Hospitalization Abidance:", 0f, 1f, sickHospitalThreshold, out sickHospSlider, val => sickHospitalThreshold = val));
            advancedBox.Add(CreateSliderRow("Self-Quarantine Abidance:", 0f, 1f, sickStayHomeThreshold, out sickStaySlider, val => sickStayHomeThreshold = val));

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
                isSocialDistancingToggle = evt.newValue;
            });

        loadingOverlay = new LoadingOverlay();
        root.Add(loadingOverlay);

        SwitchTab(true);
        CreateBuildingPopup(root);
        CreateDocumentationPopup(root);
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
        isVaccineCampaignActive = false;
        currentVaccineEfficacy       = 0f;
        currentVaccineAbidance       = 0f;
        totalVaccinesAvailable       = 0;

        timelineHistory.Clear();
        baselineHistory.Clear();

        if (lineGraph != null)        { lineGraph.maxScrubableDay = 0; lineGraph.ClearData(); }
        if (activeCasesGraph != null)  activeCasesGraph.ClearData();
        if (barGraph != null)          barGraph.ClearData();
        if (simulationStats != null)   simulationStats.ClearData();

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

    private void TriggerVaccineWave(int doses, float efficacy, float abidance)
    {
        isVaccineCampaignActive = true;
        currentVaccineEfficacy       = efficacy;
        currentVaccineAbidance       = abidance;
        totalVaccinesAvailable       = doses;

        int activeHospitals = hospitalPositions.Length;
        int activeCommercials = distributeToCommercial ? commercialPositions.Length : 0;
        int totalClinics = activeHospitals + activeCommercials;

        if (totalClinics == 0) return; 

        // 1. Calculate the base amount and the exact remainder
        int baseVaccinesPerClinic = doses / totalClinics;
        int leftoverVaccines = doses % totalClinics;

        // 2. Distribute the even base amounts
        for (int i = 0; i < hospitalInventory.Length; i++) {
            hospitalInventory[i] = baseVaccinesPerClinic;
        }
        
        if (distributeToCommercial) {
            for (int i = 0; i < commercialInventory.Length; i++) {
                commercialInventory[i] = baseVaccinesPerClinic;
            }
        }

        // 3. Distribute the leftovers 1-by-1 (Round-robin style)
        int currentLeftover = leftoverVaccines;
        
        for (int i = 0; i < hospitalInventory.Length && currentLeftover > 0; i++) {
            hospitalInventory[i]++;
            currentLeftover--;
        }
        
        if (distributeToCommercial) {
            for (int i = 0; i < commercialInventory.Length && currentLeftover > 0; i++) {
                commercialInventory[i]++;
                currentLeftover--;
            }
        }
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
        if (hospMultField != null)  hospMultField.value  = pData.hospTransMult; 
        if (bedsField != null)      bedsField.value      = pData.beds;
        if (commToggle != null)     commToggle.value     = pData.distComm;
        if (natImmSlider != null)   natImmSlider.value   = pData.natImmunity;
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

    private VisualElement CreateTimeStepper(string labelText, float currentVal, Action<float> onChanged)
    {
        var wrap = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginRight = 2 } };
        
        var lbl = new Label(labelText) { style = { color = new Color(0.8f, 0.8f, 0.8f), fontSize = 10, marginRight = 4 } };
        wrap.Add(lbl);

        var field = new FloatField() { value = Mathf.Round(currentVal), style = { width = 35 } };
        field.AddToClassList("inspector-field");
        field.style.marginRight = 0; 
        field.style.marginLeft = 0;

        var btnColumn = new VisualElement { style = { flexDirection = FlexDirection.Column, justifyContent = Justify.Center, marginLeft = 1 } };
        
        var upBtn = new Button(() => {
            field.value = Mathf.Clamp(Mathf.Round(field.value) + 1f, 0f, 24f);
            onChanged(field.value);
        }) { text = "▲" };
        
        upBtn.style.width = 16; upBtn.style.height = 12; upBtn.style.fontSize = 7;
        upBtn.style.paddingTop = 0; upBtn.style.paddingBottom = 0; upBtn.style.paddingLeft = 0; upBtn.style.paddingRight = 0;
        upBtn.style.marginTop = 0; upBtn.style.marginBottom = 0;
        upBtn.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f); upBtn.style.color = Color.white;
        
        upBtn.style.borderTopWidth = 0;
        upBtn.style.borderBottomWidth = 0;
        upBtn.style.borderLeftWidth = 0;
        upBtn.style.borderRightWidth = 0;

        var dnBtn = new Button(() => {
            field.value = Mathf.Clamp(Mathf.Round(field.value) - 1f, 0f, 24f);
            onChanged(field.value);
        }) { text = "▼" };
        
        dnBtn.style.width = 16; dnBtn.style.height = 12; dnBtn.style.fontSize = 7;
        dnBtn.style.paddingTop = 0; dnBtn.style.paddingBottom = 0; dnBtn.style.paddingLeft = 0; dnBtn.style.paddingRight = 0;
        dnBtn.style.marginTop = 1; dnBtn.style.marginBottom = 0;
        dnBtn.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f); dnBtn.style.color = Color.white;

        dnBtn.style.borderTopWidth = 0;
        dnBtn.style.borderBottomWidth = 0;
        dnBtn.style.borderLeftWidth = 0;
        dnBtn.style.borderRightWidth = 0;
        
        btnColumn.Add(upBtn);
        btnColumn.Add(dnBtn);

        field.RegisterValueChangedCallback(evt => {
            float val = Mathf.Clamp(Mathf.Round(evt.newValue), 0f, 24f);
            if (val != evt.newValue) field.SetValueWithoutNotify(val);
            onChanged(val);
        });

        wrap.Add(field);
        wrap.Add(btnColumn);
        return wrap;
    }

    private VisualElement CreateMinMaxRow(string title, float minVal, float maxVal, string tooltipKey, Action<float> onMinChange, Action<float> onMaxChange)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6, width = Length.Percent(100) } };
        
        var titleLabel = new Label(title) { style = { width = 110, color = Color.white, fontSize = 11, whiteSpace = WhiteSpace.Normal } };
        row.Add(titleLabel);

        var steppersContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, justifyContent = Justify.FlexEnd } };

        var minStepper = CreateTimeStepper("Min:", minVal, onMinChange);
        var maxStepper = CreateTimeStepper("Max:", maxVal, onMaxChange);

        steppersContainer.Add(minStepper);
        steppersContainer.Add(maxStepper);

        row.Add(steppersContainer);

        if (tooltips.ContainsKey(tooltipKey)) row.Add(CreateInfoButton(tooltipKey));

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

    float3 GetPersonalOffset(int agentIndex, float complianceLevel)
    {
        var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(agentIndex * 7919));
        float range = destinationOffsetRange;
        
        if (isSocialDistancingToggle && complianceLevel <= sdAbidance) {
            range *= sdOffsetExpansion;
        }

        return new float3(
            rng.NextFloat(-range, range),
            0f,
            rng.NextFloat(-range, range)
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
    
    private void CreateDocumentationPopup(VisualElement root)
    {
        var gameView = root.Q<VisualElement>("GameViewArea");
        var targetRoot = gameView != null ? gameView : root;

        // 1. The "INFO" Button
        var infoBtn = new Button { text = "i" };
        infoBtn.style.position = Position.Absolute;
        infoBtn.style.top = 15;
        infoBtn.style.right = 15;
        infoBtn.style.width = 30;
        infoBtn.style.height = 30;
        infoBtn.style.borderTopLeftRadius = 15;
        infoBtn.style.borderTopRightRadius = 15;
        infoBtn.style.borderBottomLeftRadius = 15;
        infoBtn.style.borderBottomRightRadius = 15;
        infoBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f); // Matched UI Gray
        infoBtn.style.color = Color.white;
        infoBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
        infoBtn.style.fontSize = 16;
        infoBtn.style.borderTopWidth = 1; infoBtn.style.borderBottomWidth = 1;
        infoBtn.style.borderLeftWidth = 1; infoBtn.style.borderRightWidth = 1;
        infoBtn.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f);
        infoBtn.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f);
        infoBtn.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f);
        infoBtn.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);

        infoBtn.RegisterCallback<PointerEnterEvent>(evt => infoBtn.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f));
        infoBtn.RegisterCallback<PointerLeaveEvent>(evt => infoBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f));

        // 2. The Dark Overlay Background
        docOverlay = new VisualElement();
        docOverlay.style.position = Position.Absolute;
        docOverlay.style.top = 0; docOverlay.style.bottom = 0;
        docOverlay.style.left = 0; docOverlay.style.right = 0;
        docOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
        docOverlay.style.display = DisplayStyle.None;
        docOverlay.style.alignItems = Align.Center;
        docOverlay.style.justifyContent = Justify.Center;

        // 3. The Main Content Window
        var docWindow = new VisualElement();
        docWindow.style.width = Length.Percent(80);
        docWindow.style.height = Length.Percent(85);
        docWindow.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        docWindow.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
        docWindow.style.borderTopWidth = 1; docWindow.style.borderBottomWidth = 1;
        docWindow.style.borderLeftWidth = 1; docWindow.style.borderRightWidth = 1;
        docWindow.style.borderTopLeftRadius = 8; docWindow.style.borderTopRightRadius = 8;
        docWindow.style.borderBottomLeftRadius = 8; docWindow.style.borderBottomRightRadius = 8;
        docWindow.style.flexDirection = FlexDirection.Column;

        // 4. Window Header & Close Button
        var headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, paddingLeft = 20, paddingRight = 20, paddingTop = 15, paddingBottom = 15, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f) } };
        var title = new Label("SYSTEM DOCUMENTATION & TRANSPARENCY") { style = { color = Color.white, fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold } };
        
        // Using the symmetrical '×' symbol instead of a capital 'X'
        var closeBtn = new Button { text = "×" };
        closeBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f); // Matched UI Gray
        closeBtn.style.color = Color.white;
        closeBtn.style.borderTopWidth = 1; closeBtn.style.borderBottomWidth = 1; 
        closeBtn.style.borderLeftWidth = 1; closeBtn.style.borderRightWidth = 1;
        closeBtn.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f);
        closeBtn.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f);
        closeBtn.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f);
        closeBtn.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
        closeBtn.style.borderTopLeftRadius = 4; closeBtn.style.borderTopRightRadius = 4;
        closeBtn.style.borderBottomLeftRadius = 4; closeBtn.style.borderBottomRightRadius = 4;
        closeBtn.style.width = 30; closeBtn.style.height = 30;
        closeBtn.style.fontSize = 22; // Slightly larger so the '×' looks perfectly centered
        closeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
        
        // Hover effects: Turns red on hover to indicate a "Close" action
        closeBtn.RegisterCallback<PointerEnterEvent>(evt => closeBtn.style.backgroundColor = new Color(0.8f, 0.3f, 0.3f, 1f));
        closeBtn.RegisterCallback<PointerLeaveEvent>(evt => closeBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f));
        
        closeBtn.clicked += () => docOverlay.style.display = DisplayStyle.None;
        infoBtn.clicked += () => docOverlay.style.display = DisplayStyle.Flex;

        headerRow.Add(title);
        headerRow.Add(closeBtn);
        docWindow.Add(headerRow);

        // 5. The Scrollable Text Area
        docScrollView = new ScrollView(ScrollViewMode.Vertical);
        docScrollView.style.flexGrow = 1;
        docScrollView.style.paddingTop = 15; docScrollView.style.paddingBottom = 20;
        docScrollView.style.paddingLeft = 25; docScrollView.style.paddingRight = 25;
        StyleCustomScrollbar(docScrollView); // Re-use your existing scrollbar style!

        PopulateDocumentationText(docScrollView);

        docWindow.Add(docScrollView);
        docOverlay.Add(docWindow);
        
        targetRoot.Add(docOverlay);
        targetRoot.Add(infoBtn);
    }

    private void PopulateDocumentationText(ScrollView sv)
{
    // ----------------------------------------------------------
    //  OVERVIEW
    // ----------------------------------------------------------
    AddDocSection(sv, "WHAT IS THIS SIMULATION?",
        "This is an agent-based epidemiological simulator. Unlike traditional equation-based models (which treat an entire population as a single averaged blob), this system simulates every individual in the city as a living, breathing person with their own schedule, personality, compliance score, workplace, home, and daily routine.\n\n" +
        "The result is a system that can produce emergent, realistic phenomena — silent super-spreaders, healthcare system collapse, lockdown fatigue, vaccination hesitancy — behaviors that equation-based models structurally cannot capture.\n\n" +
        "The simulation runs inside the Unity game engine and is built around three major technical pillars: a multithreaded job system for performance, a spatial partition grid for virus propagation, and a compliance-based behavioral engine for human decision-making.");
 
    // ----------------------------------------------------------
    //  PART 1: ENGINE ARCHITECTURE
    // ----------------------------------------------------------
    AddDocSection(sv, "PART 1 — ENGINE ARCHITECTURE", "");
 
    AddDocSection(sv, "1.1  The Two-Tick System",
        "The engine separates concerns into two distinct update loops running every frame:\n\n" +
        "<b>Visual Ticks (every frame):</b> The main Unity thread is responsible only for moving agent capsules smoothly across the screen. This is purely cosmetic — the agent's visual position is interpolated between its last known and next known world position. This is why the simulation always looks fluid regardless of fast-forward speed.\n\n" +
        "<b>Epic Ticks (every 1 real second of simulation time):</b> This is where science happens. Schedule logic, virus propagation, immunity decay, hospitalization decisions, and vaccine distribution all run here. Epic Ticks fire on a fixed interval and are dispatched across all available CPU cores in parallel using the Unity C# Job System.");
 
    AddDocSection(sv, "1.2  The C# Job System & Burst Compiler",
        "Running logic for 10,000+ agents sequentially on a single thread would reduce the simulation to 1–2 frames per second. To solve this, the simulation uses Unity's Job System — a framework that divides work into 'jobs' and distributes them across every logical core on your CPU simultaneously.\n\n" +
        "Four primary jobs run each Epic Tick in a carefully sequenced pipeline:\n\n" +
        "• <b>ScheduleUpdateJob:</b> Evaluates each agent's daily schedule. Should they be commuting to work? Heading home? Visiting a commercial zone? Obeying the lockdown? This job writes each agent's target destination.\n\n" +
        "• <b>WaypointAssignJob:</b> Translates abstract destinations ('go to workplace') into concrete pathfinding instructions by selecting the nearest waypoint node from a pre-baked navigation graph.\n\n" +
        "• <b>UpdateGridJob / IndoorMappingJob:</b> Places every agent into a spatial partition grid based on their current position, or registers them as 'indoors' at a specific building.\n\n" +
        "• <b>EpidemicJob:</b> The core virus simulation. For each infected agent, it queries the spatial grid to find nearby susceptible agents and rolls probability checks for transmission.\n\n" +
        "The Burst Compiler converts these jobs into highly optimised native machine code, which is why the simulation can run at 100× speed without destroying your CPU.");
 
    AddDocSection(sv, "1.3  The Spatial Partition Grid (Virus Propagation)",
        "The naive approach to virus propagation — check every agent against every other agent — costs O(n²) calculations. At 10,000 agents, that is 100 million distance checks per tick. The simulation would crash.\n\n" +
        "Instead, the city is overlaid with an invisible mathematical grid (configurable cell size, default 10 units). Every agent is assigned to one cell. When checking for virus transmission, an infected agent only examines agents in its own cell and the 8 immediately adjacent cells — typically fewer than 30 agents instead of 10,000.\n\n" +
        "The grid is rebuilt from scratch every Epic Tick, which ensures accuracy even as agents move. The <b>Distance Checks Tracker</b> in the diagnostics panel shows you how many pair-checks were performed in the last tick — this number is a direct measure of the engine's computational load.");
 
    AddDocSection(sv, "1.4  The Compliance Engine (Personality System)",
        "When an agent is spawned, it rolls a single hidden value called its <b>Compliance Score</b> — a random float between 0.0 and 1.0. This score is permanent and acts as the agent's personality for its entire simulated life.\n\n" +
        "This score governs almost every behavioural decision:\n\n" +
        "• Does this agent obey the lockdown?\n" +
        "• Does this agent stay home when sick?\n" +
        "• Does this agent go to hospital when ill?\n" +
        "• Is this agent willing to get vaccinated?\n" +
        "• Does this agent keep their distance from others?\n\n" +
        "Critically, compliance thresholds work as hard cutoffs, not random rolls. If you set Lockdown Abidance to 0.80, every agent with a compliance score above 0.80 obeys the lockdown perfectly and permanently. Every agent below 0.80 ignores it completely. This produces realistic permanent super-spreaders — the 15% who were always going to break the rules, every day, regardless of consequences.");
 
    AddDocSection(sv, "1.5  Indoor vs. Outdoor Transmission",
        "The simulation distinguishes between two spatial contexts for virus propagation:\n\n" +
        "<b>Outdoor (Spatial Grid):</b> Agents moving between waypoints in open space are tracked on the spatial grid. The base Infection Radius and Transmission Rate apply here, modified by any active Social Distancing parameters.\n\n" +
        "<b>Indoor (Building Map):</b> Agents registered as 'inside' a specific building are placed into a separate indoor hash map keyed by building ID and type. Indoor transmission uses the building's specific Transmission Multiplier and Contact Cap instead of the raw spatial grid. This is why homes can be higher-risk than commercial spaces — families are trapped indoors together, while shops have ventilation and controlled flow.\n\n" +
        "An agent cannot simultaneously contribute to both maps in the same tick.");
 
    AddDocSection(sv, "1.6  The Timeline & Branching System",
        "Every time the simulation advances to a new in-game day, it saves a complete snapshot of every agent's state to a timeline history dictionary. This enables two powerful features:\n\n" +
        "<b>Scrubbing:</b> Clicking a past day on the line graph loads that day's snapshot directly into the active agent array. You can review the exact state of the city at any point in history.\n\n" +
        "<b>Branching:</b> If you scrub to a past day and press Play, the simulation enters an 'Alternate Timeline'. It creates a ghost copy of the original run for comparison on the line graph. You can then commit the new timeline (discarding the original) or discard it (restoring the original). This allows controlled what-if experiments — 'What if I had deployed vaccines on Day 15 instead of Day 30?'");
 
    AddDocSection(sv, "1.7  Known Limitations",
        "• <b>No true pathfinding (A*):</b> Agents navigate via a pre-baked waypoint graph with nearest-node selection. This is fast and scalable, but agents can occasionally get stuck near building edges. The Stuck Timeout parameter exists specifically to detect and recover stuck agents automatically.\n\n" +
        "• <b>No age demographics:</b> All agents share the same mortality curve. There is no modelling of elderly populations being disproportionately affected. The Mortality Rate is a flat global value.\n\n" +
        "• <b>No contact tracing:</b> The simulation does not track infection chains. You cannot determine which agent infected which.\n\n" +
        "• <b>Hospital overflow is soft:</b> When hospital beds are full, agents still receive reduced (but not zero) hospitalisation benefits. There is no hard 'turned away' mechanic.\n\n" +
        "• <b>Performance ceiling:</b> Above ~20,000 agents on a mid-range CPU, frame rate will degrade. The 100× fast-forward mode reduces visual smoothness significantly at high populations.\n\n" +
        "• <b>Single-city model:</b> There is no inter-region travel, importation of cases from outside, or cross-border dynamics.");
 
    // ----------------------------------------------------------
    //  PART 2: VIRUS PARAMETERS
    // ----------------------------------------------------------
    AddDocSection(sv, "PART 2 — VIRUS PARAMETERS", "");
 
    AddDocSection(sv, "Population Size",
        "<b>Default: 10,000 | Range: 100 – 30,000</b>\n\n" +
        "The total number of agents spawned at simulation start. This is the single biggest lever on CPU cost — the Job System scales linearly with agent count.\n\n" +
        "• <b>Below 1,000:</b> Results become statistically unreliable. Random noise dominates; a single super-spreader can wipe out the city or an outbreak can fail to start entirely due to luck. Use only for rapid setup testing.\n" +
        "• <b>1,000 – 5,000:</b> Good for fast experiments. Results are broadly correct but show high variance between runs.\n" +
        "• <b>5,000 – 15,000:</b> The recommended operating range. Results are statistically stable and performance is manageable on modern hardware.\n" +
        "• <b>Above 15,000:</b> Expect frame rate drops, especially at 100× speed. The Epic Tick computation time increases significantly.");
 
    AddDocSection(sv, "Initial Infected",
        "<b>Default: 10 | Range: 1 – Population Size</b>\n\n" +
        "The number of agents that begin the simulation in the Infected state ('Patient Zeros'). These agents are placed at random home positions and immediately begin their daily schedule, unknowingly spreading the virus from Day 1.\n\n" +
        "• <b>1 – 5:</b> Simulates a true outbreak origin. There is a meaningful probability the outbreak simply fails — the infected agent recovers before contacting a susceptible one, especially in a large city. Run multiple simulations; results will vary dramatically.\n" +
        "• <b>10 – 50:</b> The standard starting condition. Reliably triggers an epidemic while still allowing intervention to make a meaningful difference.\n" +
        "• <b>100+:</b> Simulates a scenario where the virus has already silently spread before detection. The curve rises almost immediately. Intervention has less time to act.");
 
    AddDocSection(sv, "Infection Radius",
        "<b>Default: 3.0 units | Range: 0.1 – 10.0+</b>\n\n" +
        "The physical distance (in world units, approximately 1 unit = 1 metre) within which an infected agent can transmit the virus to a susceptible agent outdoors. This is used for the spatial grid transmission check.\n\n" +
        "• <b>0.5 – 1.0:</b> Contact transmission only. Agents must nearly touch to spread the virus. Models diseases requiring physical contact.\n" +
        "• <b>2.0 – 4.0:</b> Droplet transmission. The realistic range for respiratory viruses in open air. Agents standing or walking near each other can spread infection.\n" +
        "• <b>5.0+:</b> Airborne transmission. Agents across a significant distance can spread infection. The spatial grid will flag many more pairs per tick, increasing both transmission rates and CPU cost.");
 
    AddDocSection(sv, "Transmission Rate",
        "<b>Default: 0.30 (30%) | Range: 0.0 – 1.0</b>\n\n" +
        "The probability per Epic Tick that a susceptible agent within the Infection Radius of an infected agent will contract the virus. Think of this as 'viral load strength'.\n\n" +
        "• <b>0.05 – 0.15:</b> Low virulence. The virus struggles to sustain itself. With small Initial Infected counts, the outbreak may fizzle. Models measles-like scenarios only if combined with a large radius.\n" +
        "• <b>0.20 – 0.40:</b> Moderate virulence. Represents a realistic respiratory virus. The epidemic will grow but leaves room for interventions to flatten the curve.\n" +
        "• <b>0.60+:</b> High virulence. Epidemics explode rapidly. Interventions need to be deployed almost immediately after simulation start to have any effect.\n\n" +
        "Note: This rate is further modified by building multipliers indoors and by Social Distancing multipliers when that toggle is active.");
 
    AddDocSection(sv, "Recovery Time",
        "<b>Default: 14.0 days | Range: 1.0 – 60.0+</b>\n\n" +
        "The number of in-game days an agent remains in the Infected state before transitioning to either Recovered or Dead. During this entire window, the agent is contagious.\n\n" +
        "• <b>3 – 5 days:</b> Fast-burning disease. The epidemic peaks and crashes quickly. Hospital systems are stressed in a short window but recover fast.\n" +
        "• <b>10 – 21 days:</b> The realistic range for most respiratory illnesses. Provides enough time for behavioural interventions to have a measurable effect before individual cases resolve.\n" +
        "• <b>30+ days:</b> Chronic infectious period. A small initial infected population can sustain high case counts for very long periods. Hospital capacity becomes a severe bottleneck.");
 
    AddDocSection(sv, "Mortality Rate",
        "<b>Default: 0.02 (2%) | Range: 0.0 – 1.0</b>\n\n" +
        "The probability that an individual infected agent will die rather than recover at the end of their infectious period. This is a flat rate — it does not vary by age, pre-existing condition, or healthcare access unless modified by the Hospital Mortality Modifier.\n\n" +
        "• <b>0.001 – 0.01:</b> Low mortality. Most scenarios end with a large Recovered population and a small Dead count. Healthcare systems are stressed by volume but not mortality.\n" +
        "• <b>0.02 – 0.05:</b> Moderate mortality, comparable to historical influenza pandemics. Deaths accumulate visibly across the simulation timeline.\n" +
        "• <b>0.10+:</b> High mortality. Combine with a fast-spreading virus and the city can lose a significant fraction of its population within 30 simulation days. Note that high mortality also removes infected agents from the pool faster, which can paradoxically slow the epidemic.");
 
    // ----------------------------------------------------------
    //  PART 3: IMMUNITY & VACCINES
    // ----------------------------------------------------------
    AddDocSection(sv, "PART 3 — IMMUNITY & VACCINES", "");
 
    AddDocSection(sv, "Immunity Duration (Days)",
        "<b>Default: 90.0 days | Range: 1.0 – 365.0+</b>\n\n" +
        "How many in-game days an agent remains protected by immunity — either from vaccination or from natural recovery — before reverting to Susceptible. Immunity is tracked per-agent via a countdown timer.\n\n" +
        "• <b>30 days:</b> Short-lived immunity. Recovered agents quickly re-enter the susceptible pool, enabling secondary waves even in a heavily recovered population.\n" +
        "• <b>90 days:</b> Seasonal immunity. Matches realistic post-recovery immunity windows for many respiratory illnesses.\n" +
        "• <b>365+ days:</b> Durable immunity. Once a wave passes, reinfection is rare. The epidemic ends cleanly and does not resurge within the simulated window.");
 
    AddDocSection(sv, "Natural Immunity Efficacy",
        "<b>Default: 1.0 (100%) | Range: 0.0 – 1.0</b>\n\n" +
        "The base protection level granted when an agent recovers from the virus naturally (without vaccination). At 1.0, recovered agents are fully immune for the entire Immunity Duration. At 0.0, recovery grants no protection at all.\n\n" +
        "• <b>1.0:</b> Sterilising immunity. No recovered agent can be reinfected during their immunity window.\n" +
        "• <b>0.5 – 0.8:</b> Partial immunity. Recovered agents have a reduced probability of reinfection, simulating waning or incomplete immune memory.\n" +
        "• <b>0.0:</b> No natural immunity. The population never builds up a recovered buffer, and waves repeat indefinitely until the virus burns out by random luck.");
 
    AddDocSection(sv, "Vaccine: Supply Doses",
        "<b>Range: 1 – Population Size (or more for stockpiling)</b>\n\n" +
        "The total number of vaccine doses that will be distributed across all clinic locations when you press 'Deploy Wave'. Doses are divided evenly between all active clinic locations (hospitals and, if enabled, commercial buildings), with any remainder distributed one-by-one in round-robin order to ensure no dose is wasted.\n\n" +
        "• <b>Fewer doses than the susceptible population:</b> The most realistic scenario. Watch compliance thresholds determine who actually goes to get vaccinated from the available pool.\n" +
        "• <b>Doses equal to the population:</b> Simulates a fully funded national campaign with universal access. Outcome is then determined entirely by public abidance.");
 
    AddDocSection(sv, "Vaccine: Base Efficacy",
        "<b>Default: 0.0 (deploy to set) | Range: 0.0 – 1.0</b>\n\n" +
        "The probability that a vaccine dose administered to an eligible agent actually confers immunity. An efficacy of 0.85 means 15% of vaccinated agents gain no protection and remain susceptible, though they will not seek re-vaccination.\n\n" +
        "• <b>0.50:</b> Low efficacy. The vaccine slows but does not prevent the epidemic. Herd immunity thresholds require very high coverage to compensate.\n" +
        "• <b>0.90 – 0.95:</b> High efficacy. A realistic target for well-developed vaccines. Even moderate abidance can drive the effective reproduction number below 1.0.");
 
    AddDocSection(sv, "Vaccine: Public Abidance",
        "<b>Default: 0.0 (deploy to set) | Range: 0.0 – 1.0</b>\n\n" +
        "The compliance threshold required for an eligible agent to voluntarily seek vaccination. Agents with a compliance score above this value will seek out a clinic. Agents below it will refuse, regardless of vaccine availability or efficacy.\n\n" +
        "• <b>0.3:</b> High vaccine hesitancy. Only the most compliant third of the population gets vaccinated. Herd immunity is unlikely unless the vaccine is extremely effective.\n" +
        "• <b>0.7 – 0.8:</b> Majority acceptance. Most agents seek vaccination. Combined with a high-efficacy vaccine, this is sufficient to collapse most epidemic curves.\n" +
        "• <b>1.0:</b> Universal acceptance. Every eligible agent will seek vaccination. Purely tests the logistical capacity of your clinic network.");
 
    AddDocSection(sv, "Vaccine: Commercial Distribution",
        "<b>Default: Enabled</b>\n\n" +
        "When enabled, vaccine doses are split between hospitals and commercial buildings, which also serve as vaccination clinics. Commercial buildings are far more numerous and spatially distributed across the city, making vaccines geographically accessible to more agents.\n\n" +
        "Disabling this concentrates the entire supply in hospitals only. In cities with few healthcare buildings, this creates geographic bottlenecks — agents whose nearest healthcare facility is far away may take many sim-hours to reach and receive a dose.\n\n" +
        "The 'Vaccine Commercial Preference' slider (in Societal Behavior) controls the probability that an agent will choose a commercial clinic over a hospital clinic when both have doses available.");
 
    AddDocSection(sv, "Vaccine: Wait Time (Min / Max)",
        "<b>Default: 1.0 – 3.0 sim-hours | Range: 0.1 – 10.0+</b>\n\n" +
        "When an agent arrives at a vaccination clinic, they do not receive the dose instantly. They wait a random duration between these two values, simulating queue time and administration. The agent is 'locked' at the clinic during this window.\n\n" +
        "• <b>Low wait time (0.1 – 0.5 hours):</b> High-throughput clinics. Doses are administered quickly. The vaccination campaign completes faster but requires agents to detour from normal schedules for a shorter period.\n" +
        "• <b>High wait time (4+ hours):</b> Overwhelmed clinics. Agents spend a large fraction of the day waiting. This can cause secondary disruption — agents miss work, arrive home late, and interact with more agents at the clinic than normal.");
 
    // ----------------------------------------------------------
    //  PART 4: BUILDING TRANSMISSION
    // ----------------------------------------------------------
    AddDocSection(sv, "PART 4 — BUILDING TRANSMISSION CONTROLS", "");
 
    AddDocSection(sv, "How Indoor Transmission Works",
        "When an agent enters a building, it is removed from the outdoor spatial grid and placed into an indoor building map. Virus transmission inside buildings does not use the spatial grid at all — instead, all agents inside the same building are considered to be within potential contact range of each other, up to the building's Contact Cap.\n\n" +
        "The indoor transmission probability is calculated as:\n" +
        "<b>Base Rate × Building Transmission Multiplier × (optional Social Distancing modifiers)</b>\n\n" +
        "The Contact Cap then limits how many other agents a single agent interacts with per tick, regardless of how many people are physically present in the building. This prevents a 200-person office from generating 200 transmission checks — it stops at the Contact Cap.");
 
    AddDocSection(sv, "Home Multiplier & Contact Cap",
        "<b>Multiplier Default: 1.5 | Cap Default: 4</b>\n\n" +
        "<b>Multiplier:</b> Residential buildings apply a 1.5× boost to the base transmission rate. Homes are enclosed, poorly ventilated spaces where family members share air for many hours — this is epidemiologically accurate. Household transmission is typically the dominant vector in early epidemic phases.\n\n" +
        "<b>Contact Cap:</b> An agent at home interacts with at most 4 other agents per tick, simulating a realistic household size. In small cities with many single-occupant homes, this cap is rarely reached.\n\n" +
        "• <b>Raising the multiplier to 3.0+:</b> Makes homes dangerous congregation points. Even lockdowns will not contain the virus because family transmission at home is rampant.\n" +
        "• <b>Lowering the cap to 1–2:</b> Simulates isolated single-occupant households. Home transmission nearly disappears.");
 
    AddDocSection(sv, "Workplace Multiplier & Contact Cap",
        "<b>Multiplier Default: 1.2 | Cap Default: 10</b>\n\n" +
        "<b>Multiplier:</b> Workplaces apply a moderate 1.2× multiplier. Offices are enclosed but have controlled airflow and agents maintain some social norms of distance. They are less risky per-minute than homes, but agents spend more hours there.\n\n" +
        "<b>Contact Cap:</b> An agent at work interacts with at most 10 coworkers per tick, simulating an open-plan office environment.\n\n" +
        "• <b>Raising the cap to 30–50:</b> Simulates factory floors or dense call centres. Workplaces become major outbreak hotspots, and lockdowns that close workplaces have a dramatic effect.\n" +
        "• <b>Raising the multiplier above 2.0:</b> Models poor ventilation (e.g., meat-packing plants during COVID-19). A single infected worker can infect the majority of their shift.");
 
    AddDocSection(sv, "Commercial Multiplier & Contact Cap",
        "<b>Multiplier Default: 0.8 | Cap Default: 20</b>\n\n" +
        "<b>Multiplier:</b> Commercial spaces (shops, malls) apply a below-baseline 0.8× multiplier. These are large, better-ventilated spaces with relatively brief per-visit contact durations. They are high-throughput but lower individual risk.\n\n" +
        "<b>Contact Cap:</b> An agent in a commercial space interacts with up to 20 strangers per tick — more than a home or office, reflecting the volume of foot traffic.\n\n" +
        "• <b>Raising the multiplier above 1.0:</b> Turns malls into hotspots. With a high commercial propensity and leisure hours, this can dominate the epidemic curve.\n" +
        "• <b>Lowering the cap to 3–5:</b> Simulates controlled entry (one-way systems, capacity limits). Very effective at reducing commercial transmission even without a full lockdown.");
 
    AddDocSection(sv, "Hospital Multiplier & Contact Cap",
        "<b>Multiplier Default: 0.1 | Cap Default: 50</b>\n\n" +
        "<b>Multiplier:</b> Hospitals apply a very low 0.1× multiplier, simulating PPE, negative-pressure rooms, rigorous hygiene protocols, and spatial separation of infected from healthy patients. Despite being full of sick people, hospitals are among the safest buildings in the simulation by default.\n\n" +
        "<b>Contact Cap:</b> The cap is high (50) because hospitals house many agents simultaneously — patients, staff, and visitors. However, the low multiplier keeps the actual transmission probability minimal.\n\n" +
        "• <b>Raising the multiplier above 0.5:</b> Simulates a healthcare system under PPE shortage or overwhelm. Hospitals become significant nosocomial (in-hospital) transmission sources — a realistic concern in severe pandemic scenarios.\n" +
        "• <b>Lowering the cap:</b> Simulates strict visitor policies and patient isolation.");
 
    AddDocSection(sv, "Hospital: Recovery Multiplier",
        "<b>Default: 2.0 | Range: 0.5 – 10.0</b>\n\n" +
        "When an agent is hospitalised and occupies a bed, their effective recovery time is divided by this value. A multiplier of 2.0 means hospitalised agents recover in half the standard Recovery Time.\n\n" +
        "• <b>1.0:</b> Hospitals provide no clinical benefit — effectively a containment-only model.\n" +
        "• <b>2.0 – 3.0:</b> Realistic clinical acceleration. Represents IV fluids, antivirals, oxygen supplementation.\n" +
        "• <b>5.0+:</b> Heroic medical intervention. Hospitalised agents recover very quickly, freeing beds faster. In bed-constrained scenarios, this can be the difference between system collapse and survival.");
 
    AddDocSection(sv, "Hospital: Mortality Modifier",
        "<b>Default: 0.2 | Range: 0.0 – 1.0</b>\n\n" +
        "A multiplier applied to the base Mortality Rate for agents who are hospitalised and in a bed. A value of 0.2 means hospitalised agents face only 20% of the base mortality risk (an 80% reduction).\n\n" +
        "• <b>0.0:</b> Hospitalisation completely prevents death. Anyone who reaches a hospital lives.\n" +
        "• <b>0.5:</b> Moderate benefit. Hospitalisation halves the death risk.\n" +
        "• <b>1.0:</b> No benefit — hospital beds provide no survival advantage. Useful for modelling overwhelmed systems where clinical care has broken down entirely.");
 
    AddDocSection(sv, "Beds Per Hospital",
        "<b>Default: 50 | Range: 1 – 500+</b>\n\n" +
        "The number of physical patient beds available in each individual hospital building. When these beds are full, agents who attempt to hospitalise themselves receive no mortality or recovery benefit — they are turned away at the door and continue their normal schedule while sick.\n\n" +
        "Total city bed capacity = Beds Per Hospital × Number of Healthcare Buildings.\n\n" +
        "• <b>10 – 20 beds:</b> Critically constrained healthcare. Expect rapid bed saturation even in moderate outbreaks. This tests your ability to flatten the curve.\n" +
        "• <b>50 – 100 beds:</b> Realistic municipal hospital capacity for a mid-sized simulated population.\n" +
        "• <b>500+ beds:</b> Abundant healthcare. The system will not saturate under most epidemic scenarios. Mortality is low. Use this to study pure epidemic dynamics without healthcare as a variable.");
 
    // ----------------------------------------------------------
    //  PART 5: SOCIETAL BEHAVIOUR
    // ----------------------------------------------------------
    AddDocSection(sv, "PART 5 — SOCIETAL BEHAVIOR & LOGISTICS", "");
 
    AddDocSection(sv, "Non-Worker Ratio",
        "<b>Default: 0.25 (25%) | Range: 0.0 – 1.0</b>\n\n" +
        "The percentage of the population that does not have a standard work schedule. Non-workers (retirees, students, unemployed) instead spend a window of daytime hours running errands in commercial zones rather than commuting to a workplace.\n\n" +
        "• <b>0.0:</b> Everyone is a 9-to-5 worker. Daytime commercial zones are nearly empty. Workplace and residential transmission dominate the epidemic.\n" +
        "• <b>0.25:</b> A quarter of the city is active during work hours. Creates realistic daytime density in commercial zones.\n" +
        "• <b>0.60+:</b> Simulates high unemployment or a retiree-heavy city. Daytime commercial density is enormous, accelerating spread throughout the day regardless of workplace closures.");
 
    AddDocSection(sv, "Max Leisure Hours",
        "<b>Default: 4.0 hours | Range: 0.0 – 8.0</b>\n\n" +
        "After a worker completes their shift, they may visit commercial zones for leisure before returning home. This value sets the maximum number of hours any agent will spend on leisure activities. Each agent rolls a random leisure duration between 0 and this maximum.\n\n" +
        "• <b>0.0:</b> Workers go directly home after their shift. No post-work commercial activity. Evening commercial zones are empty.\n" +
        "• <b>2.0 – 4.0:</b> Realistic after-work socialising. Creates an 'evening rush' in commercial zones as workers leave offices.\n" +
        "• <b>8.0:</b> Agents spend their entire evening in commercial zones. This dramatically increases virus exposure and is the primary driver of nighttime epidemic growth.");
 
    AddDocSection(sv, "Errand Duration (Min / Max)",
        "<b>Default: 1.0 – 3.0 hours</b>\n\n" +
        "The range of time that non-workers spend in a commercial zone during their daytime errand. Each non-worker rolls a random duration within this window when they arrive.\n\n" +
        "• <b>Low duration (0.5 – 1.0 hours):</b> Non-workers make brief stops — quick shopping trips. Overlap time between different agents at the same location is minimised.\n" +
        "• <b>High duration (4+ hours):</b> Non-workers spend most of their day in commercial zones. Combined with a high commercial multiplier, this group becomes a primary transmission vector.");
 
    AddDocSection(sv, "Commercial Propensity",
        "<b>Default: 0.40 (40%) | Range: 0.0 – 1.0</b>\n\n" +
        "The probability that a working agent will choose to visit a commercial zone after their shift ends, before heading home. This is a per-agent random roll each day — an agent may visit on some days but not others.\n\n" +
        "• <b>0.0:</b> Workers never visit commercial areas. Commercial zones are populated only by non-workers.\n" +
        "• <b>0.4:</b> Moderate social behaviour. Roughly two out of five workers stop somewhere after work each day.\n" +
        "• <b>0.9+:</b> Highly social culture. The commercial zone is packed with off-duty workers every evening. Closing workplaces without closing commercial areas will have a limited impact.");
 
    AddDocSection(sv, "Work Shift Start & End (Min / Max)",
        "<b>Start Default: 07:00 – 09:00 | End Default: 16:00 – 18:00</b>\n\n" +
        "The window within which agents begin and end their workday. Each agent rolls a fixed start and end time on spawn, simulating different departments, shift rosters, and commuting preferences.\n\n" +
        "Widening the window staggers commutes over a longer period, reducing the synchronised morning and evening transmission spikes. Narrowing the window creates sharp 'rush hour' peaks where thousands of agents are navigating waypoints simultaneously — the most dangerous windows for outdoor transmission.\n\n" +
        "For non-workers, the 'start' time determines when their daytime errand begins, and the 'end' time is computed as start + Errand Duration.");
 
    AddDocSection(sv, "Speed Variance",
        "<b>Default: 0.25 (±25%) | Range: 0.0 – 1.0</b>\n\n" +
        "Each agent's walking speed is randomised at spawn to a value between (base speed × (1 − variance)) and (base speed × (1 + variance)). This simulates the spectrum of age, athleticism, and mobility in a real population.\n\n" +
        "• <b>0.0:</b> All agents walk at exactly the same speed. Visually robotic — agents move in perfect lockstep. This can cause bunching near waypoints as agents arrive at destinations simultaneously.\n" +
        "• <b>0.25:</b> Natural variation. Fast walkers pull ahead, slow walkers lag behind. Creates organic-looking crowd movement.\n" +
        "• <b>0.8+:</b> Extreme variation. Some agents sprint while others shuffle. Slow agents may not complete their commute before their shift grace period expires.");
 
    AddDocSection(sv, "Shift Grace Period",
        "<b>Default: 1.0 hours | Range: 0.0 – 4.0</b>\n\n" +
        "A technical tolerance window that determines how long an agent has to successfully trigger its commute after the scheduled start time. If the simulation is running at 100× fast-forward, visual frames are skipped — an agent might miss the exact simulation tick when its schedule said 'start commuting'. The grace period ensures the agent still receives the commute instruction.\n\n" +
        "• <b>0.0:</b> Zero tolerance. At very high fast-forward speeds, some agents will miss their commute and stay home or at work permanently for that day. This causes incorrect schedule behaviour.\n" +
        "• <b>1.0 – 2.0:</b> Safe for all fast-forward speeds. Agents will always trigger their schedule even if a tick was skipped.\n" +
        "• <b>4.0:</b> Very generous grace period. Agents will always trigger their commute but may start up to 4 sim-hours late, compressing work hours.");
 
    AddDocSection(sv, "Evacuation Panic (Stagger)",
        "<b>Default: 0.75 hours | Range: 0.0 – 4.0</b>\n\n" +
        "When a lockdown is suddenly declared (via the Toggle Lockdown button), agents do not all pathfind home on the same frame. Each agent waits a random delay between 0 and this value before beginning their evacuation.\n\n" +
        "• <b>0.0:</b> Simultaneous mass evacuation. All 10,000 agents attempt to pathfind home on the same frame. This creates visual 'stampede' bunching and can stress the pathfinding system.\n" +
        "• <b>0.75 hours:</b> A 45-minute staggered evacuation. Agents trickle home in a realistic, orderly fashion.\n" +
        "• <b>3.0+ hours:</b> Delayed reaction. Simulates slow information spread or disorganised coordination. Agents are still out in the city for many hours after lockdown is declared, meaning lockdown takes a long time to become effective.");
 
    // ----------------------------------------------------------
    //  PART 6: SOCIAL DISTANCING
    // ----------------------------------------------------------
    AddDocSection(sv, "PART 6 — SOCIAL DISTANCING", "");
 
    AddDocSection(sv, "How Social Distancing Works",
        "Social Distancing is an overlay policy that modifies the behaviour and transmission parameters of compliant agents. It does not create new simulation logic — it adjusts the inputs to existing systems.\n\n" +
        "When Social Distancing is toggled on, each agent checks its compliance score against the SD Abidance threshold. Compliant agents receive all four distancing modifications simultaneously. Non-compliant agents are completely unaffected — they continue behaving as though Social Distancing does not exist. This is why SD abidance below ~0.6 rarely produces a visible effect on the epidemic curve.");
 
    AddDocSection(sv, "SD Abidance",
        "<b>Default: 0.80 (80%) | Range: 0.0 – 1.0</b>\n\n" +
        "The compliance threshold required for an agent to observe Social Distancing rules. Agents with a compliance score above this value will apply all SD modifications to their behaviour.\n\n" +
        "• <b>0.5:</b> Only the most law-abiding half of the population distances. Marginal epidemic effect.\n" +
        "• <b>0.8:</b> Realistic compliance rate for a well-informed population in an early pandemic wave.\n" +
        "• <b>1.0:</b> Universal compliance. Every agent maintains social distance. Combined with effective multipliers, this nearly eliminates outdoor transmission.");
 
    AddDocSection(sv, "SD Radius Multiplier",
        "<b>Default: 0.6 | Range: 0.1 – 1.0</b>\n\n" +
        "Reduces the outdoor Infection Radius for compliant agents. A value of 0.6 means the effective outdoor infection radius drops to 60% of its base value when both the infected and susceptible agent are SD-compliant.\n\n" +
        "• <b>1.0:</b> No radius reduction — distancing has no spatial effect.\n" +
        "• <b>0.5:</b> Cuts effective outdoor transmission range in half.\n" +
        "• <b>0.1:</b> Near-contact-only transmission. Compliant agents would need to almost physically touch to spread the virus outdoors.");
 
    AddDocSection(sv, "SD Transmission Multiplier",
        "<b>Default: 0.4 | Range: 0.1 – 1.0</b>\n\n" +
        "Reduces the outdoor transmission probability for compliant agents, simulating masking, hand hygiene, and avoidance behaviours. A value of 0.4 means the transmission probability drops to 40% of its base value.\n\n" +
        "Best used in conjunction with the SD Radius Multiplier — reducing both radius and transmission probability compounds to produce a significant reduction in R₀ for the compliant fraction of the population.");
 
    AddDocSection(sv, "SD Cap Multiplier",
        "<b>Default: 0.5 | Range: 0.1 – 1.0</b>\n\n" +
        "Reduces the indoor Contact Cap for compliant agents in all building types. A value of 0.5 means a compliant agent in a building with a cap of 20 will only interact with 10 other agents per tick, simulating table limits, queue management, and voluntary spacing inside buildings.\n\n" +
        "This is arguably the most impactful SD parameter for indoor-dominant epidemics — cutting indoor contact counts in half directly halves the potential indoor transmission events per tick.");
 
    AddDocSection(sv, "SD Offset Expansion",
        "<b>Default: 2.0 | Range: 1.0 – 5.0</b>\n\n" +
        "Expands the personal offset radius that compliant agents use when arriving at a destination. Normally, agents cluster near their target waypoint within a small random offset. When distancing, this offset is multiplied by this value — agents physically spread themselves further apart at gathering locations.\n\n" +
        "• <b>1.0:</b> No physical spacing. Agents still cluster tightly at destinations.\n" +
        "• <b>2.0:</b> Agents stand roughly twice as far from each other at destinations.\n" +
        "• <b>4.0+:</b> Aggressive spacing. Combined with a small commercial contact cap, compliant agents in commercial zones barely overlap — very effective at breaking commercial transmission chains.");
 
    // ----------------------------------------------------------
    //  PART 7: MEDICAL TRIAGE
    // ----------------------------------------------------------
    AddDocSection(sv, "PART 7 — MEDICAL TRIAGE & COMPLIANCE", "");
 
    AddDocSection(sv, "The Triage Decision Tree",
        "When an agent's health state transitions to Infected, it immediately evaluates the following decision tree based on its personal Compliance Score:\n\n" +
        "<b>Step 1 — Hospital?</b> If compliance > Hospitalisation Abidance threshold: the agent abandons all scheduled activities and pathfinds directly to its assigned hospital. If a bed is available, it occupies it and receives clinical benefits.\n\n" +
        "<b>Step 2 — Stay Home?</b> If compliance > Self-Quarantine Abidance threshold (but below hospitalisation): the agent cancels work and leisure trips and stays home for the duration of the illness. It is removed from public infection chains but continues transmitting within the household.\n\n" +
        "<b>Step 3 — Denier.</b> If compliance is below both thresholds: the agent continues its normal daily schedule — commuting, working, shopping — while fully contagious. These are the simulation's super-spreaders.");
 
    AddDocSection(sv, "Hospitalisation Abidance",
        "<b>Default: 0.50 (50%) | Range: 0.0 – 1.0</b>\n\n" +
        "The minimum compliance score required for a sick agent to independently seek hospital admission. Only agents above this threshold will self-refer.\n\n" +
        "• <b>0.1:</b> Almost everyone rushes to hospital when sick. Bed capacity is saturated immediately, even with small outbreaks. The system collapses under its own compliance.\n" +
        "• <b>0.5:</b> The higher-compliance half of sick agents seek beds. Realistic for a society with strong but not universal health-seeking behaviour.\n" +
        "• <b>0.9+:</b> Only the most obedient agents seek hospitalisation. Beds are rarely full, but most infected agents receive no clinical benefit and die at higher rates.");
 
    AddDocSection(sv, "Self-Quarantine Abidance",
        "<b>Default: 0.20 (20%) | Range: 0.0 – 1.0</b>\n\n" +
        "The minimum compliance score for a sick agent to stay home rather than continue their normal schedule. This is the threshold above which sick workers will call in sick.\n\n" +
        "• <b>0.0:</b> No one self-quarantines. Every sick agent continues normal activities. The simulation models a culture of complete denial or zero information about the illness.\n" +
        "• <b>0.2:</b> Only the bottom 20% of the compliance distribution continues working while sick. The majority of sick agents quarantine at home.\n" +
        "• <b>0.8+:</b> Only the most compliant agents stay home. Everyone else continues working sick. Combined with a low hospitalisation threshold, this creates maximum public spread.");
 
    AddDocSection(sv, "Lockdown Abidance",
        "<b>Default: 0.85 (85%) | Range: 0.0 – 1.0</b>\n\n" +
        "When a city-wide lockdown is declared (via the Lockdown toggle), agents with a compliance score above this threshold will immediately return home and refuse to leave for any non-essential reason. Agents below the threshold continue their normal schedule as though no lockdown exists.\n\n" +
        "• <b>0.5:</b> Half the city ignores the lockdown. Commercial zones remain populated. The lockdown reduces but does not eliminate epidemic growth.\n" +
        "• <b>0.85:</b> Strong compliance. 85% of the population stays home. Commercial and workplace transmission drops dramatically.\n" +
        "• <b>1.0:</b> Perfect compliance. Every agent stays home. Outdoor and workplace transmission drops to near zero. Residual transmission continues only within households.");
 
    // ----------------------------------------------------------
    //  PART 8: SAVE / LOAD & EXPORT
    // ----------------------------------------------------------
    AddDocSection(sv, "PART 8 — SAVE, LOAD & EXPORT", "");
 
    AddDocSection(sv, "Saving a Simulation",
        "Pressing 'Export' pauses the simulation and opens a file dialog. The save file (.sim format) contains two packages:\n\n" +
        "1. <b>Parameter Snapshot:</b> All the configuration values active at the time of export — population size, transmission rates, building multipliers, etc. This allows the simulation to be re-initialised with the same setup on load.\n\n" +
        "2. <b>Full Timeline History:</b> A complete day-by-day archive of every agent's position and health state for every recorded day. This is what allows graph scrubbing and timeline branching to work after loading.\n\n" +
        "Warning: Large simulations (high population, many simulated days) produce large save files. Saving is performed asynchronously on a background thread to prevent the game from freezing.");
 
    AddDocSection(sv, "Loading a Simulation",
        "Pressing 'Load Save File' on the Setup tab opens a file dialog. The loader restores both the parameter snapshot and the timeline history, then calls StartSimulationFromUI() to re-initialise the engine in the same state as when the file was saved.\n\n" +
        "After loading, the line graph will be pre-populated with all historical data, and you can scrub through the timeline or press Play to continue from the saved day.\n\n" +
        "Note: The building layout (which buildings are homes, workplaces, etc.) is determined by the city scene, not the save file. If you load a save on a different city layout, agent home and work assignments may not match the intended building types.");
 
    AddDocSection(sv, "Timeline Branching (What-If Analysis)",
        "The branching system allows controlled experimental comparisons without losing your original run:\n\n" +
        "1. Run the simulation to a point of interest (e.g., Day 20, just before an outbreak peak).\n" +
        "2. Click on Day 20 in the line graph to scrub back to that moment.\n" +
        "3. Change a parameter (e.g., deploy vaccines, toggle lockdown).\n" +
        "4. Press Play — the simulation automatically enters 'Alternate Timeline' mode.\n" +
        "5. The line graph shows the ghost baseline (your original run in a muted colour) alongside the new timeline.\n" +
        "6. Use 'Commit Timeline' to keep the new run, or 'Discard Timeline' to restore the original.\n\n" +
        "Each branch permanently removes all timeline snapshots after the branch point to save memory. You cannot have more than one ghost baseline at a time.");
}


    private void AddDocSection(ScrollView sv, string title, string body)
    {
        var titleLabel = new Label(title);
        titleLabel.style.color = new Color(0.4f, 0.8f, 0.9f, 1f); // Light blue
        titleLabel.style.fontSize = 14;
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.marginTop = 15;
        titleLabel.style.marginBottom = 5;

        var bodyLabel = new Label(body);
        bodyLabel.enableRichText = true;
        bodyLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1f); // Off-white
        bodyLabel.style.fontSize = 12;
        bodyLabel.style.whiteSpace = WhiteSpace.Normal; // Allows text wrapping
        bodyLabel.style.marginBottom = 10;

        sv.Add(titleLabel);
        sv.Add(bodyLabel);
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