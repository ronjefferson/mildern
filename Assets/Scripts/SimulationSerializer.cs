using UnityEngine;
using System;

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
}

public static class SimulationSerializer
{
    public static string ExportState(SimSaveData data)
    {
        string json = JsonUtility.ToJson(data);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    public static SimSaveData ImportState(string base64)
    {
        try 
        {
            byte[] bytes = Convert.FromBase64String(base64);
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            return JsonUtility.FromJson<SimSaveData>(json);
        } 
        catch 
        {
            Debug.LogError("Invalid Simulation Save Code.");
            return null;
        }
    }
}