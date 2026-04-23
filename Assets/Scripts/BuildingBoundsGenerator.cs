//TO BE DELETED

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class BuildingBoundsGenerator : MonoBehaviour
{
    public BuildingBoundsData boundsData;

    [ContextMenu("Bake Building Bounds")]
    public void BakeBounds()
    {
        if (boundsData == null)
        {
            Debug.LogError("Assign a BuildingBoundsData asset first!");
            return;
        }

        Building[] buildings = FindObjectsOfType<Building>();
        List<Vector3> centers = new List<Vector3>();
        List<Vector3> sizes = new List<Vector3>();

        foreach (Building b in buildings)
        {
            Renderer r = b.GetComponent<Renderer>();
            if (r == null) r = b.GetComponentInChildren<Renderer>();
            if (r == null) continue;

            Bounds bounds = r.bounds;
            centers.Add(new Vector3(bounds.center.x, 0f, bounds.center.z));
            sizes.Add(new Vector3(bounds.size.x, 0f, bounds.size.z));
        }

        boundsData.centers = centers.ToArray();
        boundsData.sizes = sizes.ToArray();

        Debug.Log($"Baked {centers.Count} building bounds");

#if UNITY_EDITOR
        EditorUtility.SetDirty(boundsData);
        AssetDatabase.SaveAssets();
#endif
    }
}