using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;

public class SpatialGrid
{
    public float cellSize;
    public int gridWidth;
    public int gridHeight;
    public float3 gridOrigin;

    public NativeParallelMultiHashMap<int, int> grid;

    public SpatialGrid(float cellSize, float3 origin, int width, int height, int populationCapacity)
    {
        this.cellSize = cellSize;
        this.gridOrigin = origin;
        this.gridWidth = width;
        this.gridHeight = height;
        grid = new NativeParallelMultiHashMap<int, int>(populationCapacity, Allocator.Persistent);
    }

    public int GetCellKey(float3 position)
    {
        int x = (int)((position.x - gridOrigin.x) / cellSize);
        int z = (int)((position.z - gridOrigin.z) / cellSize);
        x = math.clamp(x, 0, gridWidth - 1);
        z = math.clamp(z, 0, gridHeight - 1);
        return x + z * gridWidth;
    }

    public void Dispose()
    {
        if (grid.IsCreated) grid.Dispose();
    }
}

[BurstCompile]
public struct UpdateGridJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<SimulationAgent> agents;
    public NativeParallelMultiHashMap<int, int>.ParallelWriter gridWriter;
    public float cellSize;
    public float3 gridOrigin;
    public int gridWidth;
    public int gridHeight;

    public void Execute(int i)
    {
        if (!agents[i].isActive || agents[i].isInsideBuilding) return;

        int x = (int)((agents[i].position.x - gridOrigin.x) / cellSize);
        int z = (int)((agents[i].position.z - gridOrigin.z) / cellSize);
        x = math.clamp(x, 0, gridWidth - 1);
        z = math.clamp(z, 0, gridHeight - 1);
        int key = x + z * gridWidth;
        gridWriter.Add(key, i);
    }
}