//TO BE DELETED

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "BuildingBoundsData", menuName = "Simulation/BuildingBoundsData")]
public class BuildingBoundsData : ScriptableObject
{
    public Vector3[] centers;
    public Vector3[] sizes;
}