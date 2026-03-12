using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class WaypointGenerator : MonoBehaviour
{
    [Header("Generation Settings")]
    public int waypointCount = 5000;
    public float minDistanceBetweenWaypoints = 5f;

    [Header("Output")]
    public WaypointData waypointData;
    public BuildingBoundsData buildingBoundsData;

    [ContextMenu("Generate Waypoints From NavMesh")]
    public void GenerateWaypoints()
    {
        if (waypointData == null)
        {
            Debug.LogError("Assign a WaypointData asset first!");
            return;
        }

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.vertices.Length == 0)
        {
            Debug.LogError("No NavMesh found! Bake it first.");
            return;
        }

        // Build spatial grid for fast proximity checks
        float cellSize = minDistanceBetweenWaypoints;
        Dictionary<Vector2Int, List<Vector3>> grid = new Dictionary<Vector2Int, List<Vector3>>();

        List<Vector3> points = new List<Vector3>();
        int attempts = 0;
        int maxAttempts = waypointCount * 100;

        while (points.Count < waypointCount && attempts < maxAttempts)
        {
            attempts++;

            int randomIndex = Random.Range(0, tri.indices.Length / 3) * 3;
            Vector3 point = Vector3.Lerp(
                tri.vertices[tri.indices[randomIndex]],
                tri.vertices[tri.indices[randomIndex + 1]],
                Random.value
            );
            point = Vector3.Lerp(
                point,
                tri.vertices[tri.indices[randomIndex + 2]],
                Random.value
            );

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(point, out hit, 2f, NavMesh.AllAreas))
                continue;

            if (buildingBoundsData != null && IsInsideBuilding(hit.position))
                continue;

            // Check proximity using grid
            if (IsTooClose(hit.position, grid, cellSize))
                continue;

            // Add to grid
            Vector2Int cell = GetCell(hit.position, cellSize);
            if (!grid.ContainsKey(cell))
                grid[cell] = new List<Vector3>();
            grid[cell].Add(hit.position);

            points.Add(hit.position);
        }

        waypointData.waypoints = points.ToArray();
        Debug.Log($"Generated {points.Count} waypoints in {attempts} attempts");

#if UNITY_EDITOR
        EditorUtility.SetDirty(waypointData);
        AssetDatabase.SaveAssets();
#endif
    }

    bool IsTooClose(Vector3 point, Dictionary<Vector2Int, List<Vector3>> grid, float cellSize)
    {
        Vector2Int cell = GetCell(point, cellSize);

        // Only check neighboring cells
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                Vector2Int neighbor = new Vector2Int(cell.x + dx, cell.y + dz);
                if (!grid.ContainsKey(neighbor)) continue;

                foreach (Vector3 existing in grid[neighbor])
                {
                    if (Vector3.Distance(point, existing) < minDistanceBetweenWaypoints)
                        return true;
                }
            }
        }
        return false;
    }

    Vector2Int GetCell(Vector3 point, float cellSize)
    {
        return new Vector2Int(
            Mathf.FloorToInt(point.x / cellSize),
            Mathf.FloorToInt(point.z / cellSize)
        );
    }

    bool IsInsideBuilding(Vector3 point)
    {
        if (buildingBoundsData == null) return false;
        for (int b = 0; b < buildingBoundsData.centers.Length; b++)
        {
            Vector3 toPoint = point - buildingBoundsData.centers[b];
            Vector3 halfSize = buildingBoundsData.sizes[b] * 0.5f + Vector3.one * 1f;

            if (Mathf.Abs(toPoint.x) < halfSize.x && Mathf.Abs(toPoint.z) < halfSize.z)
                return true;
        }
        return false;
    }

    void OnDrawGizmos()
    {
        if (waypointData == null || waypointData.waypoints == null) return;
        Gizmos.color = Color.cyan;
        foreach (Vector3 wp in waypointData.waypoints)
            Gizmos.DrawSphere(wp, 1f);
    }
}