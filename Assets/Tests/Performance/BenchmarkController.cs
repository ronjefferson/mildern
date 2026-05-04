using UnityEngine;
using System.IO;
using System.Collections;

public class BenchmarkController : MonoBehaviour
{
    [Header("Benchmark Settings")]
    public int targetFramesToRun = 1000;

    private int frameCount = 0;
    private float totalDeltaTime = 0f;
    private long totalDistanceChecks = 0;
    private int epidemicTicksRecorded = 0;
    private bool isRunning = false;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);
        
        SimulationManager.Instance.hospitalizationAbidance = 0.5f;
        SimulationManager.Instance.selfQuarantineAbidance = 0.2f;
        SimulationManager.Instance.speedVariance = 0.25f;
        SimulationManager.Instance.shiftGracePeriodHours = 1.0f;
        SimulationManager.Instance.evacuationStaggerMax = 0.75f;
        SimulationManager.Instance.isLockdown = false;

        
        SimulationManager.Instance.Initialize();

        if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = 1f;
        
        Debug.Log($"--- STARTING BENCHMARK: Profiling {targetFramesToRun} Frames ---");
        
        isRunning = true;
    }

    void Update()
    {
        if (!isRunning || SimulationManager.Instance == null) return;

        frameCount++;
        totalDeltaTime += Time.deltaTime;

        if (SimulationManager.Instance.TotalDistanceChecksThisTick > 0)
        {
            totalDistanceChecks += SimulationManager.Instance.TotalDistanceChecksThisTick;
            epidemicTicksRecorded++;
            SimulationManager.Instance.TotalDistanceChecksThisTick = 0; 
        }
        
        if (frameCount >= targetFramesToRun)
        {
            isRunning = false;
            GenerateBenchmarkReport();
        }
    }

    private void GenerateBenchmarkReport()
    {
        float averageFPS = frameCount / totalDeltaTime;
        long averageChecksPerTick = epidemicTicksRecorded > 0 ? (totalDistanceChecks / epidemicTicksRecorded) : 0;
        
        int testedPopulation = SimulationManager.Instance.populationSize;
        
        string directory = Application.dataPath + "/Tests/Performance/";
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        
        string reportPath = directory + "BenchmarkReport.txt";
        
        string reportContent = "=== SCALABILITY BENCHMARK REPORT ===\n" +
                               "Target Population: " + testedPopulation + "\n" +
                               "Frames Analyzed: " + targetFramesToRun + "\n" +
                               "Total Real-Time Taken: " + totalDeltaTime.ToString("F2") + " seconds\n" +
                               "------------------------------------\n" +
                               "Average FPS: " + averageFPS.ToString("F2") + "\n" +
                               "Average Distance Checks per Epidemic Tick: " + averageChecksPerTick + "\n" +
                               "====================================";

        File.WriteAllText(reportPath, reportContent);
        
        Debug.Log($"<b>BENCHMARK COMPLETE!</b> Analyzed {testedPopulation} agents. Average FPS: {averageFPS.ToString("F0")}. Report saved.");
        
        Time.timeScale = 0; 
    }
}