using UnityEngine;

public enum BuildingType
{
    Unassigned,
    Residential,
    Commercial,
    Workplace,
    Healthcare
}

public class Building : MonoBehaviour
{
    [Header("Building Info")]
    public BuildingType buildingType = BuildingType.Unassigned;
    public int capacity = 10;
    public int currentOccupants = 0;

    [Header("Entrance")]
    public Vector3 entranceOffset = new Vector3(0f, 0f, 2f);

    private Renderer buildingRenderer;
    private Material buildingMaterial;
    
    private bool isHighlighted = false;

    public static readonly Color UnassignedColor  = new Color(0.6f, 0.6f, 0.6f);
    public static readonly Color ResidentialColor = new Color(0.3f, 0.8f, 0.3f);
    public static readonly Color CommercialColor  = new Color(0.9f, 0.75f, 0.1f);
    public static readonly Color WorkplaceColor   = new Color(0.2f, 0.5f, 1f);
    public static readonly Color HealthcareColor  = new Color(1f, 0.2f, 0.2f);

    void Awake()
    {
        buildingRenderer = GetComponent<Renderer>();
        if (buildingRenderer != null)
        {
            buildingMaterial = new Material(buildingRenderer.sharedMaterial);
            buildingRenderer.material = buildingMaterial;
        }
        UpdateColor();
    }

    void OnValidate()
    {
        if (buildingRenderer == null)
            buildingRenderer = GetComponent<Renderer>();
#if UNITY_EDITOR
        if (!UnityEditor.EditorApplication.isPlaying)
        {
            if (buildingRenderer != null)
                buildingRenderer.sharedMaterial.color = GetColorForType(buildingType);
            return;
        }
#endif
        UpdateColor();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(GetEntrancePosition(), 0.5f);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(transform.position, GetEntrancePosition());
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(GetEntrancePosition(), 1f);
        Gizmos.DrawWireCube(transform.position, GetComponent<Renderer>() != null ? 
            GetComponent<Renderer>().bounds.size : Vector3.one);
    }

    Color GetColorForType(BuildingType type)
    {
        if (BuildingManager.Instance != null && !BuildingManager.Instance.showColors)
        {
            return UnassignedColor;
        }

        switch (type)
        {
            case BuildingType.Residential: return ResidentialColor;
            case BuildingType.Commercial:  return CommercialColor;
            case BuildingType.Workplace:   return WorkplaceColor;
            case BuildingType.Healthcare:  return HealthcareColor;
            default:                       return UnassignedColor;
        }
    }

    public void SetType(BuildingType type)
    {
        buildingType = type;
        UpdateColor();
    }
    
    public void SetHighlight(bool active)
    {
        isHighlighted = active;
        UpdateColor();
    }

    public void UpdateColor()
    {
        if (buildingMaterial == null && buildingRenderer != null)
            buildingMaterial = buildingRenderer.material = new Material(buildingRenderer.sharedMaterial);
        
        if (buildingMaterial != null)
        {
            Color targetColor = GetColorForType(buildingType);
            
            if (isHighlighted)
            {
                targetColor = Color.Lerp(targetColor, Color.white, 0.4f);
            }
            
            buildingMaterial.color = targetColor;
        }
    }

    public Vector3 GetEntrancePosition()
    {
        return transform.position + transform.TransformDirection(entranceOffset);
    }

    public Vector3 GetNavMeshEntrance()
    {
        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(GetEntrancePosition(), out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;
        return transform.position;
    }

    public bool HasCapacity() => currentOccupants < capacity;
    public void AddOccupant() => currentOccupants++;
    public void RemoveOccupant() => currentOccupants = Mathf.Max(0, currentOccupants - 1);
    public Vector3 GetPosition() => transform.position;
    public Vector3 GetEntranceNavMeshPosition() => GetNavMeshEntrance();
}