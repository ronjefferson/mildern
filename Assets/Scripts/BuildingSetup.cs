using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class BuildingSetup : MonoBehaviour
{
    [ContextMenu("Setup Building Navmesh Modifiers")]
    void SetupModifiers()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int count = 0;

        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("building") || obj.name.Contains("Building"))
            {
                NavMeshModifier modifier = obj.GetComponent<NavMeshModifier>();
                if (modifier == null)
                    modifier = obj.AddComponent<NavMeshModifier>();

                modifier.overrideArea = true;
                modifier.area = NavMesh.GetAreaFromName("Not Walkable");
                count++;
            }
        }

        Debug.Log($"Setup {count} building modifiers");
    }
    
    [ContextMenu("Add Building Components")]
    void AddBuildingComponents()
    {
        int count = 0;
        GameObject[] allObjects = FindObjectsOfType<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("building") || obj.name.Contains("Building"))
            {
                if (obj.GetComponent<Building>() == null)
                {
                    obj.AddComponent<Building>();
                    count++;
                }
            }
        }

        Debug.Log($"Added Building component to {count} objects");
    }
    
    [ContextMenu("Set Selected To Residential")]
    void SetSelectedResidential() => SetSelectedType(BuildingType.Residential);

    [ContextMenu("Set Selected To Commercial")]
    void SetSelectedCommercial() => SetSelectedType(BuildingType.Commercial);

    [ContextMenu("Set Selected To Workplace")]
    void SetSelectedWorkplace() => SetSelectedType(BuildingType.Workplace);

    [ContextMenu("Set Selected To Healthcare")]
    void SetSelectedHealthcare() => SetSelectedType(BuildingType.Healthcare);
    
    [ContextMenu("Create Unique Materials For All Buildings")]
    void CreateUniqueMaterials()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        int count = 0;

        foreach (Building b in buildings)
        {
            Renderer r = b.GetComponent<Renderer>();
            if (r == null) continue;
            
            Material uniqueMat = new Material(r.sharedMaterial);
            uniqueMat.name = $"Building_Mat_{count}";
            r.sharedMaterial = uniqueMat;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(b);
            UnityEditor.EditorUtility.SetDirty(r);
#endif
            count++;
        }

        Debug.Log($"Created unique materials for {count} buildings");
    }
    
    

    void SetSelectedType(BuildingType type)
    {
#if UNITY_EDITOR
        foreach (GameObject obj in UnityEditor.Selection.gameObjects)
        {
            Building b = obj.GetComponent<Building>();
            if (b != null)
            {
                b.SetType(type);
                UnityEditor.EditorUtility.SetDirty(b);
            }
        }
        Debug.Log($"Set selected buildings to {type}");
#endif
    }
}