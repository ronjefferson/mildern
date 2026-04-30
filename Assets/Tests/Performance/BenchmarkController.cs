using UnityEngine;
using System.IO;
using System.Collections;

public class BenchmarkController : MonoBehaviour
{
    [Header("Benchmark Settings")]
    public int targetPopulation = 100000; 
    public float benchmarkDuration = 60f; 

    private float currentTimer = 0f;
    private int frameCount = 0;
    private float totalDeltaTime = 0f;
    private long totalDistanceChecks = 0;
    private int epidemicTicksRecorded = 0;
    private bool isRunning = false;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f); 

        SimulationManager.Instance.populationSize = targetPopulation;
        SimulationManager.Instance.initialInfected = 1; 
        
        SimulationManager.Instance.Initialize();

        if (TimeManager.Instance != null) TimeManager.Instance.timeMultiplier = 1f;

        isRunning = true;
    }

    void Update()
    {
        if (!isRunning || SimulationManager.Instance == null) return;

        currentTimer += Time.deltaTime;

        frameCount++;
        totalDeltaTime += Time.deltaTime;

        if (SimulationManager.Instance.TotalDistanceChecksThisTick > 0)
        {
            totalDistanceChecks += SimulationManager.Instance.TotalDistanceChecksThisTick;
            epidemicTicksRecorded++;
            SimulationManager.Instance.TotalDistanceChecksThisTick = 0; 
        }

        if (currentTimer >= benchmarkDuration)
        {
            isRunning = false;
            GenerateBenchmarkReport();
        }
    }

    private void GenerateBenchmarkReport()
    {
        float averageFPS = frameCount / totalDeltaTime;
        long averageChecksPerTick = epidemicTicksRecorded > 0 ? (totalDistanceChecks / epidemicTicksRecorded) : 0;
        
        string directory = Application.dataPath + "/Tests/Performance/";
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        
        string reportPath = directory + "BenchmarkReport.txt";
        
        string reportContent = "=== SCALABILITY BENCHMARK REPORT ===\n" +
                               "Target Population: " + targetPopulation + "\n" +
                               "Duration: " + benchmarkDuration + " seconds\n" +
                               "------------------------------------\n" +
                               "Average FPS: " + averageFPS.ToString("F2") + "\n" +
                               "Average Distance Checks per Epidemic Tick: " + averageChecksPerTick + "\n" +
                               "====================================";

        File.WriteAllText(reportPath, reportContent);
        Time.timeScale = 0; 
    }
}