using UnityEngine;
using UnityEngine.UIElements; 
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public class SimulationManager : MonoBehaviour
{
    public static SimulationManager Instance;

    [Header("UI Document")]
    public UIDocument dashboardDocument;
    private VisualElement setupContainer, runtimeContainer;
    private TextField popInput, infectedInput, radiusInput, transRateInput, recoveryInput, mortalityInput;
    
    private Button startSimButton, pauseButton, playButton, fastButton, resetButton;
    
    private Button tabSetupBtn, tabRuntimeBtn; 
    private Toggle colorToggle, socialDistanceToggle, lockdownToggle;
    
    private EpidemicLineGraph lineGraph; 
    private EpidemicBarGraph barGraph; 
    private ActiveCasesGraph activeCasesGraph;
    private SimulationStatsDashboard simulationStats; 
    private VirusVaccineControls virusVaccineControls; 

    private float[] fastSpeeds = { 2f, 10f, 100f }; private int currentFastIndex = 0;

    [Header("Parameters")]
    public int populationSize = 10000; public int initialInfected = 10;
    public float infectionRadius = 3f; public float transmissionRate = 0.3f;
    public float recoveryTime = 14f; public float mortalityRate = 0.02f;
    public float minIncubationDays = 3f; public float maxIncubationDays = 6f;

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

    private int[] hospitalInventory;
    private int[] commercialInventory;

    private float baseTransmissionRate; private float baseInfectionRadius; 
    
    public bool isLockdown = false;
    public float lockdownAbidance = 0.85f; 
    
    public float realSecondsPerInGameDay = 60f; public float agentSpeed = 5f;
    public float waypointReachDistance = 5f; public float destinationOffsetRange = 8f;
    public float stuckTimeoutSimHours = 8f; public float agentGroundOffset = 0f;

    [Header("References")]
    public WaypointData waypointData; public float cellSize = 10f;
    private NativeArray<SimulationAgent> agents, agentsBuffer;
    private NativeArray<float3> waypoints;
    private NativeArray<int> neighborData, neighborStart, neighborCount;
    private NativeArray<int> nativeHomeNearest, nativeWorkNearest, nativeCommercialNearest, nativeHospitalNearest;
    private NativeArray<float3> nativeHomePositions, nativeHospitalPositions, nativeCommercialPositions; 
    private SpatialGrid spatialGrid; private NativeParallelMultiHashMap<int, int> indoorMap; 
    
    private bool initialized = false;
    private float3[] homePositions, workPositions, commercialPositions, hospitalPositions;
    private int[] homeNearestWaypoint, workNearestWaypoint, commercialNearestWaypoint, hospitalNearestWaypoint;

    private const float groundY = 0f; private float realTime = 0f;
    private float epicAccumulator = 0f; private const float epicTickInterval = 1f;
    private float fixedTickAccumulator = 0f; private float fixedTickInterval = 0.05f; 

    void Awake() { Instance = this; }
    void Start() { SetupUI(); }

    void SetupUI()
    {
        if (dashboardDocument != null && dashboardDocument.rootVisualElement != null)
        {
            var root = dashboardDocument.rootVisualElement;
            setupContainer = root.Q<VisualElement>("SetupContainer"); runtimeContainer = root.Q<VisualElement>("RuntimeContainer");
            
            if (setupContainer != null) {
                var setupScroll = new ScrollView(ScrollViewMode.Vertical);
                setupScroll.style.flexGrow = 1;
                setupScroll.contentContainer.style.paddingRight = 10; 
                
                StyleCustomScrollbar(setupScroll);
                
                var children = new List<VisualElement>(setupContainer.Children());
                foreach (var c in children) setupScroll.Add(c);
                
                setupContainer.Add(setupScroll);
            }
            
            tabSetupBtn = root.Q<Button>("TabSetupButton"); tabRuntimeBtn = root.Q<Button>("TabRuntimeButton");
            if (tabSetupBtn != null) tabSetupBtn.clicked += () => SwitchTab(true);
            if (tabRuntimeBtn != null) tabRuntimeBtn.clicked += () => SwitchTab(false);
            
            popInput = root.Q<TextField>("PopInput"); infectedInput = root.Q<TextField>("InfectedInput"); radiusInput = root.Q<TextField>("RadiusInput"); 
            transRateInput = root.Q<TextField>("TransRateInput"); recoveryInput = root.Q<TextField>("RecoveryInput"); mortalityInput = root.Q<TextField>("MortalityInput");

            if (setupContainer != null) { var allTextFields = setupContainer.Query<TextField>().ToList(); foreach (var field in allTextFields) field.AddToClassList("inspector-field"); }
            startSimButton = root.Q<Button>("StartSimButton");
            
            if (popInput != null) popInput.value = populationSize.ToString(); if (infectedInput != null) infectedInput.value = initialInfected.ToString(); if (radiusInput != null) radiusInput.value = infectionRadius.ToString(); 
            if (transRateInput != null) transRateInput.value = transmissionRate.ToString(); if (recoveryInput != null) recoveryInput.value = recoveryTime.ToString(); if (mortalityInput != null) mortalityInput.value = mortalityRate.ToString();
            
            if (setupContainer != null)
            {
                var advancedBox = new VisualElement { style = { marginTop = 15, paddingBottom = 10, marginBottom = 10, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f) } };

                var immDurField = new FloatField("Immunity Duration (Days):") { value = immunityDurationDays };
                immDurField.AddToClassList("inspector-field"); immDurField.RegisterValueChangedCallback(evt => immunityDurationDays = evt.newValue);
                advancedBox.Add(immDurField);

                advancedBox.Add(CreateSliderRow("Natural Immunity Efficacy:", 0f, 1f, naturalImmunityEfficacy, val => naturalImmunityEfficacy = val));
                advancedBox.Add(CreateSliderRow("Reinfection Resistance:", 0f, 1f, historicalImmunityEfficacy, val => historicalImmunityEfficacy = val));
                advancedBox.Add(CreateSliderRow("Lockdown Abidance:", 0f, 1f, lockdownAbidance, val => lockdownAbidance = val));

                var histRecField = new FloatField("Reinfection Recovery Boost:") { value = historicalRecoveryMultiplier };
                histRecField.AddToClassList("inspector-field"); histRecField.RegisterValueChangedCallback(evt => historicalRecoveryMultiplier = evt.newValue);
                advancedBox.Add(histRecField);

                var hospTransField = new FloatField("Hospital Trans. Multiplier:") { value = hospitalTransmissionMultiplier };
                hospTransField.AddToClassList("inspector-field"); hospTransField.RegisterValueChangedCallback(evt => hospitalTransmissionMultiplier = evt.newValue);
                advancedBox.Add(hospTransField);

                var bedsField = new IntegerField("Beds Per Hospital:") { value = hospitalBedsPerFacility };
                bedsField.AddToClassList("inspector-field"); bedsField.RegisterValueChangedCallback(evt => hospitalBedsPerFacility = evt.newValue);
                advancedBox.Add(bedsField);

                var commToggle = new Toggle("Vaccines at Commercial:") { value = distributeToCommercial };
                commToggle.AddToClassList("inspector-toggle"); commToggle.RegisterValueChangedCallback(evt => distributeToCommercial = evt.newValue);
                advancedBox.Add(commToggle);

                if (startSimButton != null && startSimButton.parent != null) {
                    startSimButton.parent.Insert(startSimButton.parent.IndexOf(startSimButton), advancedBox);
                } else {
                    setupContainer.Add(advancedBox);
                }
            }

            pauseButton = root.Q<Button>("PauseButton"); 
            playButton = root.Q<Button>("PlayButton"); 
            fastButton = root.Q<Button>("FastButton");
            resetButton = root.Q<Button>("ResetButton");

            if (pauseButton != null) { pauseButton.AddToClassList("action-button"); }
            if (playButton != null)  { playButton.AddToClassList("action-button"); }
            if (fastButton != null)  { fastButton.AddToClassList("action-button"); }
            
            if (resetButton != null) { 
                resetButton.AddToClassList("action-button"); 
                resetButton.clicked += ResetSimulation;
            }

            colorToggle = root.Q<Toggle>("ColorToggle"); socialDistanceToggle = root.Q<Toggle>("SocialDistanceToggle"); lockdownToggle = root.Q<Toggle>("LockdownToggle");
            if (colorToggle != null) colorToggle.AddToClassList("inspector-toggle");
            if (socialDistanceToggle != null) socialDistanceToggle.AddToClassList("inspector-toggle");
            
            if (lockdownToggle != null) {
                lockdownToggle.AddToClassList("inspector-toggle");
                lockdownToggle.RegisterValueChangedCallback(evt => { isLockdown = evt.newValue; });
            }
            
            lineGraph = root.Q<EpidemicLineGraph>(); barGraph = root.Q<EpidemicBarGraph>(); activeCasesGraph = root.Q<ActiveCasesGraph>();
            simulationStats = root.Q<SimulationStatsDashboard>(); virusVaccineControls = root.Q<VirusVaccineControls>();
            
            if (virusVaccineControls != null) { virusVaccineControls.OnMutateVirus += TriggerMutationEvent; virusVaccineControls.OnDeployVaccine += TriggerVaccineWave; }

            if (startSimButton != null) startSimButton.clicked += () => { StartSimulationFromUI(); };
            if (colorToggle != null) colorToggle.RegisterValueChangedCallback(evt => { if (BuildingManager.Instance != null) BuildingManager.Instance.ToggleColors(evt.newValue); });
            if (socialDistanceToggle != null) socialDistanceToggle.RegisterValueChangedCallback(evt => { if (evt.newValue) { transmissionRate = baseTransmissionRate * 0.4f; infectionRadius = baseInfectionRadius * 0.6f; } else { transmissionRate = baseTransmissionRate; infectionRadius = baseInfectionRadius; } });
            
            if (pauseButton != null) pauseButton.clicked += () => { if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = 0f; };
            if (playButton != null) playButton.clicked += () => { if (TimeManager.Instance != null) { TimeManager.Instance.timeMultiplier = 1f; currentFastIndex = 0; } };
            if (fastButton != null) fastButton.clicked += () => { if (TimeManager.Instance != null) { TimeManager.Instance.timeMultiplier = fastSpeeds[currentFastIndex]; currentFastIndex = (currentFastIndex + 1) % fastSpeeds.Length; } }; 
            
            SwitchTab(true);
        }
    }

    private void StyleCustomScrollbar(ScrollView sv)
    {
        sv.RegisterCallback<GeometryChangedEvent>(evt => {
            var scroller = sv.verticalScroller;
            if (scroller == null) return;
            
            scroller.style.width = 6;
            scroller.style.minWidth = 6;
            scroller.style.maxWidth = 6;
            scroller.style.borderLeftWidth = 0;
            scroller.style.borderRightWidth = 0;

            var upBtn = scroller.Q(className: "unity-scroller__high-button");
            if (upBtn != null) upBtn.style.display = DisplayStyle.None;
            
            var downBtn = scroller.Q(className: "unity-scroller__low-button");
            if (downBtn != null) downBtn.style.display = DisplayStyle.None;
            
            var tracker = scroller.Q(className: "unity-base-slider__tracker");
            if (tracker != null) {
                tracker.style.backgroundColor = Color.clear;
                tracker.style.borderLeftWidth = 0;
                tracker.style.borderRightWidth = 0;
            }

            var dragger = scroller.Q(className: "unity-base-slider__dragger");
            if (dragger != null) {
                dragger.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 1f);
                dragger.style.borderTopLeftRadius = 3;
                dragger.style.borderTopRightRadius = 3;
                dragger.style.borderBottomLeftRadius = 3;
                dragger.style.borderBottomRightRadius = 3;
                dragger.style.width = 6;
                dragger.style.left = 0; 
                dragger.style.marginLeft = 0;
            }
        });
    }

    private VisualElement CreateSliderRow(string labelText, float min, float max, float defaultVal, System.Action<float> onValueChanged)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
        
        var slider = new Slider(labelText, min, max) { value = defaultVal, style = { flexGrow = 1 } };
        slider.AddToClassList("inspector-field");
        
        var floatField = new FloatField { value = defaultVal, style = { width = 45, marginLeft = 5 } };
        floatField.AddToClassList("inspector-field");

        slider.RegisterValueChangedCallback(evt => {
            floatField.SetValueWithoutNotify((float)System.Math.Round(evt.newValue, 3));
            onValueChanged(evt.newValue);
        });
        
        floatField.RegisterValueChangedCallback(evt => {
            float val = Mathf.Clamp(evt.newValue, min, max);
            if (evt.newValue != val) floatField.SetValueWithoutNotify(val);
            slider.SetValueWithoutNotify(val);
            onValueChanged(val);
        });

        row.Add(slider); row.Add(floatField); return row;
    }

    private void SwitchTab(bool showSetup)
    {
        if (setupContainer != null) setupContainer.style.display = showSetup ? DisplayStyle.Flex : DisplayStyle.None;
        if (runtimeContainer != null) runtimeContainer.style.display = !showSetup ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabSetupBtn != null) { if (showSetup) tabSetupBtn.AddToClassList("tab-button--active"); else tabSetupBtn.RemoveFromClassList("tab-button--active"); }
        if (tabRuntimeBtn != null) { if (!showSetup) tabRuntimeBtn.AddToClassList("tab-button--active"); else tabRuntimeBtn.RemoveFromClassList("tab-button--active"); }
    }

    public void ResetSimulation()
    {
        if (!initialized) return;

        initialized = false;
        if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = 0f;

        DisposeArrays();

        realTime = 0f;
        epicAccumulator = 0f;
        fixedTickAccumulator = 0f;
        
        currentDeployedVaccineStrain = -1;
        currentVaccineEfficacy = 0f;
        currentVaccineAbidance = 0f;
        totalVaccinesAvailable = 0;
        
        // --- FIX: ALL GRAPHS AND DASHBOARDS ARE NOW COMPLETELY CLEARED ---
        if (lineGraph != null) lineGraph.ClearData();
        if (activeCasesGraph != null) activeCasesGraph.ClearData();
        if (barGraph != null) barGraph.ClearData();               
        if (simulationStats != null) simulationStats.ClearData(); 
        
        if (virusVaccineControls != null) {
            virusVaccineControls.UpdateActiveStrains(new Dictionary<int, int>());
            virusVaccineControls.UpdateActiveVaccines(new Dictionary<int, int>());
        }

        if (setupContainer != null) setupContainer.SetEnabled(true);
        SwitchTab(true);
    }

    private void TriggerMutationEvent(int newStrainID, float evasion, float trans, float incubation, float fatality)
    {
        baseTransmissionRate = trans; 
        if (socialDistanceToggle != null && socialDistanceToggle.value) transmissionRate = baseTransmissionRate * 0.4f; 
        else transmissionRate = baseTransmissionRate;
        
        minIncubationDays = Mathf.Max(1f, incubation - 1f); maxIncubationDays = incubation + 1f;
        mortalityRate = fatality;

        for (int i = 0; i < agents.Length; i++) {
            var agent = agents[i];
            
            if (agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated) {
                if (Random.value <= evasion) {
                    agent.healthState = HealthState.Susceptible;
                    agent.protectedStrainID = -1; 
                }
            }
            
            if (agent.healthState == HealthState.Infected || agent.healthState == HealthState.Exposed) {
                agent.activeStrainID = newStrainID; 
            }
            
            agents[i] = agent;
        }
    }

    private void TriggerVaccineWave(int strainID, int doses, float efficacy, float abidance)
    {
        currentDeployedVaccineStrain = strainID;
        currentVaccineEfficacy = efficacy;
        currentVaccineAbidance = abidance; 
        totalVaccinesAvailable = doses;

        int activeClinics = hospitalPositions.Length + (distributeToCommercial ? commercialPositions.Length : 0);
        int vaccinesPerClinic = doses / Mathf.Max(1, activeClinics);

        for (int i = 0; i < hospitalInventory.Length; i++) hospitalInventory[i] = vaccinesPerClinic; 
        if (distributeToCommercial) { for (int i = 0; i < commercialInventory.Length; i++) commercialInventory[i] = vaccinesPerClinic; }
    }

    void StartSimulationFromUI()
    {
        if (popInput != null && int.TryParse(popInput.value, out int p)) populationSize = p; if (infectedInput != null && int.TryParse(infectedInput.value, out int i)) initialInfected = i; if (radiusInput != null && float.TryParse(radiusInput.value, out float r)) infectionRadius = r; if (transRateInput != null && float.TryParse(transRateInput.value, out float tr)) transmissionRate = tr; if (recoveryInput != null && float.TryParse(recoveryInput.value, out float rec)) recoveryTime = rec; if (mortalityInput != null && float.TryParse(mortalityInput.value, out float m)) mortalityRate = m;
        baseTransmissionRate = transmissionRate; baseInfectionRadius = infectionRadius;
        
        if (lineGraph != null) lineGraph.maxPopulation = populationSize; 
        if (barGraph != null) barGraph.maxPopulation = populationSize; 
        if (activeCasesGraph != null) activeCasesGraph.maxPopulation = populationSize;
        
        if (setupContainer != null) setupContainer.SetEnabled(false); 
        SwitchTab(false); Initialize();
    }

    void Initialize()
    {
        if (BuildingManager.Instance == null || waypointData == null || waypointData.waypoints.Length == 0) return; 

        List<Building> homes = BuildingManager.Instance.GetResidential(); List<Building> works = BuildingManager.Instance.GetWorkplace();
        List<Building> commercials = BuildingManager.Instance.GetCommercial(); List<Building> hospitals = BuildingManager.Instance.GetHealthcare();
        if (hospitals.Count == 0) hospitals = commercials; 
        if (homes.Count == 0) return;

        homePositions = new float3[homes.Count]; for (int i = 0; i < homes.Count; i++) homePositions[i] = new float3(homes[i].GetPosition().x, groundY, homes[i].GetPosition().z);
        workPositions = new float3[Mathf.Max(works.Count, 1)]; for (int i = 0; i < works.Count; i++) workPositions[i] = new float3(works[i].GetPosition().x, groundY, works[i].GetPosition().z);
        commercialPositions = new float3[Mathf.Max(commercials.Count, 1)]; for (int i = 0; i < commercials.Count; i++) commercialPositions[i] = new float3(commercials[i].GetPosition().x, groundY, commercials[i].GetPosition().z);
        hospitalPositions = new float3[Mathf.Max(hospitals.Count, 1)]; for (int i = 0; i < hospitals.Count; i++) hospitalPositions[i] = new float3(hospitals[i].GetPosition().x, groundY, hospitals[i].GetPosition().z);

        hospitalInventory = new int[hospitalPositions.Length]; commercialInventory = new int[commercialPositions.Length];
        waypoints = new NativeArray<float3>(waypointData.waypoints.Length, Allocator.Persistent); for (int i = 0; i < waypointData.waypoints.Length; i++) waypoints[i] = new float3(waypointData.waypoints[i].x, groundY, waypointData.waypoints[i].z);
        neighborData = new NativeArray<int>(waypointData.neighborData, Allocator.Persistent); neighborStart = new NativeArray<int>(waypointData.neighborStart, Allocator.Persistent); neighborCount = new NativeArray<int>(waypointData.neighborCount, Allocator.Persistent);

        homeNearestWaypoint = new int[homePositions.Length]; for (int i = 0; i < homePositions.Length; i++) homeNearestWaypoint[i] = FindNearestWaypointIndex(homePositions[i]);
        workNearestWaypoint = new int[workPositions.Length]; for (int i = 0; i < workPositions.Length; i++) workNearestWaypoint[i] = FindNearestWaypointIndex(workPositions[i]);
        commercialNearestWaypoint = new int[commercialPositions.Length]; for (int i = 0; i < commercialPositions.Length; i++) commercialNearestWaypoint[i] = FindNearestWaypointIndex(commercialPositions[i]);
        hospitalNearestWaypoint = new int[hospitalPositions.Length]; for (int i = 0; i < hospitalPositions.Length; i++) hospitalNearestWaypoint[i] = FindNearestWaypointIndex(hospitalPositions[i]);

        nativeHomeNearest = new NativeArray<int>(homeNearestWaypoint, Allocator.Persistent); nativeWorkNearest = new NativeArray<int>(workNearestWaypoint, Allocator.Persistent); nativeCommercialNearest = new NativeArray<int>(commercialNearestWaypoint, Allocator.Persistent); nativeHospitalNearest = new NativeArray<int>(hospitalNearestWaypoint, Allocator.Persistent);
        nativeHomePositions = new NativeArray<float3>(homePositions, Allocator.Persistent); nativeHospitalPositions = new NativeArray<float3>(hospitalPositions, Allocator.Persistent); nativeCommercialPositions = new NativeArray<float3>(commercialPositions, Allocator.Persistent);

        float3 min = homePositions[0], max = homePositions[0]; foreach (float3 p in homePositions) { min = math.min(min, p); max = math.max(max, p); } foreach (float3 p in workPositions) { min = math.min(min, p); max = math.max(max, p); }
        spatialGrid = new SpatialGrid(cellSize, min - new float3(100f, 0f, 100f), (int)((max.x - min.x + 200f) / cellSize) + 1, (int)((max.z - min.z + 200f) / cellSize) + 1);
        indoorMap = new NativeParallelMultiHashMap<int, int>(populationSize, Allocator.Persistent);
        agents = new NativeArray<SimulationAgent>(populationSize, Allocator.Persistent); agentsBuffer = new NativeArray<SimulationAgent>(populationSize, Allocator.Persistent);

        for (int i = 0; i < populationSize; i++)
        {
            int hIdx = i % homePositions.Length;
            agents[i] = new SimulationAgent
            {
                position = waypoints[homeNearestWaypoint[hIdx]], moveStartPosition = waypoints[homeNearestWaypoint[hIdx]], moveEndPosition = waypoints[homeNearestWaypoint[hIdx]], targetPosition = waypoints[homeNearestWaypoint[hIdx]], personalOffset = GetPersonalOffset(i),
                healthState = i < initialInfected ? HealthState.Infected : HealthState.Susceptible, scheduleState = AgentScheduleState.Home,
                
                activeStrainID = i < initialInfected ? baseStartingStrainID : -1,
                protectedStrainID = -1,
                immunityTimer = 0f,
                historicalStrainMask = i < initialInfected ? (1u << baseStartingStrainID) : 0u, 
                
                isSeekingVaccine = false, vaccineClinicID = -1, isVaccineClinicCommercial = false, vaccineWaitTimer = 0f, 
                healthcareID = i % hospitalPositions.Length, speed = Random.Range(agentSpeed * 0.75f, agentSpeed * 1.25f),
                homeID = hIdx, workID = i % workPositions.Length, commercialID = i % commercialPositions.Length,
                currentWaypointIndex = homeNearestWaypoint[hIdx], destinationWaypointIndex = homeNearestWaypoint[hIdx],
                workStartHour = Random.Range(7f, 9f), workEndHour = Random.Range(16f, 18f), returnHomeHour = Mathf.Max(Random.Range(18f, 22f), Random.Range(16f, 18f) + 1f),
                commercialArrivalHour = Random.Range(16f, 18f) + Random.Range(0.25f, 1.0f), complianceLevel = Random.Range(0f, 1f), commutingStartTime = -9999f, isActive = true, isInsideBuilding = true, visitsCommercial = Random.value < 0.4f
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
            float dynamicTick = math.max(fixedTickInterval * multiplier, 0.01f); 
            fixedTickAccumulator += Time.deltaTime * multiplier;

            while (fixedTickAccumulator >= dynamicTick)
            {
                fixedTickAccumulator -= dynamicTick; realTime += dynamicTick;
                if (TimeManager.Instance != null) TimeManager.Instance.AdvanceTime(dynamicTick / multiplier);
                int currentDay = Mathf.FloorToInt(realTime / realSecondsPerInGameDay) + 1;

                for (int i = 0; i < agents.Length; i++)
                {
                    var agent = agents[i];
                    if (agent.isSeekingVaccine) 
                    {
                        bool arrivedAtHospital = !agent.isVaccineClinicCommercial && agent.scheduleState == AgentScheduleState.AtHospital && agent.isAtHospital;
                        bool arrivedAtCommercial = agent.isVaccineClinicCommercial && agent.scheduleState == AgentScheduleState.AtCommercial && agent.isInsideBuilding && agent.commutingStartTime == -9999f;

                        if (arrivedAtHospital || arrivedAtCommercial)
                        {
                            agent.vaccineWaitTimer -= dynamicTick; 

                            if (agent.vaccineWaitTimer <= 0f)
                            {
                                bool isEligible = agent.healthState == HealthState.Susceptible || agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated;
                                
                                if (isEligible) 
                                {
                                    bool gotShot = false;
                                    if (!agent.isVaccineClinicCommercial && hospitalInventory[agent.vaccineClinicID] > 0) { hospitalInventory[agent.vaccineClinicID]--; gotShot = true; } 
                                    else if (agent.isVaccineClinicCommercial && commercialInventory[agent.vaccineClinicID] > 0) { commercialInventory[agent.vaccineClinicID]--; gotShot = true; }

                                    if (gotShot && Random.value <= currentVaccineEfficacy) {
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
                }

                ScheduleUpdateJob scheduleJob = new ScheduleUpdateJob {
                    agents = agents, 
                    currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f, 
                    isLockdown = this.isLockdown,
                    lockdownAbidanceThreshold = this.lockdownAbidance,
                    homeNearestWaypoint = nativeHomeNearest, workNearestWaypoint = nativeWorkNearest, commercialNearestWaypoint = nativeCommercialNearest, hospitalNearestWaypoint = nativeHospitalNearest, homePositions = nativeHomePositions, hospitalPositions = nativeHospitalPositions, commercialPositions = nativeCommercialPositions, waypoints = waypoints, stuckTimeoutSimHours = stuckTimeoutSimHours, waypointReachDistance = waypointReachDistance, destinationOffsetRange = destinationOffsetRange, groundY = groundY
                };
                JobHandle scheduleHandle = scheduleJob.Schedule(agents.Length, 64);

                WaypointAssignJob waypointJob = new WaypointAssignJob {
                    agents = agents, waypoints = waypoints, neighborData = neighborData, neighborStart = neighborStart, neighborCount = neighborCount, currentSimTime = realTime, realTime = realTime, waypointReachDistance = waypointReachDistance, currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 8f
                };
                JobHandle waypointHandle = waypointJob.Schedule(agents.Length, 64, scheduleHandle); waypointHandle.Complete();

                epicAccumulator += dynamicTick;
                if (epicAccumulator >= epicTickInterval)
                {
                    float epidemicDelta = epicAccumulator; epicAccumulator = 0f;
                    spatialGrid.grid.Clear(); indoorMap.Clear(); 
                    
                    if (currentDeployedVaccineStrain != -1)
                    {
                        for (int i = 0; i < agents.Length; i++)
                        {
                            var agent = agents[i];
                            
                            if (!agent.isSeekingVaccine && agent.protectedStrainID != currentDeployedVaccineStrain && agent.healthState != HealthState.Dead)
                            {
                                bool isEligible = agent.healthState == HealthState.Susceptible || agent.healthState == HealthState.Recovered || agent.healthState == HealthState.Vaccinated;
                                
                                if (isEligible && agent.complianceLevel <= currentVaccineAbidance)
                                {
                                    bool wantsCommercial = distributeToCommercial && Random.value > 0.5f;
                                    int primaryClinic = wantsCommercial ? agent.commercialID : agent.healthcareID;
                                    int primaryStock = wantsCommercial ? commercialInventory[primaryClinic] : hospitalInventory[primaryClinic];

                                    if (primaryStock > 0) {
                                        agent.isSeekingVaccine = true; agent.isVaccineClinicCommercial = wantsCommercial; agent.vaccineClinicID = primaryClinic;
                                        agent.vaccineWaitTimer = Random.Range(1f, 3f) * (realSecondsPerInGameDay / 24f); agents[i] = agent;
                                    } 
                                    else {
                                        bool fallbackCommercial = !wantsCommercial;
                                        if (!fallbackCommercial || distributeToCommercial) {
                                            int fallbackClinic = fallbackCommercial ? agent.commercialID : agent.healthcareID;
                                            int fallbackStock = fallbackCommercial ? commercialInventory[fallbackClinic] : hospitalInventory[fallbackClinic];
                                            
                                            if (fallbackStock > 0) {
                                                agent.isSeekingVaccine = true; agent.isVaccineClinicCommercial = fallbackCommercial; agent.vaccineClinicID = fallbackClinic;
                                                agent.vaccineWaitTimer = Random.Range(1f, 3f) * (realSecondsPerInGameDay / 24f); agents[i] = agent;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    UpdateGridJob gridJob = new UpdateGridJob { agents = agents, gridWriter = spatialGrid.grid.AsParallelWriter(), cellSize = spatialGrid.cellSize, gridOrigin = spatialGrid.gridOrigin, gridWidth = spatialGrid.gridWidth, gridHeight = spatialGrid.gridHeight }; JobHandle gridHandle = gridJob.Schedule(agents.Length, 64);
                    IndoorMappingJob indoorJob = new IndoorMappingJob { agents = agents, indoorMapWriter = indoorMap.AsParallelWriter() }; JobHandle indoorHandle = indoorJob.Schedule(agents.Length, 64, gridHandle);

                    EpidemicJob epidemicJob = new EpidemicJob {
                        agentsIn = agents, 
                        agentsOut = agentsBuffer, 
                        grid = spatialGrid.grid, 
                        indoorMap = indoorMap, 
                        deltaTime = epidemicDelta, 
                        currentSimTime = realTime, 
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
                    JobHandle epidemicHandle = epidemicJob.Schedule(agents.Length, 64, indoorHandle); epidemicHandle.Complete();
                    agentsBuffer.CopyTo(agents);

                    int alive = 0, dead = 0, infected = 0, recovered = 0, susceptible = 0, exposed = 0, vaccinated = 0;
                    int hospitalized = 0; 
                    
                    Dictionary<int, int> activeStrainCounts = new Dictionary<int, int>();
                    Dictionary<int, int> activeVaccineCounts = new Dictionary<int, int>();

                    for (int i = 0; i < agents.Length; i++) {
                        if (agents[i].healthState == HealthState.Dead) dead++;
                        else { 
                            alive++;
                            if (agents[i].healthState == HealthState.Infected) { 
                                infected++; 
                                int sID = agents[i].activeStrainID;
                                if (sID != -1) { if (!activeStrainCounts.ContainsKey(sID)) activeStrainCounts[sID] = 0; activeStrainCounts[sID]++; }
                            }
                            else if (agents[i].healthState == HealthState.Exposed) exposed++;
                            else if (agents[i].healthState == HealthState.Recovered) {
                                recovered++;
                                int vID = agents[i].protectedStrainID;
                                if (vID != -1) { if (!activeVaccineCounts.ContainsKey(vID)) activeVaccineCounts[vID] = 0; activeVaccineCounts[vID]++; }
                            }
                            else if (agents[i].healthState == HealthState.Vaccinated) {
                                vaccinated++;
                                int vID = agents[i].protectedStrainID;
                                if (vID != -1) { if (!activeVaccineCounts.ContainsKey(vID)) activeVaccineCounts[vID] = 0; activeVaccineCounts[vID]++; }
                            }
                            else if (agents[i].healthState == HealthState.Susceptible) susceptible++;
                            
                            if (agents[i].scheduleState == AgentScheduleState.AtHospital) hospitalized++;
                        }
                    }

                    int remainingVaccines = 0;
                    if (hospitalInventory != null) { foreach (int v in hospitalInventory) remainingVaccines += v; if (distributeToCommercial) foreach (int v in commercialInventory) remainingVaccines += v; }

                    float exactDay = realTime / realSecondsPerInGameDay;

                    if (lineGraph != null) lineGraph.AddData(susceptible, exposed, infected, recovered, vaccinated, dead, exactDay);
                    if (barGraph != null) barGraph.UpdateData(susceptible, exposed, infected, recovered, vaccinated, dead);
                    if (activeCasesGraph != null) activeCasesGraph.AddData(infected, exactDay);
                    
                    if (virusVaccineControls != null) {
                        virusVaccineControls.UpdateActiveStrains(activeStrainCounts);
                        virusVaccineControls.UpdateActiveVaccines(activeVaccineCounts);
                    }
                    
                    if (simulationStats != null) {
                        float currentHour = TimeManager.Instance != null ? TimeManager.Instance.currentHour : 0f;
                        int totalBeds = (hospitalPositions != null) ? hospitalPositions.Length * hospitalBedsPerFacility : 0;
                        simulationStats.UpdateData(currentDay, currentHour, multiplier, alive, dead, susceptible, exposed, infected, recovered, vaccinated, hospitalized, totalBeds, remainingVaccines, totalVaccinesAvailable);
                    }
                }
            }
        }
        if (AgentRenderer.Instance != null) AgentRenderer.Instance.UpdateRender(agents, realTime, agentGroundOffset);
    }

    float3 GetPersonalOffset(int agentIndex) { var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(agentIndex * 7919)); return new float3(rng.NextFloat(-destinationOffsetRange, destinationOffsetRange), 0f, rng.NextFloat(-destinationOffsetRange, destinationOffsetRange)); }
    int FindNearestWaypointIndex(float3 target) { float bestDist = float.MaxValue; int bestIdx = 0; for (int i = 0; i < waypoints.Length; i++) { float dist = math.distancesq(new float3(target.x, 0f, target.z), new float3(waypoints[i].x, 0f, waypoints[i].z)); if (dist < bestDist) { bestDist = dist; bestIdx = i; } } return bestIdx; }

    void OnDestroy()
    {
        DisposeArrays();
    }

    private void DisposeArrays()
    {
        if (agents.IsCreated) agents.Dispose(); if (agentsBuffer.IsCreated) agentsBuffer.Dispose();
        if (waypoints.IsCreated) waypoints.Dispose(); if (neighborData.IsCreated) neighborData.Dispose(); if (neighborStart.IsCreated) neighborStart.Dispose(); if (neighborCount.IsCreated) neighborCount.Dispose();
        if (nativeHomeNearest.IsCreated) nativeHomeNearest.Dispose(); if (nativeWorkNearest.IsCreated) nativeWorkNearest.Dispose(); if (nativeCommercialNearest.IsCreated) nativeCommercialNearest.Dispose(); if (nativeHospitalNearest.IsCreated) nativeHospitalNearest.Dispose(); 
        if (nativeHomePositions.IsCreated) nativeHomePositions.Dispose(); if (nativeHospitalPositions.IsCreated) nativeHospitalPositions.Dispose(); if (nativeCommercialPositions.IsCreated) nativeCommercialPositions.Dispose();
        if (indoorMap.IsCreated) indoorMap.Dispose(); spatialGrid?.Dispose();
    }
}