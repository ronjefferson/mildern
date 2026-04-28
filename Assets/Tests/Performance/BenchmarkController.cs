using UnityEngine;
using System.IO;

public class BenchmarkController : MonoBehaviour
{
    [Header("Benchmark Settings")]
    public int targetPopulation = 100000;
    public float benchmarkDuration = 60f; // Run for 60 seconds

    // Internal Trackers
    private float currentTimer = 0f;
    private int frameCount = 0;
    private float totalDeltaTime = 0f;
    private long totalDistanceChecks = 0;
    private bool isRunning = true;

    void Start()
    {
        Debug.Log("Starting Scalability Benchmark...");
        
        // 1. Force the Simulation Manager to spawn the massive crowd instantly
        // (Replace this with your actual spawn command)
        SimulationManager.Instance.SpawnAgents(targetPopulation, 1); // 1 Infected, 99,999 Susceptible
    }

    void Update()
    {
        if (!isRunning) return;

        currentTimer += Time.deltaTime;

        // 2. Track Performance (FPS and Grid Filter Math)
        frameCount++;
        totalDeltaTime += Time.deltaTime;
        totalDistanceChecks += EpidemicSystem.DistanceChecksThisFrame;

        // 3. End Benchmark and Write Log
        if (currentTimer >= benchmarkDuration)
        {
            isRunning = false;
            GenerateBenchmarkReport();
        }
    }

    private void GenerateBenchmarkReport()
    {
        float averageFPS = frameCount / totalDeltaTime;
        long averageChecksPerFrame = totalDistanceChecks / frameCount;

        string reportPath = Application.dataPath + "/Tests/Performance/BenchmarkReport.txt";
        
        string reportContent = "=== SCALABILITY BENCHMARK REPORT ===\n" +
                               "Target Population: " + targetPopulation + "\n" +
                               "Duration: " + benchmarkDuration + " seconds\n" +
                               "------------------------------------\n" +
                               "Average FPS: " + averageFPS.ToString("F2") + "\n" +
                               "Average Distance Checks Per Frame: " + averageChecksPerFrame + "\n" +
                               "====================================\n" +
                               "STATUS: " + (averageFPS >= 30 ? "PASSED" : "FAILED");

        File.WriteAllText(reportPath, reportContent);
        Debug.Log("Benchmark Complete. Report saved to: " + reportPath);
        
        // Optional: Freeze the simulation when done
        Time.timeScale = 0; 
    }
}