using UnityEngine;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

[Serializable]
public class SimSaveData
{
    // Sliders & Parameters
    public int popSize;
    public float radius, transRate, recTime, mortRate;
    public float natImmunity, immDuration, histImmunity, histRecMult, lockAbidance, hospTransMult;
    public int beds;
    public bool distComm;
    
    // Agent Macro States
    public int s, e, i, r, v, d;
    public int savedDay; 
}

public static class SimulationSerializer
{
    public static async Task SaveSimulationToFileAsync(string filePath, SimSaveData parameters, Dictionary<int, SimulationAgent[]> history)
    {
        await Task.Run(() =>
        {
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Compress))
                using (BinaryWriter writer = new BinaryWriter(gzipStream))
                {
                    // 1. Write the parameters
                    string jsonParams = JsonUtility.ToJson(parameters);
                    writer.Write(jsonParams);

                    // 2. Write the amount of history days
                    writer.Write(history.Count);
                    int agentSize = Marshal.SizeOf(typeof(SimulationAgent));

                    // 3. Dump the RAW MEMORY of the structs perfectly to disk
                    foreach (var kvp in history)
                    {
                        writer.Write(kvp.Key); // Day Number
                        SimulationAgent[] agents = kvp.Value;
                        writer.Write(agents.Length); 

                        byte[] bytes = new byte[agents.Length * agentSize];
                        GCHandle handle = GCHandle.Alloc(agents, GCHandleType.Pinned);
                        try {
                            Marshal.Copy(handle.AddrOfPinnedObject(), bytes, 0, bytes.Length);
                        } finally {
                            handle.Free();
                        }
                        
                        writer.Write(bytes);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to save simulation: " + ex.Message);
            }
        });
    }

    public static async Task<(SimSaveData paramsData, Dictionary<int, SimulationAgent[]> history)> LoadSimulationFromFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            SimSaveData loadedParams = null;
            Dictionary<int, SimulationAgent[]> loadedHistory = new Dictionary<int, SimulationAgent[]>();

            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
                using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                using (BinaryReader reader = new BinaryReader(gzipStream))
                {
                    string jsonParams = reader.ReadString();
                    loadedParams = JsonUtility.FromJson<SimSaveData>(jsonParams);

                    int dayCount = reader.ReadInt32();
                    int agentSize = Marshal.SizeOf(typeof(SimulationAgent));

                    // Perfectly reconstruct the RAM blocks
                    for (int d = 0; d < dayCount; d++)
                    {
                        int dayNumber = reader.ReadInt32();
                        int agentCount = reader.ReadInt32();
                        
                        SimulationAgent[] agents = new SimulationAgent[agentCount];
                        byte[] bytes = reader.ReadBytes(agentCount * agentSize);
                        
                        GCHandle handle = GCHandle.Alloc(agents, GCHandleType.Pinned);
                        try {
                            Marshal.Copy(bytes, 0, handle.AddrOfPinnedObject(), bytes.Length);
                        } finally {
                            handle.Free();
                        }
                        
                        loadedHistory[dayNumber] = agents;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load simulation: " + ex.Message);
                return (null, null);
            }

            return (loadedParams, loadedHistory);
        });
    }
}