using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class WaypointGenerator : MonoBehaviour
{
    [Header("Generation Settings")]
    [Tooltip("Approximate spacing between waypoints in world units")]
    public float waypointSpacing = 8f;
    [Tooltip("Min distance between any two waypoints")]
    public float minDistance = 5f;
    [Tooltip("Max distance to connect two waypoints")]
    public float maxConnectionDistance = 20f;
    [Tooltip("Max neighbors per waypoint")]
    public int maxNeighbors = 8;
    [Tooltip("Min NavMesh triangle area to sample — ignores tiny gaps")]
    public float minTriangleArea = 2f;

    [Header("Data")]
    public WaypointData waypointData;

#if UNITY_EDITOR
    [ContextMenu("Generate Waypoints")]
    void GenerateWaypoints()
    {
        if (waypointData == null) { Debug.LogError("No WaypointData!"); return; }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.vertices.Length == 0)
        { Debug.LogError("No NavMesh found! Please bake NavMesh first."); return; }

        Debug.Log($"NavMesh has {triangulation.vertices.Length} vertices, " +
                  $"{triangulation.indices.Length / 3} triangles");

        List<Vector3> candidates = new List<Vector3>();

        // Sample points uniformly across every NavMesh triangle
        for (int i = 0; i < triangulation.indices.Length; i += 3)
        {
            Vector3 a = triangulation.vertices[triangulation.indices[i]];
            Vector3 b = triangulation.vertices[triangulation.indices[i + 1]];
            Vector3 c = triangulation.vertices[triangulation.indices[i + 2]];

            a.y = 0f; b.y = 0f; c.y = 0f;

            float area = TriangleArea(a, b, c);

            // Skip tiny triangles — these are gaps between buildings
            // too small for agents to walk through
            if (area < minTriangleArea) continue;

            // How many points to sample based on triangle area and spacing
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(area / (waypointSpacing * waypointSpacing)));

            for (int s = 0; s < sampleCount; s++)
            {
                // Random barycentric coordinates
                float r1 = Random.value;
                float r2 = Random.value;
                if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                float r3 = 1f - r1 - r2;

                Vector3 point = r1 * a + r2 * b + r3 * c;
                point.y = 0f;
                candidates.Add(point);
            }
        }

        Debug.Log($"Raw candidates from NavMesh: {candidates.Count}");

        // Filter by min distance using spatial grid for speed
        List<Vector3> filtered = FilterByMinDistance(candidates, minDistance);

        Debug.Log($"After min distance filter: {filtered.Count} waypoints");

        // Build neighbor graph — no line-of-sight check needed
        // NavMesh already guarantees these points are on walkable surface
        int count = filtered.Count;
        List<int>[] neighbors = new List<int>[count];
        for (int i = 0; i < count; i++)
            neighbors[i] = new List<int>();

        int edgesCreated = 0;
        for (int i = 0; i < count; i++)
        {
            if (i % 1000 == 0)
                EditorUtility.DisplayProgressBar("Building neighbor graph",
                    $"Processing {i}/{count}", (float)i / count);

            List<(int idx, float dist)> nearby = new List<(int, float)>();
            for (int j = 0; j < count; j++)
            {
                if (i == j) continue;
                float dist = Vector3.Distance(filtered[i], filtered[j]);
                if (dist <= maxConnectionDistance)
                    nearby.Add((j, dist));
            }

            nearby.Sort((a, b) => a.dist.CompareTo(b.dist));

            int connected = 0;
            foreach (var (j, dist) in nearby)
            {
                if (connected >= maxNeighbors) break;
                if (neighbors[i].Contains(j)) { connected++; continue; }
                neighbors[i].Add(j);
                neighbors[j].Add(i);
                edgesCreated++;
                connected++;
            }
        }

        EditorUtility.ClearProgressBar();

        // Keep only largest connected cluster
        List<int> mainCluster = FindLargestCluster(count, neighbors);

        Debug.Log($"Main cluster: {mainCluster.Count} / {count} waypoints " +
                  $"({(float)mainCluster.Count / count * 100f:0}% connected)");

        // Remap to main cluster only
        int[] remap = new int[count];
        for (int i = 0; i < count; i++) remap[i] = -1;
        for (int i = 0; i < mainCluster.Count; i++) remap[mainCluster[i]] = i;

        List<Vector3> finalWaypoints = new List<Vector3>();
        List<int>[] finalNeighbors = new List<int>[mainCluster.Count];
        for (int i = 0; i < mainCluster.Count; i++)
            finalNeighbors[i] = new List<int>();

        foreach (int idx in mainCluster)
        {
            finalWaypoints.Add(filtered[idx]);
            int newIdx = remap[idx];
            foreach (int neighbor in neighbors[idx])
            {
                int newNeighbor = remap[neighbor];
                if (newNeighbor >= 0 && !finalNeighbors[newIdx].Contains(newNeighbor))
                    finalNeighbors[newIdx].Add(newNeighbor);
            }
        }

        // Flatten
        List<int> neighborDataList = new List<int>();
        int[] neighborStart = new int[finalWaypoints.Count];
        int[] neighborCount = new int[finalWaypoints.Count];

        for (int i = 0; i < finalWaypoints.Count; i++)
        {
            neighborStart[i] = neighborDataList.Count;
            neighborCount[i] = finalNeighbors[i].Count;
            neighborDataList.AddRange(finalNeighbors[i]);
        }

        waypointData.waypoints = finalWaypoints.ToArray();
        waypointData.neighborData = neighborDataList.ToArray();
        waypointData.neighborStart = neighborStart;
        waypointData.neighborCount = neighborCount;

        EditorUtility.SetDirty(waypointData);
        AssetDatabase.SaveAssets();

        float avgNeighbors = finalWaypoints.Count > 0
            ? (float)neighborDataList.Count / finalWaypoints.Count : 0;
        Debug.Log($"Done! {finalWaypoints.Count} waypoints, " +
                  $"{edgesCreated} edges, avg {avgNeighbors:0.0} neighbors/node");
    }

    // Fast min distance filter using spatial grid
    List<Vector3> FilterByMinDistance(List<Vector3> candidates, float minDist)
    {
        float cellSize = minDist;
        Dictionary<(int, int), List<Vector3>> grid = new Dictionary<(int, int), List<Vector3>>();

        List<Vector3> result = new List<Vector3>();

        foreach (var c in candidates)
        {
            int cx = Mathf.FloorToInt(c.x / cellSize);
            int cz = Mathf.FloorToInt(c.z / cellSize);

            bool tooClose = false;

            // Check 3x3 neighborhood of cells
            for (int dx = -1; dx <= 1 && !tooClose; dx++)
            {
                for (int dz = -1; dz <= 1 && !tooClose; dz++)
                {
                    var key = (cx + dx, cz + dz);
                    if (!grid.ContainsKey(key)) continue;
                    foreach (var existing in grid[key])
                    {
                        if (Vector3.Distance(c, existing) < minDist)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                }
            }

            if (!tooClose)
            {
                result.Add(c);
                var key = (cx, cz);
                if (!grid.ContainsKey(key))
                    grid[key] = new List<Vector3>();
                grid[key].Add(c);
            }
        }

        return result;
    }

    List<int> FindLargestCluster(int count, List<int>[] neighbors)
    {
        bool[] visited = new bool[count];
        List<int> largest = new List<int>();

        for (int start = 0; start < count; start++)
        {
            if (visited[start]) continue;

            List<int> cluster = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                cluster.Add(node);
                foreach (int neighbor in neighbors[node])
                {
                    if (!visited[neighbor])
                    {
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (cluster.Count > largest.Count)
                largest = cluster;
        }

        return largest;
    }

    float TriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        return Vector3.Cross(ab, ac).magnitude * 0.5f;
    }
#endif
}