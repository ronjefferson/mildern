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
    public Material exposedMaterial;      // NEW: Slot for the Orange Incubation Material!
    public Material infectedMaterial;
    public Material recoveredMaterial;
    public Material vaccinatedMaterial;
    public float agentSize = 0.5f;

    private const int batchSize = 1023;

    void Awake() { Instance = this; }

    public void UpdateRender(NativeArray<SimulationAgent> agents, float renderTime, float groundOffset = 0f)
    {
        List<Matrix4x4> susceptible = new List<Matrix4x4>();
        List<Matrix4x4> exposed = new List<Matrix4x4>();      // NEW: List to hold the exposed agents
        List<Matrix4x4> infected = new List<Matrix4x4>();
        List<Matrix4x4> recovered = new List<Matrix4x4>();
        List<Matrix4x4> vaccinated = new List<Matrix4x4>();

        for (int i = 0; i < agents.Length; i++)
        {
            SimulationAgent agent = agents[i];
            
            // Note: If they are inside a building, they turn invisible on the streets!
            if (!agent.isActive || agent.isInsideBuilding) continue;

            float3 renderPos;

            if (agent.hasMovementSegment)
            {
                float duration = agent.arrivalTime - agent.moveStartTime;
                if (duration > 0.0001f)
                {
                    // Always clamp 0-1 regardless of renderTime vs simTime relationship
                    float progress = math.clamp(
                        (renderTime - agent.moveStartTime) / duration,
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

            // Sort the agents into their correct color buckets
            switch (agent.healthState)
            {
                case HealthState.Susceptible: susceptible.Add(matrix); break;
                case HealthState.Exposed: exposed.Add(matrix); break;         // NEW: Catch the exposed state!
                case HealthState.Infected: infected.Add(matrix); break;
                case HealthState.Recovered: recovered.Add(matrix); break;
                case HealthState.Vaccinated: vaccinated.Add(matrix); break;
            }
        }

        // Paint the batches to the screen
        DrawBatched(susceptible, susceptibleMaterial);
        DrawBatched(exposed, exposedMaterial);                // NEW: Paint the exposed batch!
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