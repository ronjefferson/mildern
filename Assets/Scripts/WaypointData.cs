using UnityEngine;

[CreateAssetMenu(fileName = "WaypointData", menuName = "Simulation/WaypointData")]
public class WaypointData : ScriptableObject
{
    public Vector3[] waypoints;
}