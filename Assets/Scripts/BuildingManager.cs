using UnityEngine;
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

    [Header("Display Settings")]
    public bool showColors = true;

    private List<Building> allBuildings = new List<Building>();
    private List<Building> residentialBuildings = new List<Building>();
    private List<Building> commercialBuildings = new List<Building>();
    private List<Building> workplaceBuildings = new List<Building>();
    private List<Building> healthcareBuildings = new List<Building>();
    
    public Building currentlySelectedBuilding;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        FindAllBuildings();
        if (autoAssignTypes)
            AutoAssignTypes();
        else
            UnassignAll();
    }

    public void ToggleColors(bool show)
    {
        showColors = show;
        foreach (Building b in allBuildings)
        {
            b.UpdateColor();
        }
    }

    void FindAllBuildings()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        allBuildings.AddRange(buildings);
        foreach (Building b in allBuildings)
            SortBuilding(b);
    }

    public void AutoAssignTypes()
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
    }

    public void UnassignAll()
    {
        foreach (Building b in allBuildings)
        {
            b.SetType(BuildingType.Unassigned);
        }
        RefreshLists();
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

    public void RefreshLists()
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
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (mousePos.x < 0 || mousePos.y < 0 || mousePos.x > Screen.width || mousePos.y > Screen.height) return;

            if (SimulationManager.Instance != null && SimulationManager.Instance.IsPointerOverPopup()) return;

            if (Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(mousePos);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    Building building = hit.transform.GetComponent<Building>();
                    if (building != null)
                    {
                        SelectBuilding(building);
                    }
                    else DeselectBuilding();
                }
                else DeselectBuilding();
            }
        }
    }
    
    public void SelectBuilding(Building b)
    {
        if (currentlySelectedBuilding != null) currentlySelectedBuilding.SetHighlight(false);
        currentlySelectedBuilding = b;
        currentlySelectedBuilding.SetHighlight(true);
        if (SimulationManager.Instance != null) SimulationManager.Instance.ShowBuildingPopup(b);
    }

    public void DeselectBuilding()
    {
        if (currentlySelectedBuilding != null) currentlySelectedBuilding.SetHighlight(false);
        currentlySelectedBuilding = null;
        if (SimulationManager.Instance != null) SimulationManager.Instance.HideBuildingPopup();
    }

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