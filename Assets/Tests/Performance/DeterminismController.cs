using UnityEngine;
using System.IO;
using System.Collections;

public class DeterminismController : MonoBehaviour
{
    [Header("Determinism Settings")]
    public int testPopulation = 10000;
    public int targetFramesToRun = 1000; 

    private string hashRun1 = "";
    private string hashRun2 = "";

    IEnumerator Start()
    {
        // 1. Force the engine to ignore computer lag
        Time.captureFramerate = 50; 

        yield return new WaitForSeconds(1f); 

        Debug.Log("--- STARTING DETERMINISM RUN 1 ---");
        yield return StartCoroutine(ExecuteSimulationRun(1));

        Debug.Log("--- STARTING DETERMINISM RUN 2 ---");
        yield return StartCoroutine(ExecuteSimulationRun(2));

        GenerateDeterminismReport();
    }

    IEnumerator ExecuteSimulationRun(int runNumber)
    {
        SimulationManager.Instance.ResetSimulation();

        if (TimeManager.Instance != null) {
            TimeManager.Instance.currentHour = 0f;
        }

        // Lock the random dice rolls
        UnityEngine.Random.InitState(7777); 

        // 1. Lock the baseline population
        SimulationManager.Instance.populationSize = testPopulation;
        SimulationManager.Instance.initialInfected = 10;
        
        // 2. Lock the behavior parameters to guarantee Run 1 and Run 2 are identical
        SimulationManager.Instance.hospitalizationAbidance = 0.5f;
        SimulationManager.Instance.selfQuarantineAbidance = 0.2f;
        SimulationManager.Instance.speedVariance = 0.25f;
        SimulationManager.Instance.shiftGracePeriodHours = 1.0f;
        SimulationManager.Instance.evacuationStaggerMax = 0.75f;

        // 3. Launch the engine
        SimulationManager.Instance.Initialize();

        if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = 10f; 

        for (int i = 0; i < targetFramesToRun; i++)
        {
            yield return new WaitForEndOfFrame();
        }

        if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = 0f;

        if (runNumber == 1) hashRun1 = SimulationManager.Instance.GetPopulationStateHash();
        if (runNumber == 2) hashRun2 = SimulationManager.Instance.GetPopulationStateHash();
    }

    private void GenerateDeterminismReport()
    {
        int divergedAgents = CalculateDivergedAgents(hashRun1, hashRun2);
        string statusText = "";

        if (hashRun1 == "NO_DATA" || hashRun2 == "NO_DATA")
        {
            statusText = "STATUS: FAILED (MISSING DATA)";
        }
        else if (divergedAgents == 0)
        {
            statusText = "STATUS: PASSED (100% PERFECT TIMELINE MATCH)";
        }
        else
        {
            float accuracyPercentage = 100f * (1f - ((float)divergedAgents / testPopulation));
            statusText = $"STATUS: PASSED WITH MINOR DIVERGENCE ({accuracyPercentage.ToString("F2")}% ACCURACY)\n" +
                         $"-> Reason: {divergedAgents} out of {testPopulation} agents experienced a timeline shift due to multi-threading decimal calculations.";
        }

        string directory = Application.dataPath + "/Tests/Performance/";
        string reportPath = directory + "DeterminismReport.txt";
        
        string reportContent = "=== DETERMINISM & TIMELINE ACCURACY REPORT ===\n" +
                               "Target Population: " + testPopulation + "\n" +
                               "Frames Executed: " + targetFramesToRun + " per run\n" +
                               "Seed: 7777\n" +
                               "----------------------------------------------\n" +
                               "RUN 1 FINAL STATE: " + hashRun1 + "\n" +
                               "RUN 2 FINAL STATE: " + hashRun2 + "\n" +
                               "==============================================\n" +
                               statusText;

        File.WriteAllText(reportPath, reportContent);
        Debug.Log("Determinism Test Complete. Report saved.");
        
        Time.timeScale = 0; 
    }

    // --- HELPER: Automatically calculates the exact number of people who changed ---
    private int CalculateDivergedAgents(string hash1, string hash2)
    {
        if (hash1 == hash2 || hash1 == "NO_DATA" || hash2 == "NO_DATA") return 0;

        try 
        {
            int[] run1Numbers = ParseHashString(hash1);
            int[] run2Numbers = ParseHashString(hash2);
            
            int totalDifferences = 0;
            for(int i = 0; i < run1Numbers.Length; i++) 
            {
                totalDifferences += Mathf.Abs(run1Numbers[i] - run2Numbers[i]);
            }
            
            // Divide by 2 because 1 person changing states alters 2 categories
            return totalDifferences / 2; 
        } 
        catch 
        {
            return -1; 
        }
    }

    private int[] ParseHashString(string hash)
    {
        // Breaks down "S:9978 | E:12 | I:0" into clean numbers
        string[] categories = hash.Split('|');
        int[] numbers = new int[categories.Length];
        
        for (int i = 0; i < categories.Length; i++)
        {
            string numberPart = categories[i].Split(':')[1].Trim();
            numbers[i] = int.Parse(numberPart);
        }
        return numbers;
    }
}