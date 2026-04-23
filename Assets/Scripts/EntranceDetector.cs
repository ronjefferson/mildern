//TO BE DELETED

using UnityEngine;
using UnityEngine.AI;

public class EntranceDetector : MonoBehaviour
{
    [Header("Settings")]
    public float minClearanceRadius = 1.5f;
    public float searchRadius = 15f;
    public int searchDirections = 16;
    public bool showGizmos = false; // OFF by default

    private Building[] cachedBuildings;

    [ContextMenu("Auto Detect All Entrances")]
    public void AutoDetectAllEntrances()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        int success = 0;
        int failed = 0;

        foreach (Building building in buildings)
        {
            if (FindEntrance(building))
                success++;
            else
                failed++;
        }

        cachedBuildings = buildings;
        Debug.Log($"Entrance detection done. Success: {success} Failed: {failed}");

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
    
    [ContextMenu("Reset All Entrances")]
    public void ResetAllEntrances()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building b in buildings)
        {
            b.entranceOffset = new Vector3(0f, 0f, 2f);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(b);
#endif
        }
        Debug.Log($"Reset {buildings.Length} building entrances to default");
    }

    [ContextMenu("Clear All Entrances")]
    public void ClearAllEntrances()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building b in buildings)
        {
            b.entranceOffset = Vector3.zero;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(b);
#endif
        }
        cachedBuildings = null;
        Debug.Log($"Cleared {buildings.Length} building entrances");
    }

    bool FindEntrance(Building building)
    {
        Renderer renderer = building.GetComponent<Renderer>();
        if (renderer == null) return false;

        Bounds bounds = renderer.bounds;
        Vector3 center = bounds.center;
        center.y = building.transform.position.y;

        float bestScore = float.MinValue;
        Vector3 bestEntrance = Vector3.zero;
        bool foundAny = false;

        for (int i = 0; i < searchDirections; i++)
        {
            float angle = (360f / searchDirections) * i;
            Vector3 direction = new Vector3(
                Mathf.Sin(angle * Mathf.Deg2Rad),
                0f,
                Mathf.Cos(angle * Mathf.Deg2Rad)
            );

            float buildingEdgeDistance = GetBuildingEdgeDistance(bounds, direction);

            for (float dist = 0.5f; dist <= searchRadius; dist += 0.5f)
            {
                Vector3 candidatePos = center + direction * (buildingEdgeDistance + dist);
                candidatePos.y = building.transform.position.y;

                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidatePos, out hit, 2f, NavMesh.AllAreas))
                    continue;

                float clearance = GetNavMeshClearance(hit.position);
                if (clearance < minClearanceRadius)
                    continue;

                float distanceScore = 1f / (dist + 1f);
                float clearanceScore = clearance;
                float totalScore = distanceScore * 2f + clearanceScore;

                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    bestEntrance = hit.position;
                    foundAny = true;
                }
            }
        }

        if (foundAny)
        {
            Vector3 worldOffset = bestEntrance - building.transform.position;
            building.entranceOffset = building.transform.InverseTransformDirection(worldOffset);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(building);
#endif
            return true;
        }

        return false;
    }

    float GetBuildingEdgeDistance(Bounds bounds, Vector3 direction)
    {
        float dx = direction.x != 0 ? bounds.extents.x / Mathf.Abs(direction.x) : float.MaxValue;
        float dz = direction.z != 0 ? bounds.extents.z / Mathf.Abs(direction.z) : float.MaxValue;
        return Mathf.Min(dx, dz);
    }

    float GetNavMeshClearance(Vector3 position)
    {
        float minDist = float.MaxValue;
        int sampleCount = 8;

        for (int i = 0; i < sampleCount; i++)
        {
            float angle = (360f / sampleCount) * i;
            Vector3 dir = new Vector3(
                Mathf.Sin(angle * Mathf.Deg2Rad),
                0f,
                Mathf.Cos(angle * Mathf.Deg2Rad)
            );

            for (float dist = 0.5f; dist <= 5f; dist += 0.5f)
            {
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(position + dir * dist, out hit, 0.6f, NavMesh.AllAreas))
                {
                    minDist = Mathf.Min(minDist, dist);
                    break;
                }
            }
        }

        return minDist == float.MaxValue ? 5f : minDist;
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (cachedBuildings == null) return;

        foreach (Building b in cachedBuildings)
        {
            if (b == null) continue;
        
            Vector3 entrance = b.GetEntrancePosition();
        
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(entrance, 0.5f);
        
            // Line from building to its entrance only
            Gizmos.color = Color.white;
            Gizmos.DrawLine(b.transform.position, entrance);
        }
    }
}