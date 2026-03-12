using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class BuildingManager : MonoBehaviour
{
    public static BuildingManager Instance;

    [Header("Setup")]
    public bool autoAssignTypes = true;
    public float residentialRatio = 0.5f;
    public float commercialRatio = 0.2f;
    public float workplaceRatio = 0.2f;
    public float healthcareRatio = 0.1f;

    [Header("Click To Assign")]
    public BuildingType selectedType = BuildingType.Residential;

    private List<Building> allBuildings = new List<Building>();
    private List<Building> residentialBuildings = new List<Building>();
    private List<Building> commercialBuildings = new List<Building>();
    private List<Building> workplaceBuildings = new List<Building>();
    private List<Building> healthcareBuildings = new List<Building>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        FindAllBuildings();
        if (autoAssignTypes)
            AutoAssignTypes();
    }

    void FindAllBuildings()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        allBuildings.AddRange(buildings);
        Debug.Log($"Found {allBuildings.Count} buildings");
        foreach (Building b in allBuildings)
            SortBuilding(b);
        Debug.Log($"Pre-assigned - Residential: {residentialBuildings.Count} Commercial: {commercialBuildings.Count} Workplace: {workplaceBuildings.Count} Healthcare: {healthcareBuildings.Count}");
    }

    void AutoAssignTypes()
    {
        int total = allBuildings.Count;
        int residentialCount = Mathf.RoundToInt(total * residentialRatio);
        int commercialCount  = Mathf.RoundToInt(total * commercialRatio);
        int workplaceCount   = Mathf.RoundToInt(total * workplaceRatio);
        int healthcareCount  = Mathf.RoundToInt(total * healthcareRatio);

        List<Building> shuffled = new List<Building>(allBuildings);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Building temp = shuffled[i];
            shuffled[i] = shuffled[j];
            shuffled[j] = temp;
        }

        int idx = 0;
        AssignType(shuffled, ref idx, residentialCount, BuildingType.Residential);
        AssignType(shuffled, ref idx, commercialCount,  BuildingType.Commercial);
        AssignType(shuffled, ref idx, workplaceCount,   BuildingType.Workplace);
        AssignType(shuffled, ref idx, healthcareCount,  BuildingType.Healthcare);

        RefreshLists();
        Debug.Log($"Auto assigned {total} buildings");
        Debug.Log($"Residential: {residentialBuildings.Count} Commercial: {commercialBuildings.Count} Workplace: {workplaceBuildings.Count} Healthcare: {healthcareBuildings.Count}");
    }

    void AssignType(List<Building> buildings, ref int idx, int count, BuildingType type)
    {
        for (int i = 0; i < count && idx < buildings.Count; i++, idx++)
            buildings[idx].SetType(type);
    }

    void SortBuilding(Building b)
    {
        switch (b.buildingType)
        {
            case BuildingType.Residential:
                if (!residentialBuildings.Contains(b)) residentialBuildings.Add(b);
                break;
            case BuildingType.Commercial:
                if (!commercialBuildings.Contains(b)) commercialBuildings.Add(b);
                break;
            case BuildingType.Workplace:
                if (!workplaceBuildings.Contains(b)) workplaceBuildings.Add(b);
                break;
            case BuildingType.Healthcare:
                if (!healthcareBuildings.Contains(b)) healthcareBuildings.Add(b);
                break;
        }
    }

    void RefreshLists()
    {
        residentialBuildings.Clear();
        commercialBuildings.Clear();
        workplaceBuildings.Clear();
        healthcareBuildings.Clear();
        foreach (Building b in allBuildings)
            SortBuilding(b);
    }

    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                Building building = hit.transform.GetComponent<Building>();
                if (building != null)
                {
                    building.SetType(selectedType);
                    RefreshLists();
                    Debug.Log($"Set {building.name} to {selectedType}");
                }
            }
        }
    }

    public void SetSelectedType(BuildingType type) => selectedType = type;

    public Building GetRandomBuilding(BuildingType type)
    {
        List<Building> list = GetList(type);
        if (list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }

    public Building GetRandomBuildingWithCapacity(BuildingType type)
    {
        List<Building> list = GetList(type);
        List<Building> available = list.FindAll(b => b.HasCapacity());
        if (available.Count == 0) return null;
        return available[Random.Range(0, available.Count)];
    }

    List<Building> GetList(BuildingType type)
    {
        switch (type)
        {
            case BuildingType.Residential: return residentialBuildings;
            case BuildingType.Commercial:  return commercialBuildings;
            case BuildingType.Workplace:   return workplaceBuildings;
            case BuildingType.Healthcare:  return healthcareBuildings;
            default:                       return allBuildings;
        }
    }

    public List<Building> GetAllBuildings()   => allBuildings;
    public List<Building> GetResidential()    => residentialBuildings;
    public List<Building> GetCommercial()     => commercialBuildings;
    public List<Building> GetWorkplace()      => workplaceBuildings;
    public List<Building> GetHealthcare()     => healthcareBuildings;
}