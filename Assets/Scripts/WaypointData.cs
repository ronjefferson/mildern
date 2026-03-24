using UnityEngine;

[CreateAssetMenu(fileName = "WaypointData", menuName = "Simulation/WaypointData")]
public class WaypointData : ScriptableObject
{
    public Vector3[] waypoints;
    public int[] neighborData;
    public int[] neighborStart;
    public int[] neighborCount;
}