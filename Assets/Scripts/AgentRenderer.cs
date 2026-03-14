using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;

public class AgentRenderer : MonoBehaviour
{
    public static AgentRenderer Instance;

    [Header("Rendering")]
    public Mesh agentMesh;
    public Material susceptibleMaterial;
    public Material infectedMaterial;
    public Material recoveredMaterial;
    public Material vaccinatedMaterial;
    public float agentSize = 0.5f;

    private const int batchSize = 1023;

    void Awake()
    {
        Instance = this;
    }

    public void UpdateRender(NativeArray<SimulationAgent> agents, float currentSimTime, float groundOffset = 0f)
    {
        List<Matrix4x4> susceptible = new List<Matrix4x4>();
        List<Matrix4x4> infected = new List<Matrix4x4>();
        List<Matrix4x4> recovered = new List<Matrix4x4>();
        List<Matrix4x4> vaccinated = new List<Matrix4x4>();

        for (int i = 0; i < agents.Length; i++)
        {
            SimulationAgent agent = agents[i];
            if (!agent.isActive || agent.isInsideBuilding) continue;

            // Derive render position purely from sim time - no stored position needed
            float3 renderPos;
            if (agent.hasMovementSegment)
            {
                float duration = agent.arrivalTime - agent.moveStartTime;
                if (duration > 0.0001f)
                {
                    float progress = math.clamp(
                        (currentSimTime - agent.moveStartTime) / duration,
                        0f, 1f
                    );
                    renderPos = math.lerp(agent.moveStartPosition, agent.moveEndPosition, progress);
                }
                else
                {
                    renderPos = agent.moveEndPosition;
                }
            }
            else
            {
                renderPos = agent.position;
            }

            Matrix4x4 matrix = Matrix4x4.TRS(
                new Vector3(renderPos.x, renderPos.y + groundOffset, renderPos.z),
                Quaternion.identity,
                Vector3.one * agentSize
            );

            switch (agent.healthState)
            {
                case HealthState.Susceptible: susceptible.Add(matrix); break;
                case HealthState.Infected: infected.Add(matrix); break;
                case HealthState.Recovered: recovered.Add(matrix); break;
                case HealthState.Vaccinated: vaccinated.Add(matrix); break;
            }
        }

        DrawBatched(susceptible, susceptibleMaterial);
        DrawBatched(infected, infectedMaterial);
        DrawBatched(recovered, recoveredMaterial);
        DrawBatched(vaccinated, vaccinatedMaterial);
    }

    void DrawBatched(List<Matrix4x4> matrices, Material material)
    {
        if (material == null || agentMesh == null) return;

        for (int i = 0; i < matrices.Count; i += batchSize)
        {
            int count = Mathf.Min(batchSize, matrices.Count - i);
            Matrix4x4[] batch = matrices.GetRange(i, count).ToArray();
            Graphics.DrawMeshInstanced(agentMesh, 0, material, batch);
        }
    }
}