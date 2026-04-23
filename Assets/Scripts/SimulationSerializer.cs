using UnityEngine;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

[Serializable]
public class SimSaveData
{
    public int popSize;
    public float radius, transRate, recTime, mortRate;
    public float natImmunity, immDuration, histImmunity, histRecMult, lockAbidance, hospTransMult;
    public int beds;
    public bool distComm;
    public int s, e, i, r, v, d;
    public int savedDay; 
}

public static class SimulationSerializer
{
    public static async Task SaveSimulationToFileAsync(string filePath, SimSaveData parameters, Dictionary<int, SimulationAgent[]> history, CancellationToken token, IProgress<float> progress = null)
    {
        string jsonParams = "";
        try {
            jsonParams = JsonUtility.ToJson(parameters);
        } catch (Exception ex) {
            Debug.LogError("Failed to serialize parameters: " + ex.Message);
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                string tempPath = filePath + ".tmp";
                bool wasCancelled = false;

                using (FileStream fileStream = new FileStream(tempPath, FileMode.Create))
                using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Compress))
                using (BinaryWriter writer = new BinaryWriter(gzipStream))
                {
                    writer.Write(jsonParams);

                    writer.Write(history.Count);
                    int agentSize = UnsafeUtility.SizeOf<SimulationAgent>();
                    
                    int processed = 0;
                    int totalDays = history.Count;

                    foreach (var kvp in history)
                    {
                        if (token.IsCancellationRequested) {
                            wasCancelled = true;
                            break;
                        }

                        writer.Write(kvp.Key);
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
                        
                        processed++;
                        progress?.Report((float)processed / totalDays);
                    }
                }

                if (wasCancelled) {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    return;
                }

                if (File.Exists(filePath)) File.Delete(filePath);
                File.Move(tempPath, filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to save simulation to disk: " + ex.Message);
            }
        }, token);
    }

    public static async Task<(SimSaveData paramsData, Dictionary<int, SimulationAgent[]> history)> LoadSimulationFromFileAsync(string filePath, CancellationToken token, IProgress<float> progress = null)
    {
        string jsonParams = null;
        Dictionary<int, SimulationAgent[]> loadedHistory = new Dictionary<int, SimulationAgent[]>();

        await Task.Run(() =>
        {
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
                using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                using (BinaryReader reader = new BinaryReader(gzipStream))
                {
                    jsonParams = reader.ReadString();

                    int dayCount = reader.ReadInt32();
                    int agentSize = UnsafeUtility.SizeOf<SimulationAgent>();

                    for (int d = 0; d < dayCount; d++)
                    {
                        if (token.IsCancellationRequested) return;

                        int dayNumber = reader.ReadInt32();
                        int agentCount = reader.ReadInt32();
                        
                        SimulationAgent[] agents = new SimulationAgent[agentCount];
                        
                        byte[] bytes = new byte[agentCount * agentSize];
                        int totalRead = 0;
                        while (totalRead < bytes.Length)
                        {
                            int bytesRead = reader.Read(bytes, totalRead, bytes.Length - totalRead);
                            if (bytesRead == 0) throw new EndOfStreamException("Save file unexpectedly ended.");
                            totalRead += bytesRead;
                        }
                        
                        GCHandle handle = GCHandle.Alloc(agents, GCHandleType.Pinned);
                        try {
                            Marshal.Copy(bytes, 0, handle.AddrOfPinnedObject(), bytes.Length);
                        } finally {
                            handle.Free();
                        }
                        
                        loadedHistory[dayNumber] = agents;
                        
                        progress?.Report((float)(d + 1) / dayCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to read simulation file: " + ex.Message);
                jsonParams = null; 
            }
        }, token);

        if (token.IsCancellationRequested || string.IsNullOrEmpty(jsonParams)) {
            return (null, null);
        }

        SimSaveData loadedParams = null;
        try {
            loadedParams = JsonUtility.FromJson<SimSaveData>(jsonParams);
        } catch (Exception ex) {
            Debug.LogError("Failed to parse simulation parameters: " + ex.Message);
            return (null, null);
        }

        return (loadedParams, loadedHistory);
    }
}